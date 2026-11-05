using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using SqlGlotSharp.Corpus;

namespace SqlGlotSharp.Tests;

public class L3ReplayTests
{
    private static readonly string Artifacts = Path.Combine(RepoPaths.Scratch, "l3-agent");

    [Fact]
    public void Selected_real_external_base_and_postgres_transpile_records_match_exactly()
    {
        var sqls = new HashSet<string>(StringComparer.Ordinal)
        {
            "SELECT 1", "SELECT a FROM x", "SELECT a, b FROM x", "SELECT * FROM x",
            "SELECT a + 1 FROM x", "SELECT CURRENT_TIMESTAMP", "SELECT 1; SELECT 2",
            "CREATE TABLE products (price DECIMAL CHECK price > 0)"
        };
        var cases = RealCases().Where(c => c.Payload.GetProperty("boundary").GetString() == "transpile"
            && sqls.Contains(c.Payload.GetProperty("sql").GetString()!)
            && c.Payload.GetProperty("read").GetString() is "" or "postgres")
            .GroupBy(c => c.Dialect).SelectMany(g => g.Take(40)).ToArray();
        Assert.NotEmpty(cases);
        Assert.Contains(cases, c => c.Dialect == "base");
        Assert.Contains(cases, c => c.Dialect == "postgres");
        Assert.Contains(cases, c => c.Payload.GetProperty("expected").TryGetProperty("result", out _));
        Assert.Contains(cases, c => c.Payload.GetProperty("expected").TryGetProperty("error", out _));
        Passed(cases);
        foreach (var c in cases)
        {
            var wrongSql = Copy(c);
            wrongSql["expected"] = new JsonObject { ["result"] = new JsonArray("deliberately incorrect SQL") };
            Assert.Throws<InvalidOperationException>(() => CorpusVerifier.Verify(Item(wrongSql)));
            var wrongWarnings = Copy(c);
            wrongWarnings["warnings"] = new JsonArray(new JsonObject { ["level"] = "WARNING", ["message"] = "invented" });
            Assert.Throws<InvalidOperationException>(() => CorpusVerifier.Verify(Item(wrongWarnings)));
        }
        foreach (var c in cases.Where(c => c.Payload.GetProperty("expected").TryGetProperty("error", out _)))
        foreach (var field in new[] { "class", "module", "message" })
        {
            var p = Copy(c); p["expected"]!["error"]![field] = "invented";
            Assert.Throws<InvalidOperationException>(() => CorpusVerifier.Verify(Item(p)));
        }
        var unportedDdl = RealCases().Single(c => c.Payload.GetProperty("boundary").GetString() == "transpile"
            && c.Payload.GetProperty("sql").GetString() == "CREATE TABLE products (price DECIMAL, CHECK price > 1)");
        // User-defined types are ported now: this record (an expected ParseError) is verifiable.
        CorpusVerifier.Verify(unportedDdl);
        // Recorder helper contracts are not transpile list contracts. Do not coerce them.
        var helpers = RealCases().Where(c => c.Payload.TryGetProperty("pipeline", out _)).Take(3).ToArray();
        Assert.NotEmpty(helpers);
        Assert.All(helpers, c => { try { CorpusVerifier.Verify(c); } catch (NotPortedException) { } });
        Directory.CreateDirectory(Artifacts);
        File.WriteAllText(Path.Combine(Artifacts, "external-l3-selected.json"), JsonSerializer.Serialize(cases.Select(c => new { c.Id, c.Dialect, c.Sources })));
    }

