namespace SqlGlotSharp.Tests;

public class ExpressionValidationTests
{
    [Fact]
    public void Position_update_copies_only_position_metadata_and_can_store_nulls()
    {
        var first = new exp.Var(@this: "x");
        first.update_positions(new tokenizer_core.Token(tokenizer_core.TokenType.VAR, "x", 2, 4, 8, 9));
        first.meta["custom"] = true;
        var second = new exp.Var(@this: "y");
        Assert.Same(second, second.update_positions(first));
        Assert.Equal(2, second.meta["line"]);
        Assert.False(second.meta.ContainsKey("custom"));
        var empty = new exp.Var(@this: "z");
        empty.update_positions(new exp.Var(@this: "none"));
        Assert.Null(empty._meta);
        empty.update_positions();
        Assert.True(empty.meta.ContainsKey("line"));
        Assert.Null(empty.meta["line"]);
    }

    [Fact]
    public void Unnest_unalias_and_flatten_remove_only_expected_wrappers()
    {
        var a = new exp.Var(@this: "a"); var b = new exp.Var(@this: "b"); var c = new exp.Var(@this: "c");
        var root = new exp.And(@this: new exp.And(@this: a, expression: b), expression: new exp.Paren(@this: c));
        Assert.Equal(new exp.Expr[] { a, b, c }, root.flatten());
        Assert.IsType<exp.Paren>(root.flatten(unnest: false).Last());
        Assert.Same(c, new exp.Paren(@this: new exp.Paren(@this: c)).unnest());
        Assert.Same(a, new exp.Alias(@this: a).unalias());
        Assert.Same(b, b.unalias());
    }

    [Fact]
    public void Validation_reports_required_arguments_and_checks_function_arity()
    {
        var cast = new exp.Cast(@this: new exp.Var(@this: "x"));
        Assert.Contains("Required keyword: 'to' missing for <class 'sqlglot.expressions.functions.Cast'>", cast.error_messages());
        Assert.Empty(new exp.Var(@this: "x").error_messages());
        var abs = new exp.Abs(@this: new exp.Var(@this: "x"));
        Assert.Contains(abs.error_messages(new object?[] { 1, 2 }), text => text.StartsWith("The number of provided arguments (2)", StringComparison.Ordinal));
        Assert.True(exp.UNITTEST);
        Assert.Throws<ArgumentException>(() => new exp.Var(new PyDict<string, object?> { ["unexpected"] = true }).error_messages());
    }
}
