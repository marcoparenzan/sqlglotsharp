namespace SqlGlotSharp;

public static partial class generator
{
    public partial class Generator
    {
        // Ordinary CTEs only: UDF / SEARCH / CYCLE child handlers retain their
        // existing explicit boundaries rather than being skipped or approximated.
        // py: sqlglot/generator.py::Generator.with_sql #97fd5b2665
        public virtual string with_sql(exp.With expression)
        {
            var udfs = expressions(expression, key: "udfs", flat: true);
            udfs = udfs.Length != 0 ? $"WITH {udfs}" : "";
            var sql = expressions(expression, flat: true);
            var recursive = CTE_RECURSIVE_KEYWORD_REQUIRED && py.truthy(expression.args.get("recursive"))
                ? "RECURSIVE " : "";
            var search = this.sql(expression, "search");
            search = search.Length != 0 ? $" {search}" : "";
            var cycle = this.sql(expression, "cycle");
            cycle = cycle.Length != 0 ? $" {cycle}" : "";
            sql = sql.Length != 0 ? $"WITH {recursive}{sql}{search}{cycle}" : "";
            return udfs.Length != 0 && sql.Length != 0 ? $"{udfs} {sql}" : $"{udfs}{sql}";
        }

        // Comment transfer intentionally mutates the alias BEFORE rendering it,
        // including when comments=false, and before an unported body can fail.
        // py: sqlglot/generator.py::Generator.cte_sql #1458ebf390
        public virtual string cte_sql(exp.CTE expression)
        {
            var alias = expression.args.get("alias");
            if (py.truthy(alias)) ((exp.Expr)alias!).add_comments(expression.pop_comments());
            var alias_sql = sql(expression, "alias");
            var materialized = expression.args.get("materialized");
            var materialized_sql = materialized is false ? "NOT MATERIALIZED "
                : py.truthy(materialized) ? "MATERIALIZED " : "";
            var key_expressions = expressions(expression, key: "key_expressions", flat: true);
            key_expressions = key_expressions.Length != 0 ? $" USING KEY ({key_expressions})" : "";
            return $"{alias_sql}{key_expressions} AS {materialized_sql}{wrap(expression)}";
        }
    }
}
