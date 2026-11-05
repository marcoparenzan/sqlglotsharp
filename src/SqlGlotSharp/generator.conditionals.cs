namespace SqlGlotSharp;

public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.case_sql #42938a6af0
        public virtual string case_sql(exp.Case expression)
        {
            var this_sql = sql(expression, "this");
            var statements = new List<string> { this_sql.Length != 0 ? $"CASE {this_sql}" : "CASE" };

            foreach (exp.Expr branch in (System.Collections.IEnumerable)expression.args["ifs"]!)
            {
                statements.Add($"WHEN {sql(branch, "this")}");
                statements.Add($"THEN {sql(branch, "true")}");
            }

            var default_sql = sql(expression, "default");
            if (default_sql.Length != 0) statements.Add($"ELSE {default_sql}");
            statements.Add("END");

            if (py.truthy(pretty) && too_wide(statements))
                return indent(string.Join("\n", statements), skip_first: true, skip_last: true);
            return string.Join(" ", statements);
        }

        // Both base and PostgreSQL inherit this conversion. Constructing the temporary
        // Case intentionally reparents the If and its false child, just as Python does.
        // Calling case_sql directly avoids applying temporary-Case/If-branch comments.
        // py: sqlglot/generator.py::Generator.if_sql #f87babc17c
        public virtual string if_sql(exp.If expression) =>
            case_sql(new exp.Case(new PyDict<string, object?>
            {
                ["ifs"] = new List<object?> { expression },
                ["default"] = expression.args.get("false")
            }));
    }
}
