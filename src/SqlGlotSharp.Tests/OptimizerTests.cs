namespace SqlGlotSharp.Tests;

/// <summary>annotate_types and simplify; expected values come from the Python 3.11 oracle (sqlglot v30.27.0).</summary>
public class OptimizerTests
{
    [Theory]
    [InlineData("1 + 2 + 3", "6")]
    [InlineData("x + 1 + 2", "x + 3")]
    [InlineData("TRUE AND x", "x AND TRUE")]
    [InlineData("NOT (a > 1)", "a <= 1")]
    [InlineData("a = 1 AND a = 1", "a = 1")]
    [InlineData("x BETWEEN 1 AND 3", "x <= 3 AND x >= 1")]
    [InlineData("CASE WHEN TRUE THEN 1 ELSE 2 END", "1")]
    [InlineData("'a' || 'b'", "'ab'")]
    [InlineData("x + 1 = 3", "x = 2")]
    public void Simplify_matches_the_oracle(string sql, string expected)
    {
        var simplified = optimizer.simplify_module.simplify(SqlGlot.parse_one(sql));
        Assert.Equal(expected, new dialects.dialect.Dialect().generator().generate(simplified));
    }

    [Theory]
    [InlineData("1 + 2.5", "DOUBLE")]
    [InlineData("'a' || 1", "VARCHAR")]
    [InlineData("CAST(x AS INT) + 1", "INT")]
    [InlineData("x > 1 AND y", "BOOLEAN")]
    public void Annotate_types_matches_the_oracle(string sql, string expected)
    {
        var node = optimizer.annotate_types_module.annotate_types(SqlGlot.parse_one(sql));
        Assert.Equal(expected, new dialects.dialect.Dialect().generator().generate(node.type!));
    }

    [Theory]
    [InlineData("SELECT ROUND(1.5, 2)", "postgres", "postgres", "SELECT ROUND(CAST(1.5 AS DECIMAL), 2)")]
    [InlineData("SELECT ROUND(x, 2)", "postgres", "postgres", "SELECT ROUND(x, 2)")]
    [InlineData("SELECT REGEXP_REPLACE(a, 'b', 'c', 'g')", "postgres", "postgres", "SELECT REGEXP_REPLACE(a, 'b', 'c', 'g')")]
    [InlineData("SELECT col[1]", "postgres", "postgres", "SELECT col[1]")]
    [InlineData("SELECT CONCAT(a, b)", "", "postgres", "SELECT a || b")]
    public void Optimizer_backed_generation_matches_the_oracle(string sql, string read, string write, string expected) =>
        Assert.Equal(expected, Assert.Single(SqlGlot.transpile(sql, read: read, write: write)));

    [Fact]
    public void Annotating_queries_needs_the_scope_optimizer()
    {
        Assert.Throws<NotPortedException>(() => optimizer.annotate_types_module.annotate_types(SqlGlot.parse_one("SELECT x FROM t")));
    }
}
