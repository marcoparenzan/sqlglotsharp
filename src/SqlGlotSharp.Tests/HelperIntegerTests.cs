namespace SqlGlotSharp.Tests;

public class HelperIntegerTests
{
    [Theory]
    [InlineData("1.000", 1L)]
    [InlineData("12e2", 1200L)]
    [InlineData("1200e-2", 12L)]
    [InlineData("-٠١٢.٠", -12L)]
    [InlineData("_1__2_", 12L)]
    [InlineData("0e-1000000", 0L)]
    [InlineData("-9223372036854775808", long.MinValue)]
    public void Exact_decimal_integers_are_converted(string value, long expected) => Assert.Equal(expected, helper.to_int(value));

    [Theory]
    [InlineData("1.5")]
    [InlineData("1e1000000")]
    [InlineData("1e-1000000")]
    [InlineData("0e1000000")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("bad")]
    [InlineData(".")]
    public void Invalid_nonintegral_and_oversized_exponents_return_null(string value) => Assert.Null(helper.to_int(value));

    [Fact]
    public void Digit_limit_and_int64_limit_are_distinct()
    {
        Assert.Null(helper.to_int("1000", max_digits: 3));
        Assert.Equal(999L, helper.to_int("999", max_digits: 3));
        Assert.Null(helper.to_int("0", max_digits: 0));
        Assert.Equal(12L, helper.to_int(12.0m));
        Assert.Null(helper.to_int(12.5m));
        Assert.Throws<OverflowException>(() => helper.to_int("9223372036854775808"));
        Assert.Throws<OverflowException>(() => helper.to_int("1e20"));
    }
}
