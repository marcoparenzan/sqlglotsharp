namespace SqlGlotSharp.Tests;

public class StringRuntimeTests
{
    [Theory]
    [InlineData("hello", "'hello'")]
    [InlineData("it's", "\"it's\"")]
    [InlineData("a'\"b", "'a\\'\"b'")]
    [InlineData("a\n\t\\", "'a\\n\\t\\\\'")]
    [InlineData("😀é", "'😀é'")]
    [InlineData("\u00a0\u200b", "'\\xa0\\u200b'")]
    public void String_repr_uses_python_quotes_and_escapes(string input, string expected)
    {
        Assert.Equal(expected, py.repr(input));
        Assert.Equal(input, py.str(input));
    }

    [Theory]
    [InlineData(1.0, "1.0")]
    [InlineData(1e15, "1000000000000000.0")]
    [InlineData(1e16, "1e+16")]
    [InlineData(0.0001, "0.0001")]
    [InlineData(0.00001, "1e-05")]
    [InlineData(-1.25e20, "-1.25e+20")]
    public void Float_repr_uses_python_notation_thresholds(double input, string expected) => Assert.Equal(expected, py.repr(input));

    [Fact]
    public void Scalars_and_recursive_lists_are_represented_without_infinite_recursion()
    {
        Assert.Equal("None", py.str(null));
        Assert.Equal("True", py.repr(true));
        Assert.Equal("-3", py.repr(-3L));
        Assert.Equal("nan", py.repr(double.NaN));
        Assert.Equal("inf", py.repr(double.PositiveInfinity));
        Assert.Equal("-0.0", py.repr(-0.0));
        var list = new List<object?> { "x", false, null };
        Assert.Equal("['x', False, None]", py.repr(list));
        list.Add(list);
        Assert.Equal("['x', False, None, [...]]", py.repr(list));
        Assert.Throws<NotPortedException>(() => py.repr(new object()));
    }
}
