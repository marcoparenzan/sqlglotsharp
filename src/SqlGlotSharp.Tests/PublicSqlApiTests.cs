using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dialect = SqlGlotSharp.dialects.dialect.Dialect;

namespace SqlGlotSharp.Tests;

/// <summary>Live upstream base/PG and local_timescale plugin oracle; Python is test-only.</summary>
public class PublicSqlApiTests
{
    private sealed record Case(string sql, string? read = null, string? write = null,
        bool identity = true, bool pretty = false, string? level = null, string method = "transpile",
        string? dialect = null);

    [Fact]
    public async Task Public_entrypoints_match_live_SQL_AST_roundtrips_identity_and_errors()
    {
        List<Case> cases = [];
        string[] common =
        [
            "", ";", ";;", "SELECT 1", "SELECT 1; SELECT 2", "SELECT 1;;SELECT 2;", ";SELECT 1",
            "SELECT a AS x FROM t WHERE a > 2 GROUP BY a HAVING COUNT(*) > 1 ORDER BY a LIMIT 3",
            "SELECT CAST('2024-01-02 03:04:05' AS TIMESTAMP), CAST('2024-01-02 03:04:05+00' AS TIMESTAMPTZ)",
            "INSERT INTO t (a, b) VALUES (1, 'x'), (2, 'y')",
            "UPDATE t SET a = 2 WHERE b = 'x'", "DELETE FROM t WHERE a = 2"
        ];
        foreach (var read in new[] { "", "postgres", "timescale" })
        foreach (var sql in common.Concat(read == "" ? [] : new[]
        {
            "SELECT ts::TIMESTAMP, ts::TIMESTAMPTZ FROM t",
            "SELECT payload -> 'a', payload ->> 'b' FROM t",
            "SELECT INTERVAL '1 day', INTERVAL '2' HOUR"
        }))
        {
            cases.Add(new(sql, read, method: "parse"));
            cases.Add(new(sql, read, method: "parse_one"));
            foreach (var pretty in new[] { false, true })
            {
                cases.Add(new(sql, read, pretty: pretty));
                cases.Add(new(sql, read, identity: false, pretty: pretty));
                cases.Add(new(sql, read, write: "", pretty: pretty));
                cases.Add(new(sql, read, write: "postgres", pretty: pretty));
            }
        }
        foreach (var sql in new[]
        {
            "SELECT time_bucket(INTERVAL '5 minutes', time), first(value, time), last(value, time) FROM measurements",
            "SELECT locf(AVG(value)), interpolate(AVG(value)) FROM measurements",
            "SELECT create_hypertable('measurements', 'time')"
        })
        foreach (var pretty in new[] { false, true }) cases.Add(new(sql, "timescale", pretty: pretty));
        foreach (var read in new[] { "", "postgres", "timescale" })
        foreach (var sql in new[] { "SELECT a +; SELECT b +", "SELECT (1", "SELECT 'unterminated" })
        foreach (var level in new[] { "IMMEDIATE", "RAISE", "WARN", "IGNORE" })
        foreach (var method in new[] { "parse", "parse_one", "transpile" })
            cases.Add(new(sql, read, level: level, method: method));
        cases.Add(new("SELECT 2 ^ 3", "postgres", method: "parse", dialect: "timescale"));
        cases.Add(new("SELECT 1", "", method: "parse_one", dialect: "postgres"));

        const string script = """
            import sys, json
            sys.path.insert(0, 'python')
            sys.path.insert(0, 'src/tools')
            from oracle_plugins.timescale import Timescale  # local_timescale, not upstream
            import sqlglot
            from sqlglot.errors import ErrorLevel, ParseError, TokenError
            from sqlglot.serde import dump
            results = []
            for c in json.load(sys.stdin):
                level = None if c['level'] is None else ErrorLevel[c['level']]
                try:
                    if c['method'] == 'transpile':
                        sql = sqlglot.transpile(c['sql'], read=c['read'], write=c['write'],
                            identity=c['identity'], pretty=c['pretty'], error_level=level)
                        target = (c['read'] if c['write'] is None else c['write']) if c['identity'] else c['write']
                        result = dict(sql=sql, reparsed=[[dump(n) for n in sqlglot.parse(s, read=target)] for s in sql])
                    elif c['method'] == 'parse':
                        result = [dump(n) for n in sqlglot.parse(c['sql'], read=c['read'], dialect=c['dialect'], error_level=level)]
                    else:
                        result = dump(sqlglot.parse_one(c['sql'], read=c['read'], dialect=c['dialect'], error_level=level))
                    results.append(dict(result=result))
                except (ParseError, TokenError) as e:
                    results.append(dict(error=type(e).__name__, message=str(e), details=e.errors if isinstance(e, ParseError) else None))
            print(json.dumps(results))
            """;
        var expected = JsonNode.Parse(await Python(script, JsonSerializer.Serialize(cases)))!.AsArray();
        for (var i = 0; i < cases.Count; i++)
        {
            var c = cases[i];
            // JSON path generation and the JSON_EXTRACT(...) re-parse are both ported: full oracle comparison.
            var actual = Observe(c);
            Assert.True(JsonNode.DeepEquals(expected[i], actual),
                $"Case {i}: {c}\nPython: {expected[i]}\nNative: {actual}");
            if (c.method == "transpile" && actual["error"] is null)
                Assert.Equal(SqlGlot.Transpile(c.sql, c.read, c.write, c.identity,
                    c.level is null ? null : errors.ErrorLevelFromValue(c.level), c.pretty),
                    SqlGlot.Transpile(c.sql, c.read, c.write, c.identity,
                    c.level is null ? null : errors.ErrorLevelFromValue(c.level), c.pretty, copy: true));
        }
    }

