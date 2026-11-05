using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using SqlGlotSharp.Corpus;

namespace SqlGlotSharp.Tests;

public class L2ReplayTests
{
    [Fact]
    public void Real_external_L2_generate_and_sql_records_are_verified()
    {
        var sqls = new HashSet<string> { "SELECT 1", "SELECT a, b FROM x", "SELECT a FROM x", "1", "'x'", "SELECT * FROM x", "SELECT a + 1 FROM x", "CURRENT_DATE", "CURRENT_TIMESTAMP", "SELECT CURRENT_TIMESTAMP" };
        var cases = RealCases().Where(c => c.Payload.GetProperty("expected").TryGetProperty("result", out var r)
            && r.ValueKind == JsonValueKind.String && sqls.Contains(r.GetString()!)).ToArray();
        Assert.NotEmpty(cases);
        Assert.Contains(cases, c => c.Dialect == "base");
        Assert.Contains(cases, c => c.Dialect == "postgres");
        Assert.Contains(cases, c => c.Payload.GetProperty("boundary").GetString() == "generate");
        Assert.Contains(cases, c => c.Payload.GetProperty("boundary").GetString() == "sql");
        var results = CorpusRunner.Run(cases, CorpusVerifier.Verify);
        Assert.All(results, r => Assert.True(r.Status == CaseStatus.Passed, $"{r.Case.Id} {r.Status}: {r.Detail}"));
        // Corpus selection never rewrites expectations. A corrupt SQL must fail the very same native path.
        var corrupt = JsonNode.Parse(cases[0].Payload.GetRawText())!.AsObject();
        corrupt["expected"] = JsonNode.Parse("""{"result":"not the generated SQL"}""");
        Assert.Throws<InvalidOperationException>(() => CorpusVerifier.Verify(Item(corrupt)));
    }

    [Fact]
    public void Json_delete_corpus_case_now_matches_upstream()
    {
        var cases = RealCases().Where(c => c.Payload.GetProperty("expected").TryGetProperty("result", out var r)
            && r.ValueKind == JsonValueKind.String && r.GetString() == "a #- b").ToArray();
        Assert.NotEmpty(cases);
        foreach (var c in cases)
            CorpusVerifier.Verify(c);
    }

    [Fact]
    public void Runner_distinguishes_mismatch_unported_and_success_without_swallowing_errors()
    {
        var valid = JsonNode.Parse("""{"boundary":"sql","write":"","options":{},"ast":[{"c":"sqlglot.expressions.core.Literal"},{"i":0,"k":"this","v":"1"},{"i":0,"k":"is_string","v":false}],"expected":{"result":"1"},"warnings":[]}""")!.AsObject();
        JsonObject Copy() => (JsonObject)valid.DeepClone();
        var wrongSql = Copy(); wrongSql["expected"] = JsonNode.Parse("""{"result":"2"}""");
        var wrongWarnings = Copy(); wrongWarnings["warnings"] = JsonNode.Parse("""[{"level":"WARNING","message":"invented"}]""");
        var wrongError = Copy(); wrongError["expected"] = JsonNode.Parse("""{"error":{"class":"UnsupportedError","module":"sqlglot.errors","message":"invented"}}""");
        var unsupported = Copy(); unsupported["options"] = JsonNode.Parse("""{"unknown":true}""");
        var local = Copy(); local["ast"]![0]!["c"] = "tests.test_generator.TestGenerator.test_fallback_function_sql.<locals>.SpecialUdf";
        var results = CorpusRunner.Run(new[] { valid, wrongSql, wrongWarnings, wrongError, unsupported, local }.Select(Item), CorpusVerifier.Verify);
        Assert.Equal(new[] { CaseStatus.Passed, CaseStatus.Failed, CaseStatus.Failed, CaseStatus.Failed, CaseStatus.NotPorted, CaseStatus.NotPorted }, results.Select(r => r.Status));
        foreach (var update in new[] {
            """{"options":{"copy":1}}""", """{"options":{"unsupported_level":"WARN"}}""",
            """{"options":{"unsupported_level":{"$enum":"other::ErrorLevel","value":"WARN"}}}""",
            """{"write":"mysql"}""", """{"generator":"tests.test_generator::SpecialGenerator"}""",
            """{"parser":"sqlglot.parser::Parser"}""", """{"boundary":"generate_into"}""",
            """{"logger_context":{"parser":{"warning":"invented","error":"mock"}}}""" })
        {
            var p = Copy();
            foreach (var property in JsonNode.Parse(update)!.AsObject()) p[property.Key] = property.Value?.DeepClone();
            p["expected"] = wrongError["expected"]!.DeepClone();
            Assert.Throws<NotPortedException>(() => CorpusVerifier.Verify(Item(p)));
        }
    }

