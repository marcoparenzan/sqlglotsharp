using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dialect = SqlGlotSharp.dialects.dialect.Dialect;

namespace SqlGlotSharp.Tests;

public class TimeBucketParserTests
{
    private const string PythonIdentity = "oracle_plugins.timebucket_contract.TimeBucket";
    private static readonly string[] Queries =
    [
        "SELECT time_bucket(INTERVAL '5 minutes', ts)",
        "SELECT time_bucket(1000, ticks, 50)",
        "SELECT time_bucket(INTERVAL '1 day', ts, INTERVAL '1 hour')",
        "SELECT time_bucket(INTERVAL '1 month', ts, 'Europe/Berlin', '2024-01-01', INTERVAL '1 hour')",
        "SELECT time_bucket(INTERVAL '1 day', ts, timezone => 'UTC', origin => '2024-01-01', offset => INTERVAL '1 hour')",
        "SELECT time_bucket(bucket_width := INTERVAL '1 hour', ts := ts, timezone := 'UTC', origin := nested(x => 1))",
        "SELECT time_bucket(1000, ticks, origin => 50, origin => 60, 70)",
        "SELECT time_bucket(/* width */ 1000, /* time */ ticks, origin /* key */ => /* value */ 50) /* end */",
        "SELECT first(value, ts), last(value, ts), time_bucket_gapfill(1000, ticks, start => 0)"
    ];

    [Fact]
    public void Canonical_builder_retains_order_and_is_not_registered_in_base_or_Postgres()
    {
        var ts = Dialect.get_or_raise("timescale").parser();
        Assert.True(ts.FUNCTIONS.ContainsKey("TIME_BUCKET"));
        Assert.False(ts.FUNCTIONS.ContainsKey("FIRST"));
        Assert.False(ts.FUNCTIONS.ContainsKey("LAST"));
        Assert.False(new parser.Parser().FUNCTIONS.ContainsKey("TIME_BUCKET"));
        Assert.False(Dialect.get_or_raise("postgres").parser().FUNCTIONS.ContainsKey("TIME_BUCKET"));
        Assert.DoesNotContain(typeof(exp.TimeBucket), exp.EXPR_CLASSES.Values);
        Assert.False(exp.FUNCTION_BY_NAME.ContainsKey("TIME_BUCKET"));
        var bucket = Assert.IsType<exp.TimeBucket>(ts.FUNCTIONS["TIME_BUCKET"]([1, 2, 3, 4, 5]));
        Assert.Equal(new[] { "width", "this", "expressions" }, bucket.args.Keys);
        Assert.Equal(1, bucket.args["width"]);
        Assert.Equal(2, bucket.args["this"]);
        Assert.Equal(new object?[] { 3, 4, 5 }, Assert.IsType<List<object?>>(bucket.args["expressions"]));
        foreach (var dialect in new[] { "", "postgres" })
        {
            var ast = Assert.Single(Dialect.get_or_raise(dialect).parse(Queries[0]))!;
            Assert.Single(ast.walk().OfType<exp.Anonymous>());
            Assert.Empty(ast.walk().OfType<exp.TimeBucket>());
        }
        var extensions = Assert.Single(Dialect.get_or_raise("timescale").parse(Queries[^1]))!;
        Assert.Equal(3, extensions.walk().OfType<exp.Anonymous>().Count());
    }

    [Fact]
    public void Native_parse_dump_load_JSON_and_copy_retain_all_optional_arguments()
    {
        foreach (var sql in Queries[..^1])
        {
            var tree = Assert.Single(Dialect.get_or_raise("timescale").parse(sql))!;
            var bucket = Assert.Single(tree.walk().OfType<exp.TimeBucket>());
            Assert.Equal(PythonIdentity, serde.dump(bucket)[0][serde.CLASS]);
            Assert.IsType<exp.TimeBucket>(serde.load(serde.dump(bucket)));
            Assert.IsType<exp.TimeBucket>(bucket.copy());
            var payload = JsonSerializer.SerializeToNode(serde.dump(tree), serde.JsonOptions)!;
            using var json = JsonDocument.Parse(payload.ToJsonString());
            foreach (var roundtrip in new[] { serde.load(serde.dump(tree)), serde.load(json.RootElement), tree.copy() })
                Assert.True(JsonNode.DeepEquals(payload, JsonSerializer.SerializeToNode(serde.dump(roundtrip), serde.JsonOptions)), sql);
        }
        foreach (var sql in new[] { "SELECT time_bucket()", "SELECT time_bucket(1000)" })
            Assert.Throws<errors.ParseError>(() => Dialect.get_or_raise("timescale").parse(sql));
    }

