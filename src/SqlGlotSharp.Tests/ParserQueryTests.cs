using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Parser = SqlGlotSharp.parser.Parser;
using Token = SqlGlotSharp.tokenizer_core.Token;

namespace SqlGlotSharp.Tests;

public class ParserQueryTests
{
    private sealed record Case(string sql, string method = "_parse_select", string level = "IMMEDIATE",
        int max_nodes = -1, string boundary = "");
    private static Parser Start(Case c, List<Token> tokens)
    {
        var p = new Parser(error_level: Enum.Parse<errors.ErrorLevel>(c.level), max_nodes: c.max_nodes);
        p.sql = c.sql; p._tokens = tokens; p._tokens_size = tokens.Count; p._index = -1; p._advance();
        return p;
    }
    private static object Snapshot(Token t) => new
    { type = t.token_type.ToString(), t.text, t.line, t.col, t.start, t.end, comments = t.comments.ToArray() };
    private static exp.Expr? Invoke(Parser p, string method) => method switch
    {
        "_parse_select" => p._parse_select(), "_parse_select_query" => p._parse_select_query(),
        "_parse_paren" => p._parse_paren(), "_parse_expression" => p._parse_expression(),
        "_parse_from" => p._parse_from(), "_parse_table" => p._parse_table(),
        "_parse_where" => p._parse_where(), "_parse_order" => p._parse_order(),
        "_parse_limit" => p._parse_limit(), "_parse_offset" => p._parse_offset(),
        "_parse_table_parts_fast" => p._parse_table_parts_fast(),
        _ => throw new ArgumentException(method)
    };

    [Fact]
    public async Task Prepared_cursor_queries_match_upstream_AST_comments_positions_errors_and_cursor()
    {
        List<Case> cases = [];
        foreach (var sql in new[]
        {
            "", "x", ")", "SELECT", "SELECT 1", "SELECT a", "SELECT a, b, t.c",
            "SELECT ALL a FROM t", "SELECT DISTINCT a, b FROM t", "SELECT ALL DISTINCT 1",
            "SELECT a AS x, b y FROM catalog.db.tbl AS t", "SELECT a FROM t alias",
            "SELECT a, b + 2 * c AS result FROM db.t WHERE a + 2 * b > 10 AND NOT c = 0 OR d = 1",
            "SELECT a FROM t WHERE (a+b)*c >= 2 AND b > 1 AND b < 3",
            "SELECT \"a\" FROM \"catalog\".\"db\".\"t\" AS \"alias\" WHERE TRUE",
            "SELECT a FROM t ORDER BY a", "SELECT a FROM t ORDER BY a ASC, b DESC NULLS FIRST, c NULLS LAST",
            "SELECT a FROM t LIMIT 10", "SELECT a FROM t ORDER BY a DESC LIMIT 2+3 OFFSET 1",
            "SELECT a FROM t LIMIT 2, 3", "SELECT a FROM t LIMIT 5 BY a, b",
            "SELECT a FROM t LIMIT 2, 3 BY a OFFSET 7", "SELECT a OFFSET 3 ROWS",
            "SELECT a LIMIT", "SELECT a LIMIT 1", "SELECT a OFFSET 1", "SELECT a LIMIT nope",
            "SELECT a FROM t WHERE TRUE FETCH FIRST 2 ROWS ONLY", "SELECT a FROM t WHERE TRUE FETCH NEXT ROW ONLY",
            "SELECT TOP 2 a FROM t", "SELECT TOP (1+2) DISTINCT a FROM t",
            "SELECT TOP 2 a LIMIT 1", "SELECT TOP 2 a OFFSET 1",
            "SELECT a WHERE a = 1 WHERE b = 2", "SELECT a ORDER BY a ORDER BY b",
            "SELECT a LIMIT 1 LIMIT 2", "SELECT a OFFSET 1 OFFSET 2",
            "SELECT a +", "SELECT ,", "SELECT a,", "SELECT a WHERE", "SELECT a WHERE a >",
            "SELECT a ORDER BY", "SELECT a LIMIT 1 +", "SELECT (a+b), (1,2) FROM t",
            "SELECT (SELECT a FROM t WHERE a > 1 ORDER BY a LIMIT 1) AS value",
            "SELECT a FROM (SELECT b AS a FROM t WHERE b > 0) AS q WHERE a < 4 ORDER BY a",
            "SELECT a FROM ((SELECT b AS a FROM t)) AS q", "SELECT 1; next",
            "SELECT /* select */ a /* col */ AS x /* alias */ FROM /* from */ db.t /* table */ AS q /* ta */ WHERE /* where */ q.a > 1 /* operand */ ORDER BY /* order */ q.a DESC /* desc */ LIMIT /* limit */ 2 /* end */",
            "SELECT\n /* start */ (SELECT\n x /* inner */ FROM t) /* wrapped */ AS y\n FROM u\n WHERE y + 1 > 0"
        })
            foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" }) cases.Add(new(sql, level: level));
        foreach (var method in new[] { "_parse_select_query", "_parse_paren", "_parse_expression" })
            foreach (var sql in method == "_parse_select_query"
                ? new[] { "SELECT a FROM t WHERE a=2 ORDER BY a LIMIT 1", "SELECT (SELECT 2)" }
                : new[] { "(SELECT 1)", "((SELECT a FROM t))", "(1, (SELECT 2))", "(SELECT 1", "((SELECT a WHERE a>))" })
                foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" }) cases.Add(new(sql, method, level));
        foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
            foreach (var n in new[] { 0, 1, 2, 3, 4, 6, 10 })
                cases.Add(new("SELECT (SELECT a FROM t) AS x FROM db.u WHERE x > 2 LIMIT 3", level: level, max_nodes: n));
        foreach (var (method, sql) in new[]
        {
            ("_parse_from", "FROM /* from */ db.t AS x"), ("_parse_table", "a.b.c.d.e AS x"),
            ("_parse_table_parts_fast", "a /* a */ . b /* b */ . c . d"),
            ("_parse_table_parts_fast", "a.foo()"), ("_parse_table_parts_fast", ""),
            ("_parse_where", "WHERE /* where */ a+1 > 3 AND NOT b"),
            ("_parse_order", "ORDER SIBLINGS BY a DESC NULLS LAST, b ASC"),
            ("_parse_limit", "LIMIT 3 PERCENT WITH TIES"), ("_parse_limit", "FETCH FIRST 1 ROW ONLY"),
            ("_parse_offset", "OFFSET 2 ROWS BY a")
        }) cases.Add(new(sql, method));
        await Compare(cases);
    }

