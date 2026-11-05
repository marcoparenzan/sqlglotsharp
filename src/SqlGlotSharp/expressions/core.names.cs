namespace SqlGlotSharp;

public static partial class exp
{
    public abstract partial class Expr
    {
        // py: sqlglot/expressions/core.py::Expr.alias #bd86304ae2
        public abstract string alias { get; }
        // py: sqlglot/expressions/core.py::Expr.alias_column_names #41efafc7be
        public abstract List<string> alias_column_names { get; }
        // py: sqlglot/expressions/core.py::Expr.alias_or_name #70ba5788a3
        public abstract string alias_or_name { get; }
        // py: sqlglot/expressions/core.py::Expr.output_name #2b397b20d3
        public abstract string output_name { get; }
        // py: sqlglot/expressions/core.py::Expr.is_leaf #7482fa4f90
        public abstract bool is_leaf();
    }
    public partial class Expression
    {
        // py: sqlglot/expressions/core.py::Expression.alias #0863b16d5e
        public override string alias => args.get("alias") is Expression expression ? expression.name : text("alias");
        // py: sqlglot/expressions/core.py::Expression.alias_column_names #687a55cfef
        public override List<string> alias_column_names
        {
            get
            {
                if (args.get("alias") is not Expr table_alias) return [];
                return (table_alias.args.get("columns") as List<object?> ?? []).Cast<Expr>().Select(c => c.name).ToList();
            }
        }
        // py: sqlglot/expressions/core.py::Expression.alias_or_name #88097a5928
        public override string alias_or_name => alias.Length != 0 ? alias : name;
        // py: sqlglot/expressions/core.py::Expression.output_name #69b63bec1c
        public override string output_name => "";
        // py: sqlglot/expressions/core.py::Expression.is_leaf #47dc909ac1
        public override bool is_leaf() => !args.Values.Any(value => value is Expr || value is List<object?> list && list.Count != 0);
    }
}
