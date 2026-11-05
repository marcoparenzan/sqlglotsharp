namespace SqlGlotSharp;

public static partial class exp
{
    public partial class Expression
    {
        // py: sqlglot/expressions/core.py::Expression.type #8015930429
        public override DataType? type
        {
            get => is_data_type ? (DataType)this : is_cast ? _type ?? (DataType)args["to"]! : _type;
            set => _type = value is not null && value.GetType().Name != nameof(DataType) ? DataType.build(value) : value;
        }
        // py: sqlglot/expressions/core.py::Expression.is_type #e80d135e42
        public override bool is_type(params object[] dtypes) => _type is not null && _type.is_type(dtypes);
    }
    public partial class DataType
    {
        // Union setter inputs use host-language implicit conversions.
        public static implicit operator DataType(DType dtype) => build(dtype);
        public static implicit operator DataType(string dtype) => from_str(dtype);

        // py: sqlglot/expressions/datatypes.py::DataType.build #3648c2306a
        public static DataType build(object dtype, object? dialect = null, bool udt = false, bool copy = true, PyKwargs? kwargs = null)
        {
            if (dtype is string text) return from_str(text, dialect, udt, kwargs);
            if (dtype is DType enumType)
            {
                var result = new DataType(@this: enumType);
                if (kwargs is not null) result.set_kwargs(kwargs);
                return result;
            }
            if (dtype is Identifier or Dot && udt)
            {
                var arguments = new PyDict<string, object?> { ["this"] = DType.USERDEFINED, ["kind"] = dtype };
                if (kwargs is not null)
                    foreach (var (key, value) in kwargs)
                    {
                        if (arguments.ContainsKey(key)) throw new ArgumentException($"Multiple values for argument: {key}");
                        arguments[key] = value;
                    }
                return new DataType(arguments);
            }
            if (dtype is DataType type) return copy ? (DataType)type.copy() : type;
            throw new ArgumentException($"Invalid data type: {dtype.GetType().Name}. Expected str or DType");
        }

        // py: sqlglot/expressions/datatypes.py::DataType.from_str #8cfa3e9a1a
        public static DataType from_str(string dtype, object? dialect = null, bool udt = false, PyKwargs? kwargs = null)
        {
            if (dtype.ToUpperInvariant() != "UNKNOWN")
            {
                try
                {
                    var parsed = (DataType)SqlGlot.ParseOne(dtype, read: dialect, into: typeof(DataType),
                        errorLevel: errors.ErrorLevel.IGNORE);
                    return (DataType)parsed.set_kwargs(kwargs ?? []);
                }
                catch (errors.ParseError)
                {
                    if (!udt) throw;
                    var arguments = new PyDict<string, object?> { ["this"] = DType.USERDEFINED, ["kind"] = dtype };
                    if (kwargs is not null)
                        foreach (var (key, value) in kwargs)
                        {
                            if (arguments.ContainsKey(key)) throw new ArgumentException($"Multiple values for argument: {key}");
                            arguments[key] = value;
                        }
                    return new DataType(arguments);
                }
            }
            {
                var arguments = new PyDict<string, object?> { ["this"] = DType.UNKNOWN };
                if (kwargs is not null)
                    foreach (var (key, value) in kwargs)
                    {
                        if (key == "this") throw new ArgumentException("Multiple values for argument: this");
                        arguments[key] = value;
                    }
                return new DataType(arguments);
            }
        }

        // py: sqlglot/expressions/datatypes.py::DataType.is_type #b92501239b
        public override bool is_type(params object[] dtypes) => is_type(dtypes, false);
        public bool is_type(IReadOnlyList<object> dtypes, bool check_nullable)
        {
            var self_is_nullable = py.truthy(args.get("nullable"));
            foreach (var dtype in dtypes)
            {
                var other = build(dtype, copy: false, udt: true);
                var structural = other.expressions.Count != 0 || check_nullable && (self_is_nullable || py.truthy(other.args.get("nullable"))) ||
                    Equals(@this, DType.USERDEFINED) || Equals(other.@this, DType.USERDEFINED);
                if (structural ? Equals(other) : Equals(@this, other.@this)) return true;
            }
            return false;
        }
    }
}
