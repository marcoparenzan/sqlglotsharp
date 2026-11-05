namespace SqlGlotSharp.Tests;

public class ExpressionHashAndNameTests
{
    [Fact]
    public void Hash_equality_normalizes_identifier_case_but_not_literal_contents()
    {
        var upper = new exp.Var(@this: "ABC"); var lower = new exp.Var(@this: "abc");
        Assert.Equal(upper, lower);
        Assert.Equal(upper.GetHashCode(), lower.GetHashCode());
        Assert.NotEqual(new exp.Literal(@this: "ABC", is_string: true), new exp.Literal(@this: "abc", is_string: true));
        Assert.NotEqual<exp.Expr>(upper, new exp.Identifier(@this: "abc"));
        Assert.False(upper.__ne__(lower));
    }

    [Fact]
    public void Hash_ignores_metadata_and_follows_falsy_argument_rules()
    {
        var empty = new exp.Expression(new PyDict<string, object?>());
        Assert.Equal(empty, new exp.Expression(new PyDict<string, object?> { ["unused"] = null }));
        Assert.Equal(empty, new exp.Expression(new PyDict<string, object?> { ["unused"] = false }));
        Assert.NotEqual(empty, new exp.Expression(new PyDict<string, object?> { ["unused"] = 0 }));
        var first = new exp.Var(@this: "x"); var second = new exp.Var(@this: "x");
        first.comments = ["note"]; first.meta["flag"] = true;
        Assert.Equal(first, second);
    }

    [Fact]
    public void Mutations_invalidate_cached_hashes_and_deep_hashing_is_iterative()
    {
        var leaf = new exp.Var(@this: "x");
        exp.Expr root = leaf;
        for (var i = 0; i < 5000; i++) root = new exp.Paren(@this: root);
        var before = root.__hash__();
        Assert.NotNull(leaf._hash);
        leaf.set("this", "y");
        Assert.Null(root._hash);
        Assert.NotEqual(before, root.__hash__());
        Assert.Equal(root, root.copy());
    }

    [Fact]
    public void Alias_names_and_leaf_detection_use_ast_arguments()
    {
        var identifier = new exp.Identifier(@this: "alias");
        var alias = new exp.TableAlias(@this: identifier, columns: new List<object?> { new exp.Identifier(@this: "c1") });
        var table = new exp.Table(@this: new exp.Identifier(@this: "t"), alias: alias);
        Assert.Equal("alias", table.alias);
        Assert.Equal("alias", table.alias_or_name);
        Assert.Equal(new[] { "c1" }, table.alias_column_names);
        Assert.False(table.is_leaf());
        Assert.True(new exp.Var(@this: "x").is_leaf());
        Assert.False(new exp.Expression(new PyDict<string, object?> { ["list"] = new List<object?> { "not a node" } }).is_leaf());
    }
}
