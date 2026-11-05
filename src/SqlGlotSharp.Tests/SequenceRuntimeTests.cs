namespace SqlGlotSharp.Tests;

public class SequenceRuntimeTests
{
    [Theory]
    [InlineData(-5, 2, 1, "ab")]
    [InlineData(null, null, -1, "cba")]
    [InlineData(null, -1, -1, "")]
    [InlineData(100, -100, -1, "cba")]
    [InlineData(null, null, 2, "ac")]
    [InlineData(2, 1, 1, "")]
    public void String_slices_match_python(int? start, int? stop, int step, string expected) =>
        Assert.Equal(expected, py.slice("abc", start, stop, step));

    [Fact]
    public void Slice_preserves_unicode_and_handles_empty_lists_and_extreme_steps()
    {
        Assert.Equal("😀", py.slice("a😀b", 1, 2));
        Assert.Equal("b😀a", py.slice("a😀b", step: -1));
        Assert.Empty(py.slice(Array.Empty<int>(), step: -1));
        Assert.Equal(new[] { 3 }, py.slice(new[] { 1, 2, 3 }, step: int.MinValue));
        Assert.Throws<ArgumentException>(() => py.slice("abc", step: 0));
    }

    [Fact]
    public void Logical_operators_return_operands_and_are_lazy()
    {
        var calls = 0;
        string Next() { calls++; return "fallback"; }
        Assert.Equal("yes", py.or("yes", Next));
        Assert.Equal("", py.and("", Next));
        Assert.Equal(0, calls);
        Assert.Equal("fallback", py.or("", Next));
        Assert.Equal("fallback", py.and("yes", Next));
        Assert.Equal(2, calls);
        Assert.Equal(0, py.and(0, 7));
        Assert.Equal(7, py.and(1, 7));
    }

    [Fact]
    public void Zip_stops_at_shortest_sequence()
    {
        Assert.Equal(new[] { (1, "a"), (2, "b") }, py.zip(new[] { 1, 2, 3 }, new[] { "a", "b" }));
        Assert.Empty(py.zip(Array.Empty<int>(), new[] { "x" }));
    }

    [Fact]
    public void Sorting_is_stable_even_when_reversed()
    {
        var values = new[] { (Key: 1, Id: "a"), (Key: 2, Id: "b"), (Key: 1, Id: "c") };
        Assert.Equal(new[] { "a", "c", "b" }, py.sorted(values, x => x.Key).Select(x => x.Id));
        Assert.Equal(new[] { "b", "a", "c" }, py.sorted(values, x => x.Key, reverse: true).Select(x => x.Id));
    }
}
