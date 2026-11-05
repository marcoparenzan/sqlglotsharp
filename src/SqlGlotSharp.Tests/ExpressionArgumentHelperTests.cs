namespace SqlGlotSharp.Tests;

public class ExpressionArgumentHelperTests
{
    [Fact]
    public void Literal_and_star_predicates_follow_node_shape()
    {
        Assert.True(new exp.Literal(@this: "text", is_string: true).is_string);
        Assert.False(new exp.Literal(@this: "text", is_string: true).is_number);
        Assert.True(new exp.Neg(@this: new exp.Literal(@this: "1", is_string: false)).is_number);
        Assert.True(new exp.Column(@this: new exp.Star()).is_star);
        Assert.True(new exp.Dot(@this: new exp.Var(@this: "t"), expression: new exp.Star()).is_star);
        Assert.False(new exp.Var(@this: "*").is_star);
    }

    [Fact]
    public void Set_kwargs_uses_normal_mutation_and_preserves_self_identity()
    {
        var node = new exp.Column(@this: new exp.Identifier(@this: "a"));
        var table = new exp.Identifier(@this: "t");
        Assert.Same(node, node.set_kwargs(new PyKwargs { ["table"] = table }));
        Assert.Same(node, table.parent);
        Assert.Equal("t", node.table);
        node.set_kwargs(new PyKwargs { ["table"] = null });
        Assert.False(node.args.ContainsKey("table"));
    }

    [Fact]
    public void Column_to_dot_copies_qualified_parts_but_returns_single_part_directly()
    {
        var identifier = new exp.Identifier(@this: "a");
        Assert.Same(identifier, new exp.Column(@this: identifier).to_dot());
        var qualified = new exp.Column(@this: new exp.Identifier(@this: "a"), table: new exp.Identifier(@this: "t"));
        var dot = Assert.IsType<exp.Dot>(qualified.to_dot());
        Assert.NotSame(qualified.args["table"], dot.@this);
        Assert.Equal("a", dot.name);
        Assert.Same(qualified, ((exp.Expr)qualified.args["table"]!).parent);
    }
}
