using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Parser = SqlGlotSharp.parser.Parser;
using Token = SqlGlotSharp.tokenizer_core.Token;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp.Tests;

public class ParserOperatorTests
{
    private sealed record Case(string method, Token[] tokens, bool parse_mod = true,
        string? dialect = null, int max_nodes = -1, string level = "IMMEDIATE", bool exponent = false,
        string precedence = "HIGH", bool parse_is = true);
    private sealed class OracleParser : Parser
    {
        public bool exponent;
        public parser.IsPredicatePrecedence precedence;
        public override PyDict<TokenType, Type> EXPONENT => exponent
            ? new() { [TokenType.CARET] = typeof(exp.Pow) } : base.EXPONENT;
        public override parser.IsPredicatePrecedence IS_PREDICATE_PRECEDENCE => precedence;
    }
    private static Token[] Ts(params (TokenType type, string text)[] specs) => specs.Select((s, i) =>
        new Token(s.type, s.text, line: 2, col: 3 + i * 3, start: i * 3, end: i * 3 + 1,
            comments: [$"comment {i}"])).ToArray();
    private static Parser Start(Case c)
    {
        var p = new OracleParser { exponent = c.exponent, precedence = Enum.Parse<parser.IsPredicatePrecedence>(c.precedence) };
        p.dialect = parser._resolve_dialect(c.dialect);
        p.max_nodes = c.max_nodes; p.error_level = Enum.Parse<errors.ErrorLevel>(c.level);
        p.sql = "abcdefghijklmnopqrstuvwxyz0123456789";
        p._tokens = c.tokens.ToList(); p._tokens_size = c.tokens.Length; p._index = -1; p._advance();
        return p;
    }
    private static exp.Expr? Invoke(Parser p, Case c) => c.method switch
    {
        "_parse_disjunction" => p._parse_disjunction(), "_parse_conjunction" => p._parse_conjunction(),
        "_parse_equality" => p._parse_equality(c.parse_is), "_parse_comparison" => p._parse_comparison(),
        "_parse_range" => p._parse_range(parse_is: c.parse_is), "_parse_bitwise" => p._parse_bitwise(),
        "_parse_term" => p._parse_term(c.parse_mod), "_parse_factor" => p._parse_factor(c.parse_mod),
        "_parse_factor_operand" => p._parse_factor_operand(), "_parse_exponent" => p._parse_exponent(),
        "_parse_unary" => p._parse_unary(), "_parse_type" => p._parse_type(), "_parse_atom" => p._parse_atom(),
        _ => throw new ArgumentException(c.method)
    };
    private static object Snapshot(Token t) => new
    { type = t.token_type.ToString(), t.text, t.line, t.col, t.start, t.end, comments = t.comments.ToArray() };

