using Gen = SqlGlotSharp.generator.Generator;

namespace SqlGlotSharp.generators;

// Logic of sqlglot/generators/fabric.py; the class-level data tables live in fabric.data.g.cs.
public static partial class fabric
{
    public partial class FabricGenerator
    {
        public FabricGenerator(object? dialect = null, bool pretty = false)
            : base(dialect: dialect, pretty: pretty) { }

        /// <summary>
        /// Cap the precision of to a maximum of <paramref name="max_precision"/> digits.
        /// If no precision is specified, default to <paramref name="max_precision"/>.
        /// </summary>
        // py: sqlglot/generators/fabric.py::_cap_data_type_precision
        private static exp.DataType _cap_data_type_precision(exp.DataType expression, long max_precision = 6)
        {
            var precision_param = expression.find(typeof(exp.DataTypeParam));

            long target_precision;
            if (precision_param is not null && ((exp.Expr)precision_param.args["this"]!).is_int)
            {
                var current_precision = Convert.ToInt64(((exp.Expr)precision_param.args["this"]!).to_py());
                target_precision = Math.Min(current_precision, max_precision);
            }
            else target_precision = max_precision;

            return new exp.DataType(@this: expression.args["this"],
                expressions: new List<object?> { new exp.DataTypeParam(@this: exp.Literal.number(target_precision)) });
        }

        /// <summary>Transform function to add VARCHAR(MAX) or CHAR(MAX) for cross-dialect conversion.</summary>
        // py: sqlglot/generators/fabric.py::_add_default_precision_to_varchar
        internal static exp.Expr _add_default_precision_to_varchar(exp.Expr expression)
        {
            if (expression is exp.Create && py.str(expression.args.get("kind")).ToUpperInvariant() == "TABLE"
                && expression.args.get("this") is exp.Schema schema)
            {
                foreach (var column in ((System.Collections.IEnumerable)(schema.args.get("expressions") ?? new List<object?>())).Cast<object?>())
                {
                    if (column is exp.ColumnDef column_def && column_def.args.get("kind") is exp.DataType column_type
                        && column_type.args.get("this") is exp.DType t && t is exp.DType.VARCHAR or exp.DType.CHAR
                        && !(column_type.args.get("expressions") is List<object?> { Count: > 0 }))
                        // For transpilation, VARCHAR/CHAR without precision becomes VARCHAR(MAX)/CHAR(MAX)
                        column_type.set("expressions", new List<object?> { new exp.Var(@this: "MAX") });
                }
            }

            return expression;
        }

        // py: sqlglot/generators/fabric.py::FabricGenerator.TRANSFORMS (Create entry: transforms.preprocess)
        internal static string create_preprocess_sql(Gen self, exp.Expr expression)
        {
            expression = _add_default_precision_to_varchar(expression);
            return self.create_sql((exp.Create)expression);
        }

        // py: sqlglot/generators/fabric.py::FabricGenerator.datatype_sql
        public override string datatype_sql(exp.DataType expression)
        {
            // Check if this is a temporal type that needs precision handling. Fabric limits temporal
            // types to max 6 digits precision. When no precision is specified, we default to 6 digits.
            if (expression.is_type(exp.DataType.TEMPORAL_TYPES_.Cast<object>().ToArray())
                && !Equals(expression.args.get("this"), exp.DType.DATE))
                // Create a new expression with the capped precision
                expression = _cap_data_type_precision(expression);

            return base.datatype_sql(expression);
        }

