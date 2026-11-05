using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Parser = SqlGlotSharp.parser.Parser;
using Token = SqlGlotSharp.tokenizer_core.Token;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp.Tests;

public class ParserPrimaryTests
{
    private sealed class StrictParser : Parser
    {
        public override bool ADJACENT_STRINGS_CANNOT_BE_CONNECTED => true;
    }
    private sealed class BinaryDialect : SqlGlotSharp.dialects.dialect.Dialect
    {
        public override bool BYTE_STRING_IS_BYTES_TYPE => true;
        public override bool HEX_STRING_IS_INTEGER_TYPE => true;
    }
    private sealed record Case(string method, Token[] tokens, string? dialect = null,
        bool strict = false, int max_nodes = -1, string level = "IMMEDIATE", bool any_token = false,
        bool upper = false, TokenType[]? allowed = null, bool binary_types = false);

    private static Parser Start(Case c)
    {
        Parser p = c.strict ? new StrictParser() : new Parser();
        p.dialect = c.binary_types ? new BinaryDialect() : parser._resolve_dialect(c.dialect);
        p.max_nodes = c.max_nodes;
        p.error_level = Enum.Parse<errors.ErrorLevel>(c.level);
        p.sql = "abcdefghijklmnopqrstuvwxyz0123456789";
        p._tokens = c.tokens.ToList(); p._tokens_size = c.tokens.Length; p._index = -1;
        p._advance();
        return p;
    }
    private static exp.Expr? Invoke(Parser p, Case c) => c.method switch
    {
        "_parse_string" => p._parse_string(), "_parse_number" => p._parse_number(),
        "_parse_primary" => p._parse_primary(), "_parse_parameter" => p._parse_parameter(),
        "_parse_placeholder" => p._parse_placeholder(), "_parse_identifier" => p._parse_identifier(),
        "_parse_primary_or_var" => p._parse_primary_or_var(),
        "_parse_var_or_string" => p._parse_var_or_string(c.upper),
        "_parse_var" => p._parse_var(c.any_token, c.allowed, c.upper),
        _ => throw new ArgumentException(c.method)
    };
    private static Token T(TokenType type, string text = "value", int start = 3, int end = 8,
        string comment = "literal comment") => new(type, text, line: 2, col: 9, start: start, end: end, comments: [comment]);
    private static object Snapshot(Token t) => new
    {
        type = t.token_type.ToString(), t.text, t.line, t.col, t.start, t.end, comments = t.comments.ToArray()
    };

