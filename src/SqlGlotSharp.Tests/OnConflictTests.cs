using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SqlGlotSharp.Tests;

public class OnConflictTests
{
    private sealed record Case(string sql, string level, bool pretty, bool generate);

    [Fact]
    public async Task Native_Postgres_conflicts_match_live_Python_AST_SQL_comments_and_pretty()
    {
        string[] samples = [
            "INSERT INTO t VALUES (1) ON CONFLICT DO NOTHING",
            "INSERT INTO t DEFAULT VALUES ON CONFLICT DO NOTHING RETURNING *",
            "INSERT INTO t (a, b) VALUES (1, 2), (3, 4) ON CONFLICT (a) DO NOTHING",
            "INSERT INTO t VALUES (1) ON CONFLICT ON CONSTRAINT t_pkey DO NOTHING",
            "INSERT INTO t VALUES (1) ON CONFLICT ON CONSTRAINT \"Mixed Constraint\" DO UPDATE SET a = excluded.a RETURNING a",
            "INSERT INTO t AS dest (a, b) VALUES (1, 2) ON CONFLICT (a, b) DO UPDATE SET a = excluded.a, b = dest.b + 1 WHERE excluded.b > 0 RETURNING *",
            "INSERT INTO t VALUES (1) ON CONFLICT (a) WHERE a > 0 DO UPDATE SET a = excluded.a WHERE t.a < excluded.a",
            "INSERT INTO t VALUES (1) ON CONFLICT ((a + 1)) DO NOTHING",
            "INSERT INTO t VALUES (1) ON CONFLICT (custom_key(a), b::TEXT) DO NOTHING",
            "INSERT INTO t VALUES (1) ON CONFLICT (a COLLATE \"C\") DO NOTHING",
            "INSERT INTO t VALUES (1) ON CONFLICT (a DESC NULLS FIRST, b ASC NULLS LAST) DO NOTHING",
            "INSERT INTO t VALUES (1) ON CONFLICT (a) DO UPDATE SET (a, b) = (excluded.a, excluded.b)",
            "INSERT INTO t VALUES (1) ON CONFLICT (a) DO UPDATE SET a = DEFAULT, b = CASE WHEN excluded.b > 0 THEN excluded.b ELSE t.b END",
            "INSERT INTO t SELECT a FROM s WHERE a > 0 ON CONFLICT (a) DO UPDATE SET a = excluded.a",
            "WITH q AS (SELECT 1 AS a) INSERT INTO t SELECT a FROM q ON CONFLICT DO NOTHING RETURNING a",
            "INSERT /* insert */ INTO t VALUES (1) ON /* on */ CONFLICT /* conflict */ (a /* key */) WHERE /* predicate */ a > 0 DO /* do */ UPDATE /* update */ SET /* set */ a /* lhs */ = /* eq */ excluded.a /* rhs */ WHERE /* where */ t.a > 0 RETURNING /* ret */ a",
            "INSERT INTO t VALUES (1) ON CONFLICT ON CONSTRAINT /* name */ \"Key\" /* key */ DO NOTHING /* end */",
            // Upstream intentionally permits these non-native options/missing SET.
            "INSERT INTO t VALUES (1) ON CONFLICT IGNORE",
            "INSERT INTO t VALUES (1) ON CONFLICT (a) UPDATE a = 2"
        ];
        await Compare(samples.SelectMany(sql => new[] { false, true }
            .Select(pretty => new Case(sql, "RAISE", pretty, !sql.Contains("COLLATE")))).ToArray());
    }

    [Fact]
    public async Task Native_Postgres_conflict_errors_match_live_Python_in_all_error_modes()
    {
        string[] samples = [
            "INSERT INTO t VALUES (1) ON CONFLICT",
            "INSERT INTO t VALUES (1) ON CONFLICT DO",
            "INSERT INTO t VALUES (1) ON CONFLICT DO UNKNOWN",
            "INSERT INTO t VALUES (1) ON CONFLICT DO 'NOTHING'",
            "INSERT INTO t VALUES (1) ON CONFLICT () DO NOTHING",
            "INSERT INTO t VALUES (1) ON CONFLICT (a,) DO NOTHING",
            "INSERT INTO t VALUES (1) ON CONFLICT ON CONSTRAINT DO NOTHING",
            "INSERT INTO t VALUES (1) ON CONFLICT (a) DO UPDATE SET",
            "INSERT INTO t VALUES (1) ON CONFLICT (a) DO UPDATE SET a =",
            "INSERT INTO t VALUES (1) ON CONFLICT (a) DO UPDATE SET a = 2,",
            "INSERT INTO t VALUES (1) ON CONFLICT (a) WHERE DO NOTHING",
            "INSERT INTO t VALUES (1) ON CONFLICT (a) DO UPDATE SET a = 2 WHERE",
            "INSERT INTO t VALUES (1) ON CONFLICT DO NOTHING RETURNING"
        ];
        await Compare(samples.SelectMany(sql => new[] { "IMMEDIATE", "RAISE", "IGNORE" }
            .Select(level => new Case(sql, level, false, false))).ToArray());
    }

