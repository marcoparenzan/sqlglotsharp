namespace SqlGlotSharp.Tests;

public class AlterPipelineTests
{
    [Fact]
    public async Task Ordinary_alter_actions_match_Python_AST_and_native_SQL()
    {
        await TypeCastPipelineTests.CompareSamples([
            "ALTER TABLE measurements ADD COLUMN note TEXT",
            "ALTER TABLE measurements ADD COLUMN IF NOT EXISTS quality INT DEFAULT 0 NOT NULL",
            "ALTER TABLE measurements DROP COLUMN note",
            "ALTER TABLE measurements DROP COLUMN IF EXISTS note CASCADE",
            "ALTER TABLE measurements ALTER COLUMN value TYPE DECIMAL(12, 3)",
            "ALTER TABLE measurements ALTER COLUMN value TYPE DECIMAL(12, 3) USING value::DECIMAL(12, 3)",
            "ALTER TABLE measurements ALTER COLUMN value SET DEFAULT 0",
            "ALTER TABLE measurements ALTER COLUMN value DROP DEFAULT",
            "ALTER TABLE measurements ALTER COLUMN value SET NOT NULL",
            "ALTER TABLE measurements ALTER COLUMN value DROP NOT NULL",
            "ALTER TABLE measurements RENAME COLUMN value TO reading",
            "ALTER TABLE public.measurements RENAME TO readings",
            "ALTER TABLE measurements SET (timescaledb.compress=true)",
            "ALTER TABLE measurements SET (timescaledb.compress=true, timescaledb.compress_segmentby='device_id')",
            "ALTER TABLE measurements SET (fillfactor=70, autovacuum_enabled=false)",
            "ALTER /* alter */ TABLE measurements ADD /* add */ COLUMN note TEXT"
        ], ["postgres", "timescale"]);
    }
}
