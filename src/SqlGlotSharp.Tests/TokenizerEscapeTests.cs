using Core = SqlGlotSharp.tokenizer_core.TokenizerCore;

namespace SqlGlotSharp.Tests;

public class TokenizerEscapeTests
{
    private static Core Start(string input, bool bytes = false)
    {
        var core = TokenizerStateTests.Create();
        core.numeric_escapes["0"] = (8, 1, 3, 511);
        core.numeric_escapes["x"] = (16, 2, 2, 255);
        core.numeric_escapes["u"] = (16, 4, 4, 65535);
        core.numeric_escapes["U"] = (16, 8, 8, 0x10ffff);
        core.numeric_escapes_are_bytes = bytes;
        core.sql = input; core.size = py.len(input); core._advance();
        return core;
    }

    [Theory]
    [InlineData(@"\101'", "A")]
    [InlineData(@"\x41'", "A")]
    [InlineData(@"\u0041'", "A")]
    [InlineData(@"\U0001F600'", "😀")]
    [InlineData(@"\uD83D\uDE00'", "😀")]
    public void Numeric_escapes_decode_and_stop_after_digits(string input, string expected)
    {
        var core = Start(input);
        Assert.Equal(expected, core._scan_numeric_escape());
        Assert.Equal("'", core._char);
    }

    [Fact]
    public void Byte_escapes_wrap_octal_and_decode_utf8_or_fallback_per_byte()
    {
        Assert.Equal("a", Start(@"\541'", true)._scan_numeric_escape());
        Assert.Equal("é", Start(@"\xC3\xA9'", true)._scan_numeric_escape());
        Assert.Equal("😀", Start(@"\xF0\x9F\x98\x80'", true)._scan_numeric_escape());
        var invalid = Start(@"\xC3\x41'", true);
        Assert.Equal("Ã", invalid._scan_numeric_escape());
        Assert.Equal("\\", invalid._char);
    }

    [Fact]
    public void Incomplete_end_and_lone_surrogate_escapes_are_left_to_caller()
    {
        foreach (var input in new[] { @"\x4'", @"\uD800'", @"\x41", @"\q'" })
        {
            var core = Start(input);
            Assert.Equal("", core._scan_numeric_escape());
            Assert.Equal(1, core._current);
        }
        var replacement = Start(@"\uD800'");
        replacement.lone_surrogate_replacement = "?";
        Assert.Equal("?", replacement._scan_numeric_escape());
    }

    [Fact]
    public void Numeric_reader_respects_digit_and_value_limits()
    {
        var core = Start("\\xff'");
        Assert.Equal((15, 3), core._read_numeric_escape(2, 16, 1, 2, 15));
        Assert.Equal((-1, 3), core._read_numeric_escape(2, 16, 2, 2, 15));
        Assert.Equal((255, 4), core._read_byte_escape(0));
    }
}
