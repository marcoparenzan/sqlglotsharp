namespace SqlGlotSharp;

// FUNCTIONS overrides of parser.py and their module-level build_* helpers.
//
// Upstream calls func_builder(args) and, on TypeError, func_builder(args, dialect=self.dialect).
// The C# registry is static and dialect-free, so _parse_function_call publishes the dialect and
// parser in thread-static slots for the duration of the builder call (BuilderDialect/BuilderParser).
public static partial class parser
{
    public partial class Parser
    {
        [ThreadStatic] private static dialects.dialect.Dialect? _builder_dialect;
        [ThreadStatic] private static Parser? _builder_parser;
        internal static dialects.dialect.Dialect BuilderDialect => _builder_dialect
            ?? throw new InvalidOperationException("Dialect-dependent function builder invoked outside _parse_function_call.");
        internal static Parser BuilderParser => _builder_parser
            ?? throw new InvalidOperationException("Dialect-dependent function builder invoked outside _parse_function_call.");

        internal exp.Expr _call_function_builder(Func<IReadOnlyList<object?>, exp.Expr> builder, List<object?> args)
        {
            var (previous_dialect, previous_parser) = (_builder_dialect, _builder_parser);
            (_builder_dialect, _builder_parser) = (dialect, this);
            try { return builder(args); }
            finally { (_builder_dialect, _builder_parser) = (previous_dialect, previous_parser); }
        }

        private static object? SeqGet(IReadOnlyList<object?> args, int index) => index < args.Count ? args[index] : null;
        private static List<object?> Tail(IReadOnlyList<object?> args) => args.Skip(1).ToList();

        internal static T Mk<T>(params (string key, object? value)[] pairs) where T : exp.Expr
        {
            var arguments = new PyDict<string, object?>();
            foreach (var (key, value) in pairs) arguments[key] = value;
            return (T)Activator.CreateInstance(typeof(T), [arguments])!;
        }

        private static exp.Cast CastToText(object? value) =>
            Mk<exp.Cast>(("this", value), ("to", new exp.DataType(@this: exp.DType.TEXT)));

        // py: sqlglot/parser.py::build_coalesce #0000000000
        internal static exp.Coalesce build_coalesce(IReadOnlyList<object?> args, bool? is_nvl = null, bool? is_null = null) =>
            Mk<exp.Coalesce>(("this", SeqGet(args, 0)), ("expressions", Tail(args)), ("is_nvl", is_nvl), ("is_null", is_null));

        // py: sqlglot/parser.py::build_like #0000000000
        private static exp.Expr build_like(IReadOnlyList<object?> args)
        {
            var like = Mk<exp.Like>(("this", SeqGet(args, 1)), ("expression", SeqGet(args, 0)));
            return args.Count > 2 ? Mk<exp.Escape>(("this", like), ("expression", SeqGet(args, 2))) : like;
        }

        // py: sqlglot/parser.py::build_sort_array #0000000000
        private static exp.ArraySort build_sort_array(IReadOnlyList<object?> args)
        {
            // SORT_ARRAY puts NULLs first exactly when ascending
            var asc = SeqGet(args, 1);
            object? nulls_first = asc is null ? new exp.Boolean(@this: true) : ((exp.Expr)asc).copy();
            return Mk<exp.ArraySort>(("this", SeqGet(args, 0)), ("asc", asc), ("nulls_first", nulls_first));
        }

        // py: sqlglot/parser.py::build_logarithm #0000000000
        private static exp.Expr build_logarithm(IReadOnlyList<object?> args)
        {
            // Default argument order is base, expression
            var @this = SeqGet(args, 0);
            var expression = SeqGet(args, 1);

            if (py.truthy(expression))
            {
                if (!BuilderDialect.LOG_BASE_FIRST) (@this, expression) = (expression, @this);
                return Mk<exp.Log>(("this", @this), ("expression", expression));
            }

            return BuilderParser.LOG_DEFAULTS_TO_LN
                ? Mk<exp.Ln>(("this", @this))
                : Mk<exp.Log>(("this", @this));
        }

        // py: sqlglot/parser.py::build_hex #0000000000
        private static exp.Expr build_hex(IReadOnlyList<object?> args) =>
            BuilderDialect.HEX_LOWERCASE ? Mk<exp.LowerHex>(("this", SeqGet(args, 0))) : Mk<exp.Hex>(("this", SeqGet(args, 0)));

        // py: sqlglot/parser.py::build_lower #0000000000
        private static exp.Expr build_lower(IReadOnlyList<object?> args)
        {
            // LOWER(HEX(..)) can be simplified to LowerHex to simplify its transpilation
            var arg = SeqGet(args, 0);
            return arg is exp.Hex hex ? Mk<exp.LowerHex>(("this", hex.@this)) : Mk<exp.Lower>(("this", arg));
        }

