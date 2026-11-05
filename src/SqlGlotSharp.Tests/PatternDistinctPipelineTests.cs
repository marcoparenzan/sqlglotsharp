namespace SqlGlotSharp.Tests;

public class PatternDistinctPipelineTests
{
    [Fact]
    public async Task Patterns_and_distinct_match_Python_AST_and_production_SQL()
    {
        await TypeCastPipelineTests.CompareSamples([
            "SELECT DISTINCT device_id FROM measurements",
            "SELECT a IS INT FROM measurements",
            "SELECT int, interval FROM measurements",
            "SELECT DISTINCT device_id, value FROM measurements WHERE name ILIKE 'sensor%'",
            "SELECT COUNT(DISTINCT device_id) FROM measurements",
            "SELECT COUNT(DISTINCT device_id, value) FROM measurements",
            "SELECT * FROM measurements WHERE name LIKE 'sensor%'",
            "SELECT * FROM measurements WHERE name NOT ILIKE 'test%'",
            "SELECT * FROM measurements WHERE name SIMILAR TO '(sensor|device)%'",
            "SELECT * FROM measurements WHERE name LIKE 'sensor!_%' ESCAPE '!'",
            "SELECT CASE WHEN name ILIKE 'sensor%' THEN 1 ELSE 0 END FROM measurements",
            "SELECT COUNT(*) FILTER (WHERE name LIKE 'sensor%') OVER (PARTITION BY device_id) FROM measurements",
            "WITH q AS (SELECT DISTINCT device_id FROM measurements WHERE name ILIKE 'sensor%') SELECT * FROM q",
            "SELECT name LIKE 'a%' FROM measurements GROUP BY name LIKE 'a%' HAVING name LIKE 'a%'",
            "SELECT * FROM measurements WHERE name LIKE", "SELECT DISTINCT"
        ]);
    }
}
