using Token = SqlGlotSharp.tokenizer_core.Token;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp.Tests;

public class TokenModelTests
{
    [Fact]
    public void Factories_preserve_types_text_and_default_positions()
    {
        var number = Token.number(-12);
        Assert.Equal(TokenType.NUMBER, number.token_type);
        Assert.Equal("-12", number.text);
        Assert.Equal((1, 1, 0, 0), (number.line, number.col, number.start, number.end));
        Assert.Equal(TokenType.STRING, Token.@string("x").token_type);
        Assert.Equal(TokenType.IDENTIFIER, Token.identifier("x").token_type);
        Assert.Equal(TokenType.VAR, Token.@var("x").token_type);
    }

    [Fact]
    public void Comments_retain_identity_and_defaults_are_independent()
    {
        var comments = new List<string> { "note" };
        var token = new Token(TokenType.VAR, "x", comments: comments);
        Assert.Same(comments, token.comments);
        Assert.NotSame(Token.@var("x").comments, Token.@var("y").comments);
        Assert.Same(Token._attrs, Token.__slots__);
    }

    [Fact]
    public void Sentinel_truthiness_and_repr_match_python()
    {
        Assert.False(py.truthy(new Token(TokenType.SENTINEL, "")));
        Assert.True(py.truthy(Token.@var("")));
        var token = new Token(TokenType.STRING, "a'b", 2, 3, 4, 5, ["note"]);
        const string expected = "<Token token_type: TokenType.STRING, text: a'b, line: 2, col: 3, start: 4, end: 5, comments: ['note']>";
        Assert.Equal(expected, py.repr(token));
        Assert.Equal(expected, py.str(token));
        Assert.Equal(expected, token.ToString());
    }
}
