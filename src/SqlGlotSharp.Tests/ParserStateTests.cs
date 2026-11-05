using Parser = SqlGlotSharp.parser.Parser;
using Token = SqlGlotSharp.tokenizer_core.Token;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp.Tests;

public class ParserStateTests
{
    private static Parser Start(params Token[] tokens)
    {
        var parser = new Parser(dialect: "postgres");
        parser._chunks.Add(tokens.ToList());
        parser._advance_chunk();
        return parser;
    }

    [Fact]
    public void Constructor_reset_and_chunk_advance_keep_configuration_and_reset_state()
    {
        var instance = Start(new Token(TokenType.SELECT, "SELECT"), new Token(TokenType.VAR, "x"));
        Assert.Equal(errors.ErrorLevel.IMMEDIATE, instance.error_level);
        Assert.Equal(0L, instance._index);
        Assert.Equal(TokenType.SELECT, instance._curr.token_type);
        Assert.Equal(TokenType.VAR, instance._next.token_type);
        Assert.Same(parser.SENTINEL_NONE, instance._prev);
        var dialect = instance.dialect;
        var previous = instance._tokens;
        instance.reset();
        Assert.Same(dialect, instance.dialect);
        Assert.NotSame(previous, instance._tokens);
        Assert.Same(parser.SENTINEL_NONE, instance._curr);
    }

    [Fact]
    public void Match_attaches_previous_comments_and_eof_is_a_false_sentinel()
    {
        var instance = Start(new Token(TokenType.SELECT, "SELECT", comments: ["note"]), new Token(TokenType.VAR, "x"));
        var expression = new exp.Select();
        Assert.True(instance._match(TokenType.SELECT, expression: expression));
        Assert.Equal(new[] { "note" }, expression.comments);
        Assert.Empty(instance._prev_comments);
        Assert.True(instance._match_set(new[] { TokenType.VAR }));
        Assert.False(py.truthy(instance._curr));
        Assert.False(instance._is_connected());
    }

    [Fact]
    public void Text_sequence_backtracks_on_failure_and_can_match_without_consuming()
    {
        var instance = Start(new Token(TokenType.GROUP_BY, "group"), new Token(TokenType.VAR, "by"), new Token(TokenType.VAR, "x"));
        Assert.False(instance._match_text_seq(["GROUP", "WHERE"]));
        Assert.Equal(0L, instance._index);
        Assert.True(instance._match_text_seq(["GROUP", "BY"], advance: false));
        Assert.Equal(0L, instance._index);
        Assert.True(instance._match_pair(TokenType.GROUP_BY, TokenType.VAR));
        Assert.Equal(2L, instance._index);
        Assert.True(instance._match_texts(new[] { "X" }));
    }

    [Fact]
    public void Connection_checks_source_offsets_not_token_text()
    {
        var instance = Start(new Token(TokenType.VAR, "a", start: 0, end: 0), new Token(TokenType.DOT, ".", start: 1, end: 1));
        instance._advance();
        Assert.True(instance._is_connected());
        instance._curr.start = 2;
        Assert.False(instance._is_connected());
        instance._retreat(0);
        Assert.Equal("a", instance._curr.text);
    }
}
