namespace SqlGlotSharp.generators;

// Transforms of sqlglot/generators/postgres.py::PostgresGenerator.TRANSFORMS that need no scope or type annotation.
public static partial class postgres
{
    public partial class PostgresGenerator
    {
        private static readonly PyDict<string, string> DATE_DIFF_FACTOR = new()
        {
            ["MICROSECOND"] = " * 1000000", ["MILLISECOND"] = " * 1000", ["SECOND"] = "",
            ["MINUTE"] = " / 60", ["HOUR"] = " / 3600", ["DAY"] = " / 86400"
        };

        private static readonly HashSet<string> DATE_DIFF_BOUNDARY_UNITS =
            ["MICROSECOND", "MILLISECOND", "SECOND", "MINUTE", "HOUR", "MONTH", "QUARTER", "YEAR"];

        private static exp.Expr CastTo(object? expression, exp.DType to)
        {
            var result = new exp.Cast(@this: expression, to: exp.DataType.build(to));
            result.type = exp.DataType.build(to);
            return result;
        }

        // py: sqlglot/generators/postgres.py::_date_diff_sql
        internal static string date_diff_sql(generator.Generator self, exp.Expr expression)
        {
            var unit = py.upper(expression.text("unit"));
            if (unit.Length == 0) unit = "DAY";

            // Dialects like MySQL count crossed day boundaries, which maps to DATE subtraction
            if (unit == "DAY" && py.truthy(expression.args.get("date_part_boundary")))
            {
                var @this = CastTo(expression.@this, exp.DType.DATE);
                var expr = CastTo(expression.args.get("expression"), exp.DType.DATE);
                return self.sql(new exp.Paren(@this: new exp.Sub(@this: @this, expression: expr)));
            }

            var factor = DATE_DIFF_FACTOR.get(unit);

            var end = $"CAST({self.sql(expression, "this")} AS TIMESTAMP)";
            var start = $"CAST({self.sql(expression, "expression")} AS TIMESTAMP)";

            // The arithmetic below counts complete units between the operands, whereas dialects that set
            // date_part_boundary count unit boundary crossings, so the operands are truncated down to the
            // unit to make the two coincide. WEEK is left out: its start day differs across dialects
            if (py.truthy(expression.args.get("date_part_boundary")) && DATE_DIFF_BOUNDARY_UNITS.Contains(unit))
            {
                end = self.func("DATE_TRUNC", exp.Literal.@string(unit), end);
                start = self.func("DATE_TRUNC", exp.Literal.@string(unit), start);
            }

            if (factor is not null) return $"CAST(EXTRACT(epoch FROM {end} - {start}){factor} AS BIGINT)";

            var age = $"AGE({end}, {start})";

            if (unit == "WEEK") unit = $"EXTRACT(days FROM ({end} - {start})) / 7";
            else if (unit == "MONTH") unit = $"EXTRACT(year FROM {age}) * 12 + EXTRACT(month FROM {age})";
            else if (unit == "QUARTER") unit = $"EXTRACT(year FROM {age}) * 4 + EXTRACT(month FROM {age}) / 3";
            else if (unit == "YEAR") unit = $"EXTRACT(year FROM {age})";
            else unit = age;

            return $"CAST({unit} AS BIGINT)";
        }

        // py: sqlglot/generators/postgres.py::_levenshtein_sql
        internal static string levenshtein_sql(generator.Generator self, exp.Expr expression) =>
            self.rename_func(py.truthy(expression.args.get("max_dist")) ? "LEVENSHTEIN_LESS_EQUAL" : "LEVENSHTEIN", expression);

        // py: sqlglot/generators/postgres.py::_unix_to_time_sql
        internal static string unix_to_time_sql(generator.Generator self, exp.Expr expression)
        {
            var scale = expression.args.get("scale");
            var timestamp = expression.@this;

            if (scale is null || (scale is exp.Expr s && s.Equals(exp.UnixToTime.SECONDS_)))
                return self.func("TO_TIMESTAMP", timestamp, self.format_time(expression));

            return self.func("TO_TIMESTAMP",
                new exp.Div(@this: timestamp, expression: new exp.Pow(@this: exp.Literal.number(10), expression: scale)),
                self.format_time(expression));
        }

        // py: sqlglot/generators/postgres.py::_round_sql
        internal static string round_sql(generator.Generator self, exp.Expr expression)
        {
            var @this = self.sql(expression, "this");
            var decimals = self.sql(expression, "decimals");

            if (decimals.Length == 0) return self.func("ROUND", @this);

            if (expression.type is null)
                expression = optimizer.annotate_types_module.annotate_types(expression, dialect: self.dialect);

            // ROUND(double precision, integer) is not permitted in Postgres
            // so it is necessary to cast to decimal before rounding.
            if (expression.@this is exp.Expr inner && inner.is_type(exp.DType.DOUBLE))
            {
                var decimal_type = exp.DataType.build(exp.DType.DECIMAL);
                if (expression.args.get("expressions") is { } dtype_args) decimal_type.set("expressions", dtype_args);
                @this = self.sql(new exp.Cast(@this: @this, to: decimal_type));
            }

            return self.func("ROUND", @this, decimals);
        }

