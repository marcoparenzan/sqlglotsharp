namespace SqlGlotSharp.Tests;

public class ValuesPipelineTests
{
    [Fact]
    public async Task Values_and_distinct_on_match_Python_AST_and_production_SQL()
    {
        await TypeCastPipelineTests.CompareSamples([
            "VALUES (1)", "VALUES (1, 'a'), (2, 'b')",
            "SELECT * FROM (VALUES (1, 'a'), (2, 'b')) AS t(id, label)",
            "SELECT t.id FROM (VALUES (1), (2)) AS t(id) WHERE id > 1",
            "SELECT DISTINCT ON (device_id) device_id, ts FROM measurements ORDER BY device_id, ts DESC",
            "SELECT DISTINCT ON (device_id, value) device_id FROM measurements",
            "WITH q(a) AS (VALUES (1), (2)) SELECT a FROM q",
            "WITH q AS (VALUES (1) AS t(a)) SELECT * FROM q",
            "VALUES (1) UNION ALL VALUES (2)",
            "SELECT 1 UNION VALUES (2)", "SELECT 1 UNION (VALUES (2))",
            "SELECT * FROM (VALUES (1), (2)) t(a) JOIN u USING (a)",
            "SELECT DISTINCT ON (a) a FROM (VALUES (1), (2)) AS t(a)",
            "VALUES (1,", "VALUES (1),"
        ]);
    }
}
