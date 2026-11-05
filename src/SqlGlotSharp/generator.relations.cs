namespace SqlGlotSharp;

// UNNEST / LATERAL / CONCAT / TRIM rendering of the base generator.
public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.convert_concat_args #379abfc514
        public virtual List<object?> convert_concat_args(exp.Expr expression)
        {
            List<object?> args = expression.expressions;
            if (expression is exp.ConcatWs) args = args.Skip(1).ToList(); // Skip the delimiter

            if (dialect.STRICT_STRING_CONCAT && py.truthy(expression.args.get("safe")))
            {
                args = args.Select(e => (object?)generators.dialect_helpers.cast(e, exp.DataType.build(exp.DType.TEXT))).ToList();
            }

            var concat_coalesce = expression is exp.ConcatWs ? dialect.CONCAT_WS_COALESCE : dialect.CONCAT_COALESCE;

            if (!concat_coalesce && py.truthy(expression.args.get("coalesce")))
            {
                exp.Expr _wrap_with_coalesce(exp.Expr e)
                {
                    if (e.type is null) e = optimizer.annotate_types_module.annotate_types(e, dialect: dialect);

                    if (e.is_string || e.is_type(exp.DType.ARRAY)) return e;

                    return new exp.Coalesce(@this: e, expressions: new List<object?> { exp.Literal.@string("") });
                }

                args = args.Select(e => (object?)_wrap_with_coalesce((exp.Expr)e!)).ToList();
            }

            return args;
        }

        // py: sqlglot/generator.py::Generator.concat_sql #96810dac52
        public virtual string concat_sql(exp.Concat expression)
        {
            if (dialect.CONCAT_COALESCE && !py.truthy(expression.args.get("coalesce")))
            {
                // Dialect's CONCAT function coalesces NULLs to empty strings, but the expression does not.
                // Transpile to double pipe operators, which typically returns NULL if any args are NULL
                // instead of coalescing them to empty string.
                return concat_to_dpipe_sql(expression);
            }

            var expressions = convert_concat_args(expression);

            // Some dialects don't allow a single-argument CONCAT call
            if (!SUPPORTS_SINGLE_ARG_CONCAT && expressions.Count == 1) return sql(expressions[0]);

            return func("CONCAT", expressions.ToArray());
        }

        // py: sqlglot/dialects/dialect.py::concat_to_dpipe_sql #0000000000
        internal string concat_to_dpipe_sql(exp.Concat expression)
        {
            var args = convert_concat_args(expression);
            var folded = args.Aggregate((x, y) => new exp.DPipe(@this: x, expression: y));
            return sql(folded);
        }

        // py: sqlglot/generator.py::Generator.concatws_sql #8ed965bf1d
        public virtual string concatws_sql(exp.ConcatWs expression)
        {
            if (dialect.CONCAT_WS_COALESCE && !py.truthy(expression.args.get("coalesce")))
                // Needs the exp.case()/or_()/is_() builders (not ported).
                throw new NotPortedException("sqlglot/generator.py::Generator.concatws_sql (CASE wrapping for NULL arguments)");

            var args = new List<object?> { expression.expressions.Count > 0 ? expression.expressions[0] : null };
            args.AddRange(convert_concat_args(expression));
            return func("CONCAT_WS", args.ToArray());
        }

        // py: sqlglot/generator.py::Generator.trim_sql #1bb6d0339e
        public virtual string trim_sql(exp.Trim expression)
        {
            var trim_type = sql(expression, "position");

            string func_name = trim_type == "LEADING" ? "LTRIM" : trim_type == "TRAILING" ? "RTRIM" : "TRIM";

            return func(func_name, expression.@this, expression.args.get("expression"));
        }

        // py: sqlglot/generator.py::Generator.unnest_sql #bf8239abbc
        public virtual string unnest_sql(exp.Unnest expression)
        {
            var args = expressions(expression, flat: true);

            var alias_node = expression.args.get("alias") as exp.Expr;
            var offset = expression.args.get("offset");

            if (UNNEST_WITH_ORDINALITY)
            {
                if (alias_node is not null && offset is exp.Expr)
                {
                    alias_node.append("columns", offset);
                    expression.set("offset", null);
                }
            }

            string alias;
            if (alias_node is not null && dialect.UNNEST_COLUMN_ONLY)
            {
                var columns = alias_node.args.get("columns") as List<object?> ?? [];
                alias = columns.Count > 0 ? sql(columns[0]) : "";
            }
            else alias = sql(alias_node);

            alias = alias.Length != 0 ? $" AS {alias}" : alias;
            string suffix;
            if (UNNEST_WITH_ORDINALITY)
                suffix = py.truthy(offset) ? $" WITH ORDINALITY{alias}" : alias;
            else
            {
                if (offset is exp.Expr) suffix = $"{alias} WITH OFFSET AS {sql(offset)}";
                else if (py.truthy(offset)) suffix = $"{alias} WITH OFFSET";
                else suffix = alias;
            }

            return $"UNNEST({args}){suffix}";
        }

        // py: sqlglot/generator.py::Generator.lateral_op #1f5f7bec06
        public virtual string lateral_op(exp.Lateral expression)
        {
            var cross_apply = expression.args.get("cross_apply");

            // https://www.mssqltips.com/sqlservertip/1958/sql-server-cross-apply-and-outer-apply/
            var op = cross_apply is true ? "INNER JOIN " : cross_apply is false ? "LEFT JOIN " : "";

            return $"{op}LATERAL";
        }

        // py: sqlglot/generator.py::Generator.lateral_sql #edff04073a
        public virtual string lateral_sql(exp.Lateral expression)
        {
            var @this = sql(expression, "this");

            if (py.truthy(expression.args.get("view")))
            {
                var view_alias = (exp.Expr)expression.args["alias"]!;
                var view_columns = expressions(view_alias, key: "columns", flat: true);
                var table = view_alias.name.Length != 0 ? $" {view_alias.name}" : "";
                view_columns = view_columns.Length != 0 ? $" AS {view_columns}" : "";
                var op_sql = seg($"LATERAL VIEW{(py.truthy(expression.args.get("outer")) ? " OUTER" : "")}");
                return $"{op_sql}{sep()}{@this}{table}{view_columns}";
            }

            var table_alias = expression.args.get("alias") as exp.Expr;
            var offset = (expression.@this as exp.Expr)?.args.get("offset");

            if (UNNEST_WITH_ORDINALITY && table_alias is not null && expression.@this is exp.Unnest && offset is exp.Identifier)
            {
                // UNNEST ... WITH ORDINALITY stores the ordinality column's name in Unnest.offset
                table_alias = table_alias.copy();
                table_alias.append("columns", ((exp.Expr)offset).copy());
            }

            var alias = sql(table_alias);
            alias = alias.Length != 0 ? $" AS {alias}" : "";

            var ordinality = py.truthy(expression.args.get("ordinality")) ? " WITH ORDINALITY" : "";
            if (ordinality.Length != 0)
            {
                ordinality = $"{ordinality}{alias}";
                alias = "";
            }

            return $"{lateral_op(expression)} {@this}{alias}{ordinality}";
        }
    }
}
