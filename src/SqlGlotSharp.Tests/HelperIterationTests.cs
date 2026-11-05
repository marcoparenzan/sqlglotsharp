namespace SqlGlotSharp.Tests;

public class HelperIterationTests
{
    [Fact]
    public void Flatten_recurses_lazily_and_preserves_text_bytes_and_sql_nodes()
    {
        var bytes = new byte[] { 1 };
        var node = new exp.Literal(@this: "1");
        object?[] values = [new List<int> { 1, 2 }, 3, (4, "text"), bytes, node, new PyKwargs { ["key"] = 5 }];
        Assert.Equal(new object?[] { 1, 2, 3, 4, "text", bytes, node, "key" }, helper.flatten(values));
        Assert.Equal(new object?[] { "key" }, helper.flatten(new PyKwargs { ["key"] = 5 }));
    }

    [Fact]
    public void Dict_depth_follows_first_value_not_deepest_branch()
    {
        Assert.Equal(0, helper.dict_depth(null));
        Assert.Equal(1, helper.dict_depth(new PyKwargs()));
        var shallowFirst = new PyKwargs { ["a"] = 1, ["b"] = new PyKwargs { ["c"] = new PyKwargs() } };
        Assert.Equal(1, helper.dict_depth(shallowFirst));
        Assert.Equal(3, helper.dict_depth(new PyKwargs { ["a"] = new PyKwargs { ["b"] = new PyKwargs() } }));
        Assert.Equal(2, helper.dict_depth(new Dictionary<string, object> { ["a"] = new Dictionary<string, int>() }));
    }

    [Fact]
    public void First_and_bool_conversion_follow_upstream_rules()
    {
        Assert.Equal(3, helper.first(new[] { 3, 4 }));
        Assert.Throws<InvalidOperationException>(() => helper.first(Array.Empty<int>()));
        Assert.Equal(true, helper.to_bool("TRUE"));
        Assert.Equal(false, helper.to_bool("0"));
        Assert.Equal(" true ", helper.to_bool(" true "));
        Assert.Equal("yes", helper.to_bool("yes"));
        Assert.Null(helper.to_bool(null));
    }

    [Fact]
    public void Ranges_are_sorted_merged_and_do_not_mutate_input()
    {
        (int, int)[] ranges = [(5, 8), (1, 3), (3, 6), (10, 12)];
        Assert.Equal(new[] { (1, 8), (10, 12) }, helper.merge_ranges(ranges));
        Assert.Equal((5, 8), ranges[0]);
        Assert.Empty(helper.merge_ranges(Array.Empty<(int, int)>()));
        Assert.Equal(new[] { ("a", "z") }, helper.merge_ranges(new[] { ("a", "m"), ("b", "z") }));
    }

    [Fact]
    public void Date_unit_detection_uses_node_name_case_insensitively()
    {
        Assert.True(helper.is_date_unit(new exp.Var(@this: "YEAR_MONTH")));
        Assert.False(helper.is_date_unit(new exp.Var(@this: "HOUR")));
        Assert.False(helper.is_date_unit(null));
    }
}