    [Fact]
    public async Task Unsupported_query_dependencies_match_injected_upstream_boundaries_without_fallback()
    {
        await Compare([








            new("SELECT a ORDER BY a WITH FILL", boundary: "fill"),

        ]);
    }

    [Theory]
    [InlineData("SELECT a FROM t OFFSET 3 ROWS", "slow table")]
    [InlineData("SELECT a FROM t FETCH FIRST 2 ROWS ONLY", "slow table")]
    public async Task Pagination_uses_the_slow_table_path_and_matches_Python(string sql, string dependency)
    {
        _ = dependency; // Historical theory label; this branch is now ported.
        await Compare([new(sql)]);
    }

    private static async Task Compare(List<Case> cases)
    {
        var prepared = cases.Select(c => tokens.Tokenizer.ForDialect("").tokenize(c.sql)).ToArray();
        var input = cases.Select((c, i) => new { c.sql, c.method, c.level, c.max_nodes, c.boundary,
            tokens = prepared[i].Select(Snapshot).ToArray() }).ToArray();
        var actual = cases.Select((c, i) =>
        {
            var p = Start(c, prepared[i]); exp.Expr? node = null;
            List<PyDict<string, object?>> failure = []; var unavailable = false;
            try { node = Invoke(p, c.method); }
            catch (errors.ParseError e) { failure = e.errors; }
            catch (NotPortedException e)
            {
                Assert.False(c.boundary == "", $"Unexpected unavailable branch: {c.sql}: {e.Message}");
                unavailable = true;
            }
            if (c.boundary != "") Assert.True(unavailable, $"Missing boundary: {c.sql}");
            return new { ast = serde.dump(node), failure, unavailable,
                errors = p.errors.Select(e => e.errors).ToArray(), index = p._index,
                curr = Snapshot(p._curr), next = Snapshot(p._next), prev = Snapshot(p._prev),
                prev_comments = p._prev_comments.ToArray(), token_comments = prepared[i].Select(t => t.comments.ToArray()).ToArray(),
                node_count = p._node_count };
        }).ToArray();
        const string script = """
            import sys, json
            sys.path.insert(0, 'python')
            from sqlglot.parser import Parser
            from sqlglot.tokens import Token, TokenType
            from sqlglot.errors import ParseError, ErrorLevel
            from sqlglot.serde import dump
            class Unavailable(Exception): pass
            def fail(*a, **k): raise Unavailable()
            class Boundary(Parser):
                # Inject only at the first unavailable upstream dependency, retaining
                # the actual upstream query algorithm up to that dependency.
                def _parse_with(self, skip_with_token=False):
                    if self.boundary == 'cte':
                        if skip_with_token or self._match(TokenType.WITH): raise Unavailable()
                        return None
                    return super()._parse_with(skip_with_token)
                def _parse_hint(self):
                    if self.boundary == 'hint':
                        if self._match(TokenType.HINT) and self._prev_comments: raise Unavailable()
                        return None
                    return super()._parse_hint()
                def _parse_into(self):
                    if self.boundary == 'into':
                        if self._match(TokenType.INTO): raise Unavailable()
                        return None
                    return super()._parse_into()
                def _parse_table(self, *a, **k):
                    if self.boundary == 'join' and self._prev.token_type == TokenType.JOIN: raise Unavailable()
                    if self.boundary == 'comma_join' and self._prev.token_type == TokenType.COMMA: raise Unavailable()
                    return super()._parse_table(*a, **k)
                def _parse_table_parts(self, *a, **k):
                    if self.boundary == 'slow_table' and not k.get('fast'): raise Unavailable()
                    return super()._parse_table_parts(*a, **k)
                def _parse_unnest(self, with_alias=True):
                    if self.boundary == 'unnest':
                        if not self._match_pair(TokenType.UNNEST, TokenType.L_PAREN, advance=False): return None
                        self._advance()
                        raise Unavailable()
                    return super()._parse_unnest(with_alias)
                def parse_set_operation(self, *a, **k):
                    if self.boundary == 'setop':
                        start = self._index
                        self._parse_join_parts()
                        if not self._match_set(self.SET_OPERATIONS):
                            self._retreat(start)
                            return None
                        raise Unavailable()
                    return super().parse_set_operation(*a, **k)
                def _parse_group(self, *a, **k):
                    if self.boundary == 'group': raise Unavailable()
                    return super()._parse_group(*a, **k)
                def _parse_value(self, *a, **k):
                    if self.boundary == 'value': raise Unavailable()
                    return super()._parse_value(*a, **k)
                def _parse_star_ops(self, *a, **k):
                    if self.boundary == 'star': raise Unavailable()
                    return super()._parse_star_ops(*a, **k)
                def _parse_function_parameter(self):
                    if self.boundary == 'parameter': raise Unavailable()
                    return super()._parse_function_parameter()
                def _parse_pipe_syntax_query(self, *a, **k):
                    if self.boundary == 'pipe': raise Unavailable()
                    return super()._parse_pipe_syntax_query(*a, **k)
                def expression(self, instance, *a, **k):
                    from sqlglot import exp
                    # WITH FILL's first bitwise operand is conditional; this input
                    # has no FROM/TO/STEP. Its next unavailable dependency is interpolate.
                    return super().expression(instance, *a, **k)
                def _parse_interpolate(self):
                    if self.boundary == 'fill': raise Unavailable()
                    return super()._parse_interpolate()
            def snapshot(t):
                return dict(type=t.token_type.name, text=t.text, line=t.line, col=t.col,
                    start=t.start, end=t.end, comments=t.comments)
            results = []
            for c in json.load(sys.stdin):
                tokens = [Token(TokenType[t['type']], t['text'], line=t['line'], col=t['col'],
                    start=t['start'], end=t['end'], comments=t['comments']) for t in c['tokens']]
                cls = Boundary if c['boundary'] else Parser
                p = cls(error_level=ErrorLevel[c['level']], max_nodes=c['max_nodes'])
                if c['boundary']: p.boundary = c['boundary']
                p.sql = c['sql']; p._tokens, p._tokens_size, p._index = tokens, len(tokens), -1
                p._advance()
                node, failure, unavailable = None, [], False
                try: node = getattr(p, c['method'])()
                except ParseError as e: failure = e.errors
                except Unavailable: unavailable = True
                results.append(dict(ast=dump(node), failure=failure, unavailable=unavailable,
                    errors=[e.errors for e in p.errors], index=p._index, curr=snapshot(p._curr),
                    next=snapshot(p._next), prev=snapshot(p._prev), prev_comments=p._prev_comments[:],
                    token_comments=[t.comments[:] for t in tokens], node_count=p._node_count))
            print(json.dumps(results))
            """;
        var expected = JsonNode.Parse(await Python(script, JsonSerializer.Serialize(input)))!.AsArray();
        var observed = JsonSerializer.SerializeToNode(actual, serde.JsonOptions)!.AsArray();
        for (var i = 0; i < cases.Count; i++)
            Assert.True(JsonNode.DeepEquals(expected[i], observed[i]),
                $"Case {i}: {JsonSerializer.Serialize(input[i])}\nPython: {expected[i]}\nC#: {observed[i]}");
    }

