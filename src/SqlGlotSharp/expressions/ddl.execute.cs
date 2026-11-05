namespace SqlGlotSharp;

public static partial class exp
{
    public partial class Execute
    {
        // py: sqlglot/expressions/ddl.py::Execute.name
        public override string name => ((Expr)@this!).name;
    }
}
