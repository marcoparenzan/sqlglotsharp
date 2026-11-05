namespace SqlGlotSharp.generators;

public static partial class postgres
{
    public partial class PostgresGenerator
    {
        // py: sqlglot/generators/postgres.py::PostgresGenerator.bytestring_sql #0000000000
        public override string bytestring_sql(exp.ByteString expression)
        {
            if (!py.truthy(expression.args.get("is_bytes"))) return base.bytestring_sql(expression);
            return func("DECODE", exp.Literal.@string(LatinHex(expression.name)), exp.Literal.@string("hex"));
        }
    }
}
