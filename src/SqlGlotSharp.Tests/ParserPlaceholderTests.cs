using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Parser = SqlGlotSharp.parser.Parser;
using Token = SqlGlotSharp.tokenizer_core.Token;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp.Tests;

public class ParserPlaceholderTests
{
    private static Parser Start(params Token[] tokens)
    {
        var instance = new Parser(max_nodes: 0)
        {
            _tokens = tokens.ToList(), _tokens_size = tokens.Length, _index = -1
        };
        instance._advance();
        return instance;
    }

    [Fact]
    public void String_as_identifier_preserves_comments_in_cursor_not_ast_and_does_not_count_nodes()
    {
        var token = new Token(TokenType.STRING, "Mixed Case", line: 4, col: 12,
            start: 30, end: 41, comments: ["string note"]);
        var stop = new Token(TokenType.COMMA, ",");
        var instance = Start(token, stop);

        var node = Assert.IsType<exp.Identifier>(instance._parse_string_as_identifier());

        Assert.Equal("Mixed Case", node.args["this"]);
        Assert.Equal(true, node.args["quoted"]);
        Assert.Equal(token.line, node.meta["line"]);
        Assert.Equal(token.col, node.meta["col"]);
        Assert.Equal(token.start, node.meta["start"]);
        Assert.Equal(token.end, node.meta["end"]);
        Assert.Null(node.comments);
        Assert.Same(token.comments, instance._prev_comments);
        Assert.Equal(new[] { "string note" }, instance._prev_comments);
        Assert.Same(stop, instance._curr);
        Assert.Same(token, instance._prev);
        Assert.Equal(1L, instance._index);
        Assert.Equal(0, instance._node_count);
        Assert.Empty(instance.errors);

        Assert.Null(instance._parse_string_as_identifier());
        Assert.Equal(1L, instance._index);
        Assert.Same(token.comments, instance._prev_comments);
    }

    [Theory]
    [InlineData(TokenType.IDENTIFIER)]
    [InlineData(TokenType.VAR)]
    [InlineData(TokenType.NUMBER)]
    [InlineData(TokenType.PLACEHOLDER)]
    [InlineData(TokenType.PARAMETER)]
    [InlineData(TokenType.COLON)]
    public void Non_strings_are_not_consumed_or_treated_as_placeholders(TokenType type)
    {
        var previous = new Token(TokenType.VAR, "previous", comments: ["pending"]);
        var current = new Token(type, "value", comments: ["current"]);
        var instance = Start(previous, current);
        instance._advance();

        Assert.Null(instance._parse_string_as_identifier());
        Assert.Equal(1L, instance._index);
        Assert.Same(current, instance._curr);
        Assert.Same(previous, instance._prev);
        Assert.Same(previous.comments, instance._prev_comments);
        Assert.Equal(new[] { "pending" }, instance._prev_comments);
        Assert.Equal(0, instance._node_count);
    }

