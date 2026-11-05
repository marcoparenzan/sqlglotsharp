namespace SqlGlotSharp;

public static partial class exp
{
    // py: sqlglot/expressions/core.py::COLUMN_PARTS #a8e6beba06
    public static readonly IReadOnlyList<string> COLUMN_PARTS = System.Array.AsReadOnly(new[] { "this", "table", "db", "catalog" });
    public partial class Column
    {
        // py: sqlglot/expressions/core.py::Column.table #091d8e1d3d
        public virtual string table => text("table");
        // py: sqlglot/expressions/core.py::Column.db #10a6674f28
        public virtual string db => text("db");
        // py: sqlglot/expressions/core.py::Column.catalog #4395b9d6da
        public virtual string catalog => text("catalog");
        // py: sqlglot/expressions/core.py::Column.output_name #79b12af339
        public override string output_name => name;
    }
    public partial class Literal
    {
        // py: sqlglot/expressions/core.py::Literal.output_name #79b12af339
        public override string output_name => name;
    }
    public partial class Identifier
    {
        // py: sqlglot/expressions/core.py::Identifier.quoted #78886c42c6
        public virtual bool quoted => py.truthy(args.get("quoted"));
        // py: sqlglot/expressions/core.py::Identifier.output_name #79b12af339
        public override string output_name => name;
    }
    public partial class DynamicIdentifier
    {
        // py: sqlglot/expressions/core.py::DynamicIdentifier.name #78e34e84f6
        public override string name => @this is Expr expression ? expression.name : "";
    }
    public partial class Star
    {
        // py: sqlglot/expressions/core.py::Star.output_name #79b12af339
        public override string output_name => name;
    }
    public partial class Placeholder
    {
        // py: sqlglot/expressions/core.py::Placeholder.name #dd6e7aeb1e
        public override string name => text("this") is { Length: > 0 } name ? name : "?";
    }
    public partial class Dot
    {
        // py: sqlglot/expressions/core.py::Dot.output_name #79b12af339
        public override string output_name => name;
        // py: sqlglot/expressions/core.py::Dot.build #2b29d84e2b
        public static Dot build(IReadOnlyList<Expr> expressions)
        {
            if (expressions.Count < 2) throw new ArgumentException("Dot requires >= 2 expressions.");
            Expr result = expressions[0];
            foreach (var expression in expressions.Skip(1)) result = new Dot(@this: result, expression: expression);
            return (Dot)result;
        }
        // py: sqlglot/expressions/core.py::Dot.parts #a3153823b1
        public virtual List<Expr> parts
        {
            get
            {
                var flattened = flatten().ToList();
                var first = flattened[0];
                var parts = flattened.Skip(1).Reverse().ToList();
                foreach (var arg in COLUMN_PARTS) if (first.args.get(arg) is Expr part) parts.Add(part);
                parts.Reverse();
                return parts;
            }
        }
    }
    public partial class Alias
    {
        // py: sqlglot/expressions/core.py::Alias.output_name #379cd4b608
        public override string output_name => alias;
    }
}
