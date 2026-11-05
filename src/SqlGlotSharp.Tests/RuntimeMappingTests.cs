namespace SqlGlotSharp.Tests;

public class RuntimeMappingTests
{
    [Fact]
    public void Default_lookup_does_not_insert_and_setdefault_preserves_order()
    {
        var values = new PyDict<string, object?> { ["a"] = null };
        Assert.Null(values.get("missing"));
        Assert.Equal("fallback", values.get("missing", "fallback"));
        Assert.Null(values.setdefault("a", "replacement"));
        Assert.Equal(3, values.setdefault("b", 3));
        Assert.Equal(new[] { "a", "b" }, values.items().Select(pair => pair.Key));
    }

    [Fact]
    public void Pop_distinguishes_missing_from_null_and_copy_is_shallow()
    {
        var child = new exp.Var(@this: "a");
        var values = new PyDict<string, object?> { ["a"] = null, ["child"] = child };
        var copy = values.copy();
        Assert.Null(copy.pop("a"));
        Assert.Throws<KeyNotFoundException>(() => copy.pop("missing"));
        Assert.Equal(42, copy.pop("missing", 42));
        Assert.True(values.ContainsKey("a"));
        Assert.Same(child, copy["child"]);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("x", true)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(false, false)]
    public void Truthiness_matches_used_python_values(object? value, bool expected) =>
        Assert.Equal(expected, py.truthy(value));

    [Fact]
    public void Collection_truthiness_and_sequence_lookup()
    {
        Assert.False(py.truthy(new List<object?>()));
        Assert.True(py.truthy(new List<object?> { null }));
        Assert.False(py.truthy(new PyDict<string, object?>()));
        Assert.Equal("b", helper.seq_get(new[] { "a", "b" }, -1));
        Assert.Null(helper.seq_get(new[] { "a", "b" }, -3));
        Assert.Null(helper.seq_get(new[] { "a", "b" }, 2));
    }
}
