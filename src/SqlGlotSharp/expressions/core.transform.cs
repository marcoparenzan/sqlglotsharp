namespace SqlGlotSharp;

public static partial class exp
{
    public abstract partial class Expr
    {
        // py: sqlglot/expressions/core.py::Expr.transform #5646adfc0b
        public abstract object transform(Func<Expr, object?> fun, bool copy = true);

        // py: sqlglot/expressions/core.py::Expr.type #d1b119042f
        public abstract DataType? type { get; set; }
        // py: sqlglot/expressions/core.py::Expr.is_type #8698fbc294
        public abstract bool is_type(params object[] dtypes);
    }
    public partial class Expression
    {
        // py: sqlglot/expressions/core.py::Expression.transform #03be948c2a
        public override object transform(Func<Expr, object?> fun, bool copy = true)
        {
            object? root = null;
            object? new_node = null;
            foreach (var node in (copy ? this.copy() : this).dfs(prune: n => !ReferenceEquals(n, new_node)))
            {
                var parent = node.parent; var arg_key = node.arg_key; var index = node.index;
                new_node = fun(node);
                if (!py.truthy(root)) root = new_node;
                else if (parent is not null && !string.IsNullOrEmpty(arg_key) && !ReferenceEquals(new_node, node)) parent.set(arg_key, new_node, index);
            }
            return py.truthy(root) ? root! : throw new InvalidOperationException("transform requires a nonempty root");
        }
    }
}
