using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dialect = SqlGlotSharp.dialects.dialect.Dialect;
using Timescale = SqlGlotSharp.dialects.timescale.Timescale;

namespace SqlGlotSharp.Tests;

/// <summary>Live test-only oracle parity, not Timescale server execution or transpilation equivalence.</summary>
public class TimescaleIdentityTests
{
    private sealed class DerivedTimescale : Timescale;

    [Fact]
    public void Exact_dialect_routes_preserve_settings_and_fresh_instances()
    {
        var dialect = Assert.IsType<Timescale>(Dialect.get_or_raise("timescale"));
        Assert.True(dialect.Equals("timescale"));
        Assert.Same(dialect, Dialect.get_or_raise(dialect));
        Assert.IsType<Timescale>(Dialect.get_or_raise(typeof(Timescale)));
        Assert.Equal((2L, 17L, 0L), Dialect.get_or_raise("timescale, version=2.17").version);
        var parser = Assert.IsType<parsers.timescale.TimescaleParser>(dialect.parser());
        Assert.Same(dialect, parser.dialect);
        Assert.NotSame(parser, dialect.parser());
        Assert.Equal(errors.ErrorLevel.IGNORE, dialect.parser(errors.ErrorLevel.IGNORE).error_level);
        var generator = Assert.IsType<generators.timescale.TimescaleGenerator>(dialect.generator(true));
        Assert.Same(dialect, generator.dialect);
        Assert.Equal(true, generator.pretty);
        Assert.NotSame(generator, dialect.generator());
        Assert.IsType<dialects.postgres.Postgres.Tokenizer>(dialect.tokenizer());
        Assert.IsType<dialects.postgres.Postgres.Tokenizer>(tokens.Tokenizer.ForDialect("timescale"));
        Assert.Throws<NotPortedException>(() => new DerivedTimescale().parser());
        Assert.Throws<NotPortedException>(() => new DerivedTimescale().generator());
    }

    [Theory]
    [InlineData("SELECT first(value, time), last(value, time)")]
    [InlineData("SELECT FIRST(value + 1, time), LAST(value, time + 2)")]
    public void First_and_last_keep_both_arguments_without_mutating_Postgres(string sql)
    {
        var pg = Dialect.get_or_raise("postgres").parser();
        Assert.True(pg.FUNCTIONS.ContainsKey("FIRST"));
        Assert.True(pg.FUNCTIONS.ContainsKey("LAST"));
        var dialect = new Timescale();
        var ast = Assert.Single(dialect.parse(sql))!;
        var calls = ast.walk().OfType<exp.Anonymous>().ToArray();
        Assert.Equal(2, calls.Length);
        Assert.All(calls, call => Assert.Equal(2, call.expressions.Count));
        Assert.DoesNotContain(ast.walk(), node => node is exp.First or exp.Last or exp.DateBin);
        var generated = dialect.generator().generate(ast);
        var reparsed = Assert.Single(dialect.parse(generated))!;
        Assert.All(reparsed.walk().OfType<exp.Anonymous>(), call => Assert.Equal(2, call.expressions.Count));
        Assert.True(Dialect.get_or_raise("postgres").parser().FUNCTIONS.ContainsKey("FIRST"));
        Assert.IsType<exp.First>(Assert.Single(Assert.IsType<exp.Select>(
            Assert.Single(Dialect.get_or_raise("postgres").parse("SELECT FIRST(value)"))).expressions));
    }

    [Fact]
    public void Qualify_is_rewritten_like_PostgreSQL()
    {
        var ast = Assert.Single(new Timescale().parse("SELECT value FROM measurements"))!;
        ast.set("qualify", new exp.Qualify(@this: new exp.Boolean(@this: true)));
        var sql = new Timescale().generator().generate(ast);
        Assert.Contains("AS _t WHERE TRUE", sql);
    }