    [Fact]
    public async Task Port_status_inventory_has_unique_query_anchors_and_honest_incomplete_hashes()
    {
        const string script = """
            import sys, json
            from pathlib import Path
            sys.path.insert(0, 'src/tools')
            import port_status
            members = port_status.inventory(Path('python'), json.loads(Path('src/tools/scope.json').read_text()))
            report = port_status.report(members, port_status.scan_anchors(Path('src/SqlGlotSharp')))
            rows = [r for r in report['members'] if any(c['file'] in ('parser.queries.cs', 'parser.ctes.cs', 'parser.values.cs') for c in r.get('csharp', []))]
            assert len(rows) >= 25, rows
            for r in rows:
                assert len(r['csharp']) == 1, r
                # _parse_lateral and _parse_unnest are exact ports; the rest of the file is still partial.
                assert r['status'] == ('ported' if r['anchor'].split('.')[-1] in ('_parse_lateral', '_parse_unnest') else 'stub'), r
            print(json.dumps({r['anchor']:r['hash'] for r in rows if r['anchor'].split('.')[-1] in ('_parse_join_parts', '_parse_table_parts_fast', '_parse_limit_options')}))
            """;
        var output = await Python(script);
        Assert.False(string.IsNullOrWhiteSpace(output));
    }

    private static string Source([CallerFilePath] string source = "") => source;
    private static async Task<string> Python(string script, string? input = null)
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
            if (input is not null) await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
            process.StandardInput.Close(); await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) { process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await stderr); return await stdout;
    }
}
