namespace SqlGlotSharp.Tests;

public class ConstraintPipelineTests
{
    [Fact]
    public async Task Common_constraints_match_Python_AST_and_production_Postgres_SQL()
    {
        await TypeCastPipelineTests.CompareSamples([
            "CREATE TABLE devices (id BIGINT PRIMARY KEY, label TEXT UNIQUE)",
            "CREATE TABLE measurements (device_id BIGINT, ts TIMESTAMPTZ, value DECIMAL(10, 2), PRIMARY KEY (device_id, ts))",
            "CREATE TABLE measurements (value INT CHECK (value >= 0))",
            "CREATE TABLE measurements (value INT, CHECK (value >= 0))",
            "CREATE TABLE measurements (value INT, CONSTRAINT valid_value CHECK (value >= 0))",
            "CREATE TABLE measurements (device_id BIGINT REFERENCES devices(id))",
            "CREATE TABLE measurements (device_id BIGINT, FOREIGN KEY (device_id) REFERENCES devices(id))",
            "CREATE TABLE measurements (device_id BIGINT, CONSTRAINT device_fk FOREIGN KEY (device_id) REFERENCES devices(id))",
            "CREATE TABLE devices (id INT CONSTRAINT id_not_null NOT NULL)",
            "CREATE TABLE devices (id INT, label TEXT, UNIQUE (id, label))",
            "CREATE TABLE devices (id INT, CONSTRAINT device_pk PRIMARY KEY (id))",
            "CREATE TABLE devices (id INT /* id */ PRIMARY /* primary */ KEY, label TEXT UNIQUE)",
            "CREATE TABLE measurements (value INT DEFAULT 0 CHECK (value >= 0) NOT NULL)",
            "CREATE TABLE devices (id INT, PRIMARY KEY (id,))",
            "CREATE TABLE measurements (value INT CHECK (value >))"
        ], ["postgres"]);
    }
}
