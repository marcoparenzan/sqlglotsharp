using Core = SqlGlotSharp.tokenizer_core.TokenizerCore;

namespace SqlGlotSharp.Tests;

public class TokenizerStringTests
{
    private static Core Start(string sql, int delimiterLength = 1)
    {
        var core = TokenizerStateTests.Create();
        core.sql = sql; core.size = py.len(sql);
        core._advance(delimiterLength + 1);
        return core;
    }

    [Fact]
    public void Simple_string_fast_path_preserves_unicode_and_newline_positions()
    {
        var core = Start("'a😀\nb'");
        Assert.Equal("a😀\nb", core._extract_string("'"));
        Assert.Equal((2, 2, 6), (core._line, core._col, core._current));
        Assert.True(core._end);
        Assert.Equal("'", core._char);
        Assert.Equal("", Start("''")._extract_string("'"));
    }

    [Fact]
    public void Doubled_delimiters_and_fixed_escapes_are_decoded()
    {
        Assert.Equal("it's", Start("'it''s'")._extract_string("'"));
        var core = Start("'a\\nb'");
        core.string_escapes.Add("\\");
        core.unescaped_sequences["\\n"] = "\n";
        Assert.Equal("a\nb", core._extract_string("'"));
        core = Start("'a\\qb'"); core.string_escapes.Add("\\"); core.drop_unknown_escapes = true;
        Assert.Equal("aqb", core._extract_string("'"));
    }

    [Fact]
    public void Raw_and_multichar_delimiters_follow_configuration()
    {
        var core = Start("'a\\nb'"); core.string_escapes.Add("\\"); core.unescaped_sequences["\\n"] = "\n";
        Assert.Equal("a\\nb", core._extract_string("'", raw_string: true));
        Assert.Equal("abc", Start("$$abc$$", 2)._extract_string("$$"));
    }

    [Fact]
    public void Byte_literals_encode_non_ascii_as_utf8_bytes()
    {
        Assert.Equal("Ã©", Start("'é'")._extract_string("'", bytes_literal: true));
        var core = Start("'\\x41'"); core.string_escapes.Add("\\"); core.numeric_escapes["x"] = (16, 2, 2, 255);
        Assert.Equal("A", core._extract_string("'"));
    }

    [Fact]
    public void Missing_delimiters_raise_the_upstream_message_or_allow_unmatched_text()
    {
        Assert.Equal("Missing ' from 1:0", Assert.Throws<errors.TokenError>(() => Start("'abc")._extract_string("'")).Message);
        Assert.Equal("abc", Start("'abc")._extract_string("'", raise_unmatched: false));
        Assert.Equal("\ud800a", Start("'\ud800a'")._extract_string("'"));
    }
}