    [Theory]
    [InlineData("INSERT INTO t VALUES (1) ON CONFLICT ON CONSTRAINT public.key DO NOTHING", "qualified ON CONSTRAINT")]
    [InlineData("INSERT INTO t VALUES (1) ON CONFLICT ON CONSTRAINT key DEFERRABLE DO NOTHING", "modifier")]
    [InlineData("INSERT INTO t VALUES (1) ON CONFLICT (a) INCLUDE (b) DO NOTHING", "modifier")]
    [InlineData("INSERT INTO t VALUES (1) ON CONFLICT (a WITH FILL) DO NOTHING", "WithFill")]
    public void Unsupported_conflict_dependencies_are_explicit_even_with_ignored_errors(string sql, string boundary)
    {
        Assert.Contains(boundary, Assert.Throws<NotPortedException>(() =>
            new dialects.postgres.Postgres().parse(sql, errors.ErrorLevel.IGNORE)).Message);
    }

    [Fact]
    public void Collation_generation_matches_upstream()
    {
        // Python: parse_one('INSERT INTO t VALUES (1) ON CONFLICT (a COLLATE "C") DO NOTHING', read='postgres').sql('postgres')
        Assert.Equal("INSERT INTO t VALUES (1) ON CONFLICT(a COLLATE \"C\") DO NOTHING",
            Assert.Single(SqlGlot.Transpile("INSERT INTO t VALUES (1) ON CONFLICT (a COLLATE \"C\") DO NOTHING", "postgres")));
    }

    private static async Task Compare(Case[] cases)
    {
        var expected = JsonNode.Parse(await Python("""
            import sys,json
            sys.path.insert(0,'python')
            from sqlglot.dialects.postgres import Postgres
            from sqlglot.errors import ParseError,ErrorLevel
            from sqlglot.serde import dump
            out=[]
            for c in json.load(sys.stdin):
                try:
                    d=Postgres()
                    e=d.parse(c['sql'],error_level=ErrorLevel[c['level']])[0]
                    out.append(dict(ast=dump(e),sql=d.generate(e,pretty=c['pretty']) if c['generate'] else None,error=None))
                except ParseError as e:
                    out.append(dict(ast=None,sql=None,error=e.errors))
            print(json.dumps(out))
            """, JsonSerializer.Serialize(cases)))!.AsArray();
        for (var i = 0; i < cases.Length; i++)
        {
            var c = cases[i];
            var d = new dialects.postgres.Postgres();
            exp.Expr? ast = null;
            var failure = Record.Exception(() => ast = Assert.Single(d.parse(c.sql, Enum.Parse<errors.ErrorLevel>(c.level))));
            if (expected[i]!["error"] is { } error)
            {
                Assert.True(failure is errors.ParseError, $"{c}: {failure}");
                var parsed = (errors.ParseError)failure!;
                Assert.True(JsonNode.DeepEquals(error, JsonSerializer.SerializeToNode(parsed.errors, serde.JsonOptions)),
                    $"Error mismatch {c}: {failure}\nExpected: {error}");
                continue;
            }
            Assert.True(failure is null, $"{c}: {failure}");
            var actual = JsonSerializer.SerializeToNode(serde.dump(ast), serde.JsonOptions);
            Assert.True(JsonNode.DeepEquals(expected[i]!["ast"], actual),
                $"AST mismatch {c}\nPython: {expected[i]!["ast"]}\nC#: {actual}");
            if (c.generate)
            {
                var g = new generators.postgres.PostgresGenerator { dialect = d, pretty = c.pretty };
                Assert.Equal(expected[i]!["sql"]!.GetValue<string>(), g.generate(ast!));
            }
        }
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
        await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token); process.StandardInput.Close();
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await stderr);
        return await stdout;
    }
}
