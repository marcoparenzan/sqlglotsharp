namespace SqlGlotSharp.Tests;

public class FunctionConstructionTests
{
    [Fact]
    public void Fixed_arguments_are_ordered_and_excess_arguments_are_not_bound()
    {
        var argument = new exp.Var(@this: "x");
        var function = Assert.IsType<exp.Abs>(new exp.Abs().from_arg_list([argument, "extra"]));
        Assert.Same(argument, function.@this);
        Assert.Same(function, argument.parent);
        Assert.Single(function.args);
        Assert.Equal("ABS", function.sql_name());
        Assert.IsType<exp.Abs>(function.default_parser_mappings()["ABS"]([new exp.Var(@this: "y")]));
    }

    [Fact]
    public void Variable_arguments_keep_named_prefix_and_collect_remainder()
    {
        var a = new exp.Var(@this: "a"); var b = new exp.Var(@this: "b");
        var function = Assert.IsType<exp.Anonymous>(new exp.Anonymous().from_arg_list(["custom", a, b]));
        Assert.Equal("custom", function.@this);
        Assert.Equal(new object?[] { a, b }, function.expressions);
        Assert.Equal(0, a.index);
        Assert.Equal(1, b.index);
        Assert.Throws<InvalidOperationException>(() => new exp.Var().sql_names());
    }

    [Fact]
    public void Function_catalog_matches_python_counts_and_resolves_aliases()
    {
        Assert.Equal(568, exp.ALL_FUNCTIONS.Count);
        Assert.Equal(634, exp.FUNCTION_BY_NAME.Count);
        Assert.Equal(typeof(exp.Sum), exp.FUNCTION_BY_NAME["SUM"]);
        Assert.All(exp.FUNCTION_BY_NAME.Values, type => Assert.True(typeof(exp.Func).IsAssignableFrom(type)));
        Assert.DoesNotContain(typeof(exp.Anonymous), exp.ALL_FUNCTIONS);
    }

    [Fact]
    public void Node_output_names_and_dot_parts_match_python_rules()
    {
        var column = new exp.Column(@this: new exp.Identifier(@this: "a"), table: new exp.Identifier(@this: "t"));
        var dot = exp.Dot.build([column, new exp.Identifier(@this: "b")]);
        Assert.Equal(new[] { "t", "a", "b" }, dot.parts.Select(node => node.name));
        Assert.Equal("b", dot.output_name);
        Assert.Equal("t", column.table);
        Assert.Equal("a", column.output_name);
        Assert.True(new exp.Identifier(@this: "x", quoted: true).quoted);
        Assert.Equal("?", new exp.Placeholder().name);
        Assert.Equal("alias", new exp.Alias(@this: column, alias: new exp.Identifier(@this: "alias")).output_name);
        Assert.Throws<ArgumentException>(() => exp.Dot.build([column]));
    }
}