    private static readonly string[] ExtensionQueries =
    [
        "SELECT time_bucket(INTERVAL '5 minutes', time) AS bucket FROM measurements",
        "SELECT time_bucket(INTERVAL '1 day', time, INTERVAL '1 hour')",
        "SELECT time_bucket(INTERVAL '1 month', time, 'Europe/Berlin', '2024-01-01')",
        "SELECT time_bucket(1000, ticks, 50)",
        "SELECT time_bucket_gapfill(INTERVAL '1 hour', time, 'UTC', '2024-01-01', '2024-01-02')",
        "SELECT locf(AVG(value)), interpolate(AVG(value)) FROM measurements",
        "SELECT locf(value, 0, TRUE), interpolate(value, previous, next)",
        "SELECT first(value, time), last(value, time) FROM measurements",
        "SELECT FIRST(value + 1, time), LAST(value, time + 2)",
        "SELECT create_hypertable('measurements', 'time')",
        "SELECT create_hypertable('measurements', by_range('time', INTERVAL '1 day'))",
        "SELECT add_retention_policy('measurements', INTERVAL '30 days')",
        "SELECT add_compression_policy('measurements', INTERVAL '7 days')",
        "SELECT add_continuous_aggregate_policy('hourly', INTERVAL '1 month', INTERVAL '1 hour', INTERVAL '1 hour')",
        "SELECT remove_retention_policy('measurements'), remove_compression_policy('measurements')",
        "SELECT time_bucket(INTERVAL '5 minutes', time) AS bucket, first(value, time), last(value, time) FROM measurements WHERE value > 0 GROUP BY time_bucket(INTERVAL '5 minutes', time) ORDER BY bucket DESC LIMIT 10"
    ];

    private static readonly string[] OrdinaryQueries =
    [
        "SELECT 1",
        "SELECT a AS x FROM tbl WHERE a > 2 GROUP BY a LIMIT 3",
        "SELECT a FROM tbl ORDER BY a DESC NULLS LAST LIMIT 3 OFFSET 1",
        "SELECT \"a\", E'abc', $$raw$$ FROM \"tbl\"",
        "SELECT E'it\\'s', E'line\\nnext', E'\\xE9'", 
        "SELECT ABS(a), COUNT(*), mystery(a, 2) FROM tbl GROUP BY a",
        "SELECT 2 ^ 3, 5 # 3, ~5",
        "SELECT INTERVAL '1 day', INTERVAL '2' HOUR",
        "SELECT 1; SELECT 2"
    ];

    [Fact]
    public async Task Native_parse_generate_and_reparse_match_live_plugin_and_Postgres_oracles()
    {
        var cases = ExtensionQueries.Select(sql => new { sql, dialect = "timescale" })
            .Concat(OrdinaryQueries.SelectMany(sql => new[] { "timescale", "postgres" }
                .Select(dialect => new { sql, dialect })))
            .SelectMany(c => new[] { false, true }.Select(pretty => new { c.sql, c.dialect, pretty })).ToArray();
        const string script = """
            import sys, json
            sys.path.insert(0, 'python')
            sys.path.insert(0, 'src/tools')
            from oracle_plugins.timescale import Timescale
            from sqlglot.dialects.dialect import Dialect
            from sqlglot.serde import dump
            results = []
            for c in json.load(sys.stdin):
                d = Dialect.get_or_raise(c['dialect'])
                ast = d.parse(c['sql'])
                sql = [None if n is None else d.generate(n, pretty=c['pretty']) for n in ast]
                results.append(dict(ast=[dump(n) for n in ast], sql=sql,
                    reparsed=[None if s is None else [dump(n) for n in d.parse(s)] for s in sql]))
            print(json.dumps(results))
            """;
        var expected = JsonNode.Parse(await Python(script, JsonSerializer.Serialize(cases)))!.AsArray();
        for (var i = 0; i < cases.Length; i++)
        {
            var c = cases[i];
            var dialect = Dialect.get_or_raise(c.dialect);
            var ast = dialect.parse(c.sql);
            var sql = ast.Select(n => n is null ? null : dialect.generator(c.pretty).generate(n)).ToArray();
            var observed = JsonSerializer.SerializeToNode(new
            {
                ast = ast.Select(serde.dump).ToArray(), sql,
                reparsed = sql.Select(s => s is null ? null : dialect.parse(s).Select(serde.dump).ToArray()).ToArray()
            });
            Assert.True(JsonNode.DeepEquals(expected[i], observed),
                $"Case {c}\nPython: {expected[i]}\nC#: {observed}");
            if (c.dialect == "timescale")
                Assert.DoesNotContain(ast.Where(n => n is not null).SelectMany(n => n!.walk()), n => n is exp.DateBin or exp.First or exp.Last);
        }
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
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
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
