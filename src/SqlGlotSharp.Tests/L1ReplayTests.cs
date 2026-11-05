using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using SqlGlotSharp.Corpus;

namespace SqlGlotSharp.Tests;

public class L1ReplayTests
{
    [Fact]
    public void Representative_recorded_cases_replay_without_replacing_expectations()
    {
        var directory = Path.Combine(global::SqlGlotSharp.Corpus.CorpusReader.RootDirectory, "v30.27.0");
        var sqls = new HashSet<string> { "SELECT 1", "SELECT a, b FROM x", "SELECT a FROM x", "a", "1", "SELECT 1; SELECT 2", "SELECT $tag1$invalid heredoc string" };
        var cases = CorpusReader.Read(directory).Where(c => c.Level == "L1" && c.Dialect is "base" or "postgres"
            && sqls.Contains(c.Payload.GetProperty("sql").GetString()!)
            && c.Payload.GetProperty("options").EnumerateObject().All(o => o.Name == "error_level")).ToArray();
        Assert.NotEmpty(cases);
        Assert.Contains(cases, c => c.Dialect == "base");
        Assert.Contains(cases, c => c.Dialect == "postgres");
        Assert.Contains(cases, c => c.Payload.GetProperty("boundary").GetString() == "parse");
        Assert.Contains(cases, c => c.Payload.GetProperty("boundary").GetString() == "parse_one");
        Assert.Contains(cases, c => c.Payload.GetProperty("expected").TryGetProperty("error", out _));
        Assert.All(cases, CorpusVerifier.Verify);
        var regression = CorpusReader.Read(Path.Combine(global::SqlGlotSharp.Corpus.CorpusReader.RootDirectory, "regressions"))
            .Single(c => c.Level == "L1" && c.Payload.GetProperty("sql").GetString() == "SELECT a, b FROM x");
        CorpusVerifier.Verify(regression);
    }

    [Theory]
    [InlineData("{\"unexpected\":true}", "parse_one", null)]
    [InlineData("{\"into\":{\"$type\":\"not.a.module::Thing\"}}", "parse_one", null)]
    [InlineData("{\"error_level\":\"IGNORE\"}", "parse", null)]
    [InlineData("{\"error_level\":{\"$enum\":\"other::ErrorLevel\",\"value\":1}}", "parse", null)]
    [InlineData("{\"max_nodes\":\"1\"}", "parse", null)]
    [InlineData("{}", "parse_into", null)]
    [InlineData("{}", "parse", "sqlglot.parsers.mysql::MySQLParser")]
    public void Unsupported_configuration_is_not_ported_even_with_a_recorded_error(string options, string boundary, string? parser)
    {
        var payload = JsonNode.Parse("""{"sql":"SELECT '","read":"","expected":{"error":{"class":"TokenError","module":"sqlglot.errors","message":"anything"}},"warnings":[]}""")!.AsObject();
        payload["options"] = JsonNode.Parse(options);
        payload["boundary"] = boundary;
        if (parser is not null) payload["parser"] = parser;
        Assert.Throws<NotPortedException>(() => CorpusVerifier.Verify(Item(payload)));
    }

    [Fact]
    public void Runner_distinguishes_ast_error_warning_mismatches_from_missing_grammar()
    {
        var literal = JsonNode.Parse("""
            {"boundary":"parse_one","sql":"1","read":"","options":{},"warnings":[],"expected":{"result":[[{"c":"sqlglot.expressions.core.Literal","m":{"line":1,"col":1,"start":0,"end":0}},{"i":0,"k":"this","v":"1"},{"i":0,"k":"is_string","v":false}]]}}
            """)!.AsObject();
        var mismatch = (JsonObject)literal.DeepClone();
        mismatch["expected"] = JsonNode.Parse("{\"result\":[]}");
        var warning = (JsonObject)literal.DeepClone();
        warning["warnings"] = JsonNode.Parse("[{\"level\":\"WARNING\",\"message\":\"unexpected\"}]");
        var error = (JsonObject)literal.DeepClone();
        error["sql"] = "";
        error["expected"] = JsonNode.Parse("{\"error\":{\"class\":\"ParseError\",\"module\":\"sqlglot.errors\",\"message\":\"wrong\"}}");
        var missing = (JsonObject)literal.DeepClone();
        missing["sql"] = "CREATE PROCEDURE foo WITH RECOMPILE AS SELECT 1";
        var results = CorpusRunner.Run([Item(literal), Item(mismatch), Item(warning), Item(error), Item(missing)], CorpusVerifier.Verify);
        Assert.Equal(new[] { CaseStatus.Passed, CaseStatus.Failed, CaseStatus.Failed, CaseStatus.Failed, CaseStatus.NotPorted }, results.Select(r => r.Status));
        Assert.Contains("Actual warnings", Assert.Throws<InvalidOperationException>(() => CorpusVerifier.Verify(Item(warning))).Message);
    }

