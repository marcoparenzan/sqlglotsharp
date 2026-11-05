namespace SqlGlotSharp;

public static partial class typing
{
    public delegate exp.Expr Annotation(ITypeAnnotator self, exp.Expr expression);
    public interface ITypeSchema { object? get_udf_type(exp.Expr expression); }

    /// <summary>Schema-less stand-in for MappingSchema (py: sqlglot/schema.py): UDFs have unknown types, no tables are known.</summary>
    public sealed class ExpressionSchema(dialects.dialect.Dialect dialect) : ITypeSchema
    {
        public dialects.dialect.Dialect dialect { get; } = dialect;
        // py: sqlglot/schema.py::AbstractMappingSchema.get_udf_type (no UDFs registered)
        public object? get_udf_type(exp.Expr expression) => exp.DataType.build(exp.DType.UNKNOWN);
    }

    /// <summary>Contract for the optimizer annotator, which is ported in phase 7. Defaults fail explicitly.</summary>
    public interface ITypeAnnotator
    {
        ITypeSchema schema => throw new NotPortedException("optimizer.TypeAnnotator.schema");
        exp.Expr _set_type(exp.Expr expression, object? type) => throw new NotPortedException("optimizer.TypeAnnotator._set_type");
        exp.Expr _annotate_by_args(exp.Expr expression, bool array = false, bool promote = false, params object?[] args) => throw new NotPortedException("optimizer.TypeAnnotator._annotate_by_args");
        exp.Expr _annotate_binary(exp.Expr expression) => throw new NotPortedException("optimizer.TypeAnnotator._annotate_binary");
        exp.Expr _annotate_unary(exp.Expr expression) => throw new NotPortedException("optimizer.TypeAnnotator._annotate_unary");
        exp.Expr _annotate_by_array_element(exp.Expr expression) => throw new NotPortedException("optimizer.TypeAnnotator._annotate_by_array_element");
        exp.Expr _annotate_timeunit(exp.Expr expression) => throw new NotPortedException("optimizer.TypeAnnotator._annotate_timeunit");
        exp.Expr _annotate_map(exp.Expr expression) => throw new NotPortedException("optimizer.TypeAnnotator._annotate_map");
        exp.Expr _annotate_bracket(exp.Expr expression) => throw new NotPortedException("optimizer.TypeAnnotator._annotate_bracket");
        exp.Expr _annotate_div(exp.Expr expression) => throw new NotPortedException("optimizer.TypeAnnotator._annotate_div");
        exp.Expr _annotate_dot(exp.Expr expression) => throw new NotPortedException("optimizer.TypeAnnotator._annotate_dot");
        exp.Expr _annotate_explode(exp.Expr expression) => throw new NotPortedException("optimizer.TypeAnnotator._annotate_explode");
        exp.Expr _annotate_extract(exp.Expr expression) => throw new NotPortedException("optimizer.TypeAnnotator._annotate_extract");
        exp.Expr _annotate_literal(exp.Expr expression) => throw new NotPortedException("optimizer.TypeAnnotator._annotate_literal");
        exp.Expr _annotate_struct(exp.Expr expression) => throw new NotPortedException("optimizer.TypeAnnotator._annotate_struct");
        exp.Expr _annotate_subquery(exp.Expr expression) => throw new NotPortedException("optimizer.TypeAnnotator._annotate_subquery");
        exp.Expr _annotate_to_map(exp.Expr expression) => throw new NotPortedException("optimizer.TypeAnnotator._annotate_to_map");
        exp.Expr _annotate_unnest(exp.Expr expression) => throw new NotPortedException("optimizer.TypeAnnotator._annotate_unnest");
        exp.Expr _annotate_within_group(exp.Expr expression) => throw new NotPortedException("optimizer.TypeAnnotator._annotate_within_group");
    }

    // py: sqlglot/typing/__init__.py::annotate_by_numeric_arg #de3fc74a12
    public static exp.Expr annotate_by_numeric_arg(ITypeAnnotator self, exp.Expr expression)
    {
        var child = (exp.Expr)expression.@this!;
        var types = exp.DataType.NUMERIC_TYPES_.Select(type => (object)type).Prepend(exp.DType.UNKNOWN).ToArray();
        self._set_type(expression, child.is_type(types) ? (object?)child.type : exp.DType.INT);
        return expression;
    }
    // py: sqlglot/typing/__init__.py::annotate_int_as_double #00759ad2e5
    public static exp.Expr annotate_int_as_double(ITypeAnnotator self, exp.Expr expression)
    {
        var child = (exp.Expr)expression.@this!;
        var types = exp.DataType.INTEGER_TYPES_.Concat(exp.DataType.TEXT_TYPES_).Select(type => (object)type).ToArray();
        self._set_type(expression, child.is_type(types) ? (object)exp.DType.DOUBLE : child.type);
        return expression;
    }
}
