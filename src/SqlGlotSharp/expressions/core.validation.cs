using Token = SqlGlotSharp.tokenizer_core.Token;

namespace SqlGlotSharp;

public static partial class exp
{
    // py: sqlglot/expressions/core.py::POSITION_META_KEYS #b56c8ffd7d
    public static readonly IReadOnlyList<string> POSITION_META_KEYS = System.Array.AsReadOnly(new[] { "line", "col", "start", "end" });
    // py: sqlglot/expressions/core.py::UNITTEST #83c2dffb55
    public static readonly bool UNITTEST = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name?.StartsWith("xunit", StringComparison.OrdinalIgnoreCase) == true);

    public abstract partial class Expr
    {
        // py: sqlglot/expressions/core.py::Expr.unnest #fe0bed5bbd
        public abstract Expr unnest();
        // py: sqlglot/expressions/core.py::Expr.unalias #963f1b8164
        public abstract Expr unalias();
        // py: sqlglot/expressions/core.py::Expr.unnest_operands #44c5a8c9fb
        public abstract IReadOnlyList<Expr> unnest_operands();
        // py: sqlglot/expressions/core.py::Expr.flatten #70195c0d0d
        public abstract IEnumerable<Expr> flatten(bool unnest = true);
        // py: sqlglot/expressions/core.py::Expr.error_messages #e410e4f9f7
        public abstract List<string> error_messages(IReadOnlyList<object?>? args = null);
        // py: sqlglot/expressions/core.py::Expr.update_positions #7251307fa6
        public abstract Expr update_positions(object? other = null, int? line = null, int? col = null, int? start = null, int? end = null);
    }
    public partial class Expression
    {
        // py: sqlglot/expressions/core.py::Expression.unnest #0e72ec566a
        public override Expr unnest()
        {
            Expr expression = this;
            while (expression.GetType() == typeof(Paren)) expression = (Expr)expression.@this!;
            return expression;
        }
        // py: sqlglot/expressions/core.py::Expression.unalias #8afdceb463
        public override Expr unalias() => this is Alias ? (Expr)@this! : this;
        // py: sqlglot/expressions/core.py::Expression.unnest_operands #6988301a88
        public override IReadOnlyList<Expr> unnest_operands() => iter_expressions().Select(arg => arg.unnest()).ToArray();
        // py: sqlglot/expressions/core.py::Expression.flatten #b2d1d54f65
        public override IEnumerable<Expr> flatten(bool unnest = true)
        {
            foreach (var node in dfs(prune: n => n.parent is not null && n.GetType() != GetType()))
                if (node.GetType() != GetType()) yield return unnest && !node.is_subquery ? node.unnest() : node;
        }
        // py: sqlglot/expressions/core.py::Expression.error_messages #d84e7532ff
        public override List<string> error_messages(IReadOnlyList<object?>? args = null)
        {
            var className = $"<class '{serde.PythonClassName(GetType())}'>";
            if (UNITTEST)
                foreach (var key in this.args.Keys)
                    if (!arg_types.ContainsKey(key)) throw new ArgumentException($"Unexpected keyword: '{key}' for {className}");
            List<string> messages = [];
            foreach (var key in required_args)
            {
                var value = this.args.get(key);
                if (value is null || value is List<object?> list && list.Count == 0)
                    messages.Add($"Required keyword: '{key}' missing for {className}");
            }
            if (args is { Count: > 0 } && this is Func && args.Count > arg_types.Count && !is_var_len_args)
                messages.Add($"The number of provided arguments ({args.Count}) is greater than the maximum number of supported arguments ({arg_types.Count})");
            return messages;
        }
        // py: sqlglot/expressions/core.py::Expression.update_positions #2af3cfb552
        public override Expr update_positions(object? other = null, int? line = null, int? col = null, int? start = null, int? end = null)
        {
            if (other is Token token)
            {
                meta["line"] = token.line; meta["col"] = token.col; meta["start"] = token.start; meta["end"] = token.end;
            }
            else if (other is not null)
            {
                var other_meta = ((Expr)other)._meta;
                if (other_meta is { Count: > 0 })
                    foreach (var key in POSITION_META_KEYS)
                        if (other_meta.TryGetValue(key, out var value)) meta[key] = value;
            }
            else
            {
                meta["line"] = line; meta["col"] = col; meta["start"] = start; meta["end"] = end;
            }
            return this;
        }
    }
}
