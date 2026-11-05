using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Parser = SqlGlotSharp.parser.Parser;
using Token = SqlGlotSharp.tokenizer_core.Token;

namespace SqlGlotSharp.Tests;

public class ParserStarCountTests
{
    private sealed record Case(string sql, string method = "parse", string dialect = "",
        string level = "IMMEDIATE", int max_nodes = -1, bool boundary = false);

    private static Parser Start(Case c, List<Token> input)
    {
        var p = parser._resolve_dialect(c.dialect).parser(Enum.Parse<errors.ErrorLevel>(c.level));
        p.max_nodes = c.max_nodes;
        p.sql = c.sql; p._tokens = input; p._tokens_size = input.Count; p._index = -1; p._advance();
        return p;
    }
    private static object Snapshot(Token t) => new
    { type = t.token_type.ToString(), t.text, t.line, t.col, t.start, t.end, comments = t.comments.ToArray() };

    [Fact]
    public async Task Live_python_base_and_Postgres_star_count_AST_errors_comments_and_cursor()
    {
        List<Case> cases = [];
        foreach (var dialect in new[] { "", "postgres" })
        {
            foreach (var sql in new[] {
                "SELECT *", "SELECT t.*", "SELECT * FROM t", "SELECT t.* FROM t",
                "SELECT db.t.*, catalog.db.t.*, a.b.c.d.* FROM t",
                "SELECT COUNT(*)", "SELECT COUNT(a)", "SELECT COUNT(DISTINCT a)",
                "SELECT COUNT(*), COUNT(a), COUNT(DISTINCT a) FROM t",
                "SELECT COUNT(DISTINCT a,b), COUNT(ALL a), COUNT(a,b,c)",
                "SELECT COUNT(DISTINCT a+1, t.b*2) FROM t WHERE a>1",
                "SELECT COUNT((a,b)), COUNT((a+1)), COUNT(ABS(a)), ABS(COUNT(*))",
                "SELECT mystery(COUNT(*),t.*), COUNT(DISTINCT (a,b))",
                "SELECT * FROM (SELECT t.*, COUNT(*) AS n FROM t) q",
                "SELECT COUNT(*) AS n FROM t GROUP BY a HAVING COUNT(DISTINCT a)>1",
                "SELECT * FROM t WHERE a>1 ORDER BY a LIMIT 2; SELECT COUNT(a)",
                "SELECT /* select */ t /* table */ . /* dot */ * /* star */ , COUNT /* name */ ( /* open */ DISTINCT /* distinct */ a /* arg */ ) /* close */ AS n FROM t",
                "SELECT COUNT(*) /* sqlglot.anonymous */", "SELECT \"COUNT\"(a)",
                "SELECT * ILIKE 'a%'", "SELECT t.* ILIKE 'x%' FROM t",
                "SELECT COUNT()", "SELECT COUNT(DISTINCT)", "SELECT COUNT(DISTINCT ,)",
                "SELECT COUNT(DISTINCT a,)", "SELECT COUNT(,)", "SELECT COUNT(,,)",
                "SELECT COUNT(a,,b)", "SELECT COUNT(*) +", "SELECT COUNT(",
                "SELECT COUNT(*", "SELECT COUNT(a", "SELECT COUNT(DISTINCT a",
                "SELECT COUNT(DISTINCT a +)", "SELECT COUNT(a AS)", "SELECT COUNT(a AS x)",
                "SELECT COUNT(*) unexpected unexpected", "SELECT t.* AS", "SELECT t.", "SELECT * +",
                "SELECT COUNT(ALL)", "SELECT COUNT(DISTINCT ALL a)"
            })
                foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
                    cases.Add(new(sql, dialect: dialect, level: level));
            foreach (var method in new[] { "_parse_star", "_parse_primary", "_parse_field", "_parse_expression" })
                foreach (var sql in new[] { "", "*", "* /* star */ ; next", "?", ":name", "@value", "a", "* ILIKE 'x'" }
                    .Concat(method == "_parse_expression" ? [] : new[] { "* ILIKE (a)", "* ILIKE a", "* ILIKE", "* ILIKE 1" }))
                    cases.Add(new(sql, method, dialect));
            foreach (var sql in new[] { "COUNT(*)", "COUNT(a)", "COUNT(DISTINCT a,b)", "COUNT(DISTINCT a+)", "COUNT()", "COUNT(*) /* sqlglot.anonymous */" })
                foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
                    cases.Add(new(sql, "_parse_function", dialect, level));
            foreach (var limit in new[] { 0, 1, 2, 3, 4, 6, 10 })
                foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
                    cases.Add(new("SELECT t.*, COUNT(DISTINCT a,b), COUNT(*) FROM t", dialect: dialect, level: level, max_nodes: limit));
        }
        await Compare(cases);
    }

    [Fact]
    public async Task Star_modifiers_match_Python()
    {
        List<Case> cases = [];
        foreach (var dialect in new[] { "", "postgres" })
            foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
                foreach (var sql in new[] {
                    "* EXCEPT (a)", "* EXCLUDE a", "* REPLACE (a+1 AS a)", "* RENAME a AS b",
                    "* EXCEPT", "* EXCLUDE ()", "* REPLACE (", "* RENAME",
                    "* ILIKE 'a%' EXCEPT (a)", "* COLUMNS('a')", "* COLUMNS(a -> a)",
                    "* /* star */ EXCEPT /* modifier */ (a)", "* EXCEPT (SELECT 1)"
                }) cases.Add(new(sql, "_parse_star", dialect, level));
        // Compare the real upstream cursor at the dependency boundary, not a
        // hand-maintained expected index. Python's match methods are instrumented
        // only to stop where the intentionally unported dependency begins.
        await Compare(cases);
    }