    [Fact]
    public async Task Native_boundaries_options_identities_warnings_and_errors_match_live_Python()
    {
        const string script = """
            import sys,json,logging
            sys.path[:0]=['python','src/tools']
            import sqlglot,extract_corpus as extractor
            from sqlglot import exp,serde
            from sqlglot.errors import ErrorLevel
            from sqlglot.generator import Generator
            from sqlglot.generators.postgres import PostgresGenerator
            from sqlglot.parser import Parser
            from sqlglot.parsers.postgres import PostgresParser
            from sqlglot.dialects import Dialect
            out=[]
            def replay(boundary,write,opts,expression=None,sql=None,read='',identity=None):
                c=dict(boundary=boundary,write=write,options=extractor.normalize(opts))
                if expression is not None: c['ast']=serde.dump(expression)
                if sql is not None: c.update(sql=sql,read=read)
                if identity is not None: c['generator']=identity
                messages=[]
                class Capture(logging.Handler):
                    def emit(self,r):
                        if r.levelno>=logging.WARNING: messages.append(dict(level=r.levelname,message=r.getMessage()))
                logger=logging.getLogger('sqlglot'); handler=Capture(); logger.addHandler(handler)
                try:
                    if boundary=='transpile': result=sqlglot.transpile(sql,read=read,write=write,**opts)
                    elif boundary=='sql': result=expression.sql(dialect=write,**opts)
                    else:
                        args=dict(opts); copy=args.pop('copy',True)
                        cls=Generator if identity=='sqlglot.generator::Generator' or not write else PostgresGenerator
                        result=cls(dialect=write,**args).generate(expression,copy=copy)
                    c['expected']=dict(result=result)
                except Exception as e: c['expected']=dict(error=dict(class_=type(e).__name__,module=type(e).__module__,message=str(e))); c['expected']['error']['class']=c['expected']['error'].pop('class_')
                finally: logger.removeHandler(handler)
                c['warnings']=messages;out.append(c)
            for write in ['', 'postgres']:
                for boundary in ['sql','generate']:
                    for opts in [{},{'pretty':True,'pad':4,'indent':4,'leading_comma':True,'max_text_width':10},
                                 {'identify':'safe','normalize':True,'normalize_functions':'lower','comments':False,'copy':False},
                                 {'pretty':None,'identify':True,'normalize_functions':False}]:
                        replay(boundary,write,opts,sqlglot.parse_one("SELECT A, ABS(B), '😀' AS C FROM x -- comment"))
            for level in ErrorLevel:
                replay('generate','postgres',dict(unsupported_level=level,max_unsupported=1),exp.IgnoreNulls(this=exp.column('a')))
            # Explicit base generator with Postgres dialect must still use base dispatch.
            replay('generate','postgres',{},exp.IgnoreNulls(this=exp.column('a')),identity='sqlglot.generator::Generator')
            for read in ['', 'postgres']:
                for sql in ['SELECT 1; SELECT 2',';;',"SELECT '😀'",'SELECT a +']:
                    for level in [ErrorLevel.IMMEDIATE,ErrorLevel.WARN,ErrorLevel.IGNORE]:
                        replay('transpile','postgres',dict(error_level=level,identity=False),sql=sql,read=read)
            print(json.dumps(out))
            """;
        using var output = JsonDocument.Parse(await Python(script));
        var cases = output.RootElement.EnumerateArray().Select((p, i) => new CorpusCase($"live-{i}", "L2", p.GetProperty("write").GetString() == "" ? "base" : "postgres", [], p.Clone())).ToArray();
        Assert.Equal(45, cases.Length);
        Assert.Contains(cases, c => c.Payload.GetProperty("warnings").GetArrayLength() > 0);
        Assert.Contains(cases, c => c.Payload.GetProperty("expected").TryGetProperty("error", out _));
        var explicitBase = cases.Single(c => c.Payload.TryGetProperty("generator", out var identity)
            && identity.GetString() == "sqlglot.generator::Generator");
        CorpusVerifier.Verify(explicitBase); // base IGNORE NULLS is ported
        Assert.All(CorpusRunner.Run(cases, CorpusVerifier.Verify), r =>
        {
            if (r.Case.Id == explicitBase.Id) Assert.Equal(CaseStatus.Passed, r.Status);
            else Assert.True(r.Status == CaseStatus.Passed, $"{r.Case.Payload} {r.Status}: {r.Detail}");
        });
        foreach (var c in cases.Where(c => c.Payload.GetProperty("warnings").GetArrayLength() > 0))
        {
            var p = JsonNode.Parse(c.Payload.GetRawText())!.AsObject(); p["warnings"] = new JsonArray();
            Assert.Throws<InvalidOperationException>(() => CorpusVerifier.Verify(Item(p)));
        }
    }

