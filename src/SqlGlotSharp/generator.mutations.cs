namespace SqlGlotSharp;

public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.delete_sql #0000000000
        public virtual string delete_sql(exp.Delete expression)
        {
            var hint = sql(expression, "hint");
            var target = sql(expression, "this");
            target = target.Length != 0 ? $" FROM {target}" : "";
            var using_sql = expressions(expression, key: "using");
            using_sql = using_sql.Length != 0 ? $" USING {using_sql}" : "";
            var cluster = sql(expression, "cluster");
            cluster = cluster.Length != 0 ? $" {cluster}" : "";
            var where = sql(expression, "where");
            var returning = sql(expression, "returning");
            var order = sql(expression, "order");
            var limit = sql(expression, "limit");
            var tables = expressions(expression, key: "tables");
            tables = tables.Length != 0 ? $" {tables}" : "";
            var body = RETURNING_END ? $"{target}{using_sql}{cluster}{where}{returning}{order}{limit}"
                : $"{returning}{target}{using_sql}{cluster}{where}{order}{limit}";
            return prepend_ctes(expression, $"DELETE{hint}{tables}{body}");
        }

        // Bounded base/Postgres closure: FROM-to-JOIN rewriting is deliberately
        // explicit NotPorted, not a silent standard-FROM approximation.
        // py: sqlglot/generator.py::Generator._update_from_joins_sql #0000000000
        public virtual (string join_sql, string from_sql) _update_from_joins_sql(exp.Update expression)
        {
            if (UPDATE_STATEMENT_SUPPORTS_FROM || !py.truthy(expression.args.get("from_")))
                return ("", sql(expression, "from_"));
            throw new NotPortedException("sqlglot/generator.py::Generator._update_from_joins_sql (FROM-to-JOIN rewrite)");
        }

        // py: sqlglot/generator.py::Generator.update_sql #0000000000
        public virtual string update_sql(exp.Update expression)
        {
            var hint = sql(expression, "hint");
            var target = sql(expression, "this");
            var (join_sql, from_sql) = _update_from_joins_sql(expression);
            var set_sql = expressions(expression, flat: true);
            var where = sql(expression, "where");
            var returning = sql(expression, "returning");
            var order = sql(expression, "order");
            var limit = sql(expression, "limit");
            var body = RETURNING_END ? $"{from_sql}{where}{returning}" : $"{returning}{from_sql}{where}";
            var options = expressions(expression, key: "options");
            options = options.Length != 0 ? $" OPTION({options})" : "";
            var result = $"UPDATE{hint} {target}{join_sql} SET {set_sql}{body}{order}{limit}{options}";
            return prepend_ctes(expression, result);
        }
    }
}
