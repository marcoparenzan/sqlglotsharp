using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Parser = SqlGlotSharp.parser.Parser;
using Token = SqlGlotSharp.tokenizer_core.Token;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp.Tests;

public class ParserIntervalTests
{
    private sealed record Case(string sql, string dialect = "", string method = "_parse_interval",
        bool require_interval = true, bool parse_function_unit = true, string level = "IMMEDIATE",
        int max_nodes = -1, string? span_value = null, Token[]? raw = null);

    [Fact]
    public async Task Live_base_and_Postgres_interval_cursor_AST_errors_and_comments()
    {
        List<Case> cases = [];
        foreach (var dialect in new[] { "", "postgres" })
        {
            foreach (var sql in new[] {
                "", "ordinary", ";", "INTERVAL", "INTERVAL ;", "INTERVAL '1 hour'",
                "INTERVAL '1' DAY", "INTERVAL 1 DAY", "INTERVAL -1 DAY", "INTERVAL 1.50 SECOND",
                "INTERVAL -1.50 SECOND", "INTERVAL 1e2 SECOND", "INTERVAL '1.50 seconds'",
                "INTERVAL '1 day 2 hours'", "INTERVAL 'garbage'", "INTERVAL '1 day' DAY",
                "INTERVAL '1 day' AND (x)", "INTERVAL '1 day' + x", "INTERVAL '1 day' - x",
                "INTERVAL '1' DAY TO SECOND", "INTERVAL '1-2' YEAR TO MONTH",
                "INTERVAL '1' DAY TO", "INTERVAL '1' TO SECOND", "INTERVAL '1' DAY TO SECOND(2)",
                "INTERVAL '1' DAY + '2' HOUR", "INTERVAL '1' DAY '2' HOUR '3' MINUTE",
                "INTERVAL '1 day' + INTERVAL '2 hours'", "INTERVAL '1' DAY + 2 HOUR",
                "INTERVAL '1' DAY +", "INTERVAL '1' DAY + 2", "INTERVAL 1 + 2 DAY",
                "INTERVAL x", "INTERVAL x;", "INTERVAL x DAY", "INTERVAL x nope",
                "INTERVAL t.x nope", "INTERVAL \"x\" nope", "INTERVAL (1+2) DAY",
                "INTERVAL '1' foo()", "INTERVAL '1' foo(2)", "INTERVAL '1' DAY TO foo(2)",
                "INTERVAL '1' YEAR TO MONTH", "INTERVAL '1 02:03:04.5'",
                "INTERVAL '1 02:03:04.5' DAY", "INTERVAL '1 02:03:04.5' DAY TO SECOND",
                "INTERVAL '- 1 -02:03' AS x", "INTERVAL '1 :' + '2' DAY",
                "INTERVAL /* interval */ '1.2 hour' /* value */ + /* plus */ '2' /* two */ DAY /* unit */;",
                "INTERVAL /* interval */ '1' /* value */ DAY /* first */ TO /* to */ SECOND /* second */;"
            })
                foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
                    cases.Add(new(sql, dialect, level: level));
            foreach (var flag in new[] { false, true })
                foreach (var sql in new[] { "'1' DAY", "1 DAY", "x DAY", "'1' foo(2)", "INTERVAL '1' DAY TO foo(2)" })
                    cases.Add(new(sql, dialect, require_interval: false, parse_function_unit: flag));
            foreach (var max in new[] { 0, 1, 2, 3, 4, 6 })
                foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
                    cases.Add(new("INTERVAL '1.2 hours' + '3' DAY TO SECOND", dialect, level: level, max_nodes: max));
            foreach (var value in new[] { "1 hour", "1.20 seconds", "1 day 2 hours", "1 02:03", "junk", "1.2hour trailing", "١ 02:03", "𝟙 𝟘𝟚:𝟘𝟛", "1\u001chour", "1\u001c02:03" })
                foreach (var tail in new[] { "", "DAY", "DAY TO SECOND", "+ x", "foo(2)" })
                    cases.Add(new(tail, dialect, method: "_parse_interval_span", span_value: value));
            foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
                cases.Add(new("bad numeric", dialect, level: level, raw: [
                    new Token(TokenType.INTERVAL, "INTERVAL"), new Token(TokenType.NUMBER, "bad"),
                    new Token(TokenType.VAR, "DAY")]));
        }
        await Compare(cases);
    }

