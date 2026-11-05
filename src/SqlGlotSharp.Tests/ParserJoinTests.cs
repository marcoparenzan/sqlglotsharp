using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Parser = SqlGlotSharp.parser.Parser;
using Token = SqlGlotSharp.tokenizer_core.Token;
using Postgres = SqlGlotSharp.dialects.postgres.Postgres;

namespace SqlGlotSharp.Tests;

public class ParserJoinTests
{
    private sealed record Case(string sql, string method = "_parse_join", string dialect = "",
        string level = "IMMEDIATE", int max_nodes = -1, bool skip = false, bool bracket = false, bool aliases = false);
    private static object Snapshot(Token t) => new
    { type = t.token_type.ToString(), t.text, t.line, t.col, t.start, t.end, comments = t.comments.ToArray() };

    [Fact]
    public async Task Join_algorithm_matches_live_Python_AST_comments_errors_and_cursor()
    {
        List<Case> cases = [];
        foreach (var dialect in new[] { "", "postgres" })
        foreach (var sql in new[]
        {
            "", "WHERE x", "LEFT x", "INNER x", "CROSS x", "NATURAL x",
            "JOIN u ON TRUE", "INNER JOIN db.u AS u ON t.a=u.a",
            "LEFT JOIN u ON t.a=u.a AND (u.b > 2 OR NOT t.c)",
            "LEFT OUTER JOIN u USING (a, b)", "RIGHT JOIN u USING a,b",
            "RIGHT OUTER JOIN u ON t.a+1=u.a", "FULL JOIN u ON FALSE",
            "FULL OUTER JOIN u USING (\"a\", \"b\")", "CROSS JOIN u",
            "JOIN u", "JOIN u JOIN v ON TRUE", "JOIN u JOIN v ON TRUE ON FALSE",
            "JOIN u JOIN v USING (a) USING (b)", "JOIN u CROSS JOIN v",
            "NATURAL JOIN u", "NATURAL LEFT OUTER JOIN u", ", u", ", db.u AS x, v",
            "JOIN (SELECT a FROM u WHERE a > 1) AS q ON t.a=q.a",
            "LEFT JOIN (SELECT a FROM u) AS q USING (a, b)",
            "RIGHT JOIN (SELECT a FROM u) q USING (\"a\", \"b\")",
            "JOIN (SELECT a FROM u) q USING (a.b)", "JOIN (SELECT a FROM u) q USING a,b",
            "JOIN (SELECT a FROM u) q USING ()", "JOIN (SELECT a FROM u) q USING (a,)",
            "JOIN (SELECT a FROM u) q USING (a", "JOIN (SELECT a FROM u) q USING (a+1)",
            "JOIN u ON", "JOIN u ON a >", "JOIN u USING ()", "JOIN u USING (a,)",
            "JOIN u USING (a", "JOIN u USING (a.b)", "JOIN u USING (a+1)",
            "LEFT /* side */ OUTER /* kind */ JOIN /* join */ db.u /* table */ AS q /* alias */ ON /* on */ t.a /* lhs */ = q.a /* rhs */",
            "JOIN u USING /* using */ (a /* a */, b /* b */) /* end */ WHERE TRUE",
            "JOIN u ON TRUE; next", "JOIN u ON TRUE JOIN v ON FALSE"
        })
        foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
            cases.Add(new(sql, dialect: dialect, level: level));
        foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
        foreach (var n in new[] { 0, 1, 2, 3, 5, 8, 20 })
            cases.Add(new("LEFT JOIN db.u AS x ON t.a=x.a AND x.b>2", level: level, max_nodes: n));
        cases.AddRange([
            new("u ON TRUE", skip: true), new("LEFT u ON TRUE", skip: true),
            new("JOIN u ON TRUE", bracket: true), new("JOIN u AS x ON TRUE", aliases: true),
            new("JOIN u ON TRUE LEFT JOIN v USING (a)", "_parse_joins"),
            new(", u JOIN v ON TRUE, w", "_parse_joins"),
            new("(a, b)", "_parse_using_identifiers"), new("a.b, \"c\"", "_parse_using_identifiers")
        ]);
        await Compare(cases);
    }

    [Fact]
    public async Task Ordinary_Postgres_parse_uses_the_production_path_and_matches_live_Python()
    {
        var sqls = new[]
        {
            "SELECT t.a FROM t JOIN u ON t.a=u.a",
            "SELECT t.a FROM t INNER JOIN db.u AS u ON t.a=u.a WHERE u.b>2 ORDER BY t.a LIMIT 3",
            "SELECT a FROM t LEFT OUTER JOIN u USING (a, b) RIGHT JOIN v ON u.a=v.a",
            "SELECT a FROM t FULL OUTER JOIN u USING (a)", "SELECT a FROM t CROSS JOIN u",
            "SELECT a FROM t, u, v WHERE t.a=u.a AND u.a=v.a",
            "SELECT a FROM t JOIN u JOIN v ON u.a=v.a ON t.a=u.a",
            "SELECT a FROM t NATURAL LEFT JOIN u", "SELECT a FROM t JOIN u",
            "SELECT a FROM t JOIN (SELECT b AS a FROM u) AS q ON t.a=q.a",
            "SELECT a FROM t LEFT JOIN (SELECT a FROM u) AS q USING (a)",
            "SELECT a FROM t FULL OUTER JOIN (SELECT a FROM u) q USING (a)",
            "SELECT a FROM t JOIN (SELECT a FROM u) q USING ()",
            "SELECT a FROM t JOIN (SELECT a FROM u) q USING (a",
            "SELECT /* select */ t.a FROM t LEFT /* side */ JOIN /* join */ u ON /* on */ t.a=u.a /* end */",
            "SELECT a FROM t JOIN u ON", "SELECT a FROM t JOIN u USING ()",
            "SELECT a FROM t JOIN u USING (a", "SELECT a FROM t JOIN u ON a >",
            "SELECT a FROM t, u; SELECT b FROM v RIGHT JOIN w ON TRUE"
        };
        await Compare(sqls.SelectMany(sql => new[] { "IMMEDIATE", "RAISE", "IGNORE" }
            .Select(level => new Case(sql, "parse", "postgres", level))).ToList());
    }

