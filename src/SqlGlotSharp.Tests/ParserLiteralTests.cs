using Parser = SqlGlotSharp.parser.Parser;
using Token = SqlGlotSharp.tokenizer_core.Token;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp.Tests;

public class ParserLiteralTests
{
    private static Parser Start(params Token[] tokens)
    {
        var instance = new Parser
        {
            _tokens = tokens.ToList(),
            _tokens_size = tokens.Length,
            _index = -1
        };
        instance._advance();
        return instance;
    }

    private static void Positions(exp.Expr expression, Token token)
    {
        Assert.Equal(token.line, expression.meta["line"]);
        Assert.Equal(token.col, expression.meta["col"]);
        Assert.Equal(token.start, expression.meta["start"]);
        Assert.Equal(token.end, expression.meta["end"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void Identifier_expression_uses_previous_token_without_advancing(bool? quoted)
    {
        var token = new Token(TokenType.IDENTIFIER, "Mixed Case", line: 3, col: 15,
            start: 21, end: 32, comments: ["identifier note"]);
        var next = new Token(TokenType.COMMA, ",");
        var instance = Start(token, next);
        instance._advance();

        var node = instance._identifier_expression(quoted: quoted);

        Assert.Equal("Mixed Case", node.args["this"]);
        Assert.True(node.args.ContainsKey("quoted"));
        Assert.Equal(quoted, node.args["quoted"]);
        Positions(node, token);
        Assert.Equal(new[] { "identifier note" }, node.comments);
        Assert.Empty(instance._prev_comments);
        Assert.Equal(new[] { "identifier note" }, token.comments);
        Assert.Equal(1L, instance._index);
        Assert.Same(next, instance._curr);
        Assert.Same(token, instance._prev);
    }

    [Fact]
    public void Explicit_identifier_token_supplies_positions_but_comments_come_from_previous_token()
    {
        var previous = new Token(TokenType.VAR, "previous", comments: ["previous note"]);
        var instance = Start(previous);
        instance._advance();
        var explicitToken = new Token(TokenType.IDENTIFIER, "explicit", line: 5, col: 8,
            start: 40, end: 47, comments: ["not propagated"]);

        var node = instance._identifier_expression(explicitToken, quoted: true);

        Assert.Equal("explicit", node.args["this"]);
        Positions(node, explicitToken);
        Assert.Equal(new[] { "previous note" }, node.comments);
        Assert.Equal(1L, instance._index);
        Assert.Same(parser.SENTINEL_NONE, instance._curr);
        Assert.Empty(instance._prev_comments);
        // Python's token-or-previous fallback uses truthiness, not just nullness.
        Assert.Equal("previous", instance._identifier_expression(parser.SENTINEL_NONE).args["this"]);
    }

    [Fact]
    public void Advance_any_rejects_reserved_tokens_unless_requested_and_never_consumes_eof()
    {
        var reserved = new Token(TokenType.SELECT, "SELECT", comments: ["keyword note"]);
        var word = new Token(TokenType.VAR, "name", comments: ["word note"]);
        var instance = Start(reserved, word);

        Assert.Null(instance._advance_any());
        Assert.Equal(0L, instance._index);
        Assert.Same(reserved, instance._curr);
        Assert.Same(reserved, instance._advance_any(ignore_reserved: true));
        Assert.Equal(new[] { "keyword note" }, instance._prev_comments);
        Assert.Equal(1L, instance._index);
        Assert.Same(word, instance._advance_any());
        Assert.Equal(new[] { "word note" }, instance._prev_comments);
        Assert.Equal(2L, instance._index);
        Assert.Null(instance._advance_any());
        Assert.Null(instance._advance_any(ignore_reserved: true));
        Assert.Equal(2L, instance._index);
    }

    // The callback only matches a single identifier token; this is not a SQL entrypoint.
    private static exp.Identifier? Identifier(Parser instance) =>
        instance._match(TokenType.IDENTIFIER) ? instance._identifier_expression(quoted: true) : null;

    [Fact]
    public void Csv_attaches_separator_comments_to_previous_node_and_preserves_positions()
    {
        var first = new Token(TokenType.IDENTIFIER, "a", line: 1, col: 3, start: 0, end: 2,
            comments: ["first note"]);
        var second = new Token(TokenType.IDENTIFIER, "b", line: 1, col: 8, start: 5, end: 7,
            comments: ["second note"]);
        var stop = new Token(TokenType.SEMICOLON, ";");
        var instance = Start(first, new Token(TokenType.COMMA, ",", comments: ["separator note"]), second, stop);

        var nodes = instance._parse_csv(() => Identifier(instance));

        Assert.Equal(2, nodes.Count);
        Assert.Equal(new[] { "first note", "separator note" }, nodes[0].comments);
        Assert.Equal(new[] { "second note" }, nodes[1].comments);
        Positions(nodes[0], first);
        Positions(nodes[1], second);
        Assert.Empty(instance._prev_comments);
        Assert.Equal(3L, instance._index);
        Assert.Same(stop, instance._curr);
    }

    [Fact]
    public void Csv_continues_after_null_results_and_consumes_a_trailing_separator()
    {
        var instance = Start(new Token(TokenType.COMMA, ","),
            new Token(TokenType.IDENTIFIER, "a"),
            new Token(TokenType.COMMA, ",", comments: ["trailing note"]));
        var calls = 0;

        var nodes = instance._parse_csv(() => { calls++; return Identifier(instance); });

        Assert.Single(nodes);
        Assert.Equal(3, calls);
        Assert.Equal(new[] { "trailing note" }, nodes[0].comments);
        Assert.Equal(3L, instance._index);
        Assert.Same(parser.SENTINEL_NONE, instance._curr);
        Assert.Empty(instance._prev_comments);
    }

    [Fact]
    public void Csv_supports_non_expression_results_and_custom_separator()
    {
        var instance = Start(new Token(TokenType.VAR, "a"),
            new Token(TokenType.DOT, ".", comments: ["dot note"]), new Token(TokenType.VAR, "b"));

        var nodes = instance._parse_csv(() => instance._advance_any(), sep: TokenType.DOT);

        Assert.Equal(new[] { "a", "b" }, nodes.Select(token => token.text));
        Assert.Equal(3L, instance._index);
        Assert.Equal(new[] { "dot note" }, instance._tokens[1].comments);
    }

    [Fact]
    public void Csv_empty_input_calls_callback_once_and_does_not_advance()
    {
        var instance = Start();
        var calls = 0;
        var nodes = instance._parse_csv(() => { calls++; return Identifier(instance); });

        Assert.Empty(nodes);
        Assert.Equal(1, calls);
        Assert.Equal(0L, instance._index);
    }
}
