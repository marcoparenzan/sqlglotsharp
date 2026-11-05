namespace SqlGlotSharp;

public static partial class exp
{
    public abstract partial class Expr
    {
        // py: sqlglot/expressions/core.py::Expr.to_py #b7e13bb672
        public abstract object? to_py();
        // py: sqlglot/expressions/core.py::Expr.is_int #4079fdbe94
        public abstract bool is_int { get; }
    }
    public partial class Expression
    {
        // py: sqlglot/expressions/core.py::Expression.to_py #0000000000
        public override object? to_py() => throw new NotPortedException("Expression.to_py error formatting requires SQL generation");
        // py: sqlglot/expressions/core.py::Expression.is_int #efff5fe89c
        public override bool is_int => is_number && to_py() is long;
    }
    public partial class Literal
    {
        // py: sqlglot/expressions/core.py::Literal.number #d82b4ce9ea
        public static Expr number(object? number)
        {
            var literal = new Literal(@this: py.str(number), is_string: false);
            try
            {
                var value = literal.to_py();
                if (value is long integer && integer < 0)
                {
                    literal.set("this", py.str(checked(-integer)));
                    return new Neg(@this: literal);
                }
                if (value is PyDecimal decimalValue && decimalValue.IsLessThanZero())
                {
                    literal.set("this", py.str(decimalValue.Abs()));
                    return new Neg(@this: literal);
                }
            }
            catch (Exception) { /* Upstream intentionally retains the original literal on conversion failure. */ }
            return literal;
        }
        // py: sqlglot/expressions/core.py::Literal.string #9532ddc3d4
        public static Literal @string(object? @string) => new(@this: py.str(@string), is_string: true);
        // py: sqlglot/expressions/core.py::Literal.to_py #1a2e8f418d
        public override object? to_py()
        {
            if (!is_number) return @this;
            try { return py.@int(@this!); }
            catch (FormatException)
            {
                try { return new PyDecimal((string)@this!); }
                catch (FormatException error) { throw new FormatException($"Invalid numeric literal: {py.repr(@this)}", error); }
            }
        }
    }
    public partial class Neg
    {
        // py: sqlglot/expressions/core.py::Neg.to_py #69072dd8b4
        public override object? to_py() => ((Expr)@this!).to_py() switch
        {
            long integer => checked(-integer),
            PyDecimal number => number.Negate(),
            _ => throw new ArgumentException("Unary minus requires a numeric value")
        };
    }
    public partial class Null
    {
        // py: sqlglot/expressions/core.py::Null.to_py #8502bed32c
        public override object? to_py() => null;
    }
    public partial class Boolean
    {
        // py: sqlglot/expressions/core.py::Boolean.to_py #9ff5c5bf9c
        public override object? to_py() => @this;
    }
}
