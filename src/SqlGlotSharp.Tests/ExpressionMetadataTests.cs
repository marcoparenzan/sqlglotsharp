namespace SqlGlotSharp.Tests;

public class ExpressionMetadataTests
{
    [Fact]
    public void Cast_metadata_dispatches_through_base_type()
    {
        exp.Expr node = new exp.Cast();
        Assert.Equal("cast", node.key);
        Assert.True(node.arg_types["this"]);
        Assert.True(node.arg_types["to"]);
        Assert.Contains("to", node.required_args);
        Assert.False(node.is_var_len_args);
        Assert.Equal(typeof(exp.Cast), exp.EXPR_CLASSES[node.key]);
    }

    [Fact]
    public void Registry_covers_concrete_types_and_trait_metadata()
    {
        Assert.Equal(typeof(exp.Select), exp.EXPR_CLASSES["select"]);
        Assert.Contains("expressions", new exp.Select().arg_types.Keys);
        // Upstream _sql_names can be empty; sql_names() computes the fallback.
        // Compare its generated runtime snapshot without changing the expected SQL name.
        Assert.Contains("SUM", new exp.Sum().sql_names__helper);
        Assert.Equal(typeof(exp.Sum), exp.EXPR_CLASSES["sum"]);
    }

    [Fact]
    public void Mapping_preserves_insertion_order_on_updates()
    {
        var mapping = new PyDict<string, bool> { ["this"] = true, ["to"] = true };
        mapping["this"] = false;
        Assert.Equal(new[] { "this", "to" }, mapping.Keys);
        Assert.False(mapping["this"]);
    }
}