        // py: sqlglot/parser.py::build_upper #0000000000
        private static exp.Expr build_upper(IReadOnlyList<object?> args)
        {
            // UPPER(HEX(..)) can be simplified to Hex to simplify its transpilation
            var arg = SeqGet(args, 0);
            return arg is exp.Hex hex ? Mk<exp.Hex>(("this", hex.@this)) : Mk<exp.Upper>(("this", arg));
        }

        // py: sqlglot/parser.py::build_extract_json_with_path #0000000000
        internal static Func<IReadOnlyList<object?>, exp.Expr> build_extract_json_with_path<T>() where T : exp.Expr => args =>
        {
            var expression = Mk<T>(("this", SeqGet(args, 0)), ("expression", BuilderDialect.to_json_path((exp.Expr?)SeqGet(args, 1))));
            if (args.Count > 2 && typeof(T) == typeof(exp.JSONExtract)) expression.set("expressions", args.Skip(2).ToList());
            if (typeof(T) == typeof(exp.JSONExtractScalar)) expression.set("scalar_only", BuilderDialect.JSON_EXTRACT_SCALAR_SCALAR_ONLY);
            return expression;
        };

        // py: sqlglot/parser.py::build_mod #0000000000
        private static exp.Mod build_mod(IReadOnlyList<object?> args)
        {
            var @this = SeqGet(args, 0);
            var expression = SeqGet(args, 1);

            // Wrap the operands if they are binary nodes, e.g. MOD(a + 1, 7) -> (a + 1) % 7
            @this = @this is exp.Binary ? Mk<exp.Paren>(("this", @this)) : @this;
            expression = expression is exp.Binary ? Mk<exp.Paren>(("this", expression)) : expression;

            return Mk<exp.Mod>(("this", @this), ("expression", expression));
        }

        // py: sqlglot/parser.py::build_pad #0000000000
        private static exp.Pad build_pad(IReadOnlyList<object?> args, bool is_left = true) =>
            Mk<exp.Pad>(("this", SeqGet(args, 0)), ("expression", SeqGet(args, 1)),
                ("fill_pattern", SeqGet(args, 2)), ("is_left", is_left));

        // py: sqlglot/parser.py::build_trim #0000000000
        private static exp.Trim build_trim(IReadOnlyList<object?> args, bool is_left = true, bool reverse_args = false)
        {
            var (@this, expression) = (SeqGet(args, 0), SeqGet(args, 1));

            if (py.truthy(expression) && reverse_args) (@this, expression) = (expression, @this);

            return Mk<exp.Trim>(("this", @this), ("expression", expression), ("position", is_left ? "LEADING" : "TRAILING"));
        }

        // py: sqlglot/parser.py::build_locate_strposition #0000000000
        private static exp.StrPosition build_locate_strposition(IReadOnlyList<object?> args) =>
            Mk<exp.StrPosition>(("this", SeqGet(args, 1)), ("substr", SeqGet(args, 0)), ("position", SeqGet(args, 2)));

        private static Func<IReadOnlyList<object?>, exp.Expr> build_array_null_propagation<T>(bool variadic = false) where T : exp.Expr => args =>
            variadic
                ? Mk<T>(("this", SeqGet(args, 0)), ("expressions", Tail(args)), ("null_propagation", BuilderDialect.ARRAY_FUNCS_PROPAGATES_NULLS))
                : Mk<T>(("this", SeqGet(args, 0)), ("expression", SeqGet(args, 1)), ("null_propagation", BuilderDialect.ARRAY_FUNCS_PROPAGATES_NULLS));

