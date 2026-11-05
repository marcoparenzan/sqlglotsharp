namespace SqlGlotSharp.Tests;

public class ExpressionCopyTests
{
    [Fact]
    public void Copy_recreates_nodes_and_parent_indexes_without_sharing_metadata()
    {
        var variable = new exp.Var(@this: "x");
        variable.comments = ["note"];
        variable._type = new exp.DataType(@this: exp.DType.INT);
        variable.meta["nested"] = new List<object?> { new PyKwargs { ["value"] = 1 } };
        var select = new exp.Select(expressions: new List<object?> { variable });
        var clone = Assert.IsType<exp.Select>(select.copy());
        var child = Assert.IsType<exp.Var>(Assert.Single(clone.expressions));
        Assert.NotSame(variable, child);
        Assert.Same(clone, child.parent);
        Assert.Equal(0, child.index);
        Assert.Null(clone.parent);
        Assert.NotSame(variable.comments, child.comments);
        Assert.NotSame(variable._type, child._type);
        var copiedMeta = Assert.IsType<List<object?>>(child.meta["nested"]);
        var copiedDict = Assert.IsType<PyKwargs>(copiedMeta[0]);
        copiedDict["value"] = 2;
        var originalDict = (PyKwargs)((List<object?>)variable.meta["nested"]!)[0]!;
        Assert.Equal(1, originalDict["value"]);
        child.set("this", "y");
        Assert.Equal("x", variable.name);
    }

    [Fact]
    public void Copy_duplicates_shared_ast_nodes_but_keeps_non_node_argument_values()
    {
        var child = new exp.Var(@this: "x");
        var opaque = new object();
        var root = new exp.Expression(new PyDict<string, object?> { ["children"] = new List<object?> { child, child, opaque } });
        var clone = root.copy();
        var list = (List<object?>)clone.args["children"]!;
        Assert.NotSame(list[0], list[1]);
        Assert.Same(opaque, list[2]);
    }

    [Fact]
    public void Deep_copy_of_ast_is_iterative_and_preserves_empty_metadata()
    {
        exp.Expr root = new exp.Var(@this: "x");
        root._meta = new();
        for (var i = 0; i < 10000; i++) root = new exp.Paren(@this: root);
        var clone = root.copy();
        Assert.Equal(10001, clone.dfs().Count());
        Assert.NotNull(clone.find<exp.Var>()!._meta);
    }

    [Fact]
    public void Metadata_deepcopy_preserves_cycles_and_shared_list_references()
    {
        var list = new List<object?>();
        list.Add(list);
        var clone = Assert.IsType<List<object?>>(py.deepcopy(list));
        Assert.NotSame(list, clone);
        Assert.Same(clone, Assert.Single(clone));
        Assert.Throws<NotPortedException>(() => py.deepcopy(new object()));
    }
}
