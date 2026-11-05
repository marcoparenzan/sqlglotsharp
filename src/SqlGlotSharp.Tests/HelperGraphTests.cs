namespace SqlGlotSharp.Tests;

public class HelperGraphTests
{
    [Fact]
    public void Topological_sort_adds_implicit_dependencies_and_empties_input()
    {
        var dag = new PyDict<string, HashSet<string>> { ["c"] = ["a", "b"], ["b"] = ["a"], ["z"] = [] };
        Assert.Equal(new[] { "a", "z", "b", "c" }, helper.tsort(dag));
        Assert.Empty(dag);
        Assert.Empty(helper.tsort(new PyDict<string, HashSet<string>>()));
    }

    [Fact]
    public void Cycles_raise_after_removing_resolvable_nodes()
    {
        var dag = new PyDict<string, HashSet<string>> { ["a"] = ["b"], ["b"] = ["a"], ["c"] = [] };
        var error = Assert.Throws<ArgumentException>(() => helper.tsort(dag));
        Assert.StartsWith("Cycle error", error.Message);
        Assert.Equal(new[] { "a", "b" }, dag.Keys);
    }

    [Fact]
    public void Sort_is_ordinal_independent_of_current_culture()
    {
        var dag = new PyDict<string, HashSet<string>> { ["a"] = [], ["Z"] = [], ["A"] = [] };
        Assert.Equal(new[] { "A", "Z", "a" }, helper.tsort(dag));
    }

    [Fact]
    public void Fixed_point_uses_hash_comparison_and_returns_transformed_value()
    {
        var calls = 0;
        Assert.Equal(3, helper.while_changing(0, x => { calls++; return Math.Min(x + 1, 3); }));
        Assert.Equal(4, calls);
    }

    [Theory]
    [InlineData("12", true, true)]
    [InlineData("1.5", false, true)]
    [InlineData("nan", false, true)]
    [InlineData("bad", false, false)]
    public void Numeric_checks_accept_only_valid_conversion_syntax(string text, bool integer, bool floating)
    {
        Assert.Equal(integer, helper.is_int(text));
        Assert.Equal(floating, helper.is_float(text));
    }

    [Fact]
    public void Type_check_does_not_swallow_unrelated_errors() =>
        Assert.Throws<InvalidOperationException>(() => helper.is_type<int>("x", _ => throw new InvalidOperationException()));
}
