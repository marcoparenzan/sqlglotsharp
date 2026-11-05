namespace SqlGlotSharp.Tests;

/// <summary>Fabric (T-SQL derivative); every expected string was produced by the Python 3.11 oracle (sqlglot v30.27.0).</summary>
public class FabricTranspileTests
{
    [Theory]
    [InlineData("CREATE TABLE t (a VARCHAR, b CHAR, c NVARCHAR(10), d DATETIME, e MONEY, f TINYINT)", "fabric", "fabric",
        "CREATE TABLE t (a VARCHAR(1), b CHAR(1), c VARCHAR(10), d DATETIME2(6), e DECIMAL(19, 4), f SMALLINT)")]
    [InlineData("CREATE TABLE t (a VARCHAR, b CHAR(3))", "tsql", "fabric", "CREATE TABLE t (a VARCHAR(MAX), b CHAR(3))")]
    [InlineData("SELECT CAST(x AS DATETIME2(7)), CAST(y AS TIME)", "fabric", "fabric", "SELECT CAST(x AS DATETIME2(6)), CAST(y AS TIME(6))")]
    [InlineData("SELECT CAST(x AS TIMESTAMPTZ) AT TIME ZONE 'UTC'", "postgres", "fabric",
        "SELECT CAST(CAST(x AS DATETIMEOFFSET(6)) AT TIME ZONE 'UTC' AS DATETIME2(6))")]
    [InlineData("SELECT TOP 3 a FROM t", "fabric", "postgres", "SELECT a FROM t LIMIT 3")]
    [InlineData("SELECT a FROM t LIMIT 3", "postgres", "fabric", "SELECT TOP 3 a FROM t")]
    public void Transpile_matches_the_python_oracle(string sql, string read, string write, string expected) =>
        Assert.Equal(expected, Assert.Single(SqlGlot.transpile(sql, read: read, write: write)));

    [Fact]
    public void Fabric_is_a_first_class_public_dialect()
    {
        Assert.IsType<parsers.fabric.FabricParser>(dialects.dialect.Dialect.get_or_raise("fabric").parser());
        Assert.IsType<generators.fabric.FabricGenerator>(dialects.dialect.Dialect.get_or_raise("fabric").generator());
    }
}
