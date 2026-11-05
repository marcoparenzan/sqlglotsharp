namespace SqlGlotSharp;

public static partial class exp
{
    public abstract partial class Expr
    {
        // py: sqlglot/expressions/core.py::Expr.is_string #c19feb8c98
        public abstract bool is_string { get; }
        // py: sqlglot/expressions/core.py::Expr.is_number #9d99c4a658
        public abstract bool is_number { get; }
        // py: sqlglot/expressions/core.py::Expr.is_star #01cbfdecd5
        public abstract bool is_star { get; }
        public abstract Expr set_kwargs(IEnumerable<KeyValuePair<string, object?>> kwargs);
    }
    public partial class Expression
    {
        // py: sqlglot/expressions/core.py::Expression.is_string #a00715b02c
        public override bool is_string => this is Literal && py.truthy(args["is_string"]);
        // py: sqlglot/expressions/core.py::Expression.is_number #8628ae84b2
        public override bool is_number => this is Literal && !py.truthy(args["is_string"]) || this is Neg && ((Expr)@this!).is_number;
        // py: sqlglot/expressions/core.py::Expression.is_star #cf657de0c3
        public override bool is_star => this is Star || this is Column && @this is Star;
        // py: sqlglot/expressions/core.py::Expression.set_kwargs #6b783c0b21
        public override Expr set_kwargs(IEnumerable<KeyValuePair<string, object?>> kwargs)
        {
            foreach (var (key, value) in kwargs) set(key, value);
            return this;
        }
    }
    public partial class Column
    {
        // py: sqlglot/expressions/core.py::Column.to_dot #7bf07b3a17
        public Expr to_dot(bool include_dots = true)
        {
            var parts = this.parts;
            var parent = this.parent;
            if (include_dots)
                while (parent is Dot)
                {
                    parts.Add((Expr)parent.expression!);
                    parent = parent.parent;
                }
            return parts.Count > 1 ? Dot.build((List<Expr>)py.deepcopy(parts)!) : parts[0];
        }
    }
    public partial class Dot
    {
        // py: sqlglot/expressions/core.py::Dot.is_star #0c804063fc
        public override bool is_star => ((Expr)expression!).is_star;
    }
}
