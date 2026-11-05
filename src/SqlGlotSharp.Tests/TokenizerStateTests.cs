using Core = SqlGlotSharp.tokenizer_core.TokenizerCore;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;
using Token = SqlGlotSharp.tokenizer_core.Token;

namespace SqlGlotSharp.Tests;

public class TokenizerStateTests
{
    internal static Core Create() => new(
        single_tokens: new() { ["("] = TokenType.L_PAREN }, keywords: new() { ["SELECT"] = TokenType.SELECT },
        quotes: new() { ["'"] = "'" }, format_strings: new(), identifiers: new(), comments: new(),
        string_escapes: ["'"], byte_string_escapes: [], identifier_escapes: [], commands: [], command_prefix_tokens: [],
        nested_comments: true, dash_comment_requires_boundary: false, comments_terminate_at_newline_only: false,
        hint_start: "/*+", tokens_preceding_hint: [], has_bit_strings: false, has_hex_strings: false,
        numeric_literals: new(), var_single_tokens: [], string_escapes_allowed_in_raw_strings: false,
        heredoc_tag_is_identifier: false, heredoc_string_alternative: TokenType.VAR,
        keyword_trie: trie.new_trie(new[] { "SELECT" }), numbers_can_be_underscore_separated: false,
        numbers_can_have_decimals: true, identifiers_can_start_with_digit: false, unescaped_sequences: new(),
        numeric_escapes: new(), drop_unknown_escapes: false, numeric_escapes_are_bytes: false,
        byte_strings_are_bytes: false, lone_surrogate_replacement: "");

    [Fact]
    public void Constructor_initializes_all_state_and_keeps_configuration_mutable()
    {
        var core = Create();
        Assert.Equal("", core.sql);
        Assert.Equal((0, 0, 0, 1, 0, -1), (core.size, core._start, core._current, core._line, core._col, core._prev_token_line));
        Assert.Empty(core.tokens);
        Assert.Empty(core._comments);
        Assert.False(core._end);
        Assert.True(core.nested_comments);
        Assert.True(core.numbers_can_have_decimals);
        Assert.Equal(TokenType.SELECT, core.keywords["SELECT"]);
        var configuration = core.keywords;
        configuration["FROM"] = TokenType.FROM;
        Assert.Same(configuration, core.keywords);
    }

    [Fact]
    public void Reset_replaces_state_collections_and_keeps_configuration_references()
    {
        var core = Create();
        var oldTokens = core.tokens;
        var oldComments = core._comments;
        var keywords = core.keywords;
        var keywordTrie = core.keyword_trie;
        oldTokens.Add(Token.@var("x"));
        oldComments.Add("note");
        core.sql = "SELECT x"; core.size = 8; core._start = 3; core._current = 8;
        core._line = 4; core._col = 8; core._char = "x"; core._peek = "y"; core._end = true; core._prev_token_line = 4;
        core.reset();
        Assert.NotSame(oldTokens, core.tokens);
        Assert.NotSame(oldComments, core._comments);
        Assert.Single(oldTokens);
        Assert.Single(oldComments);
        Assert.Same(keywords, core.keywords);
        Assert.Same(keywordTrie, core.keyword_trie);
        Assert.Equal("", core.sql);
        Assert.Equal((0, 0, 0, 1, 0, -1), (core.size, core._start, core._current, core._line, core._col, core._prev_token_line));
        Assert.Equal(("", "", false), (core._char, core._peek, core._end));
    }
}
