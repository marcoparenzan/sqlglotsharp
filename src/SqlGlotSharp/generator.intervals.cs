namespace SqlGlotSharp;

public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.interval_sql #28a22b4f3f
        public virtual string interval_sql(exp.Interval expression)
        {
            var include_keyword = !AUTO_REFRESH_BARE_INTERVALS ||
                expression.find_ancestor(typeof(exp.AutoRefreshProperty), typeof(exp.Select)) is not exp.AutoRefreshProperty;
            var interval_keyword = include_keyword ? "INTERVAL" : "";
            var unit_expression = expression.args.get("unit");
            var unit = py.truthy(unit_expression) ? sql(unit_expression) : "";
            if (!INTERVAL_ALLOWS_PLURAL_FORM) unit = TIME_PART_SINGULARS.get(unit, unit)!;
            unit = unit.Length != 0 ? " " + unit : "";

            if (SINGLE_STRING_INTERVAL)
            {
                var value = py.truthy(expression.@this) ? ((exp.Expr)expression.@this!).name : "";
                if (value.Length != 0)
                {
                    interval_keyword = interval_keyword.Length != 0 ? interval_keyword + " " : "";
                    if (unit_expression is exp.IntervalSpan)
                        return $"{interval_keyword}{sql(exp.Literal.@string(value))}{unit}";
                    return $"{interval_keyword}{sql(exp.Literal.@string(value + unit))}";
                }
                return interval_keyword + unit;
            }

            var this_sql = sql(expression, "this");
            if (this_sql.Length != 0)
            {
                var value = (exp.Expr)expression.@this!;
                if (!include_keyword && value.is_string) this_sql = value.name;
                var types = UNWRAPPED_INTERVAL_VALUES;
                if (!(types.Item1.IsInstanceOfType(value) || types.Item2.IsInstanceOfType(value) ||
                    types.Item3.IsInstanceOfType(value) || types.Item4.IsInstanceOfType(value)))
                    this_sql = $"({this_sql})";
                if (include_keyword) this_sql = " " + this_sql;
            }
            return interval_keyword + this_sql + unit;
        }
    }
}
