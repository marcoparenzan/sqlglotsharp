namespace SqlGlotSharp;

// JSON_OBJECT, key/value, LOG, PRAGMA and lambda rendering of the base generator.
public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator._jsonobject_sql #0000000000
        public virtual string _jsonobject_sql(exp.Expr expression, string name = "")
        {
            var null_handling = expression.args.get("null_handling");
            var null_handling_sql = py.truthy(null_handling) ? $" {py.str(null_handling)}" : "";

            var unique_keys = expression.args.get("unique_keys");
            var unique_keys_sql = unique_keys is not null ? $" {(py.truthy(unique_keys) ? "WITH" : "WITHOUT")} UNIQUE KEYS" : "";

            var return_type = sql(expression, "return_type");
            return_type = return_type.Length != 0 ? $" RETURNING {return_type}" : "";
            var encoding = sql(expression, "encoding");
            encoding = encoding.Length != 0 ? $" ENCODING {encoding}" : "";

            if (name.Length == 0) name = expression is exp.JSONObject ? "JSON_OBJECT" : "JSON_OBJECTAGG";

            return func(name, expression.expressions.ToArray(), prefix: "(",
                suffix: $"{null_handling_sql}{unique_keys_sql}{return_type}{encoding})");
        }

        // py: sqlglot/generator.py::Generator.jsonkeyvalue_sql #0000000000
        public virtual string jsonkeyvalue_sql(exp.JSONKeyValue expression) =>
            $"{sql(expression, "this")}{JSON_KEY_VALUE_PAIR_SEP} {sql(expression, "expression")}";

        // py: sqlglot/generator.py::Generator.formatjson_sql #0000000000
        public virtual string formatjson_sql(exp.FormatJson expression) => $"{sql(expression, "this")} FORMAT JSON";

        // py: sqlglot/generator.py::Generator.pragma_sql #0000000000
        public virtual string pragma_sql(exp.Pragma expression) => $"PRAGMA {sql(expression, "this")}";

        // py: sqlglot/generator.py::Generator.log_sql #0000000000
        public virtual string log_sql(exp.Log expression)
        {
            var @this = expression.@this;
            var expr = expression.args.get("expression");

            // LOG_BASE_FIRST is a plain bool in this port (upstream: True/False/None).
            if (!dialect.LOG_BASE_FIRST) (@this, expr) = (expr, @this);

            return func("LOG", @this, expr);
        }

        // py: sqlglot/dialects/dialect.py::rename_func #0000000000
        internal string rename_func(string name, exp.Expr expression) =>
            func(name, helper.flatten(expression.args.Values).ToArray());

        // py: sqlglot/dialects/dialect.py::trim_sql #0000000000
        internal string dialect_trim_sql(exp.Trim expression, string default_trim_type = "")
        {
            var remove_chars = sql(expression, "expression");

            // Use TRIM/LTRIM/RTRIM syntax if the expression isn't database-specific
            if (remove_chars.Length == 0) return trim_sql(expression);

            var target = sql(expression, "this");
            var trim_type = sql(expression, "position");
            if (trim_type.Length == 0) trim_type = default_trim_type;
            var collation = sql(expression, "collation");

            trim_type = trim_type.Length != 0 ? $"{trim_type} " : "";
            remove_chars = remove_chars.Length != 0 ? $"{remove_chars} " : "";
            var from_part = trim_type.Length != 0 || remove_chars.Length != 0 ? "FROM " : "";
            collation = collation.Length != 0 ? $" COLLATE {collation}" : "";
            return $"TRIM({trim_type}{remove_chars}{from_part}{target}{collation})";
        }

        // py: sqlglot/dialects/dialect.py::groupconcat_sql #0000000000
        internal string groupconcat_sql(exp.GroupConcat expression, string func_name = "LISTAGG", string? sep = ",",
            bool within_group = true, bool on_overflow = false)
        {
            var @this = expression.@this as exp.Expr;
            var separator_arg = expression.args.get("separator") ?? (sep is not null && sep.Length != 0 ? exp.Literal.@string(sep) : null);
            var separator = sql(separator_arg);

            var on_overflow_sql = sql(expression, "on_overflow");
            on_overflow_sql = on_overflow && on_overflow_sql.Length != 0 ? $" ON OVERFLOW {on_overflow_sql}" : "";

            exp.Limit? limit;
            if (@this is exp.Limit lim && py.truthy(lim.@this))
            {
                limit = lim;
                @this = (exp.Expr)limit.@this!;
                @this = @this.pop();
            }
            else limit = null;

            var order = @this!.find<exp.Order>();

            if (order is not null && py.truthy(order.@this))
                @this = ((exp.Expr)order.@this!).pop();

            var args = format_args(@this, separator.Length != 0 || on_overflow_sql.Length != 0 ? $"{separator}{on_overflow_sql}" : null);

            exp.Expr listagg = new exp.Anonymous(@this: func_name, expressions: new List<object?> { args });

            var modifiers = sql(limit);

            if (order is not null)
            {
                if (within_group) listagg = new exp.WithinGroup(@this: listagg, expression: order);
                else modifiers = $"{sql(order)}{modifiers}";
            }

            if (modifiers.Length != 0) listagg.set("expressions", new List<object?> { $"{args}{modifiers}" });

            return sql(listagg);
        }

        // py: sqlglot/generator.py::Generator.lambda_sql #0000000000
        public virtual string lambda_sql(exp.Lambda expression, string arrow_sep = "->", bool wrap = true)
        {
            var args = expressions(expression, flat: true);
            args = wrap && args.Split(',').Length > 1 ? $"({args})" : args;
            return $"{args} {arrow_sep} {sql(expression, "this")}";
        }
    }
}
