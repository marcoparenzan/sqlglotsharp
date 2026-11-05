namespace SqlGlotSharp.Tests;

public class ConditionalPipelineTests
{
    [Fact]
    public async Task Conditionals_match_Python_through_the_complete_available_pipeline()
    {
        await TypeCastPipelineTests.CompareSamples([
            "SELECT CASE WHEN value > 0 THEN 1 ELSE 0 END FROM measurements",
            "SELECT CASE device_id WHEN 1 THEN 'first' WHEN 2 THEN 'second' ELSE 'other' END FROM measurements",
            "SELECT CASE WHEN value IS NULL THEN 'missing' WHEN value > 0 THEN 'positive' ELSE 'negative' END FROM measurements",
            "SELECT CASE WHEN value > 0 THEN CASE WHEN value > 10 THEN 2 ELSE 1 END ELSE 0 END FROM measurements",
            "SELECT CASE WHEN value > 0 THEN 1 END FROM measurements",
            "SELECT CAST(CASE WHEN value > 0 THEN value ELSE 0 END AS DECIMAL(10, 2)) FROM measurements",
            "SELECT SUM(CASE WHEN value > 0 THEN value ELSE 0 END) OVER (PARTITION BY device_id ORDER BY ts) FROM measurements",
            "SELECT CASE WHEN value > 0 THEN ROW_NUMBER() OVER (ORDER BY ts) ELSE 0 END FROM measurements",
            "SELECT CASE WHEN value > 0 THEN DATE_TRUNC('hour', ts) ELSE CURRENT_TIMESTAMP END FROM measurements",
            "SELECT CASE WHEN value > 0 THEN INTERVAL '1 hour' ELSE INTERVAL '1 day' END FROM measurements",
            "SELECT /* case */ CASE /* when */ WHEN value > 0 THEN /* true */ 1 ELSE /* false */ 0 END /* end */ FROM measurements",
            "WITH q AS (SELECT value FROM measurements) SELECT CASE WHEN value > 0 THEN 1 ELSE 0 END FROM q",
            "SELECT IF(value > 0, 1, 0) FROM measurements",
            "SELECT CASE WHEN value > 0 THEN 1", "SELECT CASE WHEN THEN 1 ELSE 0 END"
        ]);
    }
}
