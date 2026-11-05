using System.Collections.Frozen;

namespace SqlGlotSharp.Tests;

public class SingleValuedMappingTests
{
    [Fact]
    public void Existing_sets_are_shared_and_values_are_not_copied()
    {
        var keys = new HashSet<string> { "a", "b" };
        var value = new List<int> { 1 };
        var mapping = new helper.SingleValuedMapping<string, List<int>>(keys, value);
        Assert.Equal(2, mapping.Count);
        Assert.Same(value, mapping["a"]);
        keys.Add("c");
        Assert.Equal(3, mapping.Count);
        Assert.Same(value, mapping["c"]);
        Assert.Same(keys, mapping.Keys);
        Assert.Equal(3, py.len(mapping));
        Assert.All(mapping.Values, item => Assert.Same(value, item));
    }

    [Fact]
    public void Non_set_keys_are_deduplicated_and_copied()
    {
        var keys = new List<string> { "a", "a", "b" };
        var mapping = new helper.SingleValuedMapping<string, object?>(keys, null);
        keys.Add("c");
        Assert.Equal(2, mapping.Count);
        Assert.Null(mapping["a"]);
        Assert.False(mapping.TryGetValue("c", out _));
        Assert.Throws<KeyNotFoundException>(() => mapping["c"]);
        Assert.Equal(new[] { "a", "b" }, mapping.keys().Order());
        Assert.Equal(new object?[] { "a", "b" }, helper.flatten(new object[] { mapping }));
    }

    [Fact]
    public void Frozen_and_empty_sets_work_with_runtime_helpers()
    {
        var keys = new[] { "a" }.ToFrozenSet();
        var mapping = new helper.SingleValuedMapping<string, int>(keys, 7);
        Assert.Same(keys, mapping.Keys);
        Assert.Equal("{'a': 7}", py.repr(mapping));
        Assert.True(mapping.TryGetValue("a", out var value));
        Assert.Equal(7, value);
        var empty = new helper.SingleValuedMapping<string, int>(Array.Empty<string>(), 7);
        Assert.False(py.truthy(empty));
        Assert.Empty(empty);
    }
}
