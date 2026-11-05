namespace SqlGlotSharp;

// py: sqlglot/typing/postgres.py and sqlglot/typing/tsql.py (EXPRESSION_METADATA overlays of the base table)
public static partial class typing
{
    private static PyDict<string, object?> Returns(exp.DType type) => new() { ["returns"] = type };
    private static PyDict<string, object?> Annotator(Annotation annotator) => new() { ["annotator"] = annotator };

    private static PyDict<Type, PyDict<string, object?>> Overlay(
        params (Type[] types, PyDict<string, object?> spec)[] groups)
    {
        var table = EXPRESSION_METADATA.copy();
        foreach (var (types, spec) in groups)
            foreach (var type in types) table[type] = spec;
        return table;
    }

    // py: sqlglot/typing/postgres.py::_annotate_math_function
    private static exp.Expr postgres_annotate_math_function(ITypeAnnotator self, exp.Expr expression)
    {
        if (((exp.Expr)expression.@this!).is_type(exp.DType.FLOAT)) return self._set_type(expression, exp.DType.DOUBLE);
        return annotate_int_as_double(self, expression);
    }

    // py: sqlglot/typing/postgres.py::EXPRESSION_METADATA
    public static readonly PyDict<Type, PyDict<string, object?>> POSTGRES_EXPRESSION_METADATA = Overlay(
        ([typeof(exp.Ntile), typeof(exp.WidthBucket)], Returns(exp.DType.INT)),
        ([typeof(exp.Encode), typeof(exp.Left), typeof(exp.Right), typeof(exp.Overlay), typeof(exp.Reverse), typeof(exp.Pad),
          typeof(exp.Format), typeof(exp.Hex), typeof(exp.SplitPart), typeof(exp.Normalize), typeof(exp.RegexpReplace),
          typeof(exp.Replace)], Returns(exp.DType.TEXT)),
        ([typeof(exp.Decode)], Returns(exp.DType.VARBINARY)),
        ([typeof(exp.TimeFromParts)], Returns(exp.DType.TIME)),
        ([typeof(exp.TimestampFromParts), typeof(exp.Localtimestamp)], Returns(exp.DType.TIMESTAMP)),
        ([typeof(exp.Ceil), typeof(exp.Floor), typeof(exp.Sign)], Annotator(postgres_annotate_math_function)),
        ([typeof(exp.BitwiseAndAgg), typeof(exp.BitwiseOrAgg), typeof(exp.BitwiseXorAgg)],
            Annotator((self, e) => self._annotate_by_args(e, false, false, "this"))),
        ([typeof(exp.ToNumber)], Returns(exp.DType.DECIMAL)));

    // py: sqlglot/typing/tsql.py::_annotate_math_function
    private static exp.Expr tsql_annotate_math_function(ITypeAnnotator self, exp.Expr expression)
    {
        var @this = (exp.Expr)expression.@this!;
        if (@this.is_type(exp.DType.UTINYINT, exp.DType.SMALLINT)) return self._set_type(expression, exp.DType.INT);
        if (@this.is_type([exp.DType.BIT, .. exp.DataType.FLOAT_TYPES_.Cast<object>(), .. exp.DataType.TEXT_TYPES_.Cast<object>()]))
            return self._set_type(expression, exp.DType.DOUBLE);
        if (@this.is_type(exp.DType.SMALLMONEY)) return self._set_type(expression, exp.DType.MONEY);
        if (@this.is_type(exp.DType.DECIMAL) && expression is not exp.Sign) return self._set_type(expression, exp.DType.UNKNOWN);
        return annotate_by_numeric_arg(self, expression);
    }

    // py: sqlglot/typing/tsql.py::EXPRESSION_METADATA
    public static readonly PyDict<Type, PyDict<string, object?>> TSQL_EXPRESSION_METADATA = Overlay(
        ([typeof(exp.Acos), typeof(exp.Asin), typeof(exp.Atan), typeof(exp.Atan2), typeof(exp.Cos), typeof(exp.Cot),
          typeof(exp.Sin), typeof(exp.Tan)], Returns(exp.DType.FLOAT)),
        ([typeof(exp.Soundex), typeof(exp.Stuff)], Returns(exp.DType.VARCHAR)),
        ([typeof(exp.Degrees), typeof(exp.Radians)], Annotator((self, e) => self._annotate_by_args(e, false, false, "this"))),
        ([typeof(exp.Ceil), typeof(exp.Floor), typeof(exp.Sign)], Annotator(tsql_annotate_math_function)),
        ([typeof(exp.CurrentTimezone)], Returns(exp.DType.NVARCHAR)),
        ([typeof(exp.CurrentTimestamp)], Returns(exp.DType.DATETIME)));
}
