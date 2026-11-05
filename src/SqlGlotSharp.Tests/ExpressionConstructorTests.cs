namespace SqlGlotSharp.Tests;

public class ExpressionConstructorTests
{
    [Fact]
    public void Omitted_and_explicit_null_are_distinct()
    {
        Assert.Empty(new exp.Select().args);
        var node = new exp.Identifier(@this: null);
        Assert.True(node.args.ContainsKey("this"));
        Assert.Null(node.args["this"]);
    }

    [Fact]
    public void Scalar_child_has_parent_and_argument_key()
    {
        var child = new exp.Identifier(@this: "a");
        var column = new exp.Column(@this: child);
        Assert.Same(child, column.args["this"]);
        Assert.Same(column, child.parent);
        Assert.Equal("this", child.arg_key);
        Assert.Null(child.index);
    }

    [Fact]
    public void List_children_preserve_original_indexes()
    {
        var first = new exp.Column(@this: new exp.Identifier(@this: "a"));
        var second = new exp.Column(@this: new exp.Identifier(@this: "b"));
        var select = new exp.Select(expressions: new List<object?> { first, null, second });
        Assert.Same(select, first.parent);
        Assert.Same(select, second.parent);
        Assert.Equal(0, first.index);
        Assert.Equal(2, second.index);
        Assert.Equal("expressions", second.arg_key);
    }

    [Fact]
    public void Time_unit_initialization_now_normalizes_abbreviation()
    {
        var node = new exp.DateDiff(unit: new exp.Var(@this: "D"));
        Assert.Equal("DAY", node.unit!.name);
        Assert.Same(node, node.unit.parent);
    }
}