        // py: sqlglot/dialects/dialect.py::regexp_replace_global_modifier
        internal static exp.Expr? regexp_replace_global_modifier(exp.Expr expression)
        {
            var modifiers = expression.args.get("modifiers") as exp.Expr;
            var single_replace = expression.args.get("single_replace");
            var occurrence = expression.args.get("occurrence") as exp.Expr;

            if (!py.truthy(single_replace) && (occurrence is null || (occurrence.is_int && long.TryParse(occurrence.name, out var occurrence_value) && occurrence_value == 0)))
            {
                if (modifiers is null || modifiers.is_string)
                {
                    // Append 'g' to the modifiers if they are not provided since
                    // the semantics of REGEXP_REPLACE from the input dialect
                    // is to replace all occurrences of the pattern.
                    var value = modifiers is null ? "" : modifiers.name;
                    modifiers = exp.Literal.@string(value + "g");
                }
            }

            return modifiers;
        }

        internal static readonly IReadOnlyDictionary<Type, Func<generator.Generator, exp.Expr, string>> MORE_TRANSFORMS =
            new Dictionary<Type, Func<generator.Generator, exp.Expr, string>>
            {
                [typeof(exp.BitwiseAndAgg)] = (self, e) => self.rename_func("BIT_AND", e),
                [typeof(exp.BitwiseOrAgg)] = (self, e) => self.rename_func("BIT_OR", e),
                [typeof(exp.BitwiseXorAgg)] = (self, e) => self.rename_func("BIT_XOR", e),
                [typeof(exp.CurrentVersion)] = (self, e) => self.rename_func("VERSION", e),
                [typeof(exp.DateDiff)] = date_diff_sql,
                [typeof(exp.TsOrDsDiff)] = date_diff_sql,
                [typeof(exp.Explode)] = (self, e) => self.rename_func("UNNEST", e),
                [typeof(exp.ExplodingGenerateSeries)] = (self, e) => self.rename_func("GENERATE_SERIES", e),
                [typeof(exp.IntDiv)] = (self, e) => self.rename_func("DIV", e),
                [typeof(exp.JSONArrayAgg)] = (self, e) => self.func("JSON_AGG", [self.sql(e, "this")], "(", $"{self.sql(e, "order")})"),
                [typeof(exp.LogicalOr)] = (self, e) => self.rename_func("BOOL_OR", e),
                [typeof(exp.LogicalAnd)] = (self, e) => self.rename_func("BOOL_AND", e),
                [typeof(exp.Rand)] = (self, e) => self.rename_func("RANDOM", e),
                [typeof(exp.RegexpReplace)] = (self, e) => self.func("REGEXP_REPLACE", e.@this, e.args.get("expression"),
                    e.args.get("replacement"), e.args.get("position"), e.args.get("occurrence"), regexp_replace_global_modifier(e)),
                [typeof(exp.Round)] = round_sql,
                [typeof(exp.StrToDate)] = (self, e) => self.func("TO_DATE", e.@this, self.format_time(e)),
                [typeof(exp.StrToTime)] = (self, e) => self.func("TO_TIMESTAMP", e.@this, self.format_time(e)),
                [typeof(exp.TimeFromParts)] = (self, e) => self.rename_func("MAKE_TIME", e),
                [typeof(exp.TimestampFromParts)] = (self, e) => self.rename_func("MAKE_TIMESTAMP", e),
                [typeof(exp.TimeToStr)] = (self, e) => self.func("TO_CHAR", e.@this, self.format_time(e)),
                [typeof(exp.UnixToTime)] = unix_to_time_sql,
                [typeof(exp.Uuid)] = (_, _) => "GEN_RANDOM_UUID()",
                [typeof(exp.TimeToUnix)] = (self, e) => self.func("DATE_PART", exp.Literal.@string("epoch"), e.@this),
                [typeof(exp.VariancePop)] = (self, e) => self.rename_func("VAR_POP", e),
                [typeof(exp.Variance)] = (self, e) => self.rename_func("VAR_SAMP", e),
                [typeof(exp.Unicode)] = (self, e) => self.rename_func("ASCII", e),
                [typeof(exp.Levenshtein)] = levenshtein_sql,
                [typeof(exp.JSONBObjectAgg)] = (self, e) => self.rename_func("JSONB_OBJECT_AGG", e),
                [typeof(exp.DateStrToDate)] = (self, e) => self.sql(CastTo(e.@this, exp.DType.DATE)),
            };
    }
}
