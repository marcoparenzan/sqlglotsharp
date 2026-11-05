namespace SqlGlotSharp.Tests;

public class TemporalPipelineTests
{
    [Fact]
    public async Task Interval_queries_use_parser_and_generator_together()
    {
        await TypeCastPipelineTests.CompareSamples([
            "SELECT INTERVAL '1 hour'",
            "SELECT INTERVAL '1' DAY",
            "SELECT INTERVAL 5 MINUTE",
            "SELECT INTERVAL '1 year 2 months'",
            "SELECT INTERVAL '1.5 seconds'",
            "SELECT INTERVAL '1' QUARTER",
            "SELECT INTERVAL '1' DAY + INTERVAL '2' HOUR",
            "SELECT CURRENT_TIMESTAMP - INTERVAL '1 hour'",
            "SELECT ts FROM measurements WHERE ts >= NOW() - INTERVAL '5 minutes'",
            "SELECT DATE_TRUNC('hour', ts) FROM measurements WHERE ts >= CURRENT_TIMESTAMP - INTERVAL '1 day'",
            "SELECT INTERVAL /* value */ '3' /* unit */ DAY",
            "WITH recent AS (SELECT ts FROM measurements WHERE ts > CURRENT_TIMESTAMP - INTERVAL '1 day') SELECT * FROM recent"
        ]);
    }

    [Fact]
    public async Task Extract_and_current_time_queries_match_Python_AST_and_production_SQL()
    {
        await TypeCastPipelineTests.CompareSamples([
            "SELECT EXTRACT(YEAR FROM ts) FROM measurements",
            "SELECT EXTRACT(MONTH FROM ts), EXTRACT(DAY FROM ts) FROM measurements",
            "SELECT EXTRACT(EPOCH FROM ts) FROM measurements",
            "SELECT EXTRACT('dow' FROM ts) FROM measurements",
            "SELECT EXTRACT(HOUR, ts) FROM measurements",
            "SELECT EXTRACT(hour FROM ts AT TIME ZONE 'UTC') FROM measurements",
            "SELECT EXTRACT(YEAR FROM CAST(ts AS TIMESTAMP)) FROM measurements",
            "SELECT /* select */ EXTRACT(/* unit */ year /* from */ FROM ts /* source */) AS y FROM measurements",
            "SELECT CURRENT_DATE", "SELECT CURRENT_DATE()", "SELECT CURRENT_DATE('UTC')",
            "SELECT CURRENT_TIMESTAMP", "SELECT CURRENT_TIMESTAMP()", "SELECT CURRENT_TIMESTAMP(3)",
            "SELECT CURRENT_TIME", "SELECT LOCALTIME", "SELECT LOCALTIMESTAMP",
            "SELECT CURRENT_USER", "SELECT CURRENT_SCHEMA", "SELECT NOW()",
            "SELECT CURRENT_TIMESTAMP AT TIME ZONE 'UTC'",
            "SELECT ts AT TIME ZONE 'Europe/Rome' AT TIME ZONE 'UTC' FROM measurements",
            "SELECT DATE_TRUNC('hour', ts) FROM measurements",
            "SELECT DATE_TRUNC('week', ts) FROM measurements",
            "SELECT DATE_TRUNC('day', ts, 'Europe/Rome') FROM measurements",
            "SELECT DATE_TRUNC('ms', CAST(ts AS TIMESTAMP)) FROM measurements",
            "WITH q AS (SELECT ts FROM measurements) SELECT EXTRACT(hour FROM ts) FROM q WHERE EXTRACT(year FROM ts) > 2020",
            "SELECT EXTRACT(year)", "SELECT EXTRACT(year FROM)", "SELECT EXTRACT()"
        ]);
    }
}