    [Fact]
    public async Task Ordinary_Postgres_queries_and_public_parse_match_live_Python()
    {
        List<Case> cases = [];
        foreach (var sql in new[] {
            "SELECT INTERVAL '1 hour'", "SELECT INTERVAL '1' DAY AS duration",
            "SELECT INTERVAL '1' DAY TO SECOND", "SELECT INTERVAL '1-2' YEAR TO MONTH",
            "SELECT INTERVAL '1 02:03:04.5' AS duration",
            "SELECT INTERVAL '1' DAY + '2' HOUR + '3' MINUTE",
            "SELECT INTERVAL '1 day' + INTERVAL '2 hours'",
            "SELECT created_at + INTERVAL '1 hour' AS expires FROM events WHERE age > INTERVAL '1' DAY",
            "SELECT INTERVAL '1 day' AND (x) FROM t", "SELECT INTERVAL '1 day' + x FROM t",
            "SELECT /* select */ INTERVAL /* interval */ '1.5 hour' /* value */ AS d /* alias */;",
            "SELECT INTERVAL '1' DAY; SELECT INTERVAL '2 hours'",
            "SELECT INTERVAL '1' DAY TO", "SELECT INTERVAL '1' DAY +"
        })
            foreach (var method in new[] { "_parse_select", "parse" })
                foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
                    cases.Add(new(sql, "postgres", method, level: level));
        await Compare(cases);
    }

    [Fact]
    public void Bare_interval_retreats_then_exposes_unported_type_literal_fallback()
    {
        var p = parser._resolve_dialect("postgres").parser();
        p.sql = "INTERVAL"; p._tokens = tokens.Tokenizer.ForDialect("postgres").tokenize(p.sql);
        p._tokens_size = p._tokens.Count; p._index = -1; p._advance();
        Assert.Null(p._parse_interval());
        Assert.Equal(0, p._index);
        Assert.IsType<exp.Column>(p._parse_disjunction());
    }

