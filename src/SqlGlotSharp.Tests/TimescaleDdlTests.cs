namespace SqlGlotSharp.Tests;

public class TimescaleDdlTests
{
    [Fact]
    public async Task Property_and_view_queries_match_live_oracles()
    {
        await TypeCastPipelineTests.CompareSamples([
            "CREATE TABLE measurements (device_id BIGINT, ts TIMESTAMPTZ) WITH (fillfactor=70)",
            "CREATE TABLE measurements (device_id BIGINT, ts TIMESTAMPTZ) WITH (autovacuum_enabled=false, fillfactor=80)",
            "CREATE TABLE measurements (device_id BIGINT, ts TIMESTAMPTZ) WITH (timescaledb.compress=true)",
            "CREATE TABLE measurements (device_id BIGINT, ts TIMESTAMPTZ) WITH (timescaledb.compress=true, timescaledb.compress_segmentby='device_id', timescaledb.compress_orderby='ts DESC')",
            "CREATE TABLE measurements (device_id BIGINT, ts TIMESTAMPTZ) WITH (tsdb.hypertable=true, tsdb.partition_column='ts')",
            "CREATE VIEW recent AS SELECT device_id, ts FROM measurements",
            "CREATE MATERIALIZED VIEW hourly WITH (timescaledb.continuous=true) AS SELECT device_id FROM measurements",
            "CREATE MATERIALIZED VIEW hourly WITH (fillfactor=80) AS SELECT device_id FROM measurements",
            "CREATE TABLE measurements (device_id INT) WITH /* options */ (fillfactor /* key */ = /* value */ 70)",
            "CREATE MATERIALIZED VIEW hourly WITH (timescaledb.continuous) AS SELECT device_id FROM measurements",
            "CREATE TABLE measurements (ts TIMESTAMPTZ) WITH (tsdb.hypertable, tsdb.partition_column='ts')"
        ], ["postgres", "timescale"]);
    }
}
