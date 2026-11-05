namespace SqlGlotSharp;

public static partial class exp
{
    public abstract partial class Expr
    {
        // py: sqlglot/expressions/core.py::Expr.__deepcopy__ #4b023efe49
        public abstract Expr __deepcopy__(object? memo = null);
        // py: sqlglot/expressions/core.py::Expr.copy #f4d951846d
        public abstract Expr copy();
    }

    public partial class Expression
    {
        private static Expr NewEmpty(Expr source) => (Expr)Activator.CreateInstance(source.GetType(), [new PyDict<string, object?>()])!;

        // py: sqlglot/expressions/core.py::Expression.__deepcopy__ #b2770de2de
        public override Expr __deepcopy__(object? memo = null)
        {
            var root = NewEmpty(this);
            var stack = new Stack<(Expr Node, Expr Copy)>();
            stack.Push((this, root));
            while (stack.TryPop(out var pair))
            {
                var (node, copy) = pair;
                if (node.comments is not null) copy.comments = (List<string>)py.deepcopy(node.comments)!;
                if (node._type is not null) copy._type = (DataType)py.deepcopy(node._type)!;
                if (node._meta is not null) copy._meta = (PyDict<string, object?>)py.deepcopy(node._meta)!;
                if (node._hash is not null) copy._hash = node._hash;
                foreach (var (key, values) in node.args)
                {
                    if (values is Expr child)
                    {
                        var clone = NewEmpty(child);
                        stack.Push((child, clone));
                        copy.set(key, clone);
                    }
                    else if (values is List<object?> list)
                    {
                        copy.args[key] = new List<object?>();
                        foreach (var value in list)
                        {
                            if (value is Expr item)
                            {
                                var clone = NewEmpty(item);
                                stack.Push((item, clone));
                                copy.append(key, clone);
                            }
                            else copy.append(key, value);
                        }
                    }
                    else copy.args[key] = values;
                }
            }
            return root;
        }
        // py: sqlglot/expressions/core.py::Expression.copy #d684704267
        public override Expr copy() => (Expr)py.deepcopy(this)!;
    }
}
