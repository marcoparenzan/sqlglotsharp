using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SqlGlotSharp.Tests;

public class TypeCastPipelineTests
{
    private sealed record Case(string sql, string read, bool pretty);
    private sealed record Result(JsonElement? ast, string? sql, string? error);

    [Fact]
    public async Task Scalar_casts_and_arrays_match_live_Python_AST_and_SQL()
    {
        string[] samples = [
            "SELECT CAST(1 AS INT)", "SELECT 1::INT", "SELECT a::BIGINT FROM t",
            "SELECT a::INT::TEXT FROM t", "SELECT CAST(a + 1 AS DOUBLE) FROM t",
            "SELECT CAST(a AS VARCHAR(20)) FROM t", "SELECT CAST(a AS DECIMAL(10, 2)) FROM t",
            "SELECT a::NUMERIC(10, 3) FROM t", "SELECT CAST(a AS CHAR(5)) FROM t",
            "SELECT CAST(a AS BOOLEAN) FROM t", "SELECT CAST(a AS DATE) FROM t",
            "SELECT CAST(a AS TIME) FROM t", "SELECT CAST(a AS TIMESTAMP) FROM t",
            "SELECT CAST(a AS TIMESTAMP(3) WITH TIME ZONE) FROM t",
            "SELECT CAST(a AS TIMESTAMP WITHOUT TIME ZONE) FROM t",
            "SELECT CAST(a AS TIME WITH TIME ZONE) FROM t",
            "SELECT CAST(a AS FLOAT(24)) FROM t", "SELECT CAST(a AS DOUBLE PRECISION) FROM t",
            "SELECT a::INT[] FROM t", "SELECT a::INT[][] FROM t", "SELECT a::INT ARRAY FROM t",
            "SELECT CAST(a AS VARCHAR(10)[]) FROM t",
            "SELECT CAST(a AS JSONB) FROM t", "SELECT CAST(a AS UUID) FROM t",
            "SELECT CAST(a AS TEXT COLLATE \"C\") FROM t",
            "SELECT CAST(a AS INT DEFAULT 0 ON CONVERSION ERROR) FROM t",
            "SELECT CAST(/* value */ a AS /* type */ INT) /* end */ FROM t",
            "WITH q(a) AS (SELECT CAST(1 AS BIGINT)) SELECT a::TEXT FROM q",
            "SELECT CAST(a AS INT) AS n FROM t JOIN u USING (a) WHERE a > 1",
            "SELECT CAST(1 INT)", "SELECT CAST(1 AS)", "SELECT 1::", "SELECT CAST(a AS VARCHAR(10) FROM t"
        ];
        await CompareSamples(samples);
        await CompareSamples(["SELECT 65::\"char\"", "SELECT CAST(a AS \"char\"[]) FROM t"], ["postgres"]);
    }

    internal static async Task CompareSamples(string[] samples, string[]? reads = null)
    {
        var cases = samples.SelectMany(sql => (reads ?? ["", "postgres"]).SelectMany(read =>
            new[] { false, true }.Select(pretty => new Case(sql, read, pretty)))).ToArray();
        var expected = JsonSerializer.Deserialize<Result[]>(await Python("""
            import sys,json
            sys.path.insert(0,'python')
            import sqlglot
            from sqlglot.errors import ParseError
            out=[]
            for c in json.load(sys.stdin):
                try:
                    if c['read']=='timescale':
                        sys.path.insert(0,'src/tools')
                        from oracle_plugins.timescale import Timescale
                    e=sqlglot.parse_one(c['sql'],read=c['read'])
                    out.append(dict(ast=e.dump(),sql=e.sql(dialect=c['read'],pretty=c['pretty']),error=None))
                except ParseError:
                    out.append(dict(ast=None,sql=None,error='ParseError'))
            print(json.dumps(out))
            """, JsonSerializer.Serialize(cases)))!;
        for (var i = 0; i < cases.Length; i++)
        {
            var c = cases[i];
            var dialect = dialects.dialect.Dialect.get_or_raise(c.read);
            exp.Expr? ast = null;
            var failure = Record.Exception(() => ast = Assert.Single(dialect.parse(c.sql)));
            if (expected[i].error is not null)
            {
                Assert.True(failure is errors.ParseError, $"{c}: {failure}");
                continue;
            }
            Assert.True(failure is null, $"{c}: {failure}");
            var actual = JsonSerializer.SerializeToNode(serde.dump(ast));
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected[i].ast!.Value.GetRawText()), actual),
                $"AST mismatch {c}\nExpected {expected[i].ast}\nActual {actual}");
            var g = dialect.generator(c.pretty);
            Assert.Equal(expected[i].sql, g.generate(ast!));
        }
    }

    // Formerly NotPorted cast branches; expected values come from the Python oracle (read="postgres").
    [Theory]
    [InlineData("SELECT CAST(a AS DATE FORMAT 'YYYY') FROM t", "SELECT TO_DATE(a, 'YYYY') FROM t")]
    [InlineData("SELECT TRY_CAST(a AS INT)", "SELECT CAST(a AS INT)")]
    [InlineData("SELECT SAFE_CAST(a AS INT)", "SELECT CAST(a AS INT)")]
    [InlineData("SELECT CONVERT(a, CHAR CHARACTER SET utf8)", "SELECT CAST(a AS CHAR CHARACTER SET utf8)")]
    [InlineData("SELECT DECODE(a, NULL, 'null')", "SELECT CASE WHEN a IS NULL THEN 'null' END")]
    [InlineData("SELECT CAST(a, 'int')", "SELECT CAST_TO_STR_TYPE(a, 'int')")]
    public void Cast_branches_match_upstream(string sql, string expected) =>
        Assert.Equal(expected, Assert.Single(SqlGlot.Transpile(sql, "postgres")));

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
        await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token); process.StandardInput.Close();
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, await stderr);
        return await stdout;
    }
}
