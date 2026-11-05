using Token = SqlGlotSharp.tokenizer_core.Token;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp.Tests;

public class TokenizerNumberTests
{
    [Theory]
    [InlineData("123.45e-6", "123.45e-6")]
    [InlineData("1e+2", "1e+2")]
    [InlineData("1e+", "1e")]
    [InlineData("100_000", "100000")]
    [InlineData("12abc", "12")]
    public void Number_scan_preserves_upstream_decimal_exponent_and_suffix_rules(string sql, string expected)
    {
        var core = TokenizerStateTests.Create();
        core.numbers_can_be_underscore_separated = true;
        core.sql = sql; core.size = py.len(sql); core._advance(); core._scan_number();
        var token = Assert.Single(core.tokens);
        Assert.Equal(TokenType.NUMBER, token.token_type);
        Assert.Equal(expected, token.text);
        Assert.Equal(py.len(expected), core._current - (sql.Contains('_') ? 1 : 0));
    }

    [Theory]
    [InlineData("0b101", TokenType.BIT_STRING, "101")]
    [InlineData("0b102", TokenType.IDENTIFIER, "0b102")]
    [InlineData("0x_ff", TokenType.HEX_STRING, "_ff")]
    [InlineData("0xFf", TokenType.HEX_STRING, "Ff")]
    [InlineData("0xGG", TokenType.IDENTIFIER, "0xGG")]
    public void Radix_scan_validates_without_materializing_a_large_integer(string sql, TokenType type, string text)
    {
        var core = TokenizerStateTests.Create();
        core.has_bit_strings = true; core.has_hex_strings = true;
        core.sql = sql; core.size = py.len(sql); core._advance(); core._scan_number();
        var token = Assert.Single(core.tokens);
        Assert.Equal(type, token.token_type);
        Assert.Equal(text, token.text);
    }

    [Fact]
    public void Suffix_casts_and_digit_identifiers_match_configuration()
    {
        var core = TokenizerStateTests.Create();
        core.numeric_literals["L"] = "BIGINT"; core.keywords["BIGINT"] = TokenType.BIGINT;
        core.sql = "123L"; core.size = 4; core._advance(); core._scan_number();
        Assert.Equal(new[] { TokenType.NUMBER, TokenType.DCOLON, TokenType.BIGINT }, core.tokens.Select(t => t.token_type));
        Assert.Equal(new[] { "123", "::", "L" }, core.tokens.Select(t => t.text));
        core.reset(); core.identifiers_can_start_with_digit = true;
        core.sql = "12abc"; core.size = 5; core._advance(); core._scan_number();
        Assert.Equal(TokenType.VAR, Assert.Single(core.tokens).token_type);
        Assert.Equal("12abc", core.tokens[0].text);
    }

    [Fact]
    public void Parameter_or_disabled_decimal_support_leaves_dot_unconsumed()
    {
        foreach (var parameter in new[] { false, true })
        {
            var core = TokenizerStateTests.Create();
            if (parameter) core.tokens.Add(new Token(TokenType.PARAMETER, "$"));
            else core.numbers_can_have_decimals = false;
            core.sql = "12.3"; core.size = 4; core._advance(); core._scan_number();
            Assert.Equal("12", core.tokens[^1].text);
            Assert.Equal(".", core._peek);
        }
    }
}
