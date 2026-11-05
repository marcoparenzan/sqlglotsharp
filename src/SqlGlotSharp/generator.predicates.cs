namespace SqlGlotSharp;

// IN / EXISTS / BETWEEN rendering of the base generator.
public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.in_sql #867a32395c
        public virtual string in_sql(exp.In expression)
        {
            var query = expression.args.get("query");
            var unnest = expression.args.get("unnest");
            var field = expression.args.get("field");
            var is_global = py.truthy(expression.args.get("is_global")) ? " GLOBAL" : "";

            string in_sql;
            if (py.truthy(query)) in_sql = sql(query);
            else if (py.truthy(unnest)) in_sql = in_unnest_op((exp.Unnest)unnest!);
            else if (py.truthy(field)) in_sql = sql(field);
            else in_sql = $"({expressions(expression, dynamic: true, new_line: true, skip_first: true, skip_last: true)})";

            return $"{sql(expression, "this")}{is_global} IN {in_sql}";
        }

        // py: sqlglot/generator.py::Generator.in_unnest_op #6890796595
        public virtual string in_unnest_op(exp.Unnest unnest) => $"(SELECT {sql(unnest)})";

        // py: sqlglot/generator.py::Generator.exists_sql #1790095ab6
        public virtual string exists_sql(exp.Exists expression) => $"EXISTS{wrap(expression)}";

        // py: sqlglot/generator.py::Generator.bracket_offset_expressions #9064647974
        public virtual List<object?> bracket_offset_expressions(exp.Bracket expression, long? index_offset = null)
        {
            if (py.truthy(expression.args.get("json_access"))) return expression.expressions;

            var offset = (index_offset is > 0 or < 0 ? index_offset!.Value : dialect.INDEX_OFFSET)
                - Convert.ToInt64(expression.args.get("offset") ?? 0L);
            return apply_index_offset(expression.@this, expression.expressions, offset, dialect);
        }

        // py: sqlglot/expressions/builders.py::apply_index_offset #0000000000
        internal static List<object?> apply_index_offset(object? @this, List<object?> expressions, long offset, object? dialect = null)
        {
            if (offset == 0 || expressions.Count != 1) return expressions;

            var expression = (exp.Expr)expressions[0]!;
            var this_expr = (exp.Expr)@this!;

            if (this_expr.type is null) optimizer.annotate_types_module.annotate_types(this_expr, dialect: dialect);

            if (!(this_expr.type!.args["this"] is exp.DType.UNKNOWN or exp.DType.ARRAY)) return expressions;

            if (expression.type is null) optimizer.annotate_types_module.annotate_types(expression, dialect: dialect);

            if (expression.type!.args["this"] is exp.DType t && exp.DataType.INTEGER_TYPES_.Contains(t))
            {
                // Applying array index offset
                expression = optimizer.simplify_module.simplify(
                    generators.postgres.PostgresGenerator.Binop(typeof(exp.Add), expression, exp.Literal.number(offset)), dialect: dialect);
                return [expression];
            }

            return expressions;
        }

        // py: sqlglot/generator.py::Generator.bracket_sql #37a703f0f3
        public virtual string bracket_sql(exp.Bracket expression)
        {
            var expressions = bracket_offset_expressions(expression);
            var expressions_sql = string.Join(", ", expressions.Select(e => sql(e)));
            return $"{sql(expression, "this")}[{expressions_sql}]";
        }

        // py: sqlglot/generator.py::Generator.between_sql #93ab3b04d2
        public virtual string between_sql(exp.Between expression)
        {
            var @this = sql(expression, "this");
            var low = sql(expression, "low");
            var high = sql(expression, "high");
            var symmetric = expression.args.get("symmetric");

            if (py.truthy(symmetric) && !SUPPORTS_BETWEEN_FLAGS)
                return $"({@this} BETWEEN {low} AND {high} OR {@this} BETWEEN {high} AND {low})";

            var flag = py.truthy(symmetric) ? " SYMMETRIC"
                : symmetric is false && SUPPORTS_BETWEEN_FLAGS ? " ASYMMETRIC"
                : ""; // silently drop ASYMMETRIC – semantics identical
            return $"{@this} BETWEEN{flag} {low} AND {high}";
        }
    }
}