    [Fact]
    public void Normalization_changes_only_positions_and_nested_types_like_extractor()
    {
        var ast = JsonSerializer.SerializeToElement(new object[]
        {
            new { c = "node", m = new { line = 2, col = 3, start = 3, end = 5, untouched = "😀" },
                t = new[] { new { m = new { line = 1, col = 1, start = -1, end = -2 } } } },
            new { v = "😀", k = "start" }
        });
        var actual = L1Replay.NormalizeAst(ast, "😀\r\n'😀'");
        using var expected = JsonDocument.Parse("""
            [{"c":"node","m":{"line":2,"col":4,"start":4,"end":7,"untouched":"😀"},"t":[{"m":{"line":1,"col":2,"start":7,"end":6}}]},{"v":"😀","k":"start"}]
            """);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual), actual.GetRawText());
        Assert.True(JsonElement.DeepEquals(ast, L1Replay.NormalizeAst(ast, "")));
        Assert.Equal(3, ast[0].GetProperty("m").GetProperty("start").GetInt32());
    }

    [Fact]
    public async Task Boundaries_options_parser_identity_and_warnings_match_python_oracle()
    {
        var inputs = new List<object>();
        foreach (var read in new[] { "", "postgres" })
        foreach (var boundary in new[] { "parse", "parse_one" })
        foreach (var sql in new[] { "", ";;", "SELECT 1; SELECT 2", "SELECT '😀',\n 'x'", "SELECT a +", "SELECT 1 SELECT 2" })
        foreach (var level in new[] { "IMMEDIATE", "RAISE", "WARN", "IGNORE" })
            inputs.Add(new { sql, read, boundary, level, options = new { error_message_context = 2, max_errors = 1, max_nodes = -1 } });
        // Explicit identity must not silently select the parser from read (CARET differs).
        inputs.Add(new { sql = "SELECT 2 ^ 3", read = "postgres", boundary = "parse_one", level = "IMMEDIATE", parser = "sqlglot.parser::Parser", options = new { max_nodes = -1 } });
        inputs.Add(new { sql = "SELECT 2 ^ 3", read = "postgres", boundary = "parse_one", level = "IMMEDIATE", parser = "sqlglot.parsers.postgres::PostgresParser", options = new { max_nodes = -1 } });
        inputs.Add(new { sql = "SELECT a + 2", read = "", boundary = "parse_one", level = "IMMEDIATE", options = new { max_nodes = 1 } });
        const string script = """
            import sys, json, logging, importlib.util
            sys.path.insert(0, 'python')
            import sqlglot
            from sqlglot import exp, serde
            from sqlglot.errors import ErrorLevel
            from sqlglot.dialects import Dialect
            from sqlglot.parser import Parser
            from sqlglot.parsers.postgres import PostgresParser
            spec = importlib.util.spec_from_file_location('extract_corpus', 'src/tools/extract_corpus.py')
            extractor = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(extractor)
            result = []
            for c in json.load(sys.stdin):
                messages = []
                class Capture(logging.Handler):
                    def emit(self, record):
                        if record.levelno >= logging.WARNING:
                            messages.append(dict(level=record.levelname, message=record.getMessage()))
                logger = logging.getLogger('sqlglot')
                capture = Capture()
                logger.addHandler(capture)
                opts = dict(c['options'], error_level=ErrorLevel[c.pop('level')])
                try:
                    if 'parser' in c:
                        dialect = Dialect.get_or_raise(c['read'])
                        cls = Parser if c['parser'] == 'sqlglot.parser::Parser' else PostgresParser
                        nodes = cls(dialect=dialect, **opts).parse(dialect.tokenize(c['sql']), c['sql'])
                        if c['boundary'] == 'parse_one':
                            from sqlglot.errors import ParseError
                            if not nodes or nodes[0] is None: raise ParseError(f"No expression was parsed from '{c['sql']}'")
                            nodes = [exp.Block(expressions=nodes) if len(nodes) > 1 else nodes[0]]
                    else:
                        nodes = getattr(sqlglot, c['boundary'])(c['sql'], read=c['read'], **opts)
                        if not isinstance(nodes, list): nodes = [nodes]
                    c['expected'] = dict(result=[extractor.normalize_ast(serde.dump(n), c['sql']) if n is not None else None for n in nodes])
                except Exception as e:
                    c['expected'] = dict(error=dict(class_=type(e).__name__, module=type(e).__module__, message=str(e)))
                    c['expected']['error']['class'] = c['expected']['error'].pop('class_')
                finally:
                    logger.removeHandler(capture)
                c['options'] = extractor.normalize(opts)
                c['warnings'] = messages
                result.append(c)
            print(json.dumps(result))
            """;
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SQLGLOT_PYTHON") ?? "python")
        {
            WorkingDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Source())!, "..", "..")),
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-B"); start.ArgumentList.Add("-c"); start.ArgumentList.Add(script);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(inputs).AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) { process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await stderr);
        using var output = JsonDocument.Parse(await stdout);
        var cases = output.RootElement.EnumerateArray().Select((c, i) => new CorpusCase($"oracle-{i}", "L1", c.GetProperty("read").GetString() == "" ? "base" : "postgres", [], c.Clone())).ToArray();
        Assert.Equal(99, cases.Length);
        Assert.Contains(cases, c => c.Payload.GetProperty("warnings").GetArrayLength() > 0);
        Assert.All(cases, CorpusVerifier.Verify);
        // Corrupt captured messages: these must fail, not be treated as unavailable warning support.
        var warned = JsonNode.Parse(cases.First(c => c.Payload.GetProperty("warnings").GetArrayLength() > 0).Payload.GetRawText())!.AsObject();
        warned["warnings"] = new JsonArray();
        Assert.Throws<InvalidOperationException>(() => CorpusVerifier.Verify(Item(warned)));
    }

    private static CorpusCase Item(JsonObject payload) => new("fixture", "L1", "base", [], JsonSerializer.SerializeToElement(payload));
    private static string Source([CallerFilePath] string source = "") => source;
}
