namespace SqlGlotSharp.Tests;

public class WindowPipelineTests
{
    [Fact]
    public async Task Window_queries_match_Python_AST_and_production_SQL()
    {
        await TypeCastPipelineTests.CompareSamples([
            "SELECT ROW_NUMBER() OVER () FROM measurements",
            "SELECT ROW_NUMBER() OVER (ORDER BY ts) AS rn FROM measurements",
            "SELECT SUM(value) OVER (PARTITION BY device_id) FROM measurements",
            "SELECT SUM(value) OVER (PARTITION BY device_id ORDER BY ts) FROM measurements",
            "SELECT COUNT(*) OVER (ORDER BY ts ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) FROM measurements",
            "SELECT SUM(value) OVER (ORDER BY ts ROWS 2 PRECEDING) FROM measurements",
            "SELECT SUM(value) OVER (ORDER BY ts RANGE BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) FROM measurements",
            "SELECT SUM(value) OVER (ORDER BY ts GROUPS BETWEEN 1 PRECEDING AND 1 FOLLOWING) FROM measurements",
            "SELECT SUM(value) OVER (ORDER BY ts ROWS BETWEEN 1 PRECEDING AND CURRENT ROW EXCLUDE CURRENT ROW) FROM measurements",
            "SELECT LAG(value) OVER (PARTITION BY device_id ORDER BY ts DESC NULLS LAST) FROM measurements",
            "SELECT ROW_NUMBER() OVER w FROM measurements WINDOW w AS (PARTITION BY device_id ORDER BY ts)",
            "SELECT SUM(value) OVER w FROM measurements WINDOW w AS ()",
            "SELECT SUM(value) OVER (w) FROM measurements WINDOW w AS (ORDER BY ts)",
            "SELECT SUM(value) OVER w, COUNT(*) OVER x FROM measurements WINDOW w AS (PARTITION BY device_id), x AS (w ORDER BY ts ROWS 2 PRECEDING)",
            "SELECT COUNT(*) FILTER (WHERE value > 0) FROM measurements",
            "SELECT COUNT(*) FILTER (WHERE value > 0) OVER (PARTITION BY device_id) FROM measurements",
            "SELECT SUM(value) FILTER (WHERE value IS NOT NULL) OVER (ORDER BY ts) FROM measurements",
            "SELECT SUM(value) WITHIN GROUP (ORDER BY ts) FROM measurements",
            "SELECT /* select */ SUM(value) /* aggregate */ OVER (PARTITION BY device_id ORDER BY ts) /* window */ FROM measurements",
            "WITH q AS (SELECT device_id, ts, value FROM measurements) SELECT ROW_NUMBER() OVER (PARTITION BY device_id ORDER BY ts) FROM q",
            "SELECT ROW_NUMBER() OVER (ORDER BY ts) FROM measurements WHERE ts > CURRENT_TIMESTAMP - INTERVAL '1 day'",
            "SELECT SUM(value) OVER (ORDER BY ts ROWS BETWEEN 1 PRECEDING AND)",
            "SELECT SUM(value) OVER (ORDER BY ts", "SELECT SUM(value) OVER w WINDOW w AS () WINDOW x AS ()"
        ]);
    }
}