    [Fact]
    public async Task Literal_parameter_oracle_matches_ast_errors_positions_comments_and_cursor()
    {
        List<Case> cases = [];
        foreach (var type in Parser.STRING_PARSERS_.Keys)
        {
            cases.Add(new("_parse_string", [T(type)]));
            cases.Add(new("_parse_primary", [T(type)]));
        }
        foreach (var type in Parser.NUMERIC_PARSERS_.Keys)
            foreach (var dialect in new string?[] { null, "postgres", "tsql", "fabric" })
            {
                cases.Add(new("_parse_number", [T(type, "12.3" )], dialect));
                cases.Add(new("_parse_primary", [T(type, "12.3")], dialect));
            }
        cases.Add(new("_parse_number", [T(TokenType.BYTE_STRING)], binary_types: true));
        cases.Add(new("_parse_number", [T(TokenType.HEX_STRING)], binary_types: true));
        cases.Add(new("_parse_string", [T(TokenType.UNICODE_STRING), T(TokenType.VAR, "uEsCaPe"), T(TokenType.STRING, "!")]));
        cases.Add(new("_parse_string", [T(TokenType.UNICODE_STRING), T(TokenType.VAR, "UESCAPE"), T(TokenType.PLACEHOLDER, "?")]));
        cases.Add(new("_parse_string", [T(TokenType.UNICODE_STRING), T(TokenType.VAR, "UESCAPE"), T(TokenType.SEMICOLON, ";")]));
        cases.Add(new("_parse_string", [T(TokenType.UNICODE_STRING), T(TokenType.VAR, "NOESCAPE")]));
        foreach (var method in new[] { "_parse_string", "_parse_number", "_parse_identifier", "_parse_placeholder", "_parse_var", "_parse_primary_or_var" })
        {
            cases.Add(new(method, []));
            cases.Add(new(method, [T(TokenType.SEMICOLON, ";")]));
            cases.Add(new(method, [T(TokenType.PLACEHOLDER, "?")]));
            cases.Add(new(method, [T(TokenType.COLON, ":"), T(TokenType.VAR, "name")]));
            cases.Add(new(method, [T(TokenType.COLON, ":"), T(TokenType.R_PAREN, ")")]));
            cases.Add(new(method, [T(TokenType.PARAMETER, "@"), T(TokenType.VAR, "name")]));
        }
        foreach (var type in new[] { TokenType.IDENTIFIER, TokenType.VAR, TokenType.NUMBER,
            TokenType.STRING, TokenType.NULL, TokenType.TRUE, TokenType.FALSE, TokenType.SELECT,
            TokenType.PLACEHOLDER })
        {
            cases.Add(new("_parse_placeholder", [T(TokenType.PARAMETER, "@"), T(type, "name")]));
            cases.Add(new("_parse_parameter", [T(type, "name")]));
        }
        cases.Add(new("_parse_placeholder", [T(TokenType.PARAMETER, "@"), T(TokenType.PARAMETER, "@"), T(TokenType.IDENTIFIER)]));
        cases.Add(new("_parse_placeholder", [T(TokenType.PARAMETER, "@")]));
        cases.Add(new("_parse_placeholder", [T(TokenType.COLON, ":")]));
        cases.Add(new("_parse_primary", [T(TokenType.DOT, "."), T(TokenType.NUMBER, "125")]));
        foreach (var type in new[] { TokenType.NULL, TokenType.TRUE, TokenType.FALSE })
            cases.Add(new("_parse_primary", [T(type)]));
        cases.Add(new("_parse_primary", [T(TokenType.INTRODUCER, "_utf8"), T(TokenType.STRING, "hello")]));
        cases.Add(new("_parse_primary", [T(TokenType.INTRODUCER, "_utf8"), T(TokenType.SEMICOLON, ";")]));
        foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
        {
            cases.Add(new("_parse_primary", [T(TokenType.STRING, "a", 0, 2), T(TokenType.STRING, "b", 3, 5), T(TokenType.STRING, "c", 8, 10)], strict: true, level: level));
            cases.Add(new("_parse_string", [T(TokenType.NATIONAL_STRING)], max_nodes: 0, level: level));
            cases.Add(new("_parse_placeholder", [T(TokenType.PARAMETER, "@")], level: level));
        }
        cases.Add(new("_parse_var", [T(TokenType.SELECT, "select")], any_token: true));
        cases.Add(new("_parse_var", [T(TokenType.SELECT, "select")], allowed: [TokenType.SELECT], upper: true));
        cases.Add(new("_parse_var", [T(TokenType.VAR, "straße")], upper: true));
        cases.Add(new("_parse_var", [T(TokenType.PARAMETER, "@")], any_token: true));
        cases.Add(new("_parse_var_or_string", [T(TokenType.VAR, "abc")], upper: true));

        var actual = cases.Select(c =>
        {
            var p = Start(c); exp.Expr? node = null; List<PyDict<string, object?>> failure = [];
            try { node = Invoke(p, c); }
            catch (errors.ParseError e) { failure = e.errors; }
            return new { ast = serde.dump(node), failure, errors = p.errors.Select(e => e.errors).ToArray(),
                index = p._index, curr = Snapshot(p._curr), next = Snapshot(p._next), prev = Snapshot(p._prev),
                prev_comments = p._prev_comments.ToArray(), token_comments = c.tokens.Select(t => t.comments.ToArray()).ToArray(), node_count = p._node_count };
        }).ToArray();
        var input = cases.Select(c => new { c.method, tokens = c.tokens.Select(Snapshot), c.dialect,
            c.strict, c.max_nodes, c.level, c.any_token, c.upper, c.binary_types, allowed = c.allowed?.Select(t => t.ToString()) });
        const string script = """
            import sys, json
            sys.path.insert(0, 'python')
            from sqlglot.parser import Parser
            from sqlglot.tokens import Token, TokenType
            from sqlglot.errors import ErrorLevel, ParseError
            from sqlglot.serde import dump
            from sqlglot.dialects.dialect import Dialect
            class BinaryDialect(Dialect):
                BYTE_STRING_IS_BYTES_TYPE = True
                HEX_STRING_IS_INTEGER_TYPE = True
            class StrictParser(Parser):
                ADJACENT_STRINGS_CANNOT_BE_CONNECTED = True
            def snapshot(t):
                return dict(type=t.token_type.name, text=t.text, line=t.line, col=t.col,
                    start=t.start, end=t.end, comments=t.comments)
            results = []
            for c in json.load(sys.stdin):
                tokens = [Token(TokenType[t['type']], t['text'], line=t['line'], col=t['col'],
                    start=t['start'], end=t['end'], comments=t['comments']) for t in c['tokens']]
                p = (StrictParser if c['strict'] else Parser)(dialect=BinaryDialect() if c['binary_types'] else c['dialect'],
                    max_nodes=c['max_nodes'], error_level=ErrorLevel[c['level']])
                p.sql = 'abcdefghijklmnopqrstuvwxyz0123456789'
                p._tokens, p._tokens_size, p._index = tokens, len(tokens), -1
                p._advance()
                node, failure = None, []
                kwargs = dict(any_token=c['any_token'], upper=c['upper'],
                    tokens=[TokenType[t] for t in c['allowed']] if c['allowed'] else None) if c['method'] == '_parse_var' else {}
                if c['method'] == '_parse_var_or_string': kwargs = dict(upper=c['upper'])
                try: node = getattr(p, c['method'])(**kwargs)
                except ParseError as e: failure = e.errors
                results.append(dict(ast=dump(node), failure=failure, errors=[e.errors for e in p.errors],
                    index=p._index, curr=snapshot(p._curr), next=snapshot(p._next), prev=snapshot(p._prev),
                    prev_comments=p._prev_comments[:], token_comments=[t.comments[:] for t in tokens], node_count=p._node_count))
            print(json.dumps(results))
            """;
        var expected = JsonNode.Parse(await Python(script, JsonSerializer.Serialize(input)))!.AsArray();
        var observed = JsonSerializer.SerializeToNode(actual, serde.JsonOptions)!.AsArray();
        for (int i = 0; i < cases.Count; i++)
            Assert.True(JsonNode.DeepEquals(expected[i], observed[i]),
                $"Case {i}: {JsonSerializer.Serialize(input.ElementAt(i))}\nPython: {expected[i]}\nC#: {observed[i]}");
    }

