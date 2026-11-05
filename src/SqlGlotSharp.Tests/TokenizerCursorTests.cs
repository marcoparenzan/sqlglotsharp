namespace SqlGlotSharp.Tests;

public class TokenizerCursorTests
{
    [Fact]
    public void Advance_tracks_previous_character_newlines_and_crlf()
    {
        var core = TokenizerStateTests.Create();
        core.sql = "a\r\nb\rc\nd"; core.size = 8;
        var expected = new[] { (1, 1), (1, 2), (1, 2), (2, 1), (2, 2), (3, 1), (3, 2), (4, 1) };
        foreach (var (line, col) in expected)
        {
            core._advance();
            Assert.Equal((line, col), (core._line, core._col));
        }
        Assert.True(core._end);
        Assert.Equal("", core._peek);
        Assert.Equal("d", core._char);
    }

    [Fact]
    public void Unicode_cursor_indexes_code_points_and_preserves_unpaired_surrogates()
    {
        var core = TokenizerStateTests.Create();
        core.sql = "a😀b"; core.size = 3;
        core._advance();
        Assert.Equal("a😀", core._chars(2));
        core._advance();
        Assert.Equal("😀", core._char);
        Assert.Equal("a😀", core._text);
        Assert.Equal(2, core._col);
        Assert.Equal("", core._chars(3));
        core._advance();
        Assert.Equal("a😀b", core._text);
        core.reset();
        core.sql = "\ud800x"; core.size = 2;
        core._advance();
        Assert.Equal("\ud800", core._char);
        Assert.Equal("\ud800", core._text);
    }

    [Fact]
    public void Alnum_fast_path_includes_python_numeric_categories()
    {
        var core = TokenizerStateTests.Create();
        core.sql = "a²Ⅲ9_ x"; core.size = 7;
        core._advance(alnum: true);
        Assert.Equal(4, core._current);
        Assert.Equal(4, core._col);
        Assert.Equal("9", core._char);
        Assert.Equal("_", core._peek);
        Assert.Equal("a²Ⅲ9", core._text);
        core._start = 1;
        Assert.Equal("²Ⅲ9", core._text);
    }
}
