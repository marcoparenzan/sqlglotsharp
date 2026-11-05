namespace SqlGlotSharp.generators;

public static partial class postgres
{
    public partial class PostgresGenerator
    {
        // py: sqlglot/generators/postgres.py::PostgresGenerator.alterset_sql #0000000000
        public override string alterset_sql(exp.AlterSet expression)
        {
            var exprs = expressions(expression, flat: true);
            exprs = exprs.Length != 0 ? $"({exprs})" : "";

            var access_method = sql(expression, "access_method");
            access_method = access_method.Length != 0 ? $"ACCESS METHOD {access_method}" : "";
            var tablespace = sql(expression, "tablespace");
            tablespace = tablespace.Length != 0 ? $"TABLESPACE {tablespace}" : "";
            var option = sql(expression, "option");

            return $"SET {exprs}{access_method}{tablespace}{option}";
        }
    }
}
