namespace SqlGlotSharp.dialects;

public static partial class postgres
{
    public partial class Postgres
    {
        // py: sqlglot/dialects/postgres.py::Postgres.EXPRESSION_METADATA
        public static new readonly PyDict<Type, PyDict<string, object?>> EXPRESSION_METADATA_ = global::SqlGlotSharp.typing.POSTGRES_EXPRESSION_METADATA.copy();
        public override PyDict<Type, PyDict<string, object?>> EXPRESSION_METADATA => EXPRESSION_METADATA_;
    }
}

public static partial class tsql
{
    public partial class TSQL
    {
        // py: sqlglot/dialects/tsql.py::TSQL.EXPRESSION_METADATA
        public static new readonly PyDict<Type, PyDict<string, object?>> EXPRESSION_METADATA_ = global::SqlGlotSharp.typing.TSQL_EXPRESSION_METADATA.copy();
        public override PyDict<Type, PyDict<string, object?>> EXPRESSION_METADATA => EXPRESSION_METADATA_;
    }
}
