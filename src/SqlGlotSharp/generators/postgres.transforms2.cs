namespace SqlGlotSharp.generators;

// Dialect-helper based entries of sqlglot/generators/postgres.py::PostgresGenerator.TRANSFORMS.
public static partial class postgres
{
    public partial class PostgresGenerator
    {
        // py: sqlglot/expressions/core.py::_wrap
        internal static exp.Expr Wrap(exp.Expr expression, Type kind) =>
            kind.IsInstanceOfType(expression) ? new exp.Paren(@this: expression) : expression;

        // py: sqlglot/expressions/core.py::Expr._binop (operator overloading: neither side is already the target class)
        internal static exp.Expr Binop(Type klass, exp.Expr left, exp.Expr right)
        {
            var @this = left.copy();
            var other = right.copy();
            if (!klass.IsInstanceOfType(@this) && !klass.IsInstanceOfType(other))
            {
                @this = Wrap(@this, typeof(exp.Binary));
                other = Wrap(other, typeof(exp.Binary));
            }
            return (exp.Expr)Activator.CreateInstance(klass, [new PyDict<string, object?> { ["this"] = @this, ["expression"] = other }])!;
        }

        // py: sqlglot/dialects/dialect.py::date_delta_to_binary_sql
        private static string date_delta_to_binary_sql(generator.Generator self, exp.Expr expression, exp.Expr binary)
        {
            var parent = expression.parent;
            if ((parent is exp.Binary or exp.Unary or exp.AtTimeZone)
                && !(parent is exp.Paren || (parent is exp.Add or exp.Sub && ReferenceEquals(parent.args.get("this"), expression))))
                return self.sql(new exp.Paren(@this: binary));
            return self.sql(binary);
        }

        // py: sqlglot/generators/postgres.py::_date_add_sql
        // py: sqlglot/dialects/dialect.py::ts_or_ds_add_cast
        internal static exp.Expr ts_or_ds_add_cast(generator.Generator self, exp.Expr expression)
        {
            var @this = ((exp.Expr)expression.args["this"]!).copy();

            var return_type = exp.DataType.build(expression.args.get("return_type") ?? exp.DType.DATE);
            if (return_type.is_type(exp.DType.DATE))
                // If we need to cast to a DATE, we cast to TIMESTAMP first to make sure we
                // can truncate timestamp strings, because some dialects can't cast them to DATE
                @this = self.exp_cast(@this, exp.DType.TIMESTAMP);

            ((exp.Expr)expression.args["this"]!).replace(self.exp_cast(@this, return_type));
            return expression;
        }

        private static Func<generator.Generator, exp.Expr, string> date_add_sql(string kind) => (self, expression) =>
        {
            if (expression is exp.TsOrDsAdd) expression = ts_or_ds_add_cast(self, expression);

            var @this = (exp.Expr)expression.args["this"]!;
            var unit = expression.args.get("unit");

            var e = self._simplify_unless_literal(expression.args.get("expression") as exp.Expr)!;
            exp.Expr interval;
            if (e is exp.Interval) interval = e;
            else if (e.is_number || e.is_string) interval = new exp.Interval(@this: exp.Literal.@string(e.to_py()), unit: unit);
            else
            {
                var one = exp.Literal.number(1);
                interval = Binop(typeof(exp.Mul), new exp.Interval(@this: one, unit: unit), e);
            }

            return date_delta_to_binary_sql(self, expression, Binop(kind == "+" ? typeof(exp.Add) : typeof(exp.Sub), @this, interval));
        };

        // py: sqlglot/generators/postgres.py::_day_month_year_sql
        private static string day_month_year_sql(generator.Generator self, exp.Expr expression)
        {
            var @this = (exp.Expr)expression.args["this"]!;
            var value = @this is exp.TsOrDsToDate ? (exp.Expr)@this.args["this"]! : @this;

            if (value.is_type(exp.DataType.INTEGER_TYPES_.Cast<object>().ToArray()) && @this.args.get("default_date") is exp.Expr default_date)
                @this = Binop(typeof(exp.Add), CastTo(default_date, exp.DType.DATE), value);

            return self.sql(new exp.Extract(@this: new exp.Var(@this: expression.sql_name()), expression: @this));
        }