    [Fact]
    public void Explicit_logger_context_does_not_infer_suppression_from_expectations()
    {
        var p = JsonNode.Parse("""{"boundary":"generate","write":"postgres","options":{},"ast":[{"c":"sqlglot.expressions.core.IgnoreNulls"},{"c":"sqlglot.expressions.core.Column","i":0,"k":"this"},{"c":"sqlglot.expressions.core.Identifier","i":1,"k":"this"},{"i":2,"k":"this","v":"a"}],"expected":{"result":"a"},"warnings":[{"level":"WARNING","message":"PostgreSQL does not support IGNORE NULLS."}]}""")!.AsObject();
        foreach (var mode in new[] { "enabled", "mock", "disabled" })
        {
            var c = (JsonObject)p.DeepClone();
            c["logger_context"] = JsonSerializer.SerializeToNode(new { generator = new { warning = mode, error = mode } });
            if (mode == "disabled") c["warnings"] = new JsonArray();
            CorpusVerifier.Verify(Item(c));
        }
        p["warnings"] = new JsonArray();
        Assert.Throws<InvalidOperationException>(() => CorpusVerifier.Verify(Item(p)));
    }

    private static IEnumerable<CorpusCase> RealCases()
    {
        var directory = Path.Combine(CorpusReader.RootDirectory, "v30.27.0-logger-context-full");
        foreach (var dialect in new[] { "base", "postgres" })
        foreach (var line in File.ReadLines(CorpusReader.ResolveFile(directory, $"L2.{dialect}.jsonl")))
        {
            using var doc = JsonDocument.Parse(line); var p = doc.RootElement;
            yield return new(p.GetProperty("id").GetString()!, "L2", dialect, p.GetProperty("sources").EnumerateArray().Select(s => s.GetString()!).ToArray(), p.Clone());
        }
    }
    private static CorpusCase Item(JsonObject p) => new("fixture", "L2", "base", [], JsonSerializer.SerializeToElement(p));
    private static async Task<string> Python(string script)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SQLGLOT_PYTHON") ?? "python")
        {
            WorkingDirectory = Root(), UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-B"); start.ArgumentList.Add("-c"); start.ArgumentList.Add(script);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); } catch (OperationCanceledException) { process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await error);
        return await output;
    }
    private static string Root([CallerFilePath] string source = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", ".."));
}
