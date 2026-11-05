using System.Globalization;
using System.Numerics;

namespace SqlGlotSharp.generators;

public static partial class postgres
{
    public partial class PostgresGenerator
    {
        // Partial only at the existing Literal.to_py numeric Int64 boundary (explicit failure).
        // py: sqlglot/generators/postgres.py::PostgresGenerator.interval_sql #0000000000
        public override string interval_sql(exp.Interval expression)
        {
            var unit = py.lower(expression.text("unit"));
            if (unit.StartsWith("quarter", StringComparison.Ordinal) && expression.@this is exp.Literal value)
            {
                object? number;
                try { number = value.to_py(); }
                catch (OverflowException)
                {
                    throw new NotPortedException("PostgresGenerator.interval_sql: Literal.to_py arbitrary-precision numeric integers");
                }
                var quarters = interval_quarters(number!);
                if (quarters is null) unsupported("Postgres intervals need a whole number of quarters");
                else
                {
                    value.replace(exp.Literal.@string((quarters.Value * 3).ToString(CultureInfo.InvariantCulture)));
                    ((exp.Expr)expression.args["unit"]!).replace(new exp.Var(@this: "MONTH"));
                }
            }
            return base.interval_sql(expression);
        }

        // Local exact to_int closure: runtime helper.to_int cannot consume PyDecimal and is
        // Int64-bounded. Do not round decimals or expand enormous exponents.
        private static BigInteger? interval_quarters(object value)
        {
            PyDecimal number;
            try
            {
                number = value as PyDecimal ?? new PyDecimal(py.str(value));
            }
            catch (FormatException) { return null; }
            catch (OverflowException)
            {
                throw new NotPortedException("PostgresGenerator.interval_sql: decimal exponents outside the runtime Int64 range");
            }
            var rendered = number.__str__();
            if (rendered.Contains("NaN", StringComparison.Ordinal) || rendered.Contains("Infinity", StringComparison.Ordinal)) return null;
            var coefficient = number.Coefficient;
            var digits = coefficient.ToString(CultureInfo.InvariantCulture).Length;
            if ((BigInteger)number.Exponent + digits - 1 >= 4300) return null;
            if (coefficient.IsZero) return BigInteger.Zero;
            if (number.Exponent < 0)
            {
                if ((BigInteger)number.Exponent < -digits) return null;
                coefficient = BigInteger.DivRem(coefficient, BigInteger.Pow(10, checked((int)-number.Exponent)), out var remainder);
                if (!remainder.IsZero) return null;
            }
            else coefficient *= BigInteger.Pow(10, checked((int)number.Exponent));
            return number.Negative ? -coefficient : coefficient;
        }
    }
}