        // py: sqlglot/dialects/dialect.py::array_concat_sql
        private static Func<generator.Generator, exp.Expr, string> array_concat_sql(string name) => (self, expression) =>
        {
            string BuildFuncCall(string func_name, List<object?> args)
            {
                if (self.ARRAY_CONCAT_IS_VAR_LEN) return self.func(func_name, args.ToArray());
                if (args.Count == 1)
                    // Single arg gets empty array to preserve semantics
                    return self.func(func_name, args[0], new exp.Array(expressions: new List<object?>()));
                // Snowflake/PostgreSQL/Redshift require binary nesting: ARRAY_CAT(a, ARRAY_CAT(b, c))
                var result = self.func(func_name, args[^2], args[^1]);
                for (var i = args.Count - 3; i >= 0; i--) result = $"{func_name}({self.sql(args[i])}, {result})";
                return result;
            }

            var @this = expression.args["this"];
            var exprs = ((System.Collections.IEnumerable)(expression.args.get("expressions") ?? new List<object?>())).Cast<object?>().ToList();
            var all_args = new List<object?> { @this };
            all_args.AddRange(exprs);

            var source_null_propagation = py.truthy(expression.args.get("null_propagation"));
            var target_null_propagation = self.dialect.ARRAY_FUNCS_PROPAGATES_NULLS;

            // Skip wrapper when source and target have matching NULL semantics,
            // or when the first argument is an array literal (which can never be NULL),
            // or when it is a single-argument call (empty array is added, preserving NULL semantics)
            if (source_null_propagation == target_null_propagation || @this is exp.Array || exprs.Count == 0)
                return BuildFuncCall(name, all_args);

            // Case 1: Source propagates NULLs, target does not
            if (source_null_propagation)
            {
                exp.Expr combined = new exp.Is(@this: ((exp.Expr)all_args[0]!).copy(), expression: new exp.Null());
                foreach (var arg in all_args.Skip(1))
                    combined = new exp.Or(@this: combined, expression: new exp.Is(@this: ((exp.Expr)arg!).copy(), expression: new exp.Null()));

                var func_sql = BuildFuncCall(name, all_args);
                return self.sql(new exp.If(@this: combined, @true: new exp.Null(), @false: func_sql));
            }

            // Case 2: Source does not propagate NULLs, target does: wrap ALL arguments in COALESCE
            var wrapped_args = all_args.Select(arg => (object?)new exp.Coalesce(
                @this: null, expressions: new List<object?> { ((exp.Expr)arg!).copy(), new exp.Array(expressions: new List<object?>()) })).ToList();

            return BuildFuncCall(name, wrapped_args);
        };

        // py: sqlglot/dialects/dialect.py::array_append_sql
        private static Func<generator.Generator, exp.Expr, string> array_append_sql(string name, bool swap_params = false) => (self, expression) =>
        {
            var @this = expression.args["this"];
            var element = expression.args.get("expression");
            object?[] Args(object? array) => swap_params ? [element, array] : [array, element];
            var func_sql = self.func(name, Args(@this));

            var source_null_propagation = py.truthy(expression.args.get("null_propagation"));
            var target_null_propagation = self.dialect.ARRAY_FUNCS_PROPAGATES_NULLS;

            // No transpilation needed when source and target have matching NULL semantics
            if (source_null_propagation == target_null_propagation) return func_sql;

            // Source propagates NULLs, target does not: wrap in conditional to return NULL explicitly
            if (source_null_propagation)
                return self.sql(new exp.If(@this: new exp.Is(@this: @this, expression: new exp.Null()), @true: new exp.Null(), @false: func_sql));

            // Source does not propagate NULLs, target does: use COALESCE to convert NULL to empty array
            var coalesced = new exp.Coalesce(@this: null, expressions: new List<object?> { @this, new exp.Array(expressions: new List<object?>()) });
            return self.func(name, Args(coalesced));
        };

        // py: sqlglot/dialects/dialect.py::generate_series_sql
        private static Func<generator.Generator, exp.Expr, string> generate_series_sql(string func_name, string? exclusive_func_name = null) => (self, expression) =>
        {
            var start = expression.args.get("start");
            var end = expression.args.get("end");
            var step = expression.args.get("step") as exp.Expr;

            if (py.truthy(expression.args.get("is_end_exclusive")))
            {
                if (exclusive_func_name is not null) return self.func(exclusive_func_name, start, end, step);
                // The last value is one before the end in the direction of the step
                exp.Expr adjusted_end;
                if (step is not null && step.is_int && step.to_py() is long step_value && step_value < 0)
                    adjusted_end = new exp.Add(@this: end, expression: exp.Literal.number(1));
                else adjusted_end = new exp.Sub(@this: end, expression: exp.Literal.number(1));
                return self.func(func_name, start, adjusted_end, step);
            }

            return self.func(func_name, start, end, step);
        };

        // py: sqlglot/dialects/dialect.py::getbit_sql
        private static string getbit_sql(generator.Generator self, exp.Expr expression)
        {
            var value = expression.args["this"];
            var position = expression.args["expression"];

            var integer_types = exp.DataType.SIGNED_INTEGER_TYPES_.Concat(exp.DataType.UNSIGNED_INTEGER_TYPES_).Cast<object>().ToArray();
            if (!py.truthy(expression.args.get("zero_is_msb")) && expression.is_type(integer_types))
            {
                // Use bitwise operations: (value >> position) & 1
                var shifted = new exp.BitwiseRightShift(@this: value, expression: position);
                var masked = new exp.BitwiseAnd(@this: shifted, expression: exp.Literal.number(1));
                return self.sql(masked);
            }

            return self.func("GET_BIT", value, position);
        }

        private static string sha_length_sql(generator.Generator self, exp.Expr expression)
        {
            var length = expression.text("length");
            return self.func($"SHA{(length.Length != 0 ? length : "256")}", expression.args["this"]);
        }

