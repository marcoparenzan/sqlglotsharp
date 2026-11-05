namespace SqlGlotSharp;

public static partial class exp
{
    // py: sqlglot/expressions/core.py::SQLGLOT_META #4c8b6e0629
    public const string SQLGLOT_META = "sqlglot.meta";

    public abstract partial class Expr
    {
        // py: sqlglot/expressions/core.py::Expr.meta #d829bf657f
        public abstract PyDict<string, object?> meta { get; }
        // py: sqlglot/expressions/core.py::Expr.meta_get #4fc93a7a53
        public abstract object? meta_get(string key, object? @default = null);
        // py: sqlglot/expressions/core.py::Expr.add_comments #550b3b3d3e
        public abstract void add_comments(List<string>? comments = null, bool prepend = false);
        // py: sqlglot/expressions/core.py::Expr.pop_comments #c97e6d1a51
        public abstract List<string> pop_comments();
        // py: sqlglot/expressions/core.py::Expr.append #b74ae01053
        public abstract void append(string arg_key, object? value);
        // py: sqlglot/expressions/core.py::Expr.replace #406eed3dd2
        public abstract T replace<T>(T expression);
        // py: sqlglot/expressions/core.py::Expr.pop #4b08a5e662
        public abstract Expr pop();
    }

    public partial class Expression
    {
        // py: sqlglot/expressions/core.py::Expression.meta #bbb4bd7e29
        public override PyDict<string, object?> meta => _meta ??= new();
        // py: sqlglot/expressions/core.py::Expression.meta_get #52471fb028
        public override object? meta_get(string key, object? @default = null) => _meta is null ? @default : _meta.get(key, @default);
        // py: sqlglot/expressions/core.py::Expression.add_comments #b4eff157d0
        public override void add_comments(List<string>? comments = null, bool prepend = false)
        {
            this.comments ??= [];
            if (comments is not { Count: > 0 }) return;
            foreach (var comment in comments)
            {
                var metadata = comment.Split(SQLGLOT_META, StringSplitOptions.None).Skip(1).ToArray();
                if (metadata.Length != 0)
                    foreach (var kv in string.Concat(metadata).Split(','))
                    {
                        var parts = kv.Split('=');
                        meta[parts[0].Trim()] = helper.to_bool(parts.Length > 1 ? parts[1].Trim() : true);
                    }
                if (!prepend) this.comments.Add(comment);
            }
            if (prepend) this.comments = comments.Concat(this.comments).ToList();
        }
        // py: sqlglot/expressions/core.py::Expression.pop_comments #e585bcabd6
        public override List<string> pop_comments()
        {
            var result = comments is { Count: > 0 } ? comments : [];
            comments = null;
            return result;
        }
        // py: sqlglot/expressions/core.py::Expression.append #ba2de9ff39
        public override void append(string arg_key, object? value)
        {
            for (Expr? node = this; node is not null && node._hash is not null; node = node.parent) node._hash = null;
            if (args.get(arg_key) is not List<object?>) args[arg_key] = new List<object?>();
            _set_parent(arg_key, value);
            var values = (List<object?>)args[arg_key]!;
            if (value is Expr child) child.index = values.Count;
            values.Add(value);
        }
        // py: sqlglot/expressions/core.py::Expression.replace #0d17f6c7c0
        public override T replace<T>(T expression)
        {
            var parent = this.parent;
            if (parent is null || ReferenceEquals(parent, expression)) return expression;
            var key = arg_key;
            if (!string.IsNullOrEmpty(key))
            {
                var value = parent.args.get(key);
                if (expression is List<object?> && value is Expr child)
                { if (child.parent is not null) child.parent.replace(expression); }
                else parent.set(key, expression, index);
            }
            if (!ReferenceEquals(expression, this)) { this.parent = null; arg_key = null; index = null; }
            return expression;
        }
        // py: sqlglot/expressions/core.py::Expression.pop #382bb805f5
        public override Expr pop() { replace<object?>(null); return this; }
    }
}