    [Fact]
    public void External_corpus_is_accessible_read_only_and_not_copied_into_workspace()
    {
        const string corpus = @"D:\clones\corpus";
        var workspace = Root();
        Assert.False(Path.GetFullPath(corpus).StartsWith(workspace, StringComparison.OrdinalIgnoreCase));
        var file = Path.Combine(corpus, "L0.base.jsonl");
        using var stream = File.OpenRead(file);
        Assert.True(stream.Length > 0);
        Assert.NotEqual(-1, stream.ReadByte());
        Assert.Empty(Directory.EnumerateFiles(workspace, "L0.base.jsonl", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateDirectories(workspace, "corpus", SearchOption.AllDirectories));
    }

    private static object Snapshot(Token t) => new
    { type = t.token_type.ToString(), t.text, t.line, t.col, t.start, t.end, comments = t.comments.ToArray() };

    [Fact]
    public async Task Case_interval_values_match_Python_after_conditional_port()
    {
        await Compare([new("INTERVAL CASE WHEN x THEN 1 END DAY"), new("INTERVAL CASE WHEN x THEN 1 END DAY", dialect: "postgres")]);
    }

    private static async Task Compare(List<Case> cases)
    {
        var prepared = cases.Select(c => c.raw?.ToList() ?? tokens.Tokenizer.ForDialect(c.dialect).tokenize(c.sql)).ToArray();
        var input = cases.Select((c, i) => new { c.sql, c.dialect, c.method, c.require_interval,
            c.parse_function_unit, c.level, c.max_nodes, c.span_value, tokens = prepared[i].Select(Snapshot).ToArray() }).ToArray();
        var actual = cases.Select((c, i) =>
        {
            var p = parser._resolve_dialect(c.dialect).parser(error_level: Enum.Parse<errors.ErrorLevel>(c.level));
            p.max_nodes = c.max_nodes;
            p.sql = c.sql; p._tokens = prepared[i]; p._tokens_size = p._tokens.Count; p._index = -1; p._advance();
            object? ast = serde.dump(null); List<PyDict<string, object?>> failure = [];
            try
            {
                if (c.method == "parse") ast = p.parse(prepared[i], c.sql).Select(serde.dump).ToArray();
                else ast = serde.dump(c.method switch {
                    "_parse_interval" => p._parse_interval(c.require_interval, c.parse_function_unit),
                    "_parse_interval_span" => p._parse_interval_span(exp.Literal.@string(c.span_value), c.parse_function_unit),
                    "_parse_select" => p._parse_select(),
                    _ => throw new ArgumentException(c.method)
                });
            }
            catch (errors.ParseError e) { failure = e.errors; }
            return new { ast, failure, errors = p.errors.Select(e => e.errors).ToArray(), index = p._index,
                curr = Snapshot(p._curr), next = Snapshot(p._next), prev = Snapshot(p._prev),
                prev_comments = p._prev_comments.ToArray(), token_comments = prepared[i].Select(t => t.comments.ToArray()).ToArray(),
                node_count = p._node_count };
        }).ToArray();
        const string script = """
            import sys, json
            sys.path.insert(0, 'python')
            from sqlglot import exp
            from sqlglot.dialects import Dialect
            from sqlglot.tokens import Token, TokenType
            from sqlglot.errors import ParseError, ErrorLevel
            from sqlglot.serde import dump
            def snapshot(t):
                return dict(type=t.token_type.name, text=t.text, line=t.line, col=t.col,
                    start=t.start, end=t.end, comments=t.comments)
            results = []
            for c in json.load(sys.stdin):
                tokens = [Token(TokenType[t['type']], t['text'], line=t['line'], col=t['col'],
                    start=t['start'], end=t['end'], comments=t['comments']) for t in c['tokens']]
                p = Dialect.get_or_raise(c['dialect']).parser(error_level=ErrorLevel[c['level']], max_nodes=c['max_nodes'])
                p.sql = c['sql']; p._tokens, p._tokens_size, p._index = tokens, len(tokens), -1
                p._advance()
                ast, failure = dump(None), []
                try:
                    if c['method'] == 'parse': ast = [dump(e) for e in p.parse(tokens, c['sql'])]
                    elif c['method'] == '_parse_interval':
                        ast = dump(p._parse_interval(c['require_interval'], c['parse_function_unit']))
                    elif c['method'] == '_parse_interval_span':
                        ast = dump(p._parse_interval_span(exp.Literal.string(c['span_value']), c['parse_function_unit']))
                    else: ast = dump(getattr(p, c['method'])())
                except ParseError as e: failure = e.errors
                results.append(dict(ast=ast, failure=failure, errors=[e.errors for e in p.errors],
                    index=p._index, curr=snapshot(p._curr), next=snapshot(p._next), prev=snapshot(p._prev),
                    prev_comments=p._prev_comments[:], token_comments=[t.comments[:] for t in tokens], node_count=p._node_count))
            print(json.dumps(results))
            """;
        var expected = JsonNode.Parse(await Python(script, JsonSerializer.Serialize(input)))!.AsArray();
        var observed = JsonSerializer.SerializeToNode(actual, serde.JsonOptions)!.AsArray();
        for (var i = 0; i < cases.Count; i++)
            Assert.True(JsonNode.DeepEquals(expected[i], observed[i]),
                $"Case {i}: {JsonSerializer.Serialize(input[i])}\nPython: {expected[i]}\nC#: {observed[i]}");
    }

    private static string Source([CallerFilePath] string source = "") => source;
    private static string Root() => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Source())!, "..", ".."));
    private static async Task<string> Python(string script, string input)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SQLGLOT_PYTHON") ?? "python") {
            WorkingDirectory = Root(), UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
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
        Assert.True(process.ExitCode == 0, await stderr); return await stdout;
    }
}
