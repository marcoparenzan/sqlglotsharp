using Token = SqlGlotSharp.tokenizer_core.Token;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp.Tests;

public class TokenizerEmissionTests
{
    [Fact]
    public void Emission_retains_comment_list_then_resets_pending_comments()
    {
        var core = TokenizerStateTests.Create();
        core.sql = "abc"; core.size = 3; core._advance(alnum: true);
        var comments = core._comments;
        comments.Add("note");
        core._add(TokenType.VAR);
        var token = Assert.Single(core.tokens);
        Assert.Equal("abc", token.text);
        Assert.Equal((1, 3, 0, 2), (token.line, token.col, token.start, token.end));
        Assert.Same(comments, token.comments);
        Assert.NotSame(comments, core._comments);
    }

    [Fact]
    public void Semicolon_pending_comments_attach_to_previous_token()
    {
        var core = TokenizerStateTests.Create();
        core.tokens.Add(Token.@var("abc"));
        core._comments.Add("note");
        core._add(TokenType.SEMICOLON, ";");
        Assert.Equal(new[] { "note" }, core.tokens[0].comments);
        Assert.Empty(core.tokens[1].comments);
        Assert.Empty(core._comments);
    }

    [Theory]
    [InlineData("select(", TokenType.SELECT, "select")]
    [InlineData("name(", TokenType.VAR, "name")]
    [InlineData("name rest", TokenType.VAR, "name")]
    [InlineData("name\u001crest", TokenType.VAR, "name")]
    public void Variable_scan_stops_at_whitespace_or_single_tokens(string sql, TokenType expected, string text)
    {
        var core = TokenizerStateTests.Create();
        core.sql = sql; core.size = py.len(sql); core._advance(); core._scan_var();
        var token = Assert.Single(core.tokens);
        Assert.Equal(expected, token.token_type);
        Assert.Equal(text, token.text);
    }

    [Fact]
    public void Parameter_following_keyword_is_a_variable_and_scan_at_end_is_a_noop()
    {
        var core = TokenizerStateTests.Create();
        core.sql = "select"; core.size = 6;
        core.tokens.Add(new Token(TokenType.PARAMETER, "@"));
        core._advance(); core._scan_var();
        Assert.Equal(TokenType.VAR, core.tokens[1].token_type);
        core._scan();
        Assert.Equal(2, core.tokens.Count);
    }
}
