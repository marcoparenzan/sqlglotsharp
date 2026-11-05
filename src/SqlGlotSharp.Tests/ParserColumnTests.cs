using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Parser = SqlGlotSharp.parser.Parser;
using Token = SqlGlotSharp.tokenizer_core.Token;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp.Tests;

public class ParserColumnTests
{
    private sealed record Case(string method, Token[] tokens, bool any_token = false,
        bool fallback = false, string level = "IMMEDIATE", int max_nodes = -1,
        string? dialect = null, TokenType[]? allowed = null);
    private static Token[] Ts(params (TokenType type, string text)[] specs) => specs.Select((s, i) =>
        new Token(s.type, s.text, line: 2, col: 3 + i * 3, start: i * 3, end: i * 3 + 1,
            comments: [$"comment {i}"])).ToArray();
    private static Parser Start(Case c)
    {
        var p = new Parser { dialect = parser._resolve_dialect(c.dialect), max_nodes = c.max_nodes,
            error_level = Enum.Parse<errors.ErrorLevel>(c.level), sql = "abcdefghijklmnopqrstuvwxyz0123456789" };
        p._tokens = c.tokens.ToList(); p._tokens_size = c.tokens.Length; p._index = -1; p._advance();
        return p;
    }
    private static exp.Expr? Invoke(Parser p, Case c) => c.method switch
    {
        "_parse_id_var" => p._parse_id_var(c.any_token, c.allowed),
        "_parse_field" => p._parse_field(c.any_token, c.allowed),
        "_parse_column_reference" => p._parse_column_reference(),
        "_parse_column_parts_fast" => p._parse_column_parts_fast(),
        "_parse_column" => p._parse_column(), "_parse_atom" => p._parse_atom(),
        "_parse_type" => p._parse_type(fallback_to_identifier: c.fallback),
        "_parse_disjunction" => p._parse_disjunction(),
        "_parse_interval" => p._parse_interval(),
        "_parse_types" => p._parse_types(allow_identifiers: false),
        "_parse_function" => p._parse_function(),
        "_parse_bracket" => p._parse_bracket(),
        _ => throw new ArgumentException(c.method)
    };
    private static object Snapshot(Token t) => new
    { type = t.token_type.ToString(), t.text, t.line, t.col, t.start, t.end, comments = t.comments.ToArray() };

