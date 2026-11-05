namespace SqlGlotSharp.generators;

public static partial class postgres
{
    public partial class PostgresGenerator
    {
        // py: sqlglot/generators/postgres.py::PostgresGenerator.datatype_sql #0000000000
        public override string datatype_sql(exp.DataType expression)
        {
            var value = expression.@this;
            if (Equals(value, exp.DType.ARRAY))
            {
                if (py.truthy(expression.args.get("expressions")))
                    return $"{expressions(expression, flat: true)}[{expressions(expression, key: "values", flat: true)}]";
                return "ARRAY";
            }
            if (Equals(value, exp.DType.ENUM)) return $"ENUM ({expressions(expression, flat: true)})";
            if (value is exp.DType.DOUBLE or exp.DType.FLOAT && py.truthy(expression.args.get("expressions")))
                return $"FLOAT({expressions(expression, flat: true)})";
            return base.datatype_sql(expression);
        }

        // py: sqlglot/generators/postgres.py::PostgresGenerator.cast_sql #0000000000
        public override string cast_sql(exp.Cast expression, string? safe_prefix = null)
        {
            if (expression.@this is exp.IntDiv && (TRANSFORMS.ContainsKey(typeof(exp.IntDiv)) || MORE_TRANSFORMS.ContainsKey(typeof(exp.IntDiv)))
                && expression.args.get("to") is exp.DataType to && to.Equals(new exp.DataType(@this: exp.DType.DECIMAL)))
                return sql(expression.@this);
            return base.cast_sql(expression, safe_prefix);
        }
    }
}
