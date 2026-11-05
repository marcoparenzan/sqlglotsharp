namespace SqlGlotSharp;

public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.generatedasidentitycolumnconstraint_sql
        public virtual string generatedasidentitycolumnconstraint_sql(exp.GeneratedAsIdentityColumnConstraint expression)
        {
            create_guard(expression, "this", "on_null", "start", "increment", "minvalue", "maxvalue", "cycle");
            var mode = "";
            if (expression.@this is not null)
                mode = py.truthy(expression.@this) ? " ALWAYS" :
                    $" BY DEFAULT{(py.truthy(expression.args.get("on_null")) ? " ON NULL" : "")}";

            // Upstream interpolates Expr.__str__, i.e. the default dialect, not this
            // generator's pretty/identify/normalization settings.
            static string Value(object? value) => value is exp.Expr node
                ? new Generator().generate(node) : value?.ToString() ?? "";
            string Option(string key, string prefix) => py.truthy(expression.args.get(key))
                ? prefix + Value(expression.args.get(key)) : "";
            var start = Option("start", "START WITH ");
            var increment = Option("increment", " INCREMENT BY ");
            var minvalue = Option("minvalue", " MINVALUE ");
            var maxvalue = Option("maxvalue", " MAXVALUE ");
            var cycle = expression.args.get("cycle");
            var cycleSql = cycle is null ? "" : $"{(!py.truthy(cycle) ? " NO" : "")} CYCLE";
            if (start.Length == 0 && increment.Length == 0) cycleSql = cycleSql.Trim();
            var options = start.Length != 0 || increment.Length != 0 || cycleSql.Length != 0
                ? $" ({(start + increment + minvalue + maxvalue + cycleSql).Trim()})" : "";
            return $"GENERATED{mode} AS IDENTITY{options}";
        }
    }
}
