namespace SqlGlotSharp;

public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.preprocess #0000000000
        public virtual exp.Expr preprocess(exp.Expr expression)
        {
            expression = _move_ctes_to_top_level(expression);
            if (ENSURE_BOOLS) expression = ensure_bools(expression);
            return expression;
        }

        // py: sqlglot/generator.py::Generator._move_ctes_to_top_level #0000000000
        public virtual exp.Expr _move_ctes_to_top_level(exp.Expr expression)
        {
            if (expression.parent is null && EXPRESSIONS_WITHOUT_NESTED_CTES.Contains(expression.GetType()) &&
                expression.find_all<exp.With>().Any(node => !ReferenceEquals(node.parent, expression)))
                expression = move_ctes_to_top_level(expression);
            return expression;
        }

        // py: sqlglot/transforms.py::move_ctes_to_top_level
        internal static exp.Expr move_ctes_to_top_level(exp.Expr expression)
        {
            var top_level_with = expression.args.get("with_") as exp.With;
            foreach (var inner_with in expression.find_all<exp.With>().ToList())
            {
                if (ReferenceEquals(inner_with.parent, expression)) continue;

                if (top_level_with is null)
                {
                    top_level_with = (exp.With)inner_with.pop();
                    expression.set("with_", top_level_with);
                }
                else
                {
                    if (py.truthy(inner_with.args.get("recursive"))) top_level_with.set("recursive", true);

                    var parent_cte = inner_with.find_ancestor(typeof(exp.CTE));
                    inner_with.pop();

                    var top = ((System.Collections.IEnumerable)top_level_with.args["expressions"]!).Cast<object?>().ToList();
                    var inner = ((System.Collections.IEnumerable)inner_with.args["expressions"]!).Cast<object?>().ToList();
                    if (parent_cte is not null) top.InsertRange(top.IndexOf(parent_cte), inner);
                    else top.AddRange(inner);
                    top_level_with.set("expressions", top);
                }
            }

            return expression;
        }

        // py: sqlglot/optimizer/canonicalize.py::ensure_bools
        private static void canonicalize_ensure_bools(exp.Expr expression, Action<exp.Expr> replace_func)
        {
            if (expression is exp.Connector connector)
            {
                replace_func((exp.Expr)((exp.Expr)connector).args["this"]!);
                replace_func((exp.Expr)((exp.Expr)connector).args["expression"]!);
            }
            else if (expression is exp.Not) replace_func((exp.Expr)expression.args["this"]!);
            // We can't replace num in CASE x WHEN num ..., because it's not the full predicate
            else if (expression is exp.If && !(expression.parent is exp.Case && py.truthy(expression.parent.args.get("this"))))
                replace_func((exp.Expr)expression.args["this"]!);
            else if (expression is exp.Where or exp.Having) replace_func((exp.Expr)expression.args["this"]!);
        }

        // py: sqlglot/transforms.py::ensure_bools
        /// <summary>Converts numeric values used in conditions into explicit boolean expressions.</summary>
        internal static exp.Expr ensure_bools(exp.Expr expression)
        {
            void _ensure_bool(exp.Expr node)
            {
                if (node.is_number
                    || (node is not exp.SubqueryPredicate
                        && node.is_type([exp.DType.UNKNOWN, .. exp.DataType.NUMERIC_TYPES_.Cast<object>()]))
                    || (node is exp.Column && node.type is null))
                {
                    var copy = node.copy();
                    var @this = copy is exp.Binary && copy is not exp.NEQ ? new exp.Paren(@this: copy) : copy;
                    node.replace(new exp.NEQ(@this: @this, expression: exp.Literal.number(0)));
                }
            }

            foreach (var node in expression.walk()) canonicalize_ensure_bools(node, _ensure_bool);

            return expression;
        }
    }
}
