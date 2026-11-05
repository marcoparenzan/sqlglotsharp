namespace SqlGlotSharp;

public static partial class exp
{
    public partial class Cast
    {
        // py: sqlglot/expressions/functions.py::Cast.name
        public override string name => ((Expr)@this!).name;

        // py: sqlglot/expressions/functions.py::Cast.to
        public DataType to => (DataType)args["to"]!;

        // py: sqlglot/expressions/functions.py::Cast.output_name
        public override string output_name => name;

        // py: sqlglot/expressions/functions.py::Cast.is_type
        public override bool is_type(params object[] dtypes) => to.is_type(dtypes);
    }
}
