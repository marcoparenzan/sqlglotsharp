using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dialect = SqlGlotSharp.dialects.dialect.Dialect;
using Postgres = SqlGlotSharp.dialects.postgres.Postgres;
using Token = SqlGlotSharp.tokenizer_core.Token;

namespace SqlGlotSharp.Tests;

public class ParserJsonOperatorTests
{
    private sealed record Case(string sql, string dialect, string mode, string level = "IMMEDIATE", int max_nodes = -1);
    private static object Snapshot(Token t) => new
    { type = t.token_type.ToString(), t.text, t.line, t.col, t.start, t.end, comments = t.comments.ToArray() };

    [Fact]
    public async Task Live_Python_AST_cursor_comments_metadata_precedence_and_errors()
    {
        List<Case> cases = [];
        var paths = new[] { "'x'", "''", "'0'", "'-1'", "'a.b'", "'$.a[0]'", "'[0]'", "'{x,0}'",
            "'a b'", "'é'", "'a''b'", "'a\\b'", "0", "12", "-1", "+2", "1.5", "1e2",
            "9223372036854775808", "key", "t.key", "NULL", "TRUE", "(1 + 2)", "ABS(k)" };
        foreach (var dialect in new[] { "postgres", "" })
            foreach (var op in new[] { "->", "->>", "#>", "#>>" })
                foreach (var path in paths)
                {
                    if (dialect == "" && path == "ABS(k)") continue; // Qualified function transform is outside this closure.
                    var sql = $"a {op} {path}";
                    cases.Add(new(sql, dialect, "cursor"));
                    cases.Add(new($"SELECT {sql}", dialect, "parse"));
                    cases.Add(new($"SELECT {sql}", dialect, "public"));
                }
        var expressions = new[]
        {
            "a -> 'x' -> 0 ->> 'y'", "a ->> 'x' -> 'y'", "a #> '{x,0}' #>> '{y}'",
            "a -> 'x' #>> '{y}' ->> 0", "a + b -> 'x'", "a -> 1 + 2 * 3", "a -> k + 2",
            "a -> 'x' || 'y'", "a || b -> 'x'", "a -> 'x' = b", "a -> 'x' IS NULL",
            "a -> 'x' AND b ->> 'y' = 'z'", "(a -> 'x') ->> 'y'", "a::JSON -> 'x'",
            "a -> 'x'::TEXT", "a -> 'x', b #>> '{y}'", "a -> ?", "a -> $1",
            "/* before */ a /* left */ -> /* op */ 'x' /* path */ ->> 0 /* end */",
            "a -> 'x' + 1 ->> 'y'", "a -> t.key ->> 'x'", "a -> 'x' FROM tbl WHERE b #>> '{y}' = 'z'"
        };
        foreach (var expression in expressions)
            foreach (var mode in new[] { "cursor", "parse", "public" })
                cases.Add(new(mode == "cursor" ? expression : "SELECT " + expression, "postgres", mode));
        foreach (var dialect in new[] { "postgres", "" })
            foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
                foreach (var sql in new[] { "a ->", "a ->>", "a #>", "a #>>", "a -> ;", "a ->> )", "a -> -", "a -> 1 +", "a -> 'x' SELECT 2" })
                    foreach (var mode in new[] { "cursor", "parse", "public" })
                        cases.Add(new(mode == "cursor" ? sql : "SELECT " + sql, dialect, mode, level));
        foreach (var max in new[] { 0, 1, 2, 3, 4, 5, 6 })
            foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
                foreach (var sql in new[] { "a -> 'x'", "a -> k", "a -> 0 ->> 'y'", "a #> '{x}'" })
                    cases.Add(new(sql, "postgres", "cursor", level, max));

        var actual = cases.Select(c =>
        {
            var dialect = Dialect.get_or_raise(c.dialect);
            var p = dialect.parser(Enum.Parse<errors.ErrorLevel>(c.level)); p.max_nodes = c.max_nodes;
            var tokens = dialect.tokenize(c.sql);
            object? ast = null; List<PyDict<string, object?>> failure = []; string? message = null;
            try
            {
                if (c.mode == "public") ast = dialect.parse(c.sql, p.error_level).Select(serde.dump).ToArray();
                else if (c.mode == "parse") ast = p.parse(tokens, c.sql).Select(serde.dump).ToArray();
                else
                {
                    p.sql = c.sql; p._tokens = tokens; p._tokens_size = tokens.Count; p._index = -1; p._advance();
                    ast = serde.dump(p._parse_disjunction());
                }
            }
            catch (errors.ParseError e) { failure = e.errors; message = e.ToString(); }
            catch (FormatException) { message = "ValueError"; } // Existing to_json_path propagates Python int() errors.
            catch (Exception e) { throw new InvalidOperationException($"Case {c}", e); }
            return new { ast, failure, message, state = c.mode == "public" ? null : (object)new
            {
                index = p._index, curr = Snapshot(p._curr), next = Snapshot(p._next), prev = Snapshot(p._prev),
                prev_comments = p._prev_comments.ToArray(), token_comments = tokens.Select(t => t.comments.ToArray()).ToArray(),
                node_count = p._node_count, errors = p.errors.Select(e => e.errors).ToArray()
            } };
        }).ToArray();
        const string script = """
            import sys, json
            sys.path.insert(0, 'python')
            from sqlglot.dialects.dialect import Dialect
            from sqlglot.errors import ErrorLevel, ParseError
            from sqlglot.serde import dump
            def snapshot(t):
                return dict(type=t.token_type.name, text=t.text, line=t.line, col=t.col,
                    start=t.start, end=t.end, comments=t.comments[:])
            results = []
            for c in json.load(sys.stdin):
                dialect = Dialect.get_or_raise(c['dialect'])
                p = dialect.parser(error_level=ErrorLevel[c['level']], max_nodes=c['max_nodes'])
                tokens = dialect.tokenize(c['sql'])
                ast, failure, message = None, [], None
                try:
                    if c['mode'] == 'public':
                        ast = [dump(n) for n in dialect.parse(c['sql'], error_level=ErrorLevel[c['level']])]
                    elif c['mode'] == 'parse': ast = [dump(n) for n in p.parse(tokens, c['sql'])]
                    else:
                        p.sql = c['sql']
                        p._tokens, p._tokens_size, p._index = tokens, len(tokens), -1
                        p._advance()
                        ast = dump(p._parse_disjunction())
                except ParseError as e: failure, message = e.errors, str(e)
                except ValueError: message = 'ValueError'
                state = None if c['mode'] == 'public' else dict(index=p._index,
                    curr=snapshot(p._curr), next=snapshot(p._next), prev=snapshot(p._prev),
                    prev_comments=p._prev_comments[:], token_comments=[t.comments[:] for t in tokens],
                    node_count=p._node_count, errors=[e.errors for e in p.errors])
                results.append(dict(ast=ast, failure=failure, message=message, state=state))
            print(json.dumps(results))
            """;
        var expected = JsonNode.Parse(await Python(script, JsonSerializer.Serialize(cases)))!.AsArray();
        var observed = JsonSerializer.SerializeToNode(actual, serde.JsonOptions)!.AsArray();
        Assert.Equal(expected.Count, observed.Count);
        for (var i = 0; i < cases.Count; i++)
            Assert.True(JsonNode.DeepEquals(expected[i], observed[i]),
                $"Case {i}: {cases[i]}\nPython: {expected[i]}\nC#: {observed[i]}");
    }

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
    private static string Source([CallerFilePath] string source = "") => source;
}
