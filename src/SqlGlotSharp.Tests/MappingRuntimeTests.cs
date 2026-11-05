namespace SqlGlotSharp.Tests;

public class MappingRuntimeTests
{
    [Fact]
    public void Merge_is_ordered_last_value_wins_and_does_not_mutate_inputs()
    {
        var child = new List<int> { 1 };
        var first = new PyKwargs { ["a"] = 1, ["b"] = child };
        var second = new PyKwargs { ["a"] = null, ["c"] = 3 };
        var merged = py.merge<string, object?>(first, second);
        Assert.Equal(new[] { "a", "b", "c" }, merged.Keys);
        Assert.Null(merged["a"]);
        Assert.Equal(1, first["a"]);
        Assert.Same(child, merged["b"]);
        merged["d"] = 4;
        Assert.False(first.ContainsKey("d"));
        Assert.Empty(py.merge<string, object?>());
    }

    [Fact]
    public void Kwargs_preserve_explicit_null_without_inserting_omitted_keys()
    {
        var args = new PyKwargs(new Dictionary<string, object?> { ["this"] = null });
        Assert.True(args.ContainsKey("this"));
        Assert.False(args.ContainsKey("to"));
        Assert.Equal(1, py.len(args));
    }

    [Fact]
    public void Len_counts_unicode_code_points_and_collection_items()
    {
        Assert.Equal(3, py.len("a😀b"));
        Assert.Equal(2, py.len("e\u0301"));
        Assert.Equal(2, py.len(new HashSet<int> { 1, 2 }));
        Assert.Equal(0, py.len(Array.Empty<int>()));
        Assert.Throws<ArgumentException>(() => py.len(Enumerable.Range(0, 3).Select(x => x)));
        Assert.Throws<ArgumentNullException>(() => py.len(null!));
    }

    [Fact]
    public void Truthiness_covers_generic_collections_tuples_and_all_numeric_zero_types()
    {
        Assert.False(py.truthy(new HashSet<int>()));
        Assert.True(py.truthy(new HashSet<int> { 1 }));
        Assert.False(py.truthy(ValueTuple.Create()));
        Assert.True(py.truthy(ValueTuple.Create(0)));
        foreach (var zero in new object[] { (byte)0, (sbyte)0, (short)0, (ushort)0, 0U, 0UL, 0f, -0.0 })
            Assert.False(py.truthy(zero));
        Assert.True(py.truthy(double.NaN));
        IEnumerable<int> EmptyIterator() { yield break; }
        Assert.True(py.truthy(EmptyIterator())); // Python iterators are truthy even when empty.
    }

    [Fact]
    public void Enumerate_accepts_a_negative_start_and_is_lazy()
    {
        var touched = false;
        IEnumerable<string> Values() { touched = true; yield return "x"; yield return "y"; }
        var sequence = py.enumerate(Values(), -2);
        Assert.False(touched);
        Assert.Equal(new[] { (-2, "x"), (-1, "y") }, sequence);
    }
}
