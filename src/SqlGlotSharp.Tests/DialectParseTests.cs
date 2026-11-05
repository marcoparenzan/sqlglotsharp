using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dialect = SqlGlotSharp.dialects.dialect.Dialect;

namespace SqlGlotSharp.Tests;

public class DialectParseTests
{
    private sealed record Case(string sql, string? level = null);
    private sealed class DerivedDialect : Dialect;

    [Fact]
    public void Factory_is_fresh_bound_to_instance_and_preserves_parser_defaults()
    {
        var dialect = new Dialect(new PyKwargs { ["version"] = "1.2.3" });
        var parser = dialect.parser();
        Assert.Same(dialect, parser.dialect);
        Assert.NotSame(parser, dialect.parser());
        Assert.Equal(errors.ErrorLevel.IMMEDIATE, parser.error_level);
        Assert.Equal(100, parser.error_message_context);
        Assert.Equal(3, parser.max_errors);
        Assert.Equal(-1, parser.max_nodes);
        Assert.Equal(errors.ErrorLevel.IGNORE, dialect.parser(errors.ErrorLevel.IGNORE).error_level);
        Assert.IsType<exp.Select>(Assert.Single(dialect.parse("SELECT 1")));
        Assert.Equal(2, dialect.parse("SELECT 1; SELECT 2").Count);
        Assert.Throws<errors.ParseError>(() => dialect.parse("SELECT a +"));
        Assert.Single(dialect.parse("SELECT a +", errors.ErrorLevel.IGNORE));
        Assert.IsType<exp.Select>(Assert.Single(dialect.parse("SELECT * FROM t", errors.ErrorLevel.IGNORE)));
    }

    [Fact]
    public void Non_base_factories_are_rejected_before_tokenization_without_fallback()
    {
        foreach (var dialect in new Dialect[] { new DerivedDialect() })
        {
            var factoryError = Assert.Throws<NotPortedException>(() => dialect.parser());
            Assert.Contains("Dialect.parser", factoryError.Anchor);
            Assert.Contains(dialect.GetType().FullName!, factoryError.Anchor);
            Assert.Throws<NotPortedException>(() => dialect.parser(errors.ErrorLevel.IGNORE));
            Assert.Throws<NotPortedException>(() => dialect.parse("SELECT 1"));
            // Even malformed lexical input must reject the unavailable factory first.
            Assert.Throws<NotPortedException>(() => dialect.parse("SELECT '", errors.ErrorLevel.IGNORE));
        }
    }

    [Fact]
    public async Task Base_parse_matches_Python_AST_and_errors()
    {
        var cases = new[] { "SELECT 1", "SELECT a AS x FROM db.t WHERE a > 2 ORDER BY a DESC LIMIT 3 OFFSET 1",
            "SELECT /* comment */ a + 2 AS x", "SELECT 1; SELECT 2", ";SELECT 1;;SELECT 2;", "", ";;",
            "SELECT a +; SELECT 2", "SELECT 1 SELECT 2" }
            .SelectMany(sql => new string?[] { null, "IMMEDIATE", "RAISE", "WARN", "IGNORE" }
                .Select(level => new Case(sql, level))).ToArray();
        var dialect = new Dialect();
        var actual = cases.Select(c =>
        {
            List<exp.Expr?>? nodes = null;
            List<PyDict<string, object?>> failure = []; string? message = null;
            try { nodes = dialect.parse(c.sql, c.level is null ? null : Enum.Parse<errors.ErrorLevel>(c.level)); }
            catch (errors.ParseError e) { failure = e.errors; message = e.ToString(); }
            return new { ast = nodes?.Select(serde.dump).ToArray(), failure, message };
        }).ToArray();
        const string script = """
            import sys, json
            sys.path.insert(0, 'python')
            from sqlglot.dialects.dialect import Dialect
            from sqlglot.errors import ErrorLevel, ParseError
            from sqlglot.serde import dump
            dialect = Dialect()
            results = []
            for c in json.load(sys.stdin):
                nodes, failure, message = None, [], None
                try:
                    nodes = dialect.parse(c['sql'], error_level=None if c['level'] is None else ErrorLevel[c['level']])
                except ParseError as e:
                    failure, message = e.errors, str(e)
                results.append(dict(ast=None if nodes is None else [dump(n) for n in nodes], failure=failure, message=message))
            print(json.dumps(results))
            """;
        var expected = JsonNode.Parse(await Python(script, JsonSerializer.Serialize(cases)))!.AsArray();
        var observed = JsonSerializer.SerializeToNode(actual)!.AsArray();
        Assert.Equal(expected.Count, observed.Count);
        for (var i = 0; i < expected.Count; i++)
            Assert.True(JsonNode.DeepEquals(expected[i], observed[i]),
                $"Case {cases[i]}\nPython: {expected[i]}\nC#: {observed[i]}");
    }

    private static string Source([CallerFilePath] string source = "") => source;
    private static async Task<string> Python(string script, string input)
    {
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
            await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
            process.StandardInput.Close(); await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) { process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await stderr);
        return await stdout;
    }
}