    [Fact]
    public async Task Live_plugin_parse_codec_copy_and_minimum_arity_match_exact_custom_identity()
    {
        var cases = Queries.Concat(new[] { "SELECT time_bucket()", "SELECT time_bucket(1000)" })
            .SelectMany(sql => new[] { "timescale", "postgres", "" }
                .SelectMany(dialect => new[] { "IMMEDIATE", "RAISE", "IGNORE" }
                    .Select(level => new { sql, dialect, level }))).ToArray();
        const string script = """
            import sys, json
            sys.path.insert(0, 'python'); sys.path.insert(0, 'src/tools')
            from oracle_plugins.timescale import Timescale, TimescaleParser, TimescaleGenerator
            from oracle_plugins.timebucket_contract import TimeBucket
            from sqlglot import exp
            from sqlglot.dialects import Dialect
            from sqlglot.parsers.postgres import PostgresParser
            from sqlglot.generators.postgres import PostgresGenerator
            from sqlglot.parser import Parser
            from sqlglot.errors import ParseError, ErrorLevel
            from sqlglot.serde import dump, load
            assert 'TIME_BUCKET' not in Parser.FUNCTIONS
            assert 'TIME_BUCKET' not in PostgresParser.FUNCTIONS
            assert TimeBucket not in exp.EXPR_CLASSES.values()
            assert TimescaleGenerator.TRANSFORMS is not PostgresGenerator.TRANSFORMS
            assert TimeBucket in TimescaleGenerator.TRANSFORMS
            results = []
            for c in json.load(sys.stdin):
                p = Dialect.get_or_raise(c['dialect']).parser(error_level=ErrorLevel[c['level']])
                ast, failure = None, []
                try: ast = p.parse(Dialect.get_or_raise(c['dialect']).tokenize(c['sql']), c['sql'])
                except ParseError as e: failure = e.errors
                if ast is not None:
                    for n in ast:
                        for bucket in n.find_all(TimeBucket):
                            assert type(load(dump(bucket))) is TimeBucket
                            assert type(bucket.copy()) is TimeBucket
                results.append(dict(ast=None if ast is None else [dump(n) for n in ast],
                    loaded=None if ast is None else [dump(load(dump(n))) for n in ast],
                    copied=None if ast is None else [dump(n.copy()) for n in ast],
                    failure=failure, errors=[e.errors for e in p.errors]))
            print(json.dumps(results))
            """;
        var expected = JsonNode.Parse(await Python(script, JsonSerializer.Serialize(cases)))!.AsArray();
        for (var i = 0; i < cases.Length; i++)
        {
            var c = cases[i];
            var d = Dialect.get_or_raise(c.dialect);
            var p = d.parser(Enum.Parse<errors.ErrorLevel>(c.level));
            List<exp.Expr?>? ast = null;
            List<PyDict<string, object?>> failure = [];
            try { ast = p.parse(d.tokenize(c.sql), c.sql); }
            catch (errors.ParseError e) { failure = e.errors; }
            var actual = JsonSerializer.SerializeToNode(new
            {
                ast = ast?.Select(serde.dump).ToArray(),
                loaded = ast?.Select(n => serde.dump(serde.load(serde.dump(n)))).ToArray(),
                copied = ast?.Select(n => serde.dump(n!.copy())).ToArray(),
                failure, errors = p.errors.Select(e => e.errors).ToArray()
            }, serde.JsonOptions);
            Assert.True(JsonNode.DeepEquals(expected[i], actual), $"{c}\nPython: {expected[i]}\nNative: {actual}");
            if (ast is null) continue;
            foreach (var bucket in ast.SelectMany(n => n!.walk()).OfType<exp.TimeBucket>())
            {
                Assert.Equal(PythonIdentity, serde.dump(bucket)[0][serde.CLASS]);
                Assert.IsType<exp.TimeBucket>(serde.load(serde.dump(bucket)));
                Assert.IsType<exp.TimeBucket>(bucket.copy());
            }
        }
    }

    [Fact]
    public async Task Native_payload_loads_in_Python_as_the_contract_class_and_returns_unchanged()
    {
        var original = Assert.Single(Dialect.get_or_raise("timescale").parse(Queries[4]))!;
        var payload = JsonSerializer.Serialize(serde.dump(original), serde.JsonOptions);
        var returned = await Python("""
            import sys, json
            sys.path.insert(0, 'python'); sys.path.insert(0, 'src/tools')
            from oracle_plugins.timebucket_contract import TimeBucket
            from sqlglot.serde import dump, load
            tree = load(json.load(sys.stdin))
            bucket = next(tree.find_all(TimeBucket))
            assert type(bucket) is TimeBucket
            assert list(bucket.args) == ['width', 'this', 'expressions']
            assert len(bucket.expressions) == 3
            print(json.dumps(dump(tree.copy())))
            """, payload);
        using var json = JsonDocument.Parse(returned);
        var loaded = Assert.IsType<exp.Select>(serde.load(json.RootElement));
        var bucket = Assert.Single(loaded.walk().OfType<exp.TimeBucket>());
        Assert.Equal(3, bucket.expressions.Count);
        Assert.All(bucket.expressions, n => Assert.IsType<exp.Kwarg>(n));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(payload), JsonSerializer.SerializeToNode(serde.dump(loaded), serde.JsonOptions)));
    }

    private static async Task<string> Python(string script, string input, [CallerFilePath] string source = "")
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SQLGLOT_PYTHON") ?? "python")
        {
            WorkingDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..")),
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-B"); start.ArgumentList.Add("-c"); start.ArgumentList.Add(script);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, await stderr);
        return await stdout;
    }
}