    [Theory]
    [InlineData("* ILIKE (a)")]
    [InlineData("* ILIKE a")]
    [InlineData("* ILIKE")]
    [InlineData("* ILIKE 1")]
    public async Task Nonstring_ILike_retreats_into_the_pattern_parser_and_matches_Python(string sql)
    {
        var cases = new[] { "", "postgres" }.SelectMany(dialect =>
            new[] { "IMMEDIATE", "RAISE", "IGNORE" }.Select(level => new Case(sql, "_parse_expression", dialect, level))).ToList();
        await Compare(cases);
    }

    private static async Task Compare(List<Case> cases)
    {
        var prepared = cases.Select(c => tokens.Tokenizer.ForDialect(c.dialect).tokenize(c.sql)).ToArray();
        var input = cases.Select((c, i) => new { c.sql, c.method, c.dialect, c.level, c.max_nodes, c.boundary,
            tokens = prepared[i].Select(Snapshot).ToArray() }).ToArray();
        var actual = cases.Select((c, i) =>
        {
            var p = Start(c, prepared[i]); object? ast = null; List<PyDict<string, object?>> failure = [];
            var boundary = false;
            try
            {
                if (c.method == "parse") ast = p.parse(prepared[i], c.sql).Select(serde.dump).ToArray();
                else ast = serde.dump(c.method switch {
                    "_parse_star" => p._parse_star(), "_parse_primary" => p._parse_primary(),
                    "_parse_field" => p._parse_field(), "_parse_expression" => p._parse_expression(),
                    "_parse_function" => p._parse_function(), _ => throw new ArgumentException(c.method) });
            }
            catch (errors.ParseError e) { failure = e.errors; }
            catch (NotPortedException e)
            {
                if (!c.boundary) throw new InvalidOperationException($"Fixture {i}: {JsonSerializer.Serialize(c)}", e);
                Assert.Contains(c.sql.Contains("COLUMNS(") ? "COLUMNS" : "_parse_star_op", e.Message);
                boundary = true;
            }
            Assert.Equal(c.boundary, boundary);
            return new { ast, failure, boundary, errors = p.errors.Select(e => e.errors).ToArray(),
                index = p._index, curr = Snapshot(p._curr), next = Snapshot(p._next), prev = Snapshot(p._prev),
                prev_comments = p._prev_comments.ToArray(), token_comments = prepared[i].Select(t => t.comments.ToArray()).ToArray(),
                consumed = prepared[i].Take((int)p._index).Select(Snapshot).ToArray(), node_count = p._node_count };
        }).ToArray();
        const string script = """
            import sys, json
            sys.path.insert(0, 'python')
            from sqlglot.dialects.dialect import Dialect
            from sqlglot.tokens import Token, TokenType
            from sqlglot.errors import ErrorLevel, ParseError
            from sqlglot.serde import dump
            class Boundary(Exception): pass
            def snapshot(t):
                return dict(type=t.token_type.name, text=t.text, line=t.line, col=t.col,
                    start=t.start, end=t.end, comments=t.comments)
            results = []
            for c in json.load(sys.stdin):
                tokens = [Token(TokenType[t['type']], t['text'], line=t['line'], col=t['col'],
                    start=t['start'], end=t['end'], comments=t['comments']) for t in c['tokens']]
                p = Dialect.get_or_raise(c['dialect']).parser(error_level=ErrorLevel[c['level']], max_nodes=c['max_nodes'])
                p.sql = c['sql']; p._tokens, p._tokens_size, p._index = tokens, len(tokens), -1; p._advance()
                if c['boundary']:
                    match_texts, match_seq = p._match_texts, p._match_text_seq
                    def stop_texts(texts, advance=True):
                        matched = match_texts(texts, advance=advance)
                        if matched and tuple(texts) in [('EXCEPT','EXCLUDE'), ('REPLACE',), ('RENAME',)]: raise Boundary()
                        return matched
                    def stop_seq(*texts, advance=True):
                        matched = match_seq(*texts, advance=advance)
                        if matched and texts == ('COLUMNS','('): raise Boundary()
                        return matched
                    # Parser instances are slotted; install on a temporary subclass.
                    p.__class__ = type('Bounded', (p.__class__,), {'__slots__': (),
                        '_match_texts': lambda self, *a, **kw: stop_texts(*a, **kw),
                        '_match_text_seq': lambda self, *a, **kw: stop_seq(*a, **kw)})
                ast, failure, boundary = None, [], False
                try:
                    if c['method'] == 'parse': ast = [dump(n) for n in p.parse(tokens, sql=c['sql'])]
                    else: ast = dump(getattr(p, c['method'])())
                except ParseError as e: failure = e.errors
                except Boundary: boundary = True
                results.append(dict(ast=ast, failure=failure, boundary=boundary, errors=[e.errors for e in p.errors],
                    index=p._index, curr=snapshot(p._curr), next=snapshot(p._next), prev=snapshot(p._prev),
                    prev_comments=p._prev_comments[:], token_comments=[t.comments[:] for t in tokens],
                    consumed=[snapshot(t) for t in tokens[:p._index]], node_count=p._node_count))
            print(json.dumps(results))
            """;
        var expected = JsonNode.Parse(await Python(script, JsonSerializer.Serialize(input)))!.AsArray();
        var observed = JsonSerializer.SerializeToNode(actual, serde.JsonOptions)!.AsArray();
        Assert.Equal(cases.Count, expected.Count);
        for (var i = 0; i < cases.Count; i++)
            Assert.True(JsonNode.DeepEquals(expected[i], observed[i]),
                $"Case {i}: {JsonSerializer.Serialize(input[i])}\nPython: {expected[i]}\nC#: {observed[i]}");
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
        Assert.True(process.ExitCode == 0, await stderr); return await stdout;
    }
}
