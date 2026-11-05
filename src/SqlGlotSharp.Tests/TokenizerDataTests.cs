using SqlGlotSharp.dialects;
using static SqlGlotSharp.tokenizer_core;

namespace SqlGlotSharp.Tests;

public class TokenizerDataTests
{
    [Fact]
    public void Fabric_overrides_timestamp_without_mutating_tsql()
    {
        var sqlServer = new tsql.TSQL.Tokenizer();
        var warehouse = new fabric.Fabric.Tokenizer();
        Assert.Equal(TokenType.ROWVERSION, sqlServer.KEYWORDS["TIMESTAMP"]);
        Assert.Equal(TokenType.TIMESTAMP, warehouse.KEYWORDS["TIMESTAMP"]);
        Assert.Equal(TokenType.UTINYINT, warehouse.KEYWORDS["UTINYINT"]);
        Assert.NotSame(sqlServer.KEYWORDS, warehouse.KEYWORDS);
        Assert.Equal(TokenType.ROWVERSION, sqlServer.KEYWORDS["TIMESTAMP"]);
    }

    [Fact]
    public void Inherited_single_token_tables_are_available()
    {
        tokens.Tokenizer tokenizer = new postgres.Postgres.Tokenizer();
        Assert.Equal(TokenType.L_PAREN, tokenizer.SINGLE_TOKENS["("]);
        Assert.Equal(TokenType.R_PAREN, tokenizer.SINGLE_TOKENS[")"]);
    }

    [Fact]
    public void Regex_adapter_caches_patterns_and_anchors_match()
    {
        var pattern = py.re.compile(@"(?P<word>\w+)\Z");
        Assert.Same(pattern, py.re.compile(@"(?P<word>\w+)\Z"));
        Assert.Null(pattern.match("!abc"));
        Assert.Equal("abc", pattern.search("!abc")!.Groups["word"].Value);
        Assert.NotNull(py.re.compile("abc", 2).match("ABC"));
    }

    [Fact]
    public void Regex_word_class_preserves_supported_python_unicode_behavior()
    {
        Assert.NotNull(py.re.compile(@"\w+\Z").match("é"));
        Assert.Null(py.re.compile(@"\w+\Z", 256).match("é"));
        Assert.Null(py.re.compile(@"\w+\Z").match("a\u0301"));
        Assert.Throws<NotSupportedException>(() => py.re.compile("a", 4));
    }
}
