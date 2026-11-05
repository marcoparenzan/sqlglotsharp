using Core = SqlGlotSharp.tokenizer_core.TokenizerCore;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp.Tests;

public class TokenizerLiteralTests
{
    private static Core Start(string sql)
    {
        var core = TokenizerStateTests.Create();
        core.format_strings["N'"] = ("'", TokenType.NATIONAL_STRING);
        core.format_strings["r'"] = ("'", TokenType.RAW_STRING);
        core.format_strings["X'"] = ("'", TokenType.HEX_STRING);
        core.format_strings["B'"] = ("'", TokenType.BIT_STRING);
        core.format_strings["$"] = ("$", TokenType.HEREDOC_STRING);
        core.sql = sql; core.size = py.len(sql); core._advance();
        return core;
    }

    [Theory]
    [InlineData("'abc'", "'", TokenType.STRING, "abc")]
    [InlineData("N'abc'", "N'", TokenType.NATIONAL_STRING, "abc")]
    [InlineData("r'a\\nb'", "r'", TokenType.RAW_STRING, "a\\nb")]
    [InlineData("X'ff'", "X'", TokenType.HEX_STRING, "ff")]
    [InlineData("B'101'", "B'", TokenType.BIT_STRING, "101")]
    [InlineData("$$abc$$", "$", TokenType.HEREDOC_STRING, "abc")]
    [InlineData("$tag$abc$tag$", "$", TokenType.HEREDOC_STRING, "abc")]
    public void Literal_scanning_recognizes_prefixes_and_heredoc_delimiters(string sql, string start, TokenType type, string text)
    {
        var core = Start(sql);
        Assert.True(core._scan_string(start));
        var token = Assert.Single(core.tokens);
        Assert.Equal(type, token.token_type);
        Assert.Equal(text, token.text);
        Assert.True(core._end);
    }

    [Fact]
    public void Quoted_identifiers_allow_doubled_closing_delimiters_without_mutating_escapes()
    {
        var core = Start("[a]]b]");
        core._scan_identifier("]");
        Assert.Equal("a]b", Assert.Single(core.tokens).text);
        Assert.Equal(TokenType.IDENTIFIER, core.tokens[0].token_type);
        Assert.Empty(core.identifier_escapes);
    }

    [Fact]
    public void Invalid_numeric_literal_raises_and_unknown_start_does_not_advance()
    {
        var core = Start("X'gg'");
        Assert.Equal("Numeric string contains invalid characters from 1:0", Assert.Throws<errors.TokenError>(() => core._scan_string("X'")).Message);
        core = Start("abc");
        Assert.False(core._scan_string("a"));
        Assert.Equal(1, core._current);
    }

    [Fact]
    public void Numeric_heredoc_tag_falls_back_to_configured_token()
    {
        var core = Start("$123$x"); core.heredoc_tag_is_identifier = true;
        Assert.True(core._scan_string("$"));
        Assert.Equal(TokenType.VAR, Assert.Single(core.tokens).token_type);
        Assert.Equal("$", core.tokens[0].text);
        Assert.Equal("1", core._peek);
    }
}