    private static JsonNode Observe(Case c)
    {
        try
        {
            var level = c.level is null ? (errors.ErrorLevel?)null : errors.ErrorLevelFromValue(c.level);
            object? result;
            if (c.method == "parse") result = SqlGlot.Parse(c.sql, c.read, c.dialect, level).Select(serde.dump).ToArray();
            else if (c.method == "parse_one") result = serde.dump(SqlGlot.ParseOne(c.sql, c.read, c.dialect, errorLevel: level));
            else
            {
                var sql = SqlGlot.Transpile(c.sql, c.read, c.write, c.identity, level, c.pretty);
                var target = c.identity && c.write is null ? c.read : c.write;
                result = new { sql, reparsed = sql.Select(s => SqlGlot.Parse(s, target).Select(serde.dump).ToArray()).ToArray() };
            }
            return JsonSerializer.SerializeToNode(new { result })!;
        }
        catch (Exception e) when (e is errors.ParseError or errors.TokenError)
        {
            return JsonSerializer.SerializeToNode(new { error = e.GetType().Name, message = e.Message,
                details = e is errors.ParseError p ? p.errors : null })!;
        }
    }

    private sealed class DerivedDialect : Dialect;

    [Fact]
    public void Bounded_resolver_aliases_Block_and_no_fallback_contracts()
    {
        Assert.IsType<exp.Block>(SqlGlot.ParseOne("SELECT 1; SELECT 2"));
        var block = Assert.IsType<exp.Block>(SqlGlot.parse_one("SELECT 1;;SELECT 2"));
        Assert.Equal(3, block.expressions.Count);
        Assert.Null(block.expressions[1]);
        Assert.Throws<errors.ParseError>(() => SqlGlot.ParseOne(";SELECT 1"));
        foreach (var read in new object?[] { null, "", "postgres", "timescale",
            dialects.dialect.Dialects.POSTGRES, typeof(dialects.postgres.Postgres),
            new dialects.timescale.Timescale(), new Dialect() })
        {
            Assert.IsType<exp.Select>(Assert.Single(SqlGlot.Parse("SELECT 1", read)));
            Assert.Equal(new[] { "SELECT 1" }, SqlGlot.transpile("SELECT 1", read));
        }
        Assert.Throws<ArgumentException>(() => SqlGlot.Parse("SELECT 1", "not-a-dialect"));
        Assert.Throws<NotPortedException>(() => SqlGlot.Parse("", new DerivedDialect()));
        // Dialects defined outside the library pass the gate; a missing generator factory is reported when a statement needs it.
        Assert.Throws<NotPortedException>(() => SqlGlot.Transpile("SELECT 1", write: new DerivedDialect()));
        Assert.Throws<NotPortedException>(() => SqlGlot.Parse("SELECT 1", "mysql"));
        // parse_into: supported target types parse; a list takes the first that succeeds; failures carry Python's message.
        Assert.IsType<exp.Select>(SqlGlot.ParseOne("SELECT 1", into: typeof(exp.Select)));
        Assert.IsType<exp.Select>(Assert.Single(SqlGlot.ParseInto(typeof(exp.Select), "SELECT 1")));
        Assert.IsType<exp.Select>(Assert.Single(SqlGlot.parse_into(typeof(exp.Select), "SELECT 1")));
        Assert.IsType<exp.Table>(SqlGlot.ParseOne("a.b", into: new List<Type> { typeof(exp.Select), typeof(exp.Table) }));
        var intoError = Assert.Throws<errors.ParseError>(() => SqlGlot.ParseInto(typeof(exp.Table), "3e"));
        Assert.Equal("Failed to parse '3e' into <class 'sqlglot.expressions.query.Table'>", intoError.Message);
        // Entries whose parser is not ported fail explicitly instead of approximating.
        Assert.Throws<errors.ParseError>(() => SqlGlot.ParseInto(typeof(exp.Sort), "x"));
        Assert.Throws<ArgumentException>(() => SqlGlot.ParseInto(typeof(exp.Add), "x"));
        Assert.Throws<ArgumentOutOfRangeException>(() => SqlGlot.Parse("SELECT 1", errorLevel: (errors.ErrorLevel)99));
        foreach (var level in Enum.GetValues<errors.ErrorLevel>())
        foreach (var read in new[] { "", "postgres", "timescale" })
        {
            // Unsupported statements parse to a Command and generate verbatim, like upstream.
            Assert.Equal(["SHOW TABLES"], SqlGlot.Transpile("SHOW TABLES", read, errorLevel: level));
        }
        Assert.Equal("SELECT a FROM (SELECT a FROM t) AS _t WHERE a > 1", Assert.Single(SqlGlot.Transpile("SELECT a FROM t QUALIFY a > 1", "postgres")));
        var ts = SqlGlot.ParseOne("SELECT time_bucket(INTERVAL '1 hour', time), first(value, time)", "timescale");
        Assert.DoesNotContain(ts.walk(), node => node is exp.DateBin or exp.First);
        Assert.Equal(JsonSerializer.Serialize(SqlGlot.Parse("SELECT 1").Select(serde.dump)),
            JsonSerializer.Serialize(SqlGlot.parse("SELECT 1").Select(serde.dump)));
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
            process.StandardInput.Close(); await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, await stderr);
        return await stdout;
    }
}
