using Core = SqlGlotSharp.tokenizer_core.TokenizerCore;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp.Tests;

public class TokenizerScanTests
{
    private static Core Create()
    {
        var core = TokenizerStateTests.Create();
        core.single_tokens[","] = TokenType.COMMA; core.single_tokens[";"] = TokenType.SEMICOLON;
        core.single_tokens[")"] = TokenType.R_PAREN; core.single_tokens["."] = TokenType.DOT;
        core.keywords["FROM"] = TokenType.FROM; core.keywords["GROUP BY"] = TokenType.GROUP_BY;
        core.keywords[">="] = TokenType.GTE;
        core.comments["--"] = null; core.comments["/*"] = "*/";
        core.identifiers["\""] = "\"";
        core.keyword_trie = trie.new_trie(new[] { "SELECT", "FROM", "GROUP BY", ">=", "'", "--", "/*" });
        return core;
    }

    [Fact]
    public void Core_scans_complete_select_and_reuses_instance_without_leaking_tokens()
    {
        var core = Create();
        var tokens = core.tokenize("SELECT a, 12.5 FROM t;");
        Assert.Equal(new[] { TokenType.SELECT, TokenType.VAR, TokenType.COMMA, TokenType.NUMBER, TokenType.FROM, TokenType.VAR, TokenType.SEMICOLON }, tokens.Select(t => t.token_type));
        Assert.Equal(new[] { "SELECT", "a", ",", "12.5", "FROM", "t", ";" }, tokens.Select(t => t.text));
        var next = core.tokenize("SELECT 1");
        Assert.Equal(2, next.Count);
        Assert.NotSame(tokens, next);
        Assert.Equal(7, tokens.Count);
        Assert.Empty(core.tokenize(" \t\r\n"));
    }

    [Fact]
    public void Keywords_collapse_whitespace_and_strings_identifiers_comments_work_together()
    {
        var tokens = Create().tokenize("/* note */ SELECT 'it''s', \"a\" GROUP  BY t >= 2 -- tail");
        Assert.Equal(" note ", Assert.Single(tokens[0].comments));
        Assert.Equal("it's", tokens.Single(t => t.token_type == TokenType.STRING).text);
        Assert.Equal("a", tokens.Single(t => t.token_type == TokenType.IDENTIFIER).text);
        Assert.Equal("GROUP BY", tokens.Single(t => t.token_type == TokenType.GROUP_BY).text);
        Assert.Contains(tokens, t => t.token_type == TokenType.GTE);
        Assert.Equal(" tail", Assert.Single(tokens[^1].comments));
    }

    [Fact]
    public void Tokenize_wraps_original_errors_with_source_context()
    {
        var error = Assert.Throws<errors.TokenError>(() => Create().tokenize("SELECT 'unfinished"));
        Assert.StartsWith("Error tokenizing '", error.Message);
        Assert.IsType<errors.TokenError>(error.InnerException);
        Assert.Equal(0, error.start);
        Assert.NotNull(error.end);
    }

    [Fact]
    public void Command_scanning_restores_tokens_and_emits_argument_string()
    {
        var core = Create();
        core.keywords["SHOW"] = TokenType.SHOW;
        core.commands.Add(TokenType.SHOW);
        var tokens = core.tokenize("SHOW tables;");
        Assert.Equal(new[] { TokenType.SHOW, TokenType.STRING, TokenType.SEMICOLON }, tokens.Select(t => t.token_type));
        Assert.Equal("tables", tokens[1].text);
    }
}
