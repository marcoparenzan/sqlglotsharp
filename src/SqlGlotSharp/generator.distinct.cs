namespace SqlGlotSharp;

public static partial class generator
{
    public partial class Generator
    {
        // The multi-argument rewrite constructs the same temporary CASE as exp.case().when().
        // Each argument is copied, matching arg.is_(exp.null())'s default copy=True.
        // py: sqlglot/generator.py::Generator.distinct_sql #0000000000
        public virtual string distinct_sql(exp.Distinct expression)
        {
            var this_sql = expressions(expression, flat: true);
            var args = expression.expressions;
            if (!MULTI_ARG_DISTINCT && args.Count > 1)
            {
                var branches = new List<object?>();
                foreach (var arg in args)
                {
                    if (arg is not exp.Expr node)
                        throw new NotPortedException("sqlglot/generator.py::Generator.distinct_sql (non-expression CASE argument)");
                    branches.Add(new exp.If(new PyDict<string, object?>
                    {
                        ["this"] = new exp.Is(@this: node.copy(), expression: new exp.Null()),
                        ["true"] = new exp.Null()
                    }));
                }
                // case.else_(string) invokes maybe_parse in the base dialect. Parsing,
                // rather than emitting a raw string, preserves tuple layout and comments.
                var default_expression = dialects.dialect.Dialect.get_or_raise("").parse($"({this_sql})").Single();
                this_sql = sql(new exp.Case(new PyDict<string, object?>
                {
                    ["ifs"] = branches,
                    ["default"] = default_expression
                }));
            }
            this_sql = this_sql.Length != 0 ? $" {this_sql}" : "";
            var on = sql(expression, "on");
            on = on.Length != 0 ? $" ON {on}" : "";
            return $"DISTINCT{this_sql}{on}";
        }
    }
}
