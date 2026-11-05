namespace SqlGlotSharp.Tests;

/// <summary>T-SQL parsing and generation; every expected string was produced by the Python 3.11 oracle (sqlglot v30.27.0).</summary>
public class TsqlTranspileTests
{
    [Theory]
    [InlineData("SELECT TOP 10 [a] FROM [dbo].[t] WITH (NOLOCK) WHERE x = 1", "tsql", "tsql", "SELECT TOP 10 [a] FROM [dbo].[t] WITH (NOLOCK) WHERE x = 1")]
    [InlineData("SELECT * FROM a CROSS APPLY b", "tsql", "tsql", "SELECT * FROM a CROSS APPLY b")]
    [InlineData("SELECT x FROM t OPTION(RECOMPILE, MAXDOP 4)", "tsql", "tsql", "SELECT x FROM t OPTION(RECOMPILE, MAXDOP 4)")]
    [InlineData("SELECT * FROM t FOR XML PATH('r'), ROOT('root')", "tsql", "tsql", "SELECT * FROM t FOR XML PATH('r'), ROOT('root')")]
    [InlineData("SELECT DATEADD(day, 1, x), DATEDIFF(month, a, b), DATEPART(year, c)", "tsql", "tsql",
        "SELECT DATEADD(DAY, 1, x), DATEDIFF(MONTH, CAST(a AS DATETIME2), CAST(b AS DATETIME2)), DATEPART(year, c)")]
    [InlineData("SELECT ISNULL(a, b), LEN(c), CHARINDEX('a', b)", "tsql", "tsql", "SELECT ISNULL(a, b), LEN(c), CHARINDEX('a', b)")]
    [InlineData("SELECT TOP 5 a FROM t ORDER BY a", "tsql", "postgres", "SELECT a FROM t ORDER BY a NULLS FIRST LIMIT 5")]
    [InlineData("SELECT a FROM t LIMIT 5", "postgres", "tsql", "SELECT TOP 5 a FROM t")]
    [InlineData("SELECT a FROM t LIMIT 5 OFFSET 2", "postgres", "tsql", "SELECT a FROM t ORDER BY (SELECT NULL) OFFSET 2 ROWS FETCH FIRST 5 ROWS ONLY")]
    [InlineData("SELECT CURRENT_TIMESTAMP, TRUE, x IS NULL FROM t", "postgres", "tsql", "SELECT GETDATE(), 1, x IS NULL FROM t")]
    [InlineData("CREATE TABLE IF NOT EXISTS foo.bar (a INT)", "postgres", "tsql",
        "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'bar' AND TABLE_SCHEMA = 'foo') EXEC('CREATE TABLE foo.bar (a INTEGER)')")]
    [InlineData("SELECT a FROM t WHERE flag", "postgres", "tsql", "SELECT a FROM t WHERE flag <> 0")]
    [InlineData("SELECT SUBSTRING(a, 1, 2) || 'x' FROM t", "postgres", "tsql", "SELECT SUBSTRING(a, 1, 2) + 'x' FROM t")]
    [InlineData("SELECT STRING_AGG(a, ',') FROM t", "tsql", "tsql", "SELECT STRING_AGG(a, ',') FROM t")]
    [InlineData("EXEC sp_executesql @stmt = @payload, @param1 = value1", "tsql", "tsql", "EXECUTE sp_executesql @stmt = @payload, @param1 = value1")]
    [InlineData("DECLARE @x INT = 1", "tsql", "tsql", "DECLARE @x INTEGER = 1")]
    [InlineData("SELECT * INTO #tmp FROM t", "tsql", "tsql", "SELECT * INTO #tmp FROM t")]
    [InlineData("SELECT HASHBYTES('MD5', 'x')", "tsql", "postgres", "SELECT MD5('x')")]
    [InlineData("SELECT DISTINCT ON (a) a, b FROM t", "postgres", "tsql",
        "SELECT a, b FROM (SELECT a AS a, b AS b, ROW_NUMBER() OVER (PARTITION BY a ORDER BY a) AS _row_number FROM t) AS _t WHERE _row_number = 1")]
    public void Transpile_matches_the_python_oracle(string sql, string read, string write, string expected) =>
        Assert.Equal(expected, Assert.Single(SqlGlot.transpile(sql, read: read, write: write)));

    [Fact]
    public void Tsql_is_a_first_class_public_dialect()
    {
        Assert.IsType<parsers.tsql.TSQLParser>(dialects.dialect.Dialect.get_or_raise("tsql").parser());
        Assert.IsType<generators.tsql.TSQLGenerator>(dialects.dialect.Dialect.get_or_raise("tsql").generator());
        Assert.IsType<exp.Select>(SqlGlot.parse_one("SELECT TOP 1 x FROM t", read: "tsql"));
    }
}
