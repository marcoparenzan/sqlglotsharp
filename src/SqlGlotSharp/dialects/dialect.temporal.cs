namespace SqlGlotSharp.dialects;

public static partial class dialect
{
    // WeekStart naming/warnings remain explicit until Generator.weekstart_name is ported.
    // py: sqlglot/dialects/dialect.py::timestamptrunc_sql #0000000000
    public static Func<generator.Generator, exp.Expr, string> timestamptrunc_sql(string func = "DATE_TRUNC", bool zone = false) =>
        (self, expression) =>
        {
            if (expression.args.get("unit") is exp.WeekStart) throw new NotPortedException("Generator.weekstart_name");
            var unit = unit_to_str(expression);
            return zone ? self.func(func, unit, expression.@this, expression.args.get("zone"))
                : self.func(func, unit, expression.@this);
        };

    // py: sqlglot/dialects/dialect.py::no_paren_current_date_sql #0000000000
    public static string no_paren_current_date_sql(generator.Generator self, exp.CurrentDate expression)
    {
        var zone = self.sql(expression, "this");
        return zone.Length > 0 ? $"CURRENT_DATE AT TIME ZONE {zone}" : "CURRENT_DATE";
    }
}