    [Fact]
    public async Task Fresh_actual_recorder6_transpile_contexts_errors_warnings_and_resolved_write_match()
    {
        const string script = """
            import sys,json
            sys.path[:0]=['python','src/tools']
            import sqlglot,extract_corpus as corpus
            from sqlglot.errors import ErrorLevel
            from unittest.mock import patch
            recorder=corpus.Recorder(); recorder.install()
            try:
                for pretty in (False,True):
                    with patch.object(sqlglot,'pretty',pretty):
                        for read in ('','postgres'):
                            for opts in ({},{'identity':False},{'write':''},{'write':'postgres'},
                                         {'pretty':None},{'pretty':False},{'pretty':True},
                                         {'identify':True,'normalize':True,'comments':False}):
                                for sql in ('SELECT A, B FROM x -- comment', 'SELECT CAST(a AS TEXT)', "SELECT '\U0001f600'", 'SELECT 1; SELECT 2', ';;'):
                                    sqlglot.transpile(sql,read=read,**opts)
                        for opts in ({},{'identity':False},{'write':''},{'write':'postgres'}):
                            sqlglot.transpile('SELECT a # b',read='postgres',**opts)
                        for level in (ErrorLevel.IMMEDIATE,ErrorLevel.WARN,ErrorLevel.IGNORE):
                            try: sqlglot.transpile('SELECT a +',read='postgres',write='postgres',error_level=level)
                            except sqlglot.errors.SqlglotError: pass
            finally: recorder.uninstall()
            cases=[c for c in recorder.cases.values() if c['level']=='L3']
            assert cases and not recorder.skipped, dict(recorder.skipped)
            print(json.dumps(cases))
            """;
        var json = await Python(script);
        Directory.CreateDirectory(Artifacts);
        File.WriteAllText(Path.Combine(Artifacts, "fresh-recorder6-l3.json"), json);
        using var doc = JsonDocument.Parse(json);
        var cases = doc.RootElement.EnumerateArray().Select(Item).ToArray();
        Assert.True(cases.Length >= 100);
        Assert.All(cases, c => Assert.Equal("L3", c.Payload.GetProperty("level").GetString()));
        Assert.Contains(cases, c => c.Payload.GetProperty("warnings").GetArrayLength() > 0);
        Assert.Contains(cases, c => c.Payload.GetProperty("expected").TryGetProperty("error", out _));
        Passed(cases);
        var identityFalse = cases.Where(c => c.Payload.GetProperty("read").GetString() == "postgres"
            && c.Payload.GetProperty("options").TryGetProperty("identity", out var v) && !v.GetBoolean()).ToArray();
        Assert.NotEmpty(identityFalse);
        Assert.All(identityFalse, c => Assert.Equal("", c.Payload.GetProperty("write").GetString()));
        Assert.Contains(cases, c => c.Payload.GetProperty("read").GetString() == "postgres"
            && !c.Payload.GetProperty("options").TryGetProperty("identity", out _)
            && c.Payload.GetProperty("write").GetString() == "postgres");
        foreach (var c in cases.Where(c => c.Payload.GetProperty("warnings").GetArrayLength() > 0))
        {
            var p = Copy(c); p["warnings"] = new JsonArray();
            Assert.Throws<InvalidOperationException>(() => CorpusVerifier.Verify(Item(p)));
        }
        foreach (var c in cases.Where(c => c.Payload.GetProperty("expected").TryGetProperty("error", out _)))
        foreach (var field in new[] { "class", "module", "message" })
        {
            var p = Copy(c); p["expected"]!["error"]![field] = "invented";
            Assert.Throws<InvalidOperationException>(() => CorpusVerifier.Verify(Item(p)));
        }
        foreach (var c in cases.Where(c => c.Payload.GetProperty("sql").GetString() == "SELECT A, B FROM x -- comment"
            && (!c.Payload.GetProperty("options").TryGetProperty("pretty", out var pretty) || pretty.ValueKind == JsonValueKind.Null)))
        {
            var p = Copy(c);
            p["runtime_context"]!["sqlglot.pretty"] = !c.Payload.GetProperty("runtime_context").GetProperty("sqlglot.pretty").GetBoolean();
            Assert.Throws<InvalidOperationException>(() => CorpusVerifier.Verify(Item(p)));
        }
        // Omitted write resolves to read; identity=False resolves to base. The two native
        // generators have genuinely distinct bitwise XOR SQL, so changing write is observable.
        var casts = cases.Where(c => c.Payload.GetProperty("sql").GetString() == "SELECT a # b").ToArray();
        Assert.True(casts.Select(c => c.Payload.GetProperty("expected").GetRawText()).Distinct().Count() > 1);
        foreach (var c in casts)
        {
            var p = Copy(c); p["write"] = c.Payload.GetProperty("write").GetString() == "" ? "postgres" : "";
            Assert.Throws<InvalidOperationException>(() => CorpusVerifier.Verify(Item(p)));
        }
    }