        // Entries of Parser.FUNCTIONS that override the generated registry (everything but the two
        // builders that still depend on exp.array / convert_timezone helpers).
        // (Parser.FUNCTIONS itself is anchored in parser.functions.cs.)
        private static void ApplyFunctionOverrides(PyDict<string, Func<IReadOnlyList<object?>, exp.Expr>> functions)
        {
            foreach (var name in new[] { "COALESCE", "IFNULL", "NVL" }) functions[name] = args => build_coalesce(args);
            functions["ARRAY"] = args => Mk<exp.Array>(("expressions", args.ToList()));
            functions["SORT_ARRAY"] = build_sort_array;
            foreach (var name in new[] { "ARRAYAGG", "ARRAY_AGG" })
                // nulls_excluded is `dialect.ARRAY_AGG_INCLUDES_NULLS is None or None`: a bool flag is never None.
                functions[name] = args => Mk<exp.ArrayAgg>(("this", SeqGet(args, 0)), ("nulls_excluded", null));
            functions["ARRAY_APPEND"] = build_array_null_propagation<exp.ArrayAppend>();
            functions["ARRAY_CAT"] = build_array_null_propagation<exp.ArrayConcat>(variadic: true);
            functions["ARRAY_CONCAT"] = build_array_null_propagation<exp.ArrayConcat>(variadic: true);
            functions["ARRAY_INTERSECT"] = args => Mk<exp.ArrayIntersect>(("expressions", args.ToList()));
            functions["ARRAY_INTERSECTION"] = args => Mk<exp.ArrayIntersect>(("expressions", args.ToList()));
            functions["ARRAY_PREPEND"] = build_array_null_propagation<exp.ArrayPrepend>();
            functions["ARRAY_REMOVE"] = build_array_null_propagation<exp.ArrayRemove>();
            functions["CONCAT"] = args => Mk<exp.Concat>(("expressions", args.ToList()),
                ("safe", !BuilderDialect.STRICT_STRING_CONCAT), ("coalesce", BuilderDialect.CONCAT_COALESCE));
            functions["CONCAT_WS"] = args => Mk<exp.ConcatWs>(("expressions", args.ToList()),
                ("safe", !BuilderDialect.STRICT_STRING_CONCAT), ("coalesce", BuilderDialect.CONCAT_WS_COALESCE));
            functions["DATE_TO_DATE_STR"] = args => CastToText(SeqGet(args, 0));
            functions["GENERATE_DATE_ARRAY"] = args => Mk<exp.GenerateDateArray>(("start", SeqGet(args, 0)), ("end", SeqGet(args, 1)),
                ("step", SeqGet(args, 2) is { } step && py.truthy(step) ? step
                    : Mk<exp.Interval>(("this", exp.Literal.@string(1)), ("unit", new exp.Var(@this: "DAY")))));
            functions["GENERATE_UUID"] = args => Mk<exp.Uuid>(("is_string", BuilderDialect.UUID_IS_STRING_TYPE ? true : null));
            functions["GLOB"] = args => Mk<exp.Glob>(("this", SeqGet(args, 1)), ("expression", SeqGet(args, 0)));
            functions["GREATEST"] = args => Mk<exp.Greatest>(("this", SeqGet(args, 0)), ("expressions", Tail(args)),
                ("ignore_nulls", BuilderDialect.LEAST_GREATEST_IGNORES_NULLS));
            functions["LEAST"] = args => Mk<exp.Least>(("this", SeqGet(args, 0)), ("expressions", Tail(args)),
                ("ignore_nulls", BuilderDialect.LEAST_GREATEST_IGNORES_NULLS));
            functions["HEX"] = build_hex;
            functions["JSON_EXTRACT"] = build_extract_json_with_path<exp.JSONExtract>();
            functions["JSON_EXTRACT_SCALAR"] = build_extract_json_with_path<exp.JSONExtractScalar>();
            functions["JSON_EXTRACT_PATH_TEXT"] = build_extract_json_with_path<exp.JSONExtractScalar>();
            functions["JSON_KEYS"] = args => Mk<exp.JSONKeys>(("this", SeqGet(args, 0)),
                ("expression", BuilderDialect.to_json_path((exp.Expr?)SeqGet(args, 1))));
            functions["LIKE"] = build_like;
            functions["LOG"] = build_logarithm;
            functions["LOG2"] = args => Mk<exp.Log>(("this", exp.Literal.number(2)), ("expression", SeqGet(args, 0)));
            functions["LOG10"] = args => Mk<exp.Log>(("this", exp.Literal.number(10)), ("expression", SeqGet(args, 0)));
            functions["LOWER"] = build_lower;
            functions["LPAD"] = args => build_pad(args);
            functions["LEFTPAD"] = args => build_pad(args);
            functions["LTRIM"] = args => build_trim(args);
            functions["MOD"] = build_mod;
            functions["RIGHTPAD"] = args => build_pad(args, is_left: false);
            functions["RPAD"] = args => build_pad(args, is_left: false);
            functions["RTRIM"] = args => build_trim(args, is_left: false);
            functions["SCOPE_RESOLUTION"] = args => args.Count != 2
                ? Mk<exp.ScopeResolution>(("expression", SeqGet(args, 0)))
                : Mk<exp.ScopeResolution>(("this", SeqGet(args, 0)), ("expression", SeqGet(args, 1)));
            functions["CHARINDEX"] = build_locate_strposition;
            functions["LOCATE"] = build_locate_strposition;
            functions["TIME_TO_TIME_STR"] = args => CastToText(SeqGet(args, 0));
            functions["TO_HEX"] = build_hex;
            functions["TS_OR_DS_TO_DATE_STR"] = args => Mk<exp.Substring>(("this", CastToText(SeqGet(args, 0))),
                ("start", exp.Literal.number(1)), ("length", exp.Literal.number(10)));
            functions["UNNEST"] = args => Mk<exp.Unnest>(("expressions", args.ToList()));
            functions["UPPER"] = build_upper;
            functions["UUID"] = args => Mk<exp.Uuid>(("is_string", BuilderDialect.UUID_IS_STRING_TYPE ? true : null));
            functions["UUID_STRING"] = args => Mk<exp.Uuid>(("this", SeqGet(args, 0)), ("name", SeqGet(args, 1)),
                ("is_string", BuilderDialect.UUID_IS_STRING_TYPE ? true : null));
        }
    }
}
