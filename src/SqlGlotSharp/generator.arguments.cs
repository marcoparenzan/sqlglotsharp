namespace SqlGlotSharp;

public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.kwarg_sql #0000000000
        public virtual string kwarg_sql(exp.Kwarg expression) => binary(expression, "=>");
    }
}
