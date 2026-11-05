namespace SqlGlotSharp.Tests;

public class CreateTablePipelineTests
{
    [Fact]
    public async Task Ordinary_tables_match_Python_AST_and_production_SQL()
    {
        await TypeCastPipelineTests.CompareSamples([
            "CREATE TABLE measurements (device_id BIGINT, value DECIMAL(10, 2), ts TIMESTAMP)",
            "CREATE TABLE IF NOT EXISTS public.measurements (device_id BIGINT NOT NULL, value DOUBLE, ts TIMESTAMPTZ)",
            "CREATE TABLE measurements (device_id INT DEFAULT 1, value DECIMAL(10, 2) DEFAULT 0)",
            "CREATE TABLE measurements (ts TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP, payload JSONB)",
            "CREATE TABLE \"Measurements\" (\"Id\" INT NOT NULL, \"Label\" TEXT NULL)",
            "CREATE TABLE archive AS SELECT device_id, value FROM measurements WHERE value > 0",
            "CREATE TABLE archive (device_id INT) AS SELECT device_id FROM measurements",
            "CREATE /* create */ TABLE /* table */ measurements (device_id /* name */ INT /* kind */ NOT NULL)",
            "CREATE TABLE measurements (device_id INT,", "CREATE TABLE measurements ()"
        ]);
    }
}
