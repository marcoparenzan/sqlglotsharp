namespace SqlGlotSharp;

public static partial class generator
{
    public partial class Generator
    {
        // WeekStart's custom naming is still an explicit dependency.
        // py: sqlglot/generator.py::Generator.extract_sql #0000000000
        public virtual string extract_sql(exp.Extract expression)
        {
            var unit = NORMALIZE_EXTRACT_DATE_PARTS
                ? dialects.dialect.map_date_part((exp.Expr?)expression.@this, dialect)
                : (exp.Expr?)expression.@this;
            string unitSql;
            if (EXTRACT_ALLOWS_QUOTES) unitSql = sql(unit);
            else if (unit is exp.WeekStart week_start) unitSql = weekstart_name(week_start);
            else unitSql = unit?.name ?? throw new InvalidOperationException("EXTRACT requires a unit");
            return $"EXTRACT({unitSql} FROM {sql(expression, "expression")})";
        }

        // py: sqlglot/generator.py::Generator.currentdate_sql #0000000000
        public virtual string currentdate_sql(exp.CurrentDate expression)
        {
            var zone = sql(expression, "this");
            return zone.Length > 0 ? $"CURRENT_DATE({zone})" : "CURRENT_DATE";
        }

        // py: sqlglot/generator.py::Generator.localtime_sql #0000000000
        public virtual string localtime_sql(exp.Localtime expression) =>
            py.truthy(expression.@this) ? func("LOCALTIME", expression.@this) : "LOCALTIME";

        // py: sqlglot/generator.py::Generator.localtimestamp_sql #0000000000
        public virtual string localtimestamp_sql(exp.Localtimestamp expression) =>
            py.truthy(expression.@this) ? func("LOCALTIMESTAMP", expression.@this) : "LOCALTIMESTAMP";

        // py: sqlglot/generator.py::Generator.attimezone_sql #0000000000
        public virtual string attimezone_sql(exp.AtTimeZone expression) =>
            $"{sql(expression, "this")} AT TIME ZONE {sql(expression, "zone")}";
    }
}
