namespace SqlGlotSharp.Tests;

public class ExpressionTraversalTests
{
    [Fact]
    public void Walk_bfs_and_dfs_preserve_argument_and_list_order()
    {
        var a = new exp.Var(@this: "a");
        var b = new exp.Var(@this: "b");
        var c = new exp.Var(@this: "c");
        var branch = new exp.Expression(new PyDict<string, object?> { ["children"] = new List<object?> { a, "ignored", b } });
        var root = new exp.Expression(new PyDict<string, object?> { ["left"] = branch, ["right"] = c });
        Assert.Equal(new exp.Expr[] { root, branch, c, a, b }, root.walk());
        Assert.Equal(new exp.Expr[] { root, branch, a, b, c }, root.walk(bfs: false));
        Assert.Equal(new exp.Expr[] { c, branch }, root.iter_expressions(reverse: true));
        Assert.Equal(new exp.Expr[] { b, a }, branch.iter_expressions(reverse: true));
        Assert.Equal(new exp.Expr[] { root, branch, c }, root.walk(prune: node => ReferenceEquals(node, branch)));
        Assert.Equal(new exp.Expr[] { root }, root.dfs(prune: _ => true));
    }

    [Fact]
    public void Find_matches_types_and_traits_and_ancestor_helpers_use_parent_links()
    {
        var variable = new exp.Var(@this: "x");
        var sum = new exp.Sum(@this: variable);
        var select = new exp.Select(expressions: new List<object?> { sum });
        Assert.Same(sum, select.find([typeof(exp.Func)], true));
        Assert.Same(variable, select.find<exp.Var>());
        Assert.Same(select, variable.parent_select);
        Assert.Same(select, variable.root());
        Assert.Equal(2, variable.depth);
        Assert.Null(select.find<exp.Column>());
        Assert.Equal(new exp.Var[] { variable }, select.find_all<exp.Var>());
        Assert.Empty(select.find_all(Array.Empty<Type>()));
        Assert.Null(select.find_ancestor(typeof(exp.Select)));
    }

    [Fact]
    public void Deep_traversal_does_not_use_the_call_stack()
    {
        exp.Expr leaf = new exp.Var(@this: "x");
        exp.Expr root = leaf;
        for (var i = 0; i < 10000; i++) root = new exp.Paren(@this: root);
        Assert.Equal(10001, root.dfs().Count());
        Assert.Equal(10000, leaf.depth);
        Assert.Same(root, leaf.root());
        Assert.True(((exp.Expr)root.args["this"]!).same_parent);
    }
}