    [Fact]
    public async Task Prepared_cursor_oracle_matches_precedence_ast_comments_positions_errors_and_state()
    {
        var n = TokenType.NUMBER;
        List<Case> cases = [];
        foreach (var method in new[] { "_parse_disjunction", "_parse_conjunction", "_parse_equality", "_parse_comparison",
            "_parse_range", "_parse_bitwise", "_parse_term", "_parse_factor", "_parse_factor_operand", "_parse_exponent",
            "_parse_unary", "_parse_type", "_parse_atom" })
            cases.Add(new(method, Ts((n, "1"), (TokenType.SEMICOLON, ";"))));
        foreach (var op in Parser.FACTOR_.Keys.Concat(Parser.TERM_.Keys).Concat(Parser.BITWISE_.Keys)
            .Concat(Parser.EQUALITY_.Keys).Concat(Parser.COMPARISON_.Keys).Concat(Parser.CONJUNCTION_.Keys)
            .Concat(Parser.DISJUNCTION_.Keys).Concat(new[] { TokenType.DPIPE, TokenType.DQMARK }))
            foreach (var dialect in new string?[] { null, "postgres", "tsql" })
                cases.Add(new("_parse_disjunction", Ts((n, "1"), (op, op == TokenType.DIV ? "DIV" : op.ToString()),
                    (n, "2"), (TokenType.SEMICOLON, ";")), dialect: dialect));
        foreach (var op in Parser.UNARY_PARSERS_.Keys)
            cases.Add(new("_parse_disjunction", Ts((op, op.ToString()), (n, "1"), (TokenType.EQ, "="),
                (n, "2"), (TokenType.AND, "AND"), (TokenType.TRUE, "TRUE"))));
        cases.Add(new("_parse_disjunction", Ts((n, "1"), (TokenType.PLUS, "+"), (n, "2"), (TokenType.STAR, "*"),
            (n, "3"), (TokenType.GT, ">"), (n, "4"), (TokenType.EQ, "="), (TokenType.TRUE, "TRUE"),
            (TokenType.OR, "OR"), (TokenType.FALSE, "FALSE"), (TokenType.AND, "AND"), (TokenType.NOT, "NOT"), (TokenType.NULL, "NULL"))));
        cases.Add(new("_parse_disjunction", Ts((n, "10"), (TokenType.DASH, "-"), (n, "3"), (TokenType.DASH, "-"), (n, "2"))));
        cases.Add(new("_parse_disjunction", Ts((n, "1"), (TokenType.NULLSAFE_EQ, "<=>"), (n, "2"),
            (TokenType.EQ, "="), (n, "3"), (TokenType.NULLSAFE_EQ, "<=>"), (n, "4"))));
        cases.Add(new("_parse_disjunction", Ts((TokenType.NOT, "NOT"), (n, "1"), (TokenType.NULLSAFE_EQ, "<=>"), (n, "2"),
            (TokenType.OR, "OR"), (TokenType.TRUE, "TRUE"))));
        foreach (var shift in new[] { TokenType.LT, TokenType.GT })
            cases.Add(new("_parse_disjunction", Ts((n, "1"), (shift, shift.ToString()), (shift, shift.ToString()),
                (n, "2"), (TokenType.PLUS, "+"), (n, "3"))));
        foreach (var method in new[] { "_parse_factor", "_parse_term" })
            foreach (var parseMod in new[] { true, false })
                cases.Add(new(method, Ts((n, "10"), (TokenType.MOD, "%"), (n, "3"), (TokenType.PLUS, "+"), (n, "2")), parse_mod: parseMod));
        cases.Add(new("_parse_factor", Ts((n, "1"), (TokenType.VAR, "AT"), (TokenType.TIME, "TIME"),
            (TokenType.VAR, "ZONE"), (TokenType.STRING, "UTC"), (TokenType.VAR, "AT"), (TokenType.TIME, "TIME"),
            (TokenType.VAR, "ZONE"), (TokenType.STRING, "GMT"))));
        cases.Add(new("_parse_factor", Ts((n, "1"), (TokenType.VAR, "AT"), (TokenType.VAR, "not_time"))));
        cases.Add(new("_parse_disjunction", Ts((n, "2"), (TokenType.CARET, "^"), (n, "3"), (TokenType.CARET, "^"), (n, "4")), exponent: true));
        foreach (var type in new[] { TokenType.ISNULL, TokenType.NOTNULL })
            foreach (var dialect in new string?[] { null, "postgres", "tsql" })
                cases.Add(new("_parse_disjunction", Ts((n, "1"), (type, type.ToString()), (TokenType.EQ, "="),
                    (TokenType.FALSE, "FALSE")), dialect: dialect));
        cases.Add(new("_parse_range", Ts((n, "1"), (TokenType.NOT, "NOT"), (TokenType.NULL, "NULL"))));
        cases.Add(new("_parse_range", Ts((n, "1"), (TokenType.NOT, "NOT"), (n, "2"))));
        cases.Add(new("_parse_equality", Ts((n, "1"), (TokenType.IS, "IS"), (TokenType.NULL, "NULL")), precedence: "LOW", parse_is: false));
        cases.Add(new("_parse_atom", []));
        foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
            foreach (var maxNodes in new[] { 0, 1, 2 })
                cases.Add(new("_parse_disjunction", Ts((TokenType.DASH, "-"), (n, "1"), (TokenType.PLUS, "+"),
                    (n, "2"), (TokenType.EQ, "="), (n, "3")), level: level, max_nodes: maxNodes));
        // Empty operator comments take the expression() fallback; nonempty comments
        // must remain attached to the correct precedence node, not to its RHS.
        var emptyComments = Ts((n, "1"), (TokenType.PLUS, "+"), (n, "2"), (TokenType.STAR, "*"), (n, "3"));
        emptyComments[1].comments.Clear(); emptyComments[3].comments.Clear();
        cases.Add(new("_parse_disjunction", emptyComments));

        var actual = cases.Select(c =>
        {
            var p = Start(c); exp.Expr? node = null; List<PyDict<string, object?>> failure = [];
            try { node = Invoke(p, c); } catch (errors.ParseError e) { failure = e.errors; }
            return new { ast = serde.dump(node), failure, errors = p.errors.Select(e => e.errors).ToArray(),
                index = p._index, curr = Snapshot(p._curr), next = Snapshot(p._next), prev = Snapshot(p._prev),
                prev_comments = p._prev_comments.ToArray(), token_comments = c.tokens.Select(t => t.comments.ToArray()).ToArray(), node_count = p._node_count };
        }).ToArray();
        var input = cases.Select(c => new { c.method, tokens = c.tokens.Select(Snapshot), c.parse_mod, c.dialect,
            c.max_nodes, c.level, c.exponent, c.precedence, c.parse_is }).ToArray();
        const string script = """
            import sys, json
            sys.path.insert(0, 'python')
            from sqlglot import exp
            from sqlglot.parser import Parser, IsPredicatePrecedence
            from sqlglot.tokens import Token, TokenType
            from sqlglot.errors import ErrorLevel, ParseError
            from sqlglot.serde import dump
            def snapshot(t):
                return dict(type=t.token_type.name, text=t.text, line=t.line, col=t.col,
                    start=t.start, end=t.end, comments=t.comments)
            results = []
            for c in json.load(sys.stdin):
                class OracleParser(Parser):
                    EXPONENT = {TokenType.CARET: exp.Pow} if c['exponent'] else {}
                    IS_PREDICATE_PRECEDENCE = IsPredicatePrecedence[c['precedence']]
                tokens = [Token(TokenType[t['type']], t['text'], line=t['line'], col=t['col'],
                    start=t['start'], end=t['end'], comments=t['comments']) for t in c['tokens']]
                p = OracleParser(dialect=c['dialect'], max_nodes=c['max_nodes'], error_level=ErrorLevel[c['level']])
                p.sql = 'abcdefghijklmnopqrstuvwxyz0123456789'
                p._tokens, p._tokens_size, p._index = tokens, len(tokens), -1
                p._advance()
                kwargs = dict(parse_mod=c['parse_mod']) if c['method'] in ('_parse_term', '_parse_factor') else {}
                if c['method'] in ('_parse_equality', '_parse_range'): kwargs = dict(parse_is=c['parse_is'])
                node, failure = None, []
                try: node = getattr(p, c['method'])(**kwargs)
                except ParseError as e: failure = e.errors
                results.append(dict(ast=dump(node), failure=failure, errors=[e.errors for e in p.errors],
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

    [Theory]
    [InlineData(TokenType.L_PAREN, "_parse_paren", 1)]
    [InlineData(TokenType.INTERVAL, "_parse_type", 1)]
    [InlineData(TokenType.INT, "_parse_type", 1)]
    [InlineData(TokenType.SESSION_PARAMETER, "_parse_session_parameter", 1)]

    public void Deep_atom_dependencies_are_explicit(TokenType type, string dependency, long index)
    {
        var p = Start(new("_parse_disjunction", Ts((type, "value"))));
        if (type == TokenType.L_PAREN)
            Assert.Contains("Required keyword: 'this'", Assert.Throws<errors.ParseError>(() => p._parse_disjunction()).Message);
        else if (type is TokenType.INT or TokenType.INTERVAL)
            Assert.IsType<exp.Column>(p._parse_disjunction());
        else
        {
            var e = Assert.Throws<NotPortedException>(() => p._parse_disjunction());
            Assert.Contains(dependency, e.Message);
        }
        Assert.Equal(index, p._index);
    }

    [Fact]
    public void Slow_literal_paths_missing_operands_and_range_callables_do_not_silently_parse()
    {
        foreach (var tail in new[] { TokenType.DOT, TokenType.DCOLON, TokenType.ARROW, TokenType.L_BRACKET })
        {
            var p = Start(new("_parse_disjunction", Ts((TokenType.NUMBER, "1"), (tail, tail.ToString()))));
            if (tail == TokenType.DOT)
                Assert.Contains("Required keyword: 'expression'", Assert.Throws<errors.ParseError>(() => p._parse_disjunction()).Message);
            else if (tail == TokenType.DCOLON)
                Assert.Contains("Expected type", Assert.Throws<errors.ParseError>(() => p._parse_disjunction()).Message);
            else if (tail == TokenType.ARROW)
                Assert.Contains("Required keyword: 'expression'", Assert.Throws<errors.ParseError>(() => p._parse_disjunction()).Message);
            else
                Assert.Contains("Expected ]", Assert.Throws<errors.ParseError>(() => p._parse_disjunction()).Message);
            Assert.True(p._index > 0);
        }
        var adjacent = Start(new("_parse_disjunction", Ts((TokenType.STRING, "a"), (TokenType.STRING, "b"))));
        Assert.IsType<exp.Concat>(adjacent._parse_disjunction()); Assert.Equal(2, adjacent._index);
        foreach (var op in new[] { TokenType.PLUS, TokenType.DIV, TokenType.AND, TokenType.EQ })
        {
            var p = Start(new("_parse_disjunction", Ts((TokenType.NUMBER, "1"), (op, op.ToString()))));
            if (op == TokenType.DIV)
            {
                // Upstream treats alphabetic DIV without an operand as a trailing identifier.
                Assert.IsType<exp.Literal>(p._parse_disjunction());
                Assert.Equal(1, p._index);
            }
            else Assert.Contains("Required keyword: 'expression'", Assert.Throws<errors.ParseError>(() => p._parse_disjunction()).Message);
        }
    }

    [Theory]
    [InlineData(TokenType.VAR)]
    [InlineData(TokenType.IDENTIFIER)]
    [InlineData(TokenType.PLACEHOLDER)]
    public void Newly_supported_atoms_consume_the_token(TokenType type)
    {
        var p = Start(new("_parse_disjunction", Ts((type, "value"))));
        var result = p._parse_disjunction();
        if (type == TokenType.PLACEHOLDER) Assert.IsType<exp.Placeholder>(result);
        else Assert.Equal("value", Assert.IsType<exp.Column>(result).name);
        Assert.Equal(1, p._index);
    }

    [Fact]
    public async Task Incomplete_dependency_closure_has_only_zero_anchors_and_no_invented_tokens_member()
    {
        const string script = """
            import sys, json
            from pathlib import Path
            sys.path.insert(0, 'src/tools')
            sys.path.insert(0, 'python')
            import port_status
            members = port_status.inventory(Path('python'), json.loads(Path('src/tools/scope.json').read_text()))
            report = port_status.report(members, port_status.scan_anchors(Path('src/SqlGlotSharp')))
            rows = [r for r in report['members'] if any(c['file'] == 'parser.operators.cs' for c in r.get('csharp', []))]
            assert {r['anchor'].split('::')[1] for r in rows} == {
                'Parser.COLUMN_OPERATORS', 'Parser.JSON_OPERATORS', 'Parser.UNARY_PARSERS', 'Parser.RANGE_PARSERS',
                'Parser._parse_disjunction', 'Parser._parse_conjunction', 'Parser._build_predicate',
                'Parser._parse_equality', 'Parser._parse_comparison', 'Parser._is_predicate_ahead',
                'Parser._parse_range', 'Parser._negate_range', 'Parser._parse_bitwise', 'Parser._parse_term',
                'Parser._normalize_collate', 'Parser._parse_factor', 'Parser._parse_factor_operand',
                'Parser._parse_exponent', 'Parser._parse_unary', 'Parser._parse_type',
                'Parser._parse_at_time_zone', 'Parser._parse_atom', 'Parser._parse_operator',
                'Parser._parse_comprehension'}, rows
            for r in rows:
                assert r['status'] == 'stub', r
                assert len(r['csharp']) == 1, r
            from sqlglot.parser import Parser
            assert not hasattr(Parser, '_parse_tokens')
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
