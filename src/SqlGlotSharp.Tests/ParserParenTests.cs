using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Parser = SqlGlotSharp.parser.Parser;
using Token = SqlGlotSharp.tokenizer_core.Token;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp.Tests;

public class ParserParenTests
{
    private sealed record Case(string sql, string method = "_parse_paren", string level = "IMMEDIATE",
        int max_nodes = -1, bool unavailable = false);

    private static Parser Start(Case c, List<Token> tokens)
    {
        var p = new Parser(error_level: Enum.Parse<errors.ErrorLevel>(c.level), max_nodes: c.max_nodes);
        p.sql = c.sql;
        p._tokens = tokens; p._tokens_size = tokens.Count; p._index = -1; p._advance();
        return p;
    }
    private static object Snapshot(Token t) => new
    {
        type = t.token_type.ToString(), t.text, t.line, t.col, t.start, t.end, comments = t.comments.ToArray()
    };
    private static exp.Expr? Invoke(Parser p, string method) => method switch
    {
        "_parse_paren" => p._parse_paren(),
        "_parse_primary" => p._parse_primary(),
        "_parse_disjunction" => p._parse_disjunction(),
        "_parse_expression" => p._parse_expression(),
        "_match_r_paren" => Match(p),
        _ => throw new ArgumentException(method)
    };
    private static exp.Expr? Match(Parser p) { p._match_r_paren(); return null; }

    [Fact]
    public async Task Python_cursor_oracle_matches_ordinary_parentheses_and_unavailable_select()
    {
        List<Case> cases = [];
        foreach (var sql in new[]
        {
            "", "1", ")", "()", "(())", "(1)", "(((1)))", "(1,)", "(1,2)", "(1,2,)",
            "((1,2),(3,(4,5)))", "((1,),())", "(1+2*3)", "((1+2)*3)", "(-1,+2,~3)",
            "(a, b.c, \"quoted\")", "('a', NULL, TRUE, FALSE)", "(?, :name, @value)",
            "(1) + 2", "(1,2); next", "(1 AS value)", "(a alias)", "(1 AS (x,y))",
            "(/* open */ 1 /* value */, /* comma */ (2 /* inner */) /* close */) /* outer */ ;",
            "(\n /* open */ (\n 1 + 2\n )\n , 3\n)", "(,)", "(,,)", "(1,,2)", "(1,,)",
            "(1 +)", "(1 +, 2)", "(1", "(", "((1)", "(1;", "(1 AS)"
        })
            foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
                cases.Add(new(sql, level: level));
        foreach (var method in new[] { "_parse_primary", "_parse_disjunction", "_parse_expression" })
            foreach (var sql in new[] { "((1,2),3)", "(1+2)*3 + (4-5)", "(a+b, (c*d))" })
                cases.Add(new(sql, method));
        foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
        {
            foreach (var limit in new[] { 0, 1, 2, 3, 4 })
                cases.Add(new("((1),2)", level: level, max_nodes: limit));
            foreach (var sql in new[] { ") /* close */ next", "", ";" })
                cases.Add(new(sql, "_match_r_paren", level));
        }
        foreach (var sql in new[] { "(SELECT 1)", "((SELECT 1))", "(1, (SELECT 2))" })
            cases.Add(new(sql));

        var prepared = cases.Select(c => tokens.Tokenizer.ForDialect("").tokenize(c.sql)).ToArray();
        // Prepared tokens, not a replacement SQL parsing entrypoint. Both sides
        // receive identical source positions and comments, independent of tokenizer parity.
        var input = cases.Select((c, i) => new { c.sql, c.method, c.level, c.max_nodes, c.unavailable,
            tokens = prepared[i].Select(Snapshot).ToArray() }).ToArray();
        var actual = cases.Select((c, i) =>
        {
            var p = Start(c, prepared[i]); exp.Expr? node = null;
            List<PyDict<string, object?>> failure = []; var unavailable = false;
            try { node = Invoke(p, c.method); }
            catch (errors.ParseError e) { failure = e.errors; }
            catch (NotPortedException e)
            {
                Assert.True(c.unavailable, $"Unexpected unavailable branch in {c.sql}: {e.Message}");
                Assert.Contains("_parse_select", e.Message); unavailable = true;
            }
            return new { ast = serde.dump(node), failure, unavailable,
                errors = p.errors.Select(e => e.errors).ToArray(), index = p._index,
                curr = Snapshot(p._curr), next = Snapshot(p._next), prev = Snapshot(p._prev),
                prev_comments = p._prev_comments.ToArray(),
                token_comments = prepared[i].Select(t => t.comments.ToArray()).ToArray(), node_count = p._node_count };
        }).ToArray();
        const string script = """
            import sys, json
            sys.path.insert(0, 'python')
            from sqlglot.parser import Parser
            from sqlglot.tokens import Token, TokenType
            from sqlglot.errors import ParseError, ErrorLevel
            from sqlglot.serde import dump
            class Unavailable(Exception): pass
            class QueryBoundary(Parser):
                # This is the first SELECT dependency after its token is matched
                # by the actual upstream _parse_select_query algorithm.
                def _parse_hint(self): raise Unavailable()
            def snapshot(t):
                return dict(type=t.token_type.name, text=t.text, line=t.line, col=t.col,
                    start=t.start, end=t.end, comments=t.comments)
            results = []
            for c in json.load(sys.stdin):
                tokens = [Token(TokenType[t['type']], t['text'], line=t['line'], col=t['col'],
                    start=t['start'], end=t['end'], comments=t['comments']) for t in c['tokens']]
                cls = QueryBoundary if c['unavailable'] else Parser
                p = cls(error_level=ErrorLevel[c['level']], max_nodes=c['max_nodes'])
                p.sql = c['sql']
                p._tokens, p._tokens_size, p._index = tokens, len(tokens), -1
                p._advance()
                node, failure, unavailable = None, [], False
                try: node = getattr(p, c['method'])()
                except ParseError as e: failure = e.errors
                except Unavailable: unavailable = True
                results.append(dict(ast=dump(node), failure=failure, unavailable=unavailable,
                    errors=[e.errors for e in p.errors], index=p._index,
                    curr=snapshot(p._curr), next=snapshot(p._next), prev=snapshot(p._prev),
                    prev_comments=p._prev_comments[:], token_comments=[t.comments[:] for t in tokens],
                    node_count=p._node_count))
            print(json.dumps(results))
            """;
        var expected = JsonNode.Parse(await Python(script, JsonSerializer.Serialize(input)))!.AsArray();
        var observed = JsonSerializer.SerializeToNode(actual, serde.JsonOptions)!.AsArray();
        for (var i = 0; i < cases.Count; i++)
            Assert.True(JsonNode.DeepEquals(expected[i], observed[i]),
                $"Case {i}: {JsonSerializer.Serialize(input[i])}\nPython: {expected[i]}\nC#: {observed[i]}");
    }

