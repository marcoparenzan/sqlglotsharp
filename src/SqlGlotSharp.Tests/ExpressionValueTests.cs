using System.Text.Json;

namespace SqlGlotSharp.Tests;

public class ExpressionValueTests
{
    [Fact]
    public void Exact_decimal_formatting_matches_python_oracle_without_exponent_expansion()
    {
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(SourceDirectory(), "decimal-cases.json")));
        foreach (var item in data.RootElement.EnumerateArray())
        {
            var value = new PyDecimal(item.GetProperty("input").GetString()!);
            Assert.Equal(item.GetProperty("str").GetString(), py.str(value));
            Assert.Equal(item.GetProperty("repr").GetString(), py.repr(value));
            Assert.Same(value, py.deepcopy(value));
        }
    }

    [Fact]
    public void Literal_factories_normalize_negative_numbers_and_preserve_numeric_kind()
    {
        var integer = exp.Literal.number(-12);
        Assert.IsType<exp.Neg>(integer);
        Assert.Equal(-12L, integer.to_py());
        Assert.True(integer.is_int);
        var number = exp.Literal.number("-1.20e3");
        Assert.IsType<exp.Neg>(number);
        Assert.Equal("1.20E+3", ((exp.Expr)number.@this!).@this);
        Assert.Equal("-1.20E+3", py.str(number.to_py()));
        Assert.False(number.is_int);
        Assert.Equal("text", exp.Literal.@string("text").to_py());
        Assert.Null(new exp.Null().to_py());
        Assert.Equal(true, new exp.Boolean(@this: true).to_py());
    }

    [Fact]
    public void Invalid_numeric_values_report_failure_and_factory_keeps_original_text()
    {
        var literal = new exp.Literal(@this: "not a number", is_string: false);
        Assert.Equal("Invalid numeric literal: 'not a number'", Assert.Throws<FormatException>(() => literal.to_py()).Message);
        Assert.Equal("not a number", exp.Literal.number("not a number").@this);
        Assert.Equal("-0.0", exp.Literal.number(-0.0).@this);
        Assert.Throws<OverflowException>(() => new exp.Literal(@this: "9223372036854775808", is_string: false).to_py());
        Assert.Throws<NotPortedException>(() => new exp.Var(@this: "x").to_py());
    }

    private static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
