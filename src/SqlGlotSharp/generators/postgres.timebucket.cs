using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace SqlGlotSharp.generators;

public static partial class postgres
{
    public partial class PostgresGenerator
    {
        public const string TimeBucketUnsafe = "TimeBucket: PostgreSQL DATE_BIN requires a positive constant fixed-width interval, explicit TIMESTAMP/TIMESTAMPTZ CAST, and absent or matching unambiguous typed origin; timezone, offset and named overloads are not ported";

        /// <summary>
        /// Narrow proof, not an approximate rewrite. Timescale's non-month default is Monday
        /// 2000-01-03 (UTC for timestamptz). Both algorithms floor fixed microsecond distances
        /// from origin, including negative distances. DATE_BIN forbids month components.
        /// Evidence: timescaledb/src/time_bucket.c; PostgreSQL functions-datetime.html#FUNCTIONS-DATETIME-BIN.
        /// </summary>
        public override string timebucket_sql(exp.TimeBucket expression)
        {
            if (!OnlyArgs(expression, "width", "this", "expressions") ||
                !FixedWidth(expression.args.get("width"), out var micros) ||
                TimestampType(expression.@this) is not exp.DType type)
                throw new NotPortedException(TimeBucketUnsafe);

            var extras = expression.expressions;
            if (extras.Count > 1) throw new NotPortedException(TimeBucketUnsafe);
            object origin;
            if (extras.Count == 1)
            {
                origin = extras[0]!;
                if (TimestampType(origin) != type || origin is not exp.Cast cast ||
                    cast.@this is not exp.Literal literal || !literal.is_string || !UnambiguousOrigin(literal.name, type))
                    throw new NotPortedException(TimeBucketUnsafe);
            }
            else
            {
                origin = new exp.Cast(@this: exp.Literal.@string(type == exp.DType.TIMESTAMP
                    ? "2000-01-03 00:00:00" : "2000-01-03 00:00:00+00:00"), to: new exp.DataType(@this: type));
            }
            // Normalize to exact microseconds: no parser rounding, calendar components or session timezone dependence.
            var stride = new exp.Interval(@this: exp.Literal.@string(micros.ToString(CultureInfo.InvariantCulture)),
                unit: new exp.Var(@this: "MICROSECONDS"));
            return func("DATE_BIN", stride, expression.@this, origin);
        }

        private static bool OnlyArgs(exp.Expr node, params string[] allowed) =>
            node.args.All(pair => allowed.Contains(pair.Key, StringComparer.Ordinal) || !py.truthy(pair.Value));

        private static exp.DType? TimestampType(object? value)
        {
            // TryCast, precision modifiers, FORMAT, DEFAULT and safe casts do not prove this contract.
            if (value?.GetType() != typeof(exp.Cast) || value is not exp.Cast cast ||
                !OnlyArgs(cast, "this", "to") || cast.@this is null ||
                cast.args.get("to") is not exp.DataType dataType || !OnlyArgs(dataType, "this")) return null;
            return dataType.@this is exp.DType.TIMESTAMP or exp.DType.TIMESTAMPTZ ? (exp.DType)dataType.@this : null;
        }

        private static bool FixedWidth(object? value, out long micros)
        {
            micros = 0;
            if (value is not exp.Interval interval || !OnlyArgs(interval, "this", "unit") ||
                interval.@this is not exp.Literal literal || interval.args.get("unit") is not exp.Var unit ||
                !Regex.IsMatch(literal.name, @"\A[0-9]+\z", RegexOptions.CultureInvariant)) return false;
            var factor = unit.name.ToLowerInvariant() switch
            {
                "microsecond" or "microseconds" => 1L,
                "millisecond" or "milliseconds" => 1000L,
                "second" or "seconds" => 1000000L,
                "minute" or "minutes" => 60000000L,
                "hour" or "hours" => 3600000000L,
                "day" or "days" => 86400000000L,
                "week" or "weeks" => 604800000000L,
                _ => 0L
            };
            if (factor == 0 || literal.name.Length > 19 ||
                !BigInteger.TryParse(literal.name, NumberStyles.None, CultureInfo.InvariantCulture, out var count)) return false;
            var total = count * factor;
            if (total <= 0 || total > long.MaxValue) return false;
            micros = (long)total;
            return true;
        }

        private static bool UnambiguousOrigin(string text, exp.DType type)
        {
            // Deliberately ISO-only, AD finite dates, <= PostgreSQL's microsecond precision.
            // UTC-only zoned literals avoid DateStyle, session timezone and timezone-name/DST resolution.
            var suffix = type == exp.DType.TIMESTAMPTZ ? @"(?:Z|\+00:00)" : "";
            if (!Regex.IsMatch(text, @"\A[0-9]{4}-[0-9]{2}-[0-9]{2}[ T][0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,6})?" + suffix + @"\z",
                RegexOptions.CultureInvariant)) return false;
            var naive = type == exp.DType.TIMESTAMPTZ ? text.TrimEnd('Z').Replace("+00:00", "", StringComparison.Ordinal) : text;
            return DateTime.TryParseExact(naive, ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm:ss.FFFFFF", "yyyy-MM-dd'T'HH:mm:ss.FFFFFF"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        }
    }
}