    [Fact]
    public async Task Python_cursor_oracle_identifiers_dots_operators_comments_positions_errors()
    {
        List<Case> cases = [];
        foreach (var method in new[] { "_parse_id_var", "_parse_field", "_parse_column_reference",
            "_parse_column_parts_fast", "_parse_column", "_parse_atom", "_parse_type", "_parse_disjunction" })
        {
            foreach (var type in new[] { TokenType.VAR, TokenType.IDENTIFIER })
                foreach (var text in new[] { "a", "a b", "a.b", "MixedName", "é\U00010400" })
                    cases.Add(new(method, Ts((type, text), (TokenType.SEMICOLON, ";"))));
            cases.Add(new(method, []));
        }
        foreach (var method in new[] { "_parse_column_parts_fast", "_parse_column", "_parse_atom", "_parse_type", "_parse_disjunction" })
            for (var n = 1; n <= 7; n++)
            {
                List<(TokenType, string)> tokens = [];
                for (var i = 0; i < n; i++)
                {
                    if (i != 0) tokens.Add((TokenType.DOT, "."));
                    tokens.Add((i % 2 == 0 ? TokenType.VAR : TokenType.IDENTIFIER, $"part{i}"));
                }
                tokens.Add((TokenType.SEMICOLON, ";"));
                cases.Add(new(method, Ts(tokens.ToArray())));
            }
        foreach (var method in new[] { "_parse_id_var", "_parse_field" })
            foreach (var any in new[] { false, true })
                foreach (var type in new[] { TokenType.VAR, TokenType.STRING, TokenType.TIME, TokenType.SELECT,
                    TokenType.PLACEHOLDER, TokenType.PARAMETER, TokenType.SEMICOLON })
                    cases.Add(new(method, Ts((type, "x")), any_token: any));
        foreach (var method in new[] { "_parse_interval", "_parse_types", "_parse_function", "_parse_bracket" })
        {
            cases.Add(new(method, []));
            cases.Add(new(method, Ts((TokenType.VAR, "ordinary"))));
            cases.Add(new(method, Ts((TokenType.SEMICOLON, ";"))));
        }
        foreach (var method in new[] { "_parse_column", "_parse_disjunction" })
        {
            cases.Add(new(method, Ts((TokenType.STRING, "a"), (TokenType.STRING, "b"))));
            cases.Add(new(method, Ts((TokenType.NUMBER, "1"), (TokenType.DOT, "."), (TokenType.IDENTIFIER, "field"))));
            cases.Add(new(method, Ts((TokenType.NUMBER, "1"), (TokenType.DOT, "."))));
        }
        cases.Add(new("_parse_id_var", Ts((TokenType.SELECT, "SELECT")), allowed: [TokenType.SELECT]));
        cases.Add(new("_parse_id_var", Ts((TokenType.VAR, "x")), allowed: []));
        cases.Add(new("_parse_type", Ts((TokenType.STRING, "x")), fallback: true));
        cases.Add(new("_parse_type", Ts((TokenType.IDENTIFIER, "Case")), fallback: true));
        cases.Add(new("_parse_type", Ts((TokenType.PLACEHOLDER, "?"))));
        foreach (var type in new[] { TokenType.TIME, TokenType.VALUES, TokenType.TRUE, TokenType.NULL })
            cases.Add(new("_parse_column", Ts((TokenType.VAR, "t"), (TokenType.DOT, "."), (type, type.ToString()))));
        foreach (var tail in new[] { TokenType.DOT, TokenType.DCOLON, TokenType.ARROW, TokenType.L_BRACKET, TokenType.L_PAREN })
            cases.Add(new("_parse_column_parts_fast", Ts((TokenType.VAR, "a"), (tail, tail.ToString()))));
        cases.Add(new("_parse_column_parts_fast", Ts((TokenType.VAR, "CASE"))));
        cases.Add(new("_parse_column_parts_fast", Ts((TokenType.IDENTIFIER, "CASE"))));
        cases.Add(new("_parse_column", Ts((TokenType.IDENTIFIER, "CASE"))));
        cases.Add(new("_parse_column", Ts((TokenType.VAR, "a"), (TokenType.DOT, "."))));
        cases.Add(new("_parse_column", Ts((TokenType.VAR, "a"), (TokenType.DOT, "."), (TokenType.DOT, "."))));
        foreach (var op in Parser.FACTOR_.Keys.Concat(Parser.TERM_.Keys.Where(t => t != TokenType.COLLATE))
            .Concat(Parser.BITWISE_.Keys).Concat(Parser.COMPARISON_.Keys).Concat(Parser.EQUALITY_.Keys)
            .Concat(Parser.CONJUNCTION_.Keys).Concat(Parser.DISJUNCTION_.Keys).Concat(new[] { TokenType.DPIPE, TokenType.DQMARK }))
            cases.Add(new("_parse_disjunction", Ts((TokenType.VAR, "a"), (op, op == TokenType.DIV ? "DIV" : op.ToString()),
                (TokenType.IDENTIFIER, "b"), (TokenType.DOT, "."), (TokenType.VAR, "c"))));
        cases.Add(new("_parse_disjunction", Ts((TokenType.VAR, "a"), (TokenType.PLUS, "+"), (TokenType.VAR, "b"),
            (TokenType.STAR, "*"), (TokenType.VAR, "c"), (TokenType.GT, ">"), (TokenType.NUMBER, "4"),
            (TokenType.OR, "OR"), (TokenType.NOT, "NOT"), (TokenType.IDENTIFIER, "d"), (TokenType.EQ, "="), (TokenType.NULL, "NULL"))));
        cases.Add(new("_parse_disjunction", Ts((TokenType.VAR, "a"), (TokenType.VAR, "AT"), (TokenType.TIME, "TIME"),
            (TokenType.VAR, "ZONE"), (TokenType.VAR, "tz"))));
        foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
            foreach (var max in new[] { 0, 1, 2 })
            {
                cases.Add(new("_parse_disjunction", Ts((TokenType.VAR, "a"), (TokenType.PLUS, "+"), (TokenType.VAR, "b")), level: level, max_nodes: max));
                cases.Add(new("_parse_disjunction", Ts((TokenType.VAR, "a"), (TokenType.EQ, "=")), level: level));
                cases.Add(new("_parse_column", Ts((TokenType.VAR, "a"), (TokenType.DOT, ".")), level: level));
            }
        var noComments = Ts((TokenType.VAR, "a"), (TokenType.DOT, "."), (TokenType.IDENTIFIER, "b"),
            (TokenType.PLUS, "+"), (TokenType.VAR, "c"));
        foreach (var t in noComments) t.comments.Clear();
        cases.Add(new("_parse_disjunction", noComments));
        var actual = cases.Select(c =>
        {
            var p = Start(c); exp.Expr? node = null; List<PyDict<string, object?>> failure = [];
            try { node = Invoke(p, c); } catch (errors.ParseError e) { failure = e.errors; }
            return new { ast = serde.dump(node), failure, errors = p.errors.Select(e => e.errors).ToArray(),
                index = p._index, curr = Snapshot(p._curr), next = Snapshot(p._next), prev = Snapshot(p._prev),
                prev_comments = p._prev_comments.ToArray(), token_comments = c.tokens.Select(t => t.comments.ToArray()).ToArray(), node_count = p._node_count };
        }).ToArray();
        var input = cases.Select(c => new { c.method, tokens = c.tokens.Select(Snapshot), c.any_token, c.fallback,
            c.level, c.max_nodes, c.dialect, allowed = c.allowed?.Select(t => t.ToString()).ToArray() }).ToArray();
        const string script = """
            import sys, json
            sys.path.insert(0, 'python')
            from sqlglot.parser import Parser
            from sqlglot.tokens import Token, TokenType
            from sqlglot.errors import ErrorLevel, ParseError
            from sqlglot.serde import dump
            def snapshot(t):
                return dict(type=t.token_type.name, text=t.text, line=t.line, col=t.col,
                    start=t.start, end=t.end, comments=t.comments)
            results = []
            for c in json.load(sys.stdin):
                tokens = [Token(TokenType[t['type']], t['text'], line=t['line'], col=t['col'],
                    start=t['start'], end=t['end'], comments=t['comments']) for t in c['tokens']]
                p = Parser(dialect=c['dialect'], max_nodes=c['max_nodes'], error_level=ErrorLevel[c['level']])
                p.sql = 'abcdefghijklmnopqrstuvwxyz0123456789'
                p._tokens, p._tokens_size, p._index = tokens, len(tokens), -1
                p._advance()
                kwargs = {}
                if c['method'] in ('_parse_id_var', '_parse_field'):
                    kwargs = dict(any_token=c['any_token'], tokens=None if c['allowed'] is None else [TokenType[t] for t in c['allowed']])
                if c['method'] == '_parse_type': kwargs = dict(fallback_to_identifier=c['fallback'])
                if c['method'] == '_parse_types': kwargs = dict(allow_identifiers=False)
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
    [InlineData(TokenType.L_PAREN, "_parse_function_call")]
    [InlineData(TokenType.DCOLON, "COLUMN_OPERATORS")]
    [InlineData(TokenType.ARROW, "COLUMN_OPERATORS")]
    [InlineData(TokenType.DOTCOLON, "COLUMN_OPERATORS")]
    public void Unavailable_postfixes_retreat_fast_path_and_throw_in_slow_path(TokenType tail, string dependency)
    {
        var p = Start(new("_parse_disjunction", Ts((TokenType.VAR, "a"), (tail, tail.ToString()), (TokenType.NUMBER, "1"))));
        Assert.Null(p._parse_column_parts_fast()); Assert.Equal(0, p._index);
        if (tail == TokenType.L_PAREN)
            Assert.Contains("Expecting )", Assert.Throws<errors.ParseError>(() => p._parse_disjunction()).Message);
        else if (tail is TokenType.DCOLON or TokenType.DOTCOLON)
            Assert.Contains("Expected type", Assert.Throws<errors.ParseError>(() => p._parse_disjunction()).Message);
        else if (tail == TokenType.ARROW)
            Assert.IsType<exp.JSONExtract>(p._parse_disjunction());
        else
        {
            var e = Assert.Throws<NotPortedException>(() => p._parse_disjunction());
            Assert.Contains(dependency, e.Message);
        }
    }

    [Theory]
    [InlineData(TokenType.INT, "INT", "_parse_type")]
    [InlineData(TokenType.INTERVAL, "INTERVAL", "_parse_type")]
    [InlineData(TokenType.VAR, "CASE", "_parse_function_call")]
    [InlineData(TokenType.L_PAREN, "(", "_parse_paren")]
    public void Deep_type_function_and_subquery_dependencies_are_explicit(TokenType type, string text, string dependency)
    {
        var p = Start(new("_parse_disjunction", Ts((type, text))));
        if (type == TokenType.L_PAREN)
            Assert.Contains("Required keyword: 'this'", Assert.Throws<errors.ParseError>(() => p._parse_disjunction()).Message);
        else if (text == "CASE")
            Assert.Contains("Expected END after CASE", Assert.Throws<errors.ParseError>(() => p._parse_disjunction()).Message);
        else if (type is TokenType.INT or TokenType.INTERVAL)
            Assert.IsType<exp.Column>(p._parse_disjunction());
        else Assert.Contains(dependency, Assert.Throws<NotPortedException>(() => p._parse_disjunction()).Message);
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