    [Theory]
    [InlineData(TokenType.L_PAREN, "_parse_paren")]
    [InlineData(TokenType.SESSION_PARAMETER, "_parse_session_parameter")]
    
    public void Deep_dependencies_throw_explicit_boundaries(TokenType type, string dependency)
    {
        foreach (var method in new[] { "_parse_primary", "_parse_parameter", "_parse_primary_or_var" })
        {
            var c = new Case(method, [T(type)]);
            if (type == TokenType.L_PAREN)
                Assert.Contains("Required keyword: 'this'", Assert.Throws<errors.ParseError>(() => Invoke(Start(c), c)).Message);
            else
            {
                var e = Assert.Throws<NotPortedException>(() => Invoke(Start(c), c));
                Assert.Contains(dependency, e.Message);
            }
        }
    }

    [Fact]
    public async Task Port_status_checks_complete_and_partial_members()
    {
        const string script = """
            import sys, json
            from pathlib import Path
            sys.path.insert(0, 'src/tools')
            import port_status
            members = port_status.inventory(Path('python'), json.loads(Path('src/tools/scope.json').read_text()))
            report = port_status.report(members, port_status.scan_anchors(Path('src/SqlGlotSharp')))
            rows = [r for r in report['members'] if any(c['file'] == 'parser.primary.cs' for c in r.get('csharp', []))]
            complete = {'Parser.STRING_PARSERS', 'Parser.NUMERIC_PARSERS'}
            assert len(rows) == 13, rows
            for r in rows:
                expected = 'ported' if r['anchor'].split('::')[1] in complete else 'stub'
                assert r['status'] == expected, r
                assert len(r['csharp']) == 1, r
            print('verified')
            """;
        Assert.Equal("verified", (await Python(script)).Trim());
    }

    private static string Root => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Source())!, "..", ".."));
    private static string Source([CallerFilePath] string source = "") => source;
    private static async Task<string> Python(string script, string? input = null)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SQLGLOT_PYTHON") ?? "python")
        {
            WorkingDirectory = Root, UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, RedirectStandardInput = true
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
        Assert.True(process.ExitCode == 0, await stderr);
        return await stdout;
    }
}