        // py: sqlglot/generators/fabric.py::FabricGenerator.cast_sql
        public override string cast_sql(exp.Cast expression, string? safe_prefix = null)
        {
            // Cast to DATETIMEOFFSET if inside an AT TIME ZONE expression
            // https://learn.microsoft.com/en-us/sql/t-sql/data-types/datetimeoffset-transact-sql#microsoft-fabric-support
            if (expression.is_type(exp.DType.TIMESTAMPTZ))
            {
                var at_time_zone = expression.find_ancestor(typeof(exp.AtTimeZone), typeof(exp.Select));

                // Return normal cast, if the expression is not in an AT TIME ZONE context
                if (at_time_zone is not exp.AtTimeZone) return base.cast_sql(expression, safe_prefix);

                // Get the precision from the original TIMESTAMPTZ cast and cap it to 6
                var capped_data_type = _cap_data_type_precision(expression.to, max_precision: 6);
                var precision = capped_data_type.find(typeof(exp.DataTypeParam));
                var precision_value = precision is not null && ((exp.Expr)precision.args["this"]!).is_int
                    ? Convert.ToInt64(((exp.Expr)precision.args["this"]!).to_py()) : 6;

                // Do the cast explicitly to bypass sqlglot's default handling
                var inner = new dialects.dialect.Dialect().generator().generate((exp.Expr)expression.args["this"]!);
                return sql($"CAST({inner} AS DATETIMEOFFSET({precision_value}))");
            }

            return base.cast_sql(expression, safe_prefix);
        }

        // py: sqlglot/generators/fabric.py::FabricGenerator.attimezone_sql
        public override string attimezone_sql(exp.AtTimeZone expression)
        {
            // Wrap the AT TIME ZONE expression in a cast to DATETIME2 if it contains a TIMESTAMPTZ
            // https://learn.microsoft.com/en-us/sql/t-sql/data-types/datetimeoffset-transact-sql#microsoft-fabric-support
            var timestamptz_cast = postgres.PostgresGenerator.find_all_in_scope(expression, typeof(exp.Cast)).FirstOrDefault() as exp.Cast;
            if (timestamptz_cast is not null && timestamptz_cast.to.is_type(exp.DType.TIMESTAMPTZ))
            {
                // Get the precision from the original TIMESTAMPTZ cast and cap it to 6
                var data_type = timestamptz_cast.to;
                var capped_data_type = _cap_data_type_precision(data_type, max_precision: 6);
                var precision_param = capped_data_type.find(typeof(exp.DataTypeParam));
                var precision = precision_param is not null ? Convert.ToInt64(((exp.Expr)precision_param.args["this"]!).to_py()) : 6;

                // Generate the AT TIME ZONE expression (which will handle the inner cast conversion)
                var at_time_zone_sql = base.attimezone_sql(expression);

                // Don't wrap again if it's already cast to DATETIME2, e.g. when re-parsing our own output
                var parent = expression.parent;
                if (parent is exp.Cast parent_cast && parent_cast.to.is_type(exp.DType.DATETIME2))
                    return at_time_zone_sql;

                // Wrap it in an outer cast to DATETIME2
                return $"CAST({at_time_zone_sql} AS DATETIME2({precision}))";
            }

            return base.attimezone_sql(expression);
        }

        // py: sqlglot/generators/fabric.py::FabricGenerator.unixtotime_sql
        public string unixtotime_sql(exp.UnixToTime expression)
        {
            var scale = expression.args.get("scale");
            var timestamp = (exp.Expr)expression.args["this"]!;

            if (scale is not null && !Equals(scale, exp.UnixToTime.SECONDS_))
            {
                unsupported($"UnixToTime scale {scale} is not supported by Fabric");
                return "";
            }

            // Convert unix timestamp (seconds) to microseconds and round to avoid decimals
            var microseconds = postgres.PostgresGenerator.Binop(typeof(exp.Mul), timestamp, exp.Literal.number("1e6"));
            var rounded = new exp.Round(@this: microseconds, decimals: exp.Literal.number(0));
            var rounded_ms_as_bigint = dialect_helpers.cast(rounded, exp.DataType.build(exp.DType.BIGINT));

            // Create the base datetime as '1970-01-01' cast to DATETIME2(6)
            var epoch_start = dialect_helpers.cast(exp.Literal.@string("1970-01-01"),
                exp.DataType.build("datetime2(6)", dialect: "fabric"));

            var dateadd = new exp.DateAdd(@this: epoch_start, expression: rounded_ms_as_bigint,
                unit: exp.Literal.@string("MICROSECONDS"));
            return sql(dateadd);
        }
    }
}
