namespace SqlGlotSharp.generators;

public static class timescale
{
    /// <summary>Identity generation, including all ordered/named canonical extension arguments.</summary>
    public class TimescaleGenerator : postgres.PostgresGenerator
    {
        public TimescaleGenerator(object? dialect = null, bool pretty = false)
            : base(dialect: dialect, pretty: pretty) { }

        public override string timebucket_sql(exp.TimeBucket expression) => function_fallback_sql(expression);
    }
}
