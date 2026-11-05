namespace SqlGlotSharp.dialects;

public static partial class dialect
{
    public partial class Dialect
    {
        // py: sqlglot/dialects/dialect.py::Dialect.EXPRESSION_METADATA #fd8df6610d
        public static readonly PyDict<Type, PyDict<string, object?>> EXPRESSION_METADATA_ = global::SqlGlotSharp.typing.EXPRESSION_METADATA.copy();
        public virtual PyDict<Type, PyDict<string, object?>> EXPRESSION_METADATA => EXPRESSION_METADATA_;
    }
}
