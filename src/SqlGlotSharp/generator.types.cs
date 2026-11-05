namespace SqlGlotSharp;

public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.datatypeparam_sql #0000000000
        public virtual string datatypeparam_sql(exp.DataTypeParam expression)
        {
            var value = sql(expression, "this");
            var specifier = sql(expression, "expression");
            return value + (specifier.Length > 0 && DATA_TYPE_SPECIFIERS_ALLOWED ? $" {specifier}" : "");
        }

        // Type-param bound/default adapters remain an explicit dependency.
        // py: sqlglot/generator.py::Generator.datatype_sql #0000000000
        public virtual string datatype_sql(exp.DataType expression)
        {
            var exprNested = py.truthy(expression.args.get("nested"));
            var typeValue = expression.@this;
            if (!exprNested && typeValue is exp.DType && TYPE_PARAM_SETTINGS.Count > 0)
                throw new NotPortedException("Generator.datatype_param_bound_limiter");
            var interior = exprNested && py.truthy(pretty)
                ? expressions(expression, dynamic: true, new_line: true, skip_first: true, skip_last: true)
                : expressions(expression, flat: true);
            if (typeValue is exp.DType dtype && UNSUPPORTED_TYPES.Contains(dtype))
                unsupported($"Data type {exp.ToValue(dtype)} is not supported when targeting {dialect.GetType().Name}");
            string typeSql;
            if (Equals(typeValue, exp.DType.USERDEFINED) && py.truthy(expression.args.get("kind"))) typeSql = sql(expression, "kind");
            else if (Equals(typeValue, exp.DType.CHARACTER_SET))
            {
                var chr = interior.Length > 0 ? $"CHAR({interior})" : "CHAR";
                return $"{chr} CHARACTER SET {sql(expression, "kind")}";
            }
            else if (typeValue is exp.DType type) typeSql = TYPE_MAPPING.get(type) ?? exp.ToValue(type);
            else if (typeValue is string text) typeSql = text;
            else typeSql = typeValue is exp.Expr typeExpr ? new Generator().generate(typeExpr, copy: false) : py.str(typeValue);
            var nested = "";
            var values = "";
            if (interior.Length > 0)
            {
                if (exprNested)
                {
                    nested = $"{STRUCT_DELIMITER.Item1}{interior}{STRUCT_DELIMITER.Item2}";
                    if (expression.args.get("values") is not null)
                    {
                        var val = expressions(expression, key: "values", flat: true);
                        values = Equals(typeValue, exp.DType.ARRAY) ? $"[{val}]" : $"({val})";
                    }
                }
                else nested = Equals(typeValue, exp.DType.INTERVAL) ? $" {interior}" : $"({interior})";
            }
            typeSql += nested + values;
            if (TZ_TO_WITH_TIME_ZONE && typeValue is exp.DType.TIMETZ or exp.DType.TIMESTAMPTZ)
                typeSql += " WITH TIME ZONE";
            var collate = sql(expression, "collate");
            return typeSql + (collate.Length > 0 ? $" COLLATE {collate}" : "");
        }

        // py: sqlglot/generator.py::Generator.cast_sql #0000000000
        public virtual string cast_sql(exp.Cast expression, string? safe_prefix = null)
        {
            var format = sql(expression, "format");
            format = format.Length > 0 ? $" FORMAT {format}" : "";
            var to = sql(expression, "to");
            to = to.Length > 0 ? $" {to}" : "";
            var action = sql(expression, "action");
            action = action.Length > 0 ? $" {action}" : "";
            var @default = sql(expression, "default");
            @default = @default.Length > 0 ? $" DEFAULT {@default} ON CONVERSION ERROR" : "";
            return $"{safe_prefix ?? ""}CAST({sql(expression, "this")} AS{to}{@default}{format}{action})";
        }
    }
}
