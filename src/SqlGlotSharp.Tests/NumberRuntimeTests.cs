using System.Globalization;

namespace SqlGlotSharp.Tests;

public class NumberRuntimeTests
{
    [Theory]
    [InlineData(" +1_234 ", 1234L)]
    [InlineData("-٠١٢", -12L)]
    [InlineData("００７", 7L)]
    public void Integer_strings_accept_python_decimal_syntax(string text, long expected) => Assert.Equal(expected, py.@int(text));

    [Theory]
    [InlineData("1__2")]
    [InlineData("_12")]
    [InlineData("12_")]
    [InlineData("1.0")]
    [InlineData("0x10")]
    [InlineData("²")]
    public void Invalid_integer_literals_are_rejected(string text) => Assert.Throws<FormatException>(() => py.@int(text));

    [Fact]
    public void Integer_numbers_truncate_and_overflow_is_explicit()
    {
        Assert.Equal(-3L, py.@int(-3.9));
        Assert.Equal(3L, py.@int(3.9m));
        Assert.Equal(1L, py.@int(true));
        Assert.Throws<OverflowException>(() => py.@int(double.NaN));
        Assert.Throws<OverflowException>(() => py.@int(double.PositiveInfinity));
        Assert.Throws<OverflowException>(() => py.@int("9223372036854775808"));
        Assert.Throws<ArgumentException>(() => py.@int(null!));
    }

    [Theory]
    [InlineData(" +١_٢.٥e-1 ", 1.25)]
    [InlineData(".5", 0.5)]
    [InlineData("1.", 1.0)]
    [InlineData("1e1_0", 1e10)]
    public void Float_strings_accept_python_syntax(string text, double expected) => Assert.Equal(expected, py.@float(text));

    [Theory]
    [InlineData("1,5")]
    [InlineData("1_.5")]
    [InlineData("1._5")]
    [InlineData("--inf")]
    [InlineData(".")]
    public void Invalid_float_literals_are_rejected(string text) => Assert.Throws<FormatException>(() => py.@float(text));

    [Fact]
    public void Float_conversion_is_culture_independent_and_supports_special_values()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("it-IT");
            Assert.Equal(1.5, py.@float("1.5"));
            Assert.Equal(1.0, py.@float(true));
            Assert.True(double.IsNaN(py.@float("NaN")));
            Assert.Equal(double.NegativeInfinity, py.@float("-Infinity"));
            Assert.Equal(double.PositiveInfinity, py.@float("1e9999"));
        }
        finally { CultureInfo.CurrentCulture = old; }
    }
}