    [Theory]


    [InlineData("(SUMMARIZE t)", "SUMMARIZE", 2)]
    [InlineData("(DESCRIBE t)", "_parse_describe", 2)]
    public void Other_query_branches_are_explicit(string sql, string dependency, long index)
    {
        var input = tokens.Tokenizer.ForDialect("").tokenize(sql);
        if (dependency == "SUMMARIZE") input[1].token_type = TokenType.SUMMARIZE;
        var p = Start(new(sql), input);
        var e = Assert.Throws<NotPortedException>(() => p._parse_paren());
        Assert.Contains(dependency, e.Message); Assert.Equal(index, p._index);
    }

    [Fact]
    public void Lambda_and_out_of_scope_assignment_are_explicit_not_reinterpreted()
    {
        var p = Start(new("(x) -> x"), tokens.Tokenizer.ForDialect("").tokenize("(x) -> x"));
        Assert.IsType<exp.Lambda>(p._parse_lambda()); // arrow lambdas are ported
        p = Start(new("(a := 1)"), tokens.Tokenizer.ForDialect("").tokenize("(a := 1)"));
        var assignment = Assert.IsType<exp.Paren>(p._parse_paren());
        Assert.Equal("propertyeq", ((exp.Expr)assignment.@this!).key);
    }

    [Fact]
    public async Task Anchor_inventory_retains_zero_hashes_for_incomplete_closures()
    {
        const string script = """
            import sys, json
            from pathlib import Path
            sys.path.insert(0, 'src/tools')
            import port_status
            members = port_status.inventory(Path('python'), json.loads(Path('src/tools/scope.json').read_text()))
            report = port_status.report(members, port_status.scan_anchors(Path('src/SqlGlotSharp')))
            rows = [r for r in report['members'] if any(c['file'] in ('parser.parentheses.cs', 'parser.arguments.cs') for c in r.get('csharp', []))]
            assert len(rows) >= 8, rows
            for r in rows:
                expected = 'ported' if r['anchor'].endswith('::_match_r_paren') or r['anchor'].endswith('::Parser._match_r_paren') else 'stub'
                assert r['status'] == expected, r
                assert len(r['csharp']) == 1, r
            print('verified')
            """;
        Assert.Equal("verified", (await Python(script)).Trim());
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