    private static Parser Start(Case c)
    {
        var p = c.dialect == "postgres" ? new Postgres().parser(Enum.Parse<errors.ErrorLevel>(c.level))
            : new Parser(error_level: Enum.Parse<errors.ErrorLevel>(c.level), max_nodes: c.max_nodes);
        p.sql = c.sql; p._tokens = tokens.Tokenizer.ForDialect(c.dialect).tokenize(c.sql);
        p._tokens_size = p._tokens.Count; p._index = -1; p._advance(); return p;
    }

    private static async Task Compare(List<Case> cases)
    {
        var prepared = cases.Select(Start).ToArray();
        var input = cases.Select((c, i) => new { c.sql, c.method, c.dialect, c.level, c.max_nodes,
            c.skip, c.bracket, c.aliases, boundary = false, tokens = prepared[i]._tokens.Select(Snapshot).ToArray() }).ToArray();
        var actual = cases.Select((c, i) =>
        {
            var p = prepared[i]; object? ast = null; List<PyDict<string, object?>> failure = []; var unavailable = false;
            try
            {
                ast = c.method switch
                {
                    "parse" => new Postgres().parse(c.sql, Enum.Parse<errors.ErrorLevel>(c.level))?.Select(serde.dump).ToArray(),
                    "_parse_joins" => p._parse_joins().Select(j => serde.dump(j)).ToArray(),
                    "_parse_using_identifiers" => p._parse_using_identifiers().Select(j => serde.dump(j)).ToArray(),
                    _ => serde.dump(p._parse_join(c.skip, c.bracket, c.aliases ? p.TABLE_ALIAS_TOKENS : null))
                };
            }
            catch (errors.ParseError e) { failure = e.errors; }
            catch (NotPortedException e)
            {
                Assert.True(input[i].boundary, $"Unexpected boundary for {c}: {e.Message}");
                Assert.Contains("slow table", e.Message); unavailable = true;
            }
            return new { ast, failure, unavailable, state = c.method == "parse" ? null : new
            {
                errors = p.errors.Select(e => e.errors).ToArray(), index = p._index,
                curr = Snapshot(p._curr), next = Snapshot(p._next), prev = Snapshot(p._prev),
                prev_comments = p._prev_comments.ToArray(), token_comments = p._tokens.Select(t => t.comments.ToArray()).ToArray(),
                node_count = p._node_count
            } };
        }).ToArray();
        const string script = """
            import sys, json
            sys.path.insert(0, 'python')
            from sqlglot.parser import Parser
            from sqlglot.dialects.postgres import Postgres
            from sqlglot.tokens import Token, TokenType
            from sqlglot.errors import ParseError, ErrorLevel
            from sqlglot.serde import dump
            def snapshot(t):
                return dict(type=t.token_type.name, text=t.text, line=t.line, col=t.col,
                    start=t.start, end=t.end, comments=t.comments)
            class Unavailable(Exception): pass
            results = []
            for c in json.load(sys.stdin):
                cls = Postgres.Parser if c['dialect'] else Parser
                if c['boundary']:
                    class Boundary(cls):
                        def _parse_table_parts(self, *a, **k):
                            if not k.get('fast'): raise Unavailable()
                            return super()._parse_table_parts(*a, **k)
                    cls = Boundary
                p = cls(dialect=Postgres() if c['dialect'] else None, error_level=ErrorLevel[c['level']], max_nodes=c['max_nodes'])
                ts = [Token(TokenType[t['type']], t['text'], line=t['line'], col=t['col'], start=t['start'], end=t['end'], comments=t['comments']) for t in c['tokens']]
                p.sql = c['sql']; p._tokens, p._tokens_size, p._index = ts, len(ts), -1; p._advance()
                ast, failure, unavailable = None, [], False
                try:
                    if c['method'] == 'parse': ast = [dump(n) for n in (p.parse(ts, c['sql']) if c['boundary'] else Postgres().parse(c['sql'], error_level=ErrorLevel[c['level']]))]
                    elif c['method'] == '_parse_joins': ast = [dump(n) for n in p._parse_joins()]
                    elif c['method'] == '_parse_using_identifiers': ast = [dump(n) for n in p._parse_using_identifiers()]
                    else: ast = dump(p._parse_join(skip_join_token=c['skip'], parse_bracket=c['bracket'], alias_tokens=p.TABLE_ALIAS_TOKENS if c['aliases'] else None))
                except ParseError as e: failure = e.errors
                except Unavailable: unavailable = True
                state = None if c['method'] == 'parse' else dict(errors=[e.errors for e in p.errors], index=p._index, curr=snapshot(p._curr), next=snapshot(p._next), prev=snapshot(p._prev), prev_comments=p._prev_comments[:], token_comments=[t.comments[:] for t in ts], node_count=p._node_count)
                results.append(dict(ast=ast, failure=failure, unavailable=unavailable, state=state))
            print(json.dumps(results))
            """;
        var expected = JsonNode.Parse(await Python(script, JsonSerializer.Serialize(input)))!.AsArray();
        var observed = JsonSerializer.SerializeToNode(actual, serde.JsonOptions)!.AsArray();
        for (var i = 0; i < cases.Count; i++)
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
        Assert.True(process.ExitCode == 0, await stderr); return await stdout;
    }
}