    [Fact]
    public void Unsupported_capabilities_are_not_mismatches_and_sql_differences_are_not_normalized()
    {
        var valid = JsonNode.Parse("""{"boundary":"transpile","sql":"select 1","read":"","write":"","options":{},"runtime_context":{"sqlglot.pretty":false},"expected":{"result":["SELECT 1"]},"warnings":[]}""")!.AsObject();
        var cases = new List<CorpusCase> { Item(valid) };
        foreach (var sql in new[] { "select 1", "SELECT 1 ", "SELECT  1", "SELECT 2" })
        {
            var p = (JsonObject)valid.DeepClone(); p["expected"]!["result"]![0] = sql; cases.Add(Item(p));
        }
        foreach (var update in new[]
        {
            """{"read":"mysql"}""", """{"write":"mysql"}""", """{"options":{"unknown":true}}""",
            """{"options":{"identity":null}}""", """{"options":{"error_level":"WARN"}}""",
            """{"options":{"copy":false}}""",
            """{"options":{"error_level":{"$enum":"other::ErrorLevel","value":"WARN"}}}""",
            """{"options":{"error_level":{"$enum":"sqlglot.errors::ErrorLevel","value":"WARN","unknown":true}}}""",
            """{"parser":"tests::CustomParser"}""", """{"generator":"tests::CustomGenerator"}""",
            """{"tokenizer":"tests::CustomTokenizer"}""", """{"pipeline":"parse_one_then_sql"}""",
            """{"boundary":"validate_identity"}""", """{"runtime_context":{"unknown":true}}""",
            """{"logger_context":{"generator":{"warning":"unknown","error":"enabled"}}}"""
        })
        {
            var p = (JsonObject)valid.DeepClone();
            foreach (var property in JsonNode.Parse(update)!.AsObject()) p[property.Key] = property.Value?.DeepClone();
            // Even with deliberately wrong expectations, unsupported validation runs first.
            p["expected"] = new JsonObject { ["result"] = new JsonArray("wrong") };
            cases.Add(Item(p));
        }
        var results = CorpusRunner.Run(cases, CorpusVerifier.Verify);
        Assert.Equal(CaseStatus.Passed, results[0].Status);
        Assert.All(results.Skip(1).Take(4), r => Assert.Equal(CaseStatus.Failed, r.Status));
        Assert.All(results.Skip(5), r => { Assert.Equal(CaseStatus.NotPorted, r.Status); Assert.Contains("corpus:", r.Detail); });
        foreach (var text in new[] {
            valid.ToJsonString().Replace("\"options\":{}", "\"options\":{\"pretty\":true,\"pretty\":false}", StringComparison.Ordinal),
            valid.ToJsonString()[..^1] + ",\"read\":\"postgres\"}" })
        {
            // JsonElement preserves duplicate keys, unlike JsonObject.
            using var doc = JsonDocument.Parse(text);
            Assert.Throws<NotPortedException>(() => CorpusVerifier.Verify(Item(doc.RootElement)));
        }
    }

    private static void Passed(IEnumerable<CorpusCase> cases) => Assert.All(CorpusRunner.Run(cases, CorpusVerifier.Verify),
        r => Assert.True(r.Status == CaseStatus.Passed, $"{r.Case.Id}: {r.Case.Payload}\n{r.Status}: {r.Detail}"));
    private static JsonObject Copy(CorpusCase c) => JsonNode.Parse(c.Payload.GetRawText())!.AsObject();
    private static CorpusCase Item(JsonObject p) => Item(JsonSerializer.SerializeToElement(p));
    private static CorpusCase Item(JsonElement p) => new(p.TryGetProperty("id", out var id) ? id.GetString()! : "fixture", "L3",
        p.TryGetProperty("write", out var write) && write.GetString() == "postgres" ? "postgres" : "base", [], p.Clone());
    private static IEnumerable<CorpusCase> RealCases()
    {
        var directory = Path.Combine(CorpusReader.RootDirectory, "v30.27.0-logger-context-full");
        foreach (var dialect in new[] { "base", "postgres" })
        foreach (var line in File.ReadLines(CorpusReader.ResolveFile(directory, $"L3.{dialect}.jsonl")))
        {
            using var doc = JsonDocument.Parse(line); var p = doc.RootElement;
            yield return new(p.GetProperty("id").GetString()!, "L3", dialect,
                p.GetProperty("sources").EnumerateArray().Select(s => s.GetString()!).ToArray(), p.Clone());
        }
    }
    private static async Task<string> Python(string script)
    {
        var scratch = Path.Combine(Artifacts, "tmp"); Directory.CreateDirectory(scratch);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SQLGLOT_PYTHON") ?? "python")
        {
            WorkingDirectory = Root(), UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.Environment["TEMP"] = scratch; start.Environment["TMP"] = scratch;
        start.ArgumentList.Add("-B"); start.ArgumentList.Add("-c"); start.ArgumentList.Add(script);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); } catch (OperationCanceledException) { process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await error);
        return await output;
    }
    private static string Root([CallerFilePath] string source = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", ".."));
}
