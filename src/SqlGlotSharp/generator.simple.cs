namespace SqlGlotSharp;

// Bounded simple handlers from sqlglot/generator.py, in Python source order.
// Child SQL still uses the explicit NotPorted dispatch boundary in generator.cs.
public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.column_parts #268fb8edcd
        public virtual string column_parts(exp.Column expression)
        {
            if (py.truthy(expression.args.get("shadow")) && dialect.PROJECTION_ALIASES_SHADOW_SOURCE_NAMES)
            {
                // The qualifier would be captured by a colliding projection alias (see qualify_columns)
                return sql(expression, "this");
            }

            return string.Join(".", new[]
            {
                expression.args.get("catalog"),
                expression.args.get("db"),
                expression.args.get("table"),
                expression.args.get("this"),
            }.Where(part => py.truthy(part)).Select(part => sql(part)));
        }

        // py: sqlglot/generator.py::Generator.column_sql #8526e29d35
        public virtual string column_sql(exp.Column expression)
        {
            var join_mark = py.truthy(expression.args.get("join_mark")) ? " (+)" : "";

            if (py.truthy(join_mark) && !dialect.SUPPORTS_COLUMN_JOIN_MARKS)
            {
                join_mark = "";
                unsupported("Outer join syntax using the (+) operator is not supported.");
            }

            return $"{column_parts(expression)}{join_mark}";
        }

        // py: sqlglot/generator.py::Generator.var_sql #36a50a5f8c
        public virtual string var_sql(exp.Var expression) => sql(expression, "this");

        // py: sqlglot/generator.py::Generator.parameter_sql #35dac9b2d3
        public virtual string parameter_sql(exp.Parameter expression)
        {
            var @this = sql(expression, "this");
            return $"{PARAMETER_TOKEN}{@this}";
        }

        // py: sqlglot/generator.py::Generator.placeholder_sql #d0055bc768
        public virtual string placeholder_sql(exp.Placeholder expression) => py.truthy(expression.@this)
            ? $"{NAMED_PLACEHOLDER_TOKEN}{expression.name}" : "?";

        // py: sqlglot/generator.py::Generator.paren_sql #d0702c319c
        public virtual string paren_sql(exp.Paren expression)
        {
            var sql = seg(indent(this.sql(expression, "this")), sep: "");
            return $"({sql}{seg(")", sep: "")}";
        }

        // py: sqlglot/generator.py::Generator.neg_sql #6100f5b480
        public virtual string neg_sql(exp.Neg expression)
        {
            // This makes sure we don't convert "- - 5" to "--5", which is a comment
            var this_sql = sql(expression, "this");
            var sep = this_sql[0] == '-' ? " " : "";
            return $"-{sep}{this_sql}";
        }

        // py: sqlglot/generator.py::Generator.alias_sql #974009e621
        public virtual string alias_sql(exp.Alias expression)
        {
            var alias = sql(expression, "alias");
            alias = py.truthy(alias) ? $" AS {alias}" : "";
            return $"{sql(expression, "this")}{alias}";
        }
    }
}
