using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SqlGlotSharp.Tests;

public class ParserCteTests
{
    private sealed record Result(JsonElement? ast, string? sql, string? error);

    [Fact]
    public async Task Ordinary_Ctes_match_Python_AST_and_production_Postgres_SQL()
    {
        string[] inputs = [
            "WITH q AS (SELECT 1) SELECT 1 FROM q",
            "SELECT * FROM t JOIN u USING (a)",
            "SELECT * FROM foo(1)",
            "SELECT * FROM t OFFSET 3 ROWS",
            "SELECT * FROM t FETCH FIRST 2 ROWS ONLY",
            "SELECT * FROM db..t AS q JOIN u USING (a)",
            "SELECT * FROM a.b.c.d JOIN u USING (a)",
            "SELECT t.a FROM t LEFT JOIN db.u AS u USING (a, b)",
            "SELECT * FROM t FULL OUTER JOIN u USING (\"a\")",
            "SELECT * FROM t JOIN u USING a, b",
            "SELECT * FROM t JOIN u USING ()",
            "SELECT * FROM t JOIN u USING (a,)",
            "SELECT * FROM t JOIN u USING (a",
            "SELECT * FROM ONLY t",
            "SELECT * FROM t*",
            "SELECT * FROM \"db\".\"t\" AS x JOIN u USING (a)",
            "SELECT * FROM t JOIN db /* db */ . u /* table */ USING /* using */ (a)",
            "SELECT * FROM t JOIN u USING (a) JOIN v USING (a) ORDER BY a LIMIT 3",
            "WITH q(a) AS (SELECT 1) SELECT a FROM q",
            "WITH q(a, b) AS (SELECT 1, 2) SELECT a, b FROM q",
            "WITH q(\"A\", b) AS (SELECT 1, 2) SELECT \"A\", b FROM q",
            "WITH q(a /* column */, b) AS (SELECT 1, 2) SELECT * FROM q",
            "SELECT * FROM (SELECT 1, 2) AS q(a, b)",
            "SELECT q.a FROM t AS q(a)",
            "SELECT t.a FROM t JOIN u AS q(a) ON t.a = q.a",
            "SELECT a FROM t JOIN (SELECT 1) q(a) ON t.a = q.a",
            "WITH q() AS (SELECT 1) SELECT 1",
            "WITH q(a,) AS (SELECT 1) SELECT 1",
            "WITH q(a AS) AS (SELECT 1) SELECT 1",
            "WITH q(a AS,) AS (SELECT 1) SELECT 1",
            "WITH q AS MATERIALIZED (SELECT a FROM t) SELECT a FROM q",
            "WITH q AS NOT MATERIALIZED (SELECT a FROM t) SELECT a FROM q",
            "WITH RECURSIVE q AS (SELECT 1 UNION ALL SELECT 2) SELECT * FROM q",
            "WITH q AS (SELECT a FROM t), r AS (SELECT a FROM q) SELECT a FROM r",
            "WITH q AS (WITH r AS (SELECT a FROM t) SELECT a FROM r) SELECT a FROM q",
            "WITH q AS (SELECT a FROM t) WITH r AS (SELECT a FROM q) SELECT a FROM r",
            "WITH q AS (SELECT a FROM t GROUP BY a HAVING COUNT(*) > 1) SELECT * FROM q",
            "WITH q AS (SELECT 1) SELECT * FROM q UNION SELECT 2",
            "WITH /* with */ q /* alias */ AS /* as */ (SELECT /* select */ 1) SELECT * FROM q",
            "WITH q AS (SELECT a FROM t) SELECT q.a FROM q LEFT JOIN u ON q.a=u.a",
            "WITH q AS (SELECT 1) (SELECT * FROM q)",
            "WITH q AS (SELECT 1)",
            "WITH q AS (SELECT 1 SELECT 2", "WITH q AS SELECT 1",
            "WITH q AS (SELECT 1), SELECT 2"
        ];
        var expected = JsonSerializer.Deserialize<Result[]>(await Python("""
            import sys,json
            sys.path.insert(0,'python')
            import sqlglot
            from sqlglot.errors import ParseError
            out=[]
            for s in json.load(sys.stdin):
                try:
                    e=sqlglot.parse_one(s,read='postgres')
                    ast=e.dump()
                    out.append(dict(ast=ast,sql=e.sql(dialect='postgres'),error=None))
                except ParseError:
                    out.append(dict(ast=None,sql=None,error='ParseError'))
            print(json.dumps(out))
            """, JsonSerializer.Serialize(inputs)))!;
        for (var i = 0; i < inputs.Length; i++)
        {
            var pg = dialects.dialect.Dialect.get_or_raise("postgres");
            exp.Expr? ast = null;
            var failure = Record.Exception(() => ast = Assert.Single(pg.parse(inputs[i])));
            if (expected[i].error is not null)
            {
                Assert.True(failure is errors.ParseError, $"{inputs[i]}: {failure}");
                continue;
            }
            Assert.Null(failure);
            Assert.NotNull(ast);
            var actual = JsonSerializer.SerializeToNode(serde.dump(ast));
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected[i].ast!.Value.GetRawText()), actual),
                $"AST difference for {inputs[i]}\n{actual}\n{expected[i].ast}");
            Assert.Equal(expected[i].sql, new generators.postgres.PostgresGenerator { dialect = pg }.generate(ast));
        }
    }

    [Theory]



    [InlineData("WITH q USING KEY (a) AS (SELECT 1) SELECT * FROM q", "_parse_wrapped_id_vars")]
    public void Unavailable_dependencies_are_not_silently_accepted(string sql, string boundary)
    {
        Assert.Contains(boundary, Assert.Throws<NotPortedException>(() =>
            dialects.dialect.Dialect.get_or_raise("postgres").parse(sql, errors.ErrorLevel.IGNORE)).Message);
    }

    private static async Task<string> Python(string script, string input, [CallerFilePath] string source = "")
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SQLGLOT_PYTHON") ?? "python")
        {
            WorkingDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..")),
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-B"); start.ArgumentList.Add("-c"); start.ArgumentList.Add(script);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
        process.StandardInput.Close();
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, await stderr);
        return await stdout;
    }
}
