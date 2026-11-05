namespace SqlGlotSharp.Tests;

public class TypingMetadataTests
{
    [Fact]
    public void Metadata_inventory_and_scalar_returns_match_python()
    {
        Assert.Equal(294, typing.EXPRESSION_METADATA.Count);
        Assert.Equal(164, typing.EXPRESSION_METADATA.Values.Count(v => v.ContainsKey("returns")));
        Assert.Equal(130, typing.EXPRESSION_METADATA.Values.Count(v => v.ContainsKey("annotator")));
        Assert.Equal(exp.DType.BOOLEAN, typing.EXPRESSION_METADATA[typeof(exp.Boolean)]["returns"]);
        Assert.Equal(exp.DType.NULL, typing.EXPRESSION_METADATA[typeof(exp.Null)]["returns"]);
        Assert.Equal(6, typing.TIMESTAMP_EXPRESSIONS.Count);
        var dialect = new dialects.dialect.Dialect();
        Assert.NotSame(typing.EXPRESSION_METADATA, dialect.EXPRESSION_METADATA);
        Assert.Same(typing.EXPRESSION_METADATA[typeof(exp.Boolean)], dialect.EXPRESSION_METADATA[typeof(exp.Boolean)]);
    }

    [Fact]
    public void Helper_annotators_preserve_numeric_types_and_promote_integers()
    {
        var spy = new Spy();
        var variable = new exp.Var(@this: "x") { type = exp.DType.DOUBLE };
        var floor = new exp.Floor(@this: variable);
        typing.annotate_by_numeric_arg(spy, floor);
        Assert.Same(variable.type, spy.LastType);
        variable.type = exp.DType.TEXT;
        typing.annotate_by_numeric_arg(spy, floor);
        Assert.Equal(exp.DType.INT, spy.LastType);
        typing.annotate_int_as_double(spy, floor);
        Assert.Equal(exp.DType.DOUBLE, spy.LastType);
    }

    [Fact]
    public void Generated_callbacks_forward_flags_arguments_and_dynamic_case_branches()
    {
        var spy = new Spy();
        var sum = new exp.Sum(@this: new exp.Var(@this: "x"));
        ((typing.Annotation)typing.EXPRESSION_METADATA[typeof(exp.Sum)]["annotator"]!)(spy, sum);
        Assert.True(spy.Promote);
        Assert.Equal(new object?[] { "this", "expressions" }, spy.Arguments);
        var count = new exp.Count(big_int: true);
        ((typing.Annotation)typing.EXPRESSION_METADATA[typeof(exp.Count)]["annotator"]!)(spy, count);
        Assert.Equal(exp.DType.BIGINT, spy.LastType);
        var branch = new exp.Var(@this: "yes");
        var expression = new exp.Case(ifs: new List<object?> { new exp.If(@true: branch) });
        ((typing.Annotation)typing.EXPRESSION_METADATA[typeof(exp.Case)]["annotator"]!)(spy, expression);
        Assert.Same(branch, spy.Arguments[0]);
        Assert.Equal("default", spy.Arguments[1]);
    }

    [Fact]
    public void Missing_optimizer_backend_fails_explicitly()
    {
        typing.ITypeAnnotator backend = new Unimplemented();
        Assert.Throws<NotPortedException>(() => backend._annotate_binary(new exp.Add()));
    }

    private sealed class Unimplemented : typing.ITypeAnnotator { }
    private sealed class Spy : typing.ITypeAnnotator
    {
        public object? LastType;
        public bool Promote;
        public object?[] Arguments = [];
        public exp.Expr _set_type(exp.Expr expression, object? type) { LastType = type; return expression; }
        public exp.Expr _annotate_by_args(exp.Expr expression, bool array = false, bool promote = false, params object?[] args)
        { Promote = promote; Arguments = args; return expression; }
    }
}
