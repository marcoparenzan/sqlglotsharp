namespace SqlGlotSharp.generators;

public static partial class postgres
{
    public partial class PostgresGenerator
    {
        // py: sqlglot/generators/postgres.py::PostgresGenerator.lateral_sql #e67fa7c442
        public override string lateral_sql(exp.Lateral expression)
        {
            var sql = base.lateral_sql(expression);

            if (expression.args.get("cross_apply") is not null) sql = $"{sql} ON TRUE";

            return sql;
        }

        // py: sqlglot/generators/postgres.py::PostgresGenerator.array_sql #415626000d
        public virtual string array_sql(exp.Array expression)
        {
            var exprs = expression.expressions;
            var func_name = normalize_func("ARRAY");

            if (exprs.Count > 0 && exprs[0] is exp.Query) return $"{func_name}({sql(exprs[0])})";

            // dialect.inline_array_sql
            return $"{func_name}[{expressions(expression, dynamic: true, new_line: true, skip_first: true, skip_last: true)}]";
        }

        // py: sqlglot/generators/postgres.py::PostgresGenerator.bracket_sql #b281ae2c40
        /// <summary>Forms like ARRAY[1, 2, 3][3] aren't allowed; we need to wrap the ARRAY.</summary>
        public override string bracket_sql(exp.Bracket expression)
        {
            if (expression.@this is exp.Array array)
                expression.set("this", new exp.Paren(@this: array));

            return base.bracket_sql(expression);
        }
    }
}
