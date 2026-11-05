using Core = SqlGlotSharp.tokenizer_core.TokenizerCore;
using Token = SqlGlotSharp.tokenizer_core.Token;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp.Tests;

public class TokenizerCommentTests
{
    private static Core Start(string sql)
    {
        var core = TokenizerStateTests.Create();
        core.comments["--"] = null; core.comments["/*"] = "*/"; core.comments["/*+"] = "*/";
        core.sql = sql; core.size = py.len(sql); core._advance();
        return core;
    }

    [Fact]
    public void Leading_and_nested_block_comments_remain_pending_for_next_token()
    {
        var core = Start("/* outer /* inner */ tail */x");
        Assert.True(core._scan_comment("/*"));
        Assert.Equal(new[] { " outer /* inner */ tail " }, core._comments);
        Assert.Equal("x", core._peek);
        Assert.Empty(core.tokens);
    }

    [Fact]
    public void Trailing_line_comments_attach_to_previous_token()
    {
        var core = Start("-- note\nx");
        core.tokens.Add(Token.@var("x")); core._prev_token_line = 1;
        Assert.True(core._scan_comment("--"));
        Assert.Equal(new[] { " note" }, core.tokens[0].comments);
        Assert.Empty(core._comments);
        Assert.Equal("\n", core._peek);
    }

    [Theory]
    [InlineData("--x", false)]
    [InlineData("-- x", true)]
    [InlineData("--", true)]
    [InlineData("--\u007fx", true)]
    public void Dash_boundary_rule_is_optional(string sql, bool expected)
    {
        var core = Start(sql); core.dash_comment_requires_boundary = true;
        Assert.Equal(expected, core._scan_comment("--"));
        if (!expected) Assert.Equal(1, core._current);
    }

    [Fact]
    public void Carriage_return_termination_and_hint_tokens_follow_configuration()
    {
        var core = Start("-- a\rb");
        core.comments_terminate_at_newline_only = true;
        core._scan_comment("--");
        Assert.Equal(" a\rb", Assert.Single(core._comments));
        core = Start("/*+ hint */x");
        core.tokens.Add(new Token(TokenType.SELECT, "SELECT"));
        core.tokens_preceding_hint.Add(TokenType.SELECT);
        core._scan_comment("/*+");
        Assert.Equal(TokenType.HINT, core.tokens[^1].token_type);
        Assert.Equal(new[] { " hint " }, core.tokens[^1].comments);
    }

    [Fact]
    public void Unknown_delimiters_do_not_advance_and_unterminated_blocks_fail()
    {
        var core = Start("abc");
        Assert.False(core._scan_comment("//"));
        Assert.Equal(1, core._current);
        core = Start("/* unfinished");
        Assert.Throws<IndexOutOfRangeException>(() => core._scan_comment("/*"));
    }
}
