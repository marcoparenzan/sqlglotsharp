namespace SqlGlotSharp.Tests;

public class TimeUnitTests
{
    [Theory]
    [InlineData("D", "DAY")]
    [InlineData("MS", "MILLISECOND")]
    [InlineData("Q", "QUARTER")]
    [InlineData("minute", "MINUTE")]
    [InlineData("d", "D")]
    public void Var_unit_uses_exact_upstream_abbreviation_rules(string input, string expected)
    {
        var original = new exp.Var(@this: input);
        var node = new exp.DateDiff(unit: original);
        Assert.IsType<exp.Var>(node.unit);
        Assert.Equal(expected, node.unit!.name);
        Assert.Equal("unit", node.unit.arg_key);
        Assert.Same(node, node.unit.parent);
        Assert.Same(node, original.parent); // Upstream does not detach replaced arguments.
    }

    [Fact]
    public void Literal_and_unqualified_column_are_normalized()
    {
        Assert.Equal("HOUR", new exp.DateDiff(unit: new exp.Literal(@this: "H", is_string: true)).unit!.name);
        var column = new exp.Column(@this: new exp.Identifier(@this: "W"));
        Assert.Equal("WEEK", new exp.DateDiff(unit: column).unit!.name);
    }

    [Fact]
    public void Qualified_column_is_preserved()
    {
        var column = new exp.Column(@this: new exp.Identifier(@this: "D"), table: new exp.Identifier(@this: "t"));
        var node = new exp.DateDiff(unit: column);
        Assert.Same(column, node.unit);
        Assert.Equal(2, column.parts.Count);
    }

    [Fact]
    public void Week_normalizes_inner_node_without_expanding_abbreviation()
    {
        var week = new exp.Week(@this: new exp.Var(@this: "monday"));
        var node = new exp.DateDiff(unit: week);
        Assert.Same(week, node.unit);
        var inner = Assert.IsType<exp.Var>(week.@this);
        Assert.Equal("MONDAY", inner.name);
        Assert.Same(week, inner.parent);
        Assert.Equal("this", inner.arg_key);
    }

    [Fact]
    public void Missing_null_and_raw_string_units_are_preserved()
    {
        Assert.False(new exp.DateDiff().args.ContainsKey("unit"));
        Assert.Null(new exp.DateDiff(unit: null).args["unit"]);
        Assert.Equal("D", new exp.DateDiff(unit: "D").args["unit"]);
    }

    [Fact]
    public void Indexed_set_splices_reindexes_and_invalidates_ancestor_hash()
    {
        var first = new exp.Var(@this: "a");
        var second = new exp.Var(@this: "b");
        var select = new exp.Select(expressions: new List<object?> { first, second });
        select._hash = 99;
        first._hash = 22;
        first.set("this", "x");
        Assert.Null(first._hash);
        Assert.Null(select._hash);
        select.set("expressions", new List<object?> { new exp.Var(@this: "c"), new exp.Var(@this: "d") }, index: 0);
        Assert.Equal(3, select.expressions.Count);
        Assert.Equal(2, second.index);
        select.set("expressions", null, index: 1);
        Assert.Equal(2, select.expressions.Count);
        Assert.Equal(1, second.index);
    }

    [Fact]
    public void Negative_splice_uses_slice_index_after_removal_like_python()
    {
        var first = new exp.Var(@this: "a");
        var second = new exp.Var(@this: "b");
        var third = new exp.Var(@this: "c");
        var replacement = new exp.Var(@this: "x");
        var select = new exp.Select(expressions: new List<object?> { first, second, third });
        select.set("expressions", new List<object?> { replacement }, index: -1);
        Assert.Equal(new object?[] { first, replacement, second }, select.expressions);
        Assert.Equal(1, replacement.index);
        Assert.Equal(2, second.index);
    }

    [Fact]
    public void Set_deletes_scalar_and_handles_negative_index()
    {
        var node = new exp.Var(@this: "a");
        node.set("this", null);
        Assert.Empty(node.args);
        var select = new exp.Select(expressions: new List<object?> { new exp.Var(@this: "a"), node });
        var replacement = new exp.Var(@this: "b");
        select.set("expressions", replacement, index: -1);
        Assert.Same(replacement, select.expressions[1]);
        Assert.Equal(1, replacement.index);
    }
}