    [Fact]
    public async Task Prepared_cursors_match_python_ast_metadata_comments_and_state()
    {
        List<Token[]> cases = [Array.Empty<Token>()];
        foreach (var text in new[] { "", "plain", "a b", "a'b", "a\"b", "line\nbreak", "é😀", "select", "123" })
            cases.Add([new Token(TokenType.STRING, text, line: 3, col: 17, start: 9, end: 25,
                comments: ["first", " sqlglot.meta flag=true"]), new Token(TokenType.COMMA, ",", comments: ["stop"])]);
        cases.Add([new Token(TokenType.STRING, "a", comments: ["a comment"]),
            new Token(TokenType.STRING, "b", comments: ["b comment"])]);
        foreach (var type in new[] { TokenType.IDENTIFIER, TokenType.VAR, TokenType.NUMBER,
            TokenType.PLACEHOLDER, TokenType.PARAMETER, TokenType.COLON, TokenType.SEMICOLON })
            cases.Add([new Token(type, "not a string", comments: ["unconsumed"])]);

        var input = cases.Select(tokens => tokens.Select(TokenSnapshot).ToArray()).ToArray();
        var actual = cases.Select(tokens =>
        {
            var instance = Start(tokens);
            var results = new List<object>();
            for (var i = 0; i < 2; i++)
            {
                var node = instance._parse_string_as_identifier();
                results.Add(new
                {
                    ast = serde.dump(node), index = instance._index,
                    curr = TokenSnapshot(instance._curr), next = TokenSnapshot(instance._next),
                    prev = TokenSnapshot(instance._prev), prev_comments = instance._prev_comments.ToArray(),
                    token_comments = tokens.Select(t => t.comments.ToArray()).ToArray(),
                    node_count = instance._node_count, errors = instance.errors.Count
                });
            }
            return results;
        }).ToArray();
        const string script = """
            import sys, json
            sys.path.insert(0, 'python')
            from sqlglot.parser import Parser
            from sqlglot.tokens import Token, TokenType
            from sqlglot.serde import dump
            def snapshot(t):
                return dict(type=t.token_type.name, text=t.text, line=t.line, col=t.col,
                            start=t.start, end=t.end, comments=t.comments)
            results = []
            for case in json.load(sys.stdin):
                tokens = [Token(TokenType[c['type']], c['text'], line=c['line'], col=c['col'],
                                start=c['start'], end=c['end'], comments=c['comments']) for c in case]
                p = Parser(max_nodes=0)
                p._tokens, p._tokens_size, p._index = tokens, len(tokens), -1
                p._advance()
                steps = []
                for _ in range(2):
                    node = p._parse_string_as_identifier()
                    steps.append(dict(ast=dump(node), index=p._index,
                        curr=snapshot(p._curr), next=snapshot(p._next), prev=snapshot(p._prev),
                        prev_comments=p._prev_comments[:], token_comments=[t.comments[:] for t in tokens],
                        node_count=p._node_count, errors=len(p.errors)))
                results.append(steps)
            print(json.dumps(results))
            """;
        var expected = await Python(script, JsonSerializer.Serialize(input));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonSerializer.SerializeToNode(actual, serde.JsonOptions)),
            $"Python: {expected}\nC#: {JsonSerializer.Serialize(actual, serde.JsonOptions)}");
    }

    [Fact]
    public async Task Port_status_verifies_exact_upstream_method_hash_and_unique_anchor()
    {
        const string script = """
            import sys, json
            from pathlib import Path
            sys.path.insert(0, 'src/tools')
            import port_status
            members = port_status.inventory(Path('python'), json.loads(Path('src/tools/scope.json').read_text()))
            report = port_status.report(members, port_status.scan_anchors(Path('src/SqlGlotSharp')))
            row = next(r for r in report['members'] if r['anchor'] ==
                       'sqlglot/parser.py::Parser._parse_string_as_identifier')
            assert row['status'] == 'ported', row
            assert row['hash'] == 'c7419fb3c8', row
            assert len(row['csharp']) == 1, row
            assert row['csharp'][0]['file'] == 'parser.placeholders.cs', row
            print(row['hash'])
            """;
        Assert.Equal("c7419fb3c8", (await Python(script)).Trim());
    }

    private static object TokenSnapshot(Token token) => new
    {
        type = token.token_type.ToString(), token.text, token.line, token.col,
        token.start, token.end, comments = token.comments.ToArray()
    };

    private static string RepositoryRoot => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath())!, "..", ".."));
    private static string SourcePath([CallerFilePath] string source = "") => source;

    private static async Task<string> Python(string script, string? input = null)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SQLGLOT_PYTHON") ?? "python")
        {
            WorkingDirectory = RepositoryRoot, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        start.ArgumentList.Add("-B");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(script);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            if (input is not null) await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, await stderr);
        return await stdout;
    }
}
