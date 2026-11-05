namespace SqlGlotSharp.Tests;

public class UpdateDeletePipelineTests
{
    [Fact]
    public async Task Mutation_statements_match_Python_AST_and_production_SQL()
    {
        await TypeCastPipelineTests.CompareSamples([
            "UPDATE measurements SET value = 10",
            "UPDATE measurements SET value = value + 1, device_id = 2 WHERE device_id = 1",
            "UPDATE measurements SET value = 10 WHERE value < 0 RETURNING device_id, value",
            "UPDATE measurements AS m SET value = s.value FROM source AS s WHERE m.device_id = s.device_id RETURNING m.value",
            "UPDATE measurements SET value = CASE WHEN value < 0 THEN 0 ELSE value END",
            "UPDATE measurements SET value = (payload ->> 'value')::DECIMAL(10, 2) WHERE device_id = 1",
            "UPDATE measurements SET (device_id, value) = (1, 20) RETURNING *",
            "DELETE FROM measurements WHERE device_id = 1",
            "DELETE FROM measurements WHERE ts < NOW() - INTERVAL '1 day' RETURNING device_id",
            "DELETE FROM measurements AS m USING source AS s WHERE m.device_id = s.device_id RETURNING m.device_id",
            "WITH q AS (SELECT device_id FROM source) DELETE FROM measurements USING q WHERE measurements.device_id = q.device_id",
            "UPDATE /* update */ measurements SET value /* lhs */ = /* eq */ 10 WHERE device_id = 1",
            "DELETE /* delete */ FROM /* from */ measurements WHERE device_id = 1",
            "UPDATE measurements SET value =", "DELETE FROM"
        ], ["postgres"]);
    }
}
