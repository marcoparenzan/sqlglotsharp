using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp.Tests;

public class TokenizerFacadeTests
{
    [Theory]
    [InlineData("")]
    [InlineData("postgres")]
    [InlineData("tsql")]
    [InlineData("fabric")]
    public void Scoped_tokenizers_scan_select_with_their_generated_configuration(string name)
    {
        var tokenizer = tokens.Tokenizer.ForDialect(name);
        var result = tokenizer.tokenize("SELECT a, 'text' FROM t WHERE a >= 2;");
        Assert.Equal(TokenType.SELECT, result[0].token_type);
        Assert.Equal(TokenType.SEMICOLON, result[^1].token_type);
        Assert.Contains(result, t => t.token_type == TokenType.STRING && t.text == "text");
        Assert.Contains(result, t => t.token_type == TokenType.GTE);
        Assert.Equal("SELECT a, 'text' FROM t WHERE a >= 2;", tokenizer.sql);
        Assert.Same(result, tokenizer.tokens);
    }

    [Fact]
    public void Dialect_specific_quoted_identifiers_and_postgres_heredoc_work()
    {
        var tsql = tokens.Tokenizer.ForDialect("tsql");
        Assert.Equal("a]b", tsql.tokenize("[a]]b]")[0].text);
        var postgres = tokens.Tokenizer.ForDialect("postgres");
        Assert.Equal("text", postgres.tokenize("$tag$text$tag$")[0].text);
        Assert.Equal(TokenType.HEREDOC_STRING, postgres.tokens[0].token_type);
    }

    [Fact]
    public void Instances_share_generated_tables_but_not_scan_state()
    {
        var first = tokens.Tokenizer.ForDialect("postgres");
        var second = tokens.Tokenizer.ForDialect("postgres");
        Assert.Same(first._core.keywords, second._core.keywords);
        first.tokenize("SELECT 1"); second.tokenize("SELECT 2");
        Assert.NotSame(first.tokens, second.tokens);
        Assert.Equal("1", first.tokens[1].text);
        Assert.Equal("2", second.tokens[1].text);
        Assert.Throws<ArgumentException>(() => tokens.Tokenizer.ForDialect("unknown"));
    }
}
