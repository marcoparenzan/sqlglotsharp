namespace SqlGlotSharp;

// SET / UNSET statements (generator.py).
public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.setitem_sql #0000000000
        public virtual string setitem_sql(exp.SetItem expression)
        {
            var kind = sql(expression, "kind");
            if (!SET_ASSIGNMENT_REQUIRES_VARIABLE_KEYWORD && kind == "VARIABLE") kind = "";
            else kind = kind.Length != 0 ? $"{kind} " : "";
            var @this = sql(expression, "this");
            var expressions = this.expressions(expression);
            var collate = sql(expression, "collate");
            collate = collate.Length != 0 ? $" COLLATE {collate}" : "";
            var global_ = py.truthy(expression.args.get("global_")) ? "GLOBAL " : "";
            return $"{global_}{kind}{@this}{expressions}{collate}";
        }

        // py: sqlglot/generator.py::Generator.set_sql #0000000000
        public virtual string set_sql(exp.Set expression)
        {
            var expressions = $" {this.expressions(expression, flat: true)}";
            var tag = py.truthy(expression.args.get("tag")) ? " TAG" : "";
            return $"{(py.truthy(expression.args.get("unset")) ? "UNSET" : "SET")}{tag}{expressions}";
        }
    }
}