        // py: sqlglot/dialects/dialect.py::strposition_sql (func_name="POSITION")
        private static string str_position_sql(generator.Generator self, exp.Expr expression)
        {
            object? @string = expression.args["this"];
            var substr = expression.args.get("substr");
            var position = expression.args.get("position") as exp.Expr;
            var zero = exp.Literal.number(0);
            var one = exp.Literal.number(1);

            var transpile_position = position is not null;
            if (transpile_position) @string = new exp.Substring(@this: @string, start: position);

            // use_ansi_position defaults to True for func_name == "POSITION"
            exp.Expr func = new exp.Anonymous(@this: "POSITION", expressions: new List<object?> { new exp.In(@this: substr, field: @string) });

            if (transpile_position)
            {
                var func_with_offset = new exp.Sub(@this: Binop(typeof(exp.Add), func, position!), expression: one);
                var func_wrapped = new exp.If(@this: Binop(typeof(exp.EQ), func, zero), @true: zero, @false: func_with_offset);
                return self.sql(func_wrapped);
            }

            return self.sql(func);
        }

        // py: sqlglot/generators/postgres.py::_versioned_anyvalue_sql
        private static string versioned_anyvalue_sql(generator.Generator self, exp.Expr expression)
        {
            // https://www.postgresql.org/docs/16/functions-aggregate.html
            // https://www.postgresql.org/about/featurematrix/
            var version = self.dialect.version;
            if (version.major < 16) return self.func("MAX", expression.args["this"]);

            return self.rename_func("ANY_VALUE", expression);
        }

        internal static readonly IReadOnlyDictionary<Type, Func<generator.Generator, exp.Expr, string>> DIALECT_HELPER_TRANSFORMS =
            new Dictionary<Type, Func<generator.Generator, exp.Expr, string>>
            {
                [typeof(exp.AnyValue)] = versioned_anyvalue_sql,
                [typeof(exp.ArrayConcat)] = array_concat_sql("ARRAY_CAT"),
                [typeof(exp.ArrayAppend)] = array_append_sql("ARRAY_APPEND"),
                [typeof(exp.ArrayPrepend)] = array_append_sql("ARRAY_PREPEND", swap_params: true),
                [typeof(exp.DateAdd)] = date_add_sql("+"),
                [typeof(exp.DateSub)] = date_add_sql("-"),
                [typeof(exp.DatetimeAdd)] = date_add_sql("+"),
                [typeof(exp.DatetimeSub)] = date_add_sql("-"),
                [typeof(exp.TimestampAdd)] = date_add_sql("+"),
                [typeof(exp.TimestampSub)] = date_add_sql("-"),
                [typeof(exp.TsOrDsAdd)] = date_add_sql("+"),
                [typeof(exp.Day)] = day_month_year_sql,
                [typeof(exp.Month)] = day_month_year_sql,
                [typeof(exp.Year)] = day_month_year_sql,
                [typeof(exp.GenerateSeries)] = generate_series_sql("GENERATE_SERIES"),
                [typeof(exp.Getbit)] = getbit_sql,
                [typeof(exp.Max)] = (self, e) => self.rename_func(py.truthy(e.args.get("expressions")) ? "GREATEST" : "MAX", e),
                [typeof(exp.Min)] = (self, e) => self.rename_func(py.truthy(e.args.get("expressions")) ? "LEAST" : "MIN", e),
                [typeof(exp.MapFromEntries)] = (self, _) => { self.unsupported("MAP_FROM_ENTRIES unsupported"); return ""; },
                [typeof(exp.ParseJSON)] = (self, e) => self.sql(self.exp_cast((exp.Expr)e.args["this"]!, exp.DType.JSON)),
                [typeof(exp.SHA2)] = (self, e) => self.func("ENCODE", sha_length_sql(self, e), exp.Literal.@string("hex")),
                [typeof(exp.SHA2Digest)] = sha_length_sql,
                [typeof(exp.StrPosition)] = str_position_sql,
                [typeof(exp.StructExtract)] = (self, e) =>
                    $"{self.sql(e, "this")}.{self.sql(exp.to_identifier(((exp.Expr)e.args["expression"]!).name))}",
                [typeof(exp.TimeStrToTime)] = (self, e) => self.sql(self.exp_cast((exp.Expr)e.args["this"]!,
                    py.truthy(e.args.get("zone")) ? exp.DType.TIMESTAMPTZ : exp.DType.TIMESTAMP)),
                [typeof(exp.Xor)] = (self, e) =>
                {
                    var a = self.sql(e.args["this"]);
                    var b = self.sql(e.args["expression"]);
                    return $"({a} AND (NOT {b})) OR ((NOT {a}) AND {b})";
                },
                [typeof(exp.CountIf)] = (self, e) =>
                {
                    var cond = e.args["this"];
                    if (cond is exp.Distinct distinct)
                    {
                        cond = ((System.Collections.IEnumerable)distinct.args["expressions"]!).Cast<object?>().First();
                        self.unsupported("DISTINCT is not supported when converting COUNT_IF to SUM");
                    }
                    return self.func("sum", new exp.If(@this: cond, @true: exp.Literal.number(1), @false: exp.Literal.number(0)));
                },
            };
    }
}
