namespace SqlGlotSharp.Tests;

public class DataTypeConstructionTests
{
    [Fact]
    public void Type_assignment_accepts_enum_and_preserves_data_type_getter_rules()
    {
        var variable = new exp.Var(@this: "x");
        variable.type = exp.DType.INT;
        Assert.Equal(exp.DType.INT, variable.type!.@this);
        Assert.True(variable.is_type(exp.DType.INT));
        var datatype = new exp.DataType(@this: exp.DType.FLOAT);
        Assert.Same(datatype, datatype.type);
        var cast = new exp.Cast(@this: variable, to: datatype);
        Assert.Same(datatype, cast.type);
        Assert.True(cast.is_type(exp.DType.FLOAT)); // Cast.is_type compares the cast target (upstream: self.to.is_type).
        cast.type = exp.DType.INT;
        Assert.False(cast.is_type(exp.DType.INT)); // the annotation does not change the cast target
        variable.type = null;
        Assert.Null(variable.type);
    }

    [Fact]
    public void Build_copies_existing_type_and_preserves_enum_kwargs_semantics()
    {
        var original = new exp.DataType(@this: exp.DType.INT);
        Assert.NotSame(original, exp.DataType.build(original));
        Assert.Same(original, exp.DataType.build(original, copy: false));
        var built = exp.DataType.build(exp.DType.INT, kwargs: new PyKwargs { ["nullable"] = true, ["unused"] = null });
        Assert.Equal(true, built.args["nullable"]);
        Assert.False(built.args.ContainsKey("unused"));
    }

    [Fact]
    public void Structural_type_comparison_respects_nested_types_and_nullable_flag()
    {
        var first = new exp.DataType(@this: exp.DType.ARRAY, expressions: new List<object?> { new exp.DataType(@this: exp.DType.INT) });
        var same = (exp.DataType)first.copy();
        var different = new exp.DataType(@this: exp.DType.ARRAY, expressions: new List<object?> { new exp.DataType(@this: exp.DType.FLOAT) });
        Assert.True(first.is_type(same));
        Assert.False(first.is_type(different));
        var nullable = new exp.DataType(@this: exp.DType.INT, nullable: true);
        Assert.True(nullable.is_type(exp.DType.INT));
        Assert.False(nullable.is_type(new object[] { exp.DType.INT }, check_nullable: true));
    }

    [Fact]
    public void User_defined_types_accept_identifier_nodes_and_unknown_bypasses_parser()
    {
        var identifier = new exp.Identifier(@this: "custom");
        var userType = exp.DataType.build(identifier, udt: true);
        Assert.Equal(exp.DType.USERDEFINED, userType.@this);
        Assert.Same(identifier, userType.args["kind"]);
        Assert.Equal(exp.DType.UNKNOWN, exp.DataType.from_str("unknown").@this);
        // from_str parses through the parser (parse_into DataType).
        var varchar = exp.DataType.from_str("VARCHAR(20)");
        Assert.Equal(exp.DType.VARCHAR, varchar.@this);
        Assert.Single(varchar.expressions);
    }
}
