namespace SqlGlotSharp;

public static partial class generator
{
    public partial class Generator
    {
        // Bounded dependency closure: Lateral, Nearest and Pivot still dispatch to
        // their explicit missing handlers. ON is a child expression; USING is a list,
        // not a synthetic AST node. No rewrite or preprocess bypass is introduced.
        // py: sqlglot/generator.py::Generator.join_sql #d4cdc82001
        public virtual string join_sql(exp.Join expression, string? join_op = null)
        {
            // Join's Python properties are text(key).upper(); the shared AST does
            // not yet expose those properties, so translate them at the call site.
            var kind = expression.text("kind").ToUpperInvariant();
            var side = !SEMI_ANTI_JOIN_WITH_SIDE && kind is "SEMI" or "ANTI"
                ? "" : expression.text("side").ToUpperInvariant();
            var op_sql = string.Join(" ", new[]
            {
                expression.text("method").ToUpperInvariant(),
                py.truthy(expression.args.get("global_")) ? "GLOBAL" : "",
                side,
                kind,
                py.truthy(JOIN_HINTS) ? expression.text("hint").ToUpperInvariant() : "",
                py.truthy(expression.args.get("directed")) && DIRECTED_JOINS ? "DIRECTED" : "",
            }.Where(op => op.Length != 0));
            var match_cond = sql(expression, "match_condition");
            match_cond = match_cond.Length != 0 ? $" MATCH_CONDITION ({match_cond})" : "";
            var nearest = sql(expression, "nearest");
            nearest = nearest.Length != 0 ? $" {nearest}" : "";
            var on_sql = sql(expression, "on");
            var using_ = expression.args.get("using");
            if (on_sql.Length == 0 && py.truthy(using_))
                on_sql = helper.csv(((System.Collections.IEnumerable)using_!).Cast<object?>().Select(column => sql(column)).ToList());

            var @this = expression.@this;
            var this_sql = sql(@this);
            var exprs = expressions(expression);
            if (exprs.Length != 0) this_sql = $"{this_sql},{seg(exprs)}";
            if (on_sql.Length != 0)
            {
                on_sql = indent(on_sql, skip_first: true);
                var space = py.truthy(pretty) ? seg(new string(' ', checked((int)pad))) : " ";
                on_sql = py.truthy(using_) ? $"{space}USING ({on_sql})" : $"{space}ON {on_sql}";
            }
            else if (op_sql.Length == 0 && nearest.Length == 0)
            {
                if (@this is exp.Lateral lateral && lateral.args.get("cross_apply") is not null)
                    return $" {this_sql}";
                return $", {this_sql}";
            }
            if (!string.IsNullOrEmpty(join_op)) op_sql = join_op;
            else if (op_sql != "STRAIGHT_JOIN") op_sql = op_sql.Length != 0 ? $"{op_sql} JOIN" : "JOIN";
            var pivots = expressions(expression, key: "pivots", sep: "", flat: true);
            return $"{seg(op_sql)} {this_sql}{match_cond}{nearest}{on_sql}{pivots}";
        }

        // py: sqlglot/generator.py::Generator.hint_sql #fc4280e71c
        public virtual string hint_sql(exp.Hint expression)
        {
            if (!QUERY_HINTS)
            {
                unsupported("Hints are not supported");
                return "";
            }
            return $" /*+ {Strip(expressions(expression, sep: QUERY_HINT_SEP))} */";
        }

        // py: sqlglot/generator.py::Generator.joinhint_sql #94a97c3515
        public virtual string joinhint_sql(exp.JoinHint expression)
        {
            var @this = sql(expression, "this");
            var expressions = this.expressions(expression, flat: true);
            return $"{@this}({expressions})";
        }
    }
}
