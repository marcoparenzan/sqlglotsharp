namespace SqlGlotSharp.Tests;

public class InsertPipelineTests
{
    [Fact]
    public async Task Insert_statements_match_Python_AST_and_production_SQL()
    {
        await TypeCastPipelineTests.CompareSamples([
            "INSERT INTO measurements VALUES (1, 10)",
            "INSERT INTO measurements (device_id, value) VALUES (1, 10), (2, 20)",
            "INSERT INTO measurements (device_id, value) VALUES (1, 10) RETURNING device_id, value",
            "INSERT INTO measurements DEFAULT VALUES",
            "INSERT INTO measurements DEFAULT VALUES RETURNING *",
            "INSERT INTO archive (device_id, value) SELECT device_id, value FROM measurements WHERE value > 0",
            "INSERT INTO archive SELECT * FROM measurements WHERE ts >= NOW() - INTERVAL '1 day'",
            "INSERT INTO measurements (device_id, value) VALUES (1, CASE WHEN 1 > 0 THEN 10 ELSE 0 END)",
            "INSERT INTO measurements (device_id, payload) VALUES (1, '{\"value\":10}'::JSONB)",
            "INSERT /* insert */ INTO /* target */ measurements (device_id, value) VALUES /* row */ (1, 10)",
            "INSERT INTO measurements (device_id) VALUES (1) RETURNING device_id AS id",
            "INSERT INTO measurements (device_id, value) VALUES (1,"
        ]);
    }
}
