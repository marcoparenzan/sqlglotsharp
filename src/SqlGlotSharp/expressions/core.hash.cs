namespace SqlGlotSharp;

public static partial class exp
{
    public abstract partial class Expr
    {
        public abstract long __hash__();
    }
    public partial class Expression
    {
        // py: sqlglot/expressions/core.py::Expression.__eq__ #03dac36936
        public override bool Equals(object? other) => ReferenceEquals(this, other) || other is Expr node &&
            GetType() == node.GetType() && __hash__() == node.__hash__();
        // py: sqlglot/expressions/core.py::Expression.__ne__ #6cc0639a87
        public bool __ne__(object? other) => !Equals(other);
        public override int GetHashCode() { var hash = __hash__(); return unchecked((int)(hash ^ (hash >> 32))); }

        // py: sqlglot/expressions/core.py::Expression.__hash__ #ec0db76642
        public override long __hash__()
        {
            if (_hash is null)
            {
                var nodes = new List<Expr>();
                var stack = new Stack<Expr>(); stack.Push(this);
                while (stack.TryPop(out var node))
                {
                    nodes.Add(node);
                    foreach (var value in node.args.Values)
                    {
                        if (value is Expr child && child._hash is null) stack.Push(child);
                        else if (value is List<object?> list)
                            foreach (var item in list) if (item is Expr entry && entry._hash is null) stack.Push(entry);
                    }
                }
                for (var i = nodes.Count - 1; i >= 0; i--)
                {
                    var node = nodes[i];
                    var hash = HashValue(node.key);
                    foreach (var key in node.args.Keys.Order(StringComparer.Ordinal))
                    {
                        var value = node.args[key];
                        if (node._hash_raw_args)
                        {
                            if (py.truthy(value)) hash = Mix(Mix(hash, HashValue(key)), value is List<object?> list ? HashSequence(list) : HashValue(value));
                        }
                        else if (value is List<object?> list)
                        {
                            foreach (var item in list)
                            {
                                hash = Mix(hash, HashValue(key));
                                if (item is not null && item is not false) hash = Mix(hash, HashValue(item is string text ? py.lower(text) : item));
                            }
                        }
                        else if (value is not null && value is not false)
                            hash = Mix(Mix(hash, HashValue(key)), HashValue(value is string text ? py.lower(text) : value));
                    }
                    node._hash = hash == 0 ? 1 : hash;
                }
            }
            return _hash!.Value;
        }

        // Host-runtime hashes differ from CPython's salted hashes. The AST ordering,
        // case normalization, falsy handling and cache/invalidation semantics are preserved.
        private static long Mix(long first, long second) => unchecked((first ^ second) * 1099511628211L + 0x517cc1b727220a95L);
        private static long HashSequence(IEnumerable<object?> values)
        {
            var hash = 0x345678L;
            foreach (var value in values) hash = Mix(hash, HashValue(value));
            return hash;
        }
        private static long HashValue(object? value) => value switch
        {
            null => 0x123456L,
            Expr node => node.__hash__(),
            string text => text.Aggregate(1469598103934665603L, (hash, c) => Mix(hash, c)),
            bool b => b ? 1 : 0,
            byte or sbyte or short or ushort or int or uint or long => System.Convert.ToInt64(value),
            double number when double.IsFinite(number) && number == Math.Truncate(number) && number >= long.MinValue && number < 9223372036854775808d => (long)number,
            decimal number when number == decimal.Truncate(number) && number >= long.MinValue && number <= long.MaxValue => (long)number,
            exp.DType dtype => HashValue(exp.ToValue(dtype)),
            List<object?> => throw new InvalidOperationException("unhashable type: list"),
            _ => value.GetHashCode()
        };
    }
}
