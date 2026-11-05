namespace SqlGlotSharp;

// Key/value argument handling (parser.py): struct literals and aliased function arguments.
public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser._to_prop_eq #0000000000
        public virtual exp.Expr _to_prop_eq(exp.Expr expression, int index) => expression;

        // py: sqlglot/parser.py::Parser._kv_to_prop_eq #0000000000
        public List<exp.Expr> _kv_to_prop_eq(IReadOnlyList<exp.Expr> expressions, bool parse_map = false)
        {
            List<exp.Expr> transformed = [];
            var definitions = KEY_VALUE_DEFINITIONS;

            for (var index = 0; index < expressions.Count; index++)
            {
                var e = expressions[index];
                if (e.GetType() == definitions.Item1 || e.GetType() == definitions.Item2
                    || e.GetType() == definitions.Item3 || e.GetType() == definitions.Item4
                    || definitions.Item1.IsInstanceOfType(e) || definitions.Item2.IsInstanceOfType(e)
                    || definitions.Item3.IsInstanceOfType(e) || definitions.Item4.IsInstanceOfType(e))
                {
                    if (e is exp.Alias)
                        e = expression(new exp.PropertyEQ(@this: e.args.get("alias"), expression: e.@this));

                    if (e is not exp.PropertyEQ)
                        e = expression(new exp.PropertyEQ(
                            @this: parse_map ? e.@this : exp.to_identifier(((exp.Expr)e.@this!).name),
                            expression: e.args.get("expression")));

                    if (!parse_map && e.@this is exp.Column column) column.replace((exp.Expr)column.@this!);
                }
                else e = _to_prop_eq(e, index);

                transformed.Add(e);
            }

            return transformed;
        }
    }
}
