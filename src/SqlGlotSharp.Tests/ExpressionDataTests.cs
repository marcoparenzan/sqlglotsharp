using System.Text.Json;

namespace SqlGlotSharp.Tests;

public class ExpressionDataTests
{
    [Fact]
    public void Primitive_and_node_kind_flags_dispatch_through_expr()
    {
        exp.Expr literal = new exp.Literal(@this: "1", is_string: false);
        Assert.True(literal.is_primitive);
        Assert.True(literal._hash_raw_args);
        Assert.True(new exp.Cast().is_cast);
        Assert.True(new exp.Subquery().is_subquery);
        Assert.True(new exp.DataType().is_data_type);
    }

    [Fact]
    public void Unix_scale_constants_preserve_numeric_literal_data()
    {
        var node = new exp.UnixToTime();
        Assert.Equal("0", node.SECONDS.name);
        Assert.Equal("3", node.MILLIS.name);
        Assert.Equal("6", node.MICROS.name);
        Assert.Equal("9", node.NANOS.name);
        Assert.Equal(false, node.NANOS.args["is_string"]);
        Assert.Null(node.NANOS.parent);
    }

    [Fact]
    public void Expression_attribute_inventory_has_no_unclassified_data()
    {
        var root = Path.GetFullPath(Path.Combine(SourceDirectory(), "..", ".."));
        using var inventory = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src", "tools", "expression_data_inventory.json")));
        var rows = inventory.RootElement.GetProperty("attributes").EnumerateArray().ToArray();
        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.Contains(row.GetProperty("status").GetString(),
            new[] { "metadata", "generated", "enum_alias", "existing_trait_data", "structural", "handwritten" }));
        Assert.Equal(24, inventory.RootElement.GetProperty("counts").GetProperty("generated").GetInt32());
    }

    private static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
