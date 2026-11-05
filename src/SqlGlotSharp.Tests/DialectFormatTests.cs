using Dialect = SqlGlotSharp.dialects.dialect.Dialect;

namespace SqlGlotSharp.Tests;

public class DialectFormatTests
{
    [Fact]
    public void Time_format_conversion_uses_dialect_mapping_and_literal_factory()
    {
        var postgres = Dialect.get_or_raise("postgres");
        var formatted = Assert.IsType<exp.Literal>(postgres.format_time("'YYYY-MM-DD'"));
        Assert.Equal("%Y-%m-%d", formatted.name);
        Assert.True(formatted.is_string);
        var original = new exp.Var(@this: "not a literal");
        Assert.Same(original, postgres.format_time(original));
        Assert.Null(postgres.format_time(null));
    }

    [Fact]
    public void Json_path_conversion_builds_ast_for_strings_and_numeric_subscripts()
    {
        var dialect = Dialect.get_or_raise("");
        var path = Assert.IsType<exp.JSONPath>(dialect.to_json_path(exp.Literal.@string("$.a")));
        Assert.Equal("a", Assert.IsType<exp.JSONPathKey>(path.expressions[1]).@this);
        var numeric = Assert.IsType<exp.JSONPath>(dialect.to_json_path(exp.Literal.number(3)));
        Assert.Equal(3L, Assert.IsType<exp.JSONPathSubscript>(numeric.expressions[1]).@this);
        var invalid = exp.Literal.@string("$[");
        Assert.Same(invalid, dialect.to_json_path(invalid));
    }
}
