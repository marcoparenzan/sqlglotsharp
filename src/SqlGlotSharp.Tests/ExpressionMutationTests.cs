namespace SqlGlotSharp.Tests;

public class ExpressionMutationTests
{
    [Fact]
    public void Append_sets_parent_index_and_invalidates_ancestor_hashes()
    {
        var select = new exp.Select();
        var outer = new exp.Paren(@this: select);
        select._hash = 1; outer._hash = 2;
        var first = new exp.Var(@this: "a"); var second = new exp.Var(@this: "b");
        select.append("expressions", first); select.append("expressions", second);
        Assert.Same(select, second.parent);
        Assert.Equal("expressions", second.arg_key);
        Assert.Equal(1, second.index);
        Assert.Null(select._hash); Assert.Null(outer._hash);
    }

    [Fact]
    public void Replace_and_pop_relink_children_and_clear_detached_node_state()
    {
        var a = new exp.Var(@this: "a"); var b = new exp.Var(@this: "b");
        var select = new exp.Select(expressions: new List<object?> { a, b });
        var replacement = new exp.Var(@this: "c");
        Assert.Same(replacement, a.replace(replacement));
        Assert.Same(select, replacement.parent);
        Assert.Null(a.parent); Assert.Null(a.arg_key); Assert.Null(a.index);
        Assert.Same(replacement, replacement.pop());
        Assert.Same(b, Assert.Single(select.expressions));
        Assert.Equal(0, b.index);
        Assert.Null(replacement.parent);
    }

    [Fact]
    public void Replacing_scalar_child_with_list_replaces_its_parent_in_the_grandparent()
    {
        var a = new exp.Var(@this: "a");
        var alias = new exp.Alias(@this: a);
        var select = new exp.Select(expressions: new List<object?> { alias });
        var replacements = new List<object?> { new exp.Var(@this: "b"), new exp.Var(@this: "c") };
        a.replace(replacements);
        Assert.Equal(replacements, select.expressions);
        Assert.Null(alias.parent);
        Assert.Null(a.parent);
    }

    [Fact]
    public void Metadata_lookup_is_lazy_and_distinguishes_null_from_missing()
    {
        var node = new exp.Var(@this: "x");
        Assert.Equal("fallback", node.meta_get("missing", "fallback"));
        Assert.Null(node._meta);
        node.meta["null"] = null;
        Assert.Null(node.meta_get("null", "fallback"));
        Assert.Same(node.meta, node.meta);
    }

    [Fact]
    public void Comment_metadata_and_prepend_follow_python_behavior()
    {
        var node = new exp.Var(@this: "x");
        node.add_comments(["note sqlglot.meta flag, enabled=false, label=hello"]);
        Assert.Equal(true, node.meta["flag"]);
        Assert.Equal(false, node.meta["enabled"]);
        Assert.Equal("hello", node.meta["label"]);
        node.add_comments(["first"], prepend: true);
        Assert.Equal("first", node.comments![0]);
        var comments = node.comments;
        Assert.Same(comments, node.pop_comments());
        Assert.Null(node.comments);
        Assert.Empty(node.pop_comments());
    }
}
