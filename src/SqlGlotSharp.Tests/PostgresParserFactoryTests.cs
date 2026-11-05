using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dialect = SqlGlotSharp.dialects.dialect.Dialect;
using Postgres = SqlGlotSharp.dialects.postgres.Postgres;

namespace SqlGlotSharp.Tests;

public class PostgresParserFactoryTests
{
    private sealed class DerivedPostgres : Postgres;

    [Fact]
    public void Factory_binds_fresh_generated_parser_and_rejects_subclasses()
    {
        var dialect = Dialect.get_or_raise("postgres");
        var parser = Assert.IsType<parsers.postgres.PostgresParser>(dialect.parser());
        Assert.Same(dialect, parser.dialect);
        Assert.NotSame(parser, dialect.parser());
        Assert.Equal(errors.ErrorLevel.IMMEDIATE, parser.error_level);
        Assert.Equal(100, parser.error_message_context);
        Assert.Equal(3, parser.max_errors);
        Assert.Equal(-1, parser.max_nodes);
        Assert.Equal(errors.ErrorLevel.IGNORE, dialect.parser(errors.ErrorLevel.IGNORE).error_level);
        Assert.Throws<NotPortedException>(() => new DerivedPostgres().parser());
        Assert.Throws<NotPortedException>(() => new DerivedPostgres().parse("SELECT '"));
    }

    [Fact]
    public async Task Tested_Postgres_paths_match_Python_AST_and_errors()
    {
        var queries = new[] { "SELECT 1", "SELECT a AS x FROM db.tbl WHERE a > 2 ORDER BY a DESC LIMIT 3 OFFSET 1",
            "SELECT \"a\" AS \"X\" FROM \"tbl\" WHERE \"a\" <> 2 ORDER BY \"a\"",
            "SELECT 'a' 'b', E'abc', $$raw$$", "SELECT /* comment */ a + 2 AS x",
            "SELECT ?, $1, :name", "SELECT 2 ^ 3 ^ 2, 5 # 3, ~5, |/9, ||/27",
            "SELECT a & 3, a | 2, a << 1, a >> 1, a || 'x'", "SELECT ABS(a), mystery(a, 2)",
            "SELECT a FROM tbl WHERE a BETWEEN 1 AND 3 AND a IS NOT NULL ORDER BY a NULLS FIRST",
            "SELECT a FROM tbl WHERE a IN (1, 2) ORDER BY a DESC NULLS LAST", "SELECT 1; SELECT 2",
            "SELECT a ~ 'x'", "SELECT a ~* 'x'", "SELECT a !~ 'x'", "SELECT 1 OPERATOR(+) 2", "SELECT a @> b", "SELECT a <@ b",
            "SELECT a -|- b", "SELECT a &< b", "SELECT a ?| b", "SELECT a ?& b", "SELECT a #- b",
            "END", "END WORK AND NO CHAIN", "SELECT %s", "SELECT %(name)s", "SELECT LENGTH(a)", "SELECT LENGTH(a, 'utf8')", "SELECT DATE_PART('day', a)",
            "SELECT JSONB_EXISTS(a, 'x')", "SELECT a ? 'x'", "SELECT a && b", "SELECT a @@ b",
            "SELECT JSON_EXTRACT_PATH(a, 'x')", "SELECT JSON_EXTRACT_PATH_TEXT(a, 'x', 1)", "SELECT JSON_EXTRACT_PATH(a, k)",
            "SELECT GENERATE_SERIES(1, 5)", "SELECT GENERATE_SERIES(d1, d2, '1 day')",
            "SELECT MAKE_INTERVAL(1, hours => 2, mins => 30)", "SELECT REGEXP_REPLACE('aaa', 'a', 'b')",
            "SELECT TO_TIMESTAMP(col)", "SELECT TO_TIMESTAMP(a, 'YYYY')", "SELECT TO_CHAR(a, 'YYYY')", "SELECT TO_DATE(a, 'YYYY')",
            "SELECT UNNEST(c) FROM t", "SELECT WIDTH_BUCKET(10, 5, 15, 25)", "SELECT WIDTH_BUCKET(a, b)",
            "SELECT BIT_AND(a)", "SELECT SHA256(a)", "SELECT LEVENSHTEIN_LESS_EQUAL(a, b, 1)", "SELECT UUID()", "SELECT UUID(a)",
            "SELECT DIV(a, b)", "SELECT JSON_AGG(DISTINCT c1 ORDER BY c1) FROM t", "SELECT f(VARIADIC a)",
            "", ";;", "SELECT a +", "SELECT 1 SELECT 2" };
        var cases = queries.SelectMany(sql => new[] { "IMMEDIATE", "RAISE", "IGNORE" }
            .Select(level => new { sql, level })).ToArray();
        var actual = cases.Select(c =>
        {
            List<exp.Expr?>? nodes = null;
            List<PyDict<string, object?>> failure = []; string? message = null;
            try { nodes = new Postgres().parse(c.sql, Enum.Parse<errors.ErrorLevel>(c.level)); }
            catch (errors.ParseError e) { failure = e.errors; message = e.ToString(); }
            return new { ast = nodes?.Select(serde.dump).ToArray(), failure, message };
        }).ToArray();
        const string script = """
            import sys, json
            sys.path.insert(0, 'python')
            from sqlglot.dialects.postgres import Postgres
            from sqlglot.errors import ErrorLevel, ParseError
            from sqlglot.serde import dump
            results = []
            for c in json.load(sys.stdin):
                nodes, failure, message = None, [], None
                try:
                    nodes = Postgres().parse(c['sql'], error_level=ErrorLevel[c['level']])
                except ParseError as e:
                    failure, message = e.errors, str(e)
                results.append(dict(ast=None if nodes is None else [dump(n) for n in nodes], failure=failure, message=message))
            print(json.dumps(results))
            """;
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SQLGLOT_PYTHON") ?? "python")
        {
            WorkingDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Source())!, "..", "..")),
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-B"); start.ArgumentList.Add("-c"); start.ArgumentList.Add(script);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(cases).AsMemory(), timeout.Token);
            process.StandardInput.Close(); await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) { process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await stderr);
        var expected = JsonNode.Parse(await stdout)!.AsArray();
        var observed = JsonSerializer.SerializeToNode(actual)!.AsArray();
        Assert.Equal(expected.Count, observed.Count);
        for (var i = 0; i < expected.Count; i++)
            Assert.True(JsonNode.DeepEquals(expected[i], observed[i]),
                $"Case {cases[i]}\nPython: {expected[i]}\nC#: {observed[i]}");
    }

    private static string Source([CallerFilePath] string source = "") => source;
}
