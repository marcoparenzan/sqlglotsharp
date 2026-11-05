using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SqlGlotSharp.Corpus;

namespace SqlGlotSharp.Tests;

public class DropTruncateTests
{
    private sealed record Case(string sql, string read, bool pretty);

    [Fact]
    public async Task Ordinary_statements_match_live_production_Python()
    {
        await Compare([
            "DROP TABLE t", "DROP VIEW t", "drop table if exists public.t cascade",
            "DROP VIEW IF EXISTS public.v RESTRICT", "DROP TABLE a, public.b, \"Mixed Name\" CASCADE",
            "DROP VIEW a, b", "DROP TABLE \"cat\".\"schema\".\"Table\" RESTRICT",
            "DROP /* drop */ TABLE /* kind */ t /* name */ CASCADE /* option */",
            "/* leading */ DROP VIEW IF EXISTS /* exists */ v -- trailing\n",
            "TRUNCATE t", "TRUNCATE TABLE t", "TRUNCATE TABLE a, public.b, \"Mixed Name\"",
            "TRUNCATE TABLE t RESTART IDENTITY CASCADE", "TRUNCATE t CONTINUE IDENTITY RESTRICT",
            "truncate table a, b restart identity cascade", "truncate a restrict",
            "TRUNCATE TABLE IF EXISTS public.t CONTINUE IDENTITY",
            "TRUNCATE /* truncate */ TABLE /* kind */ a /* a */, b /* b */ CONTINUE IDENTITY CASCADE",
            "/* leading */ TRUNCATE t -- trailing\n",
            "DROP TABLE a; TRUNCATE TABLE b; DROP VIEW c CASCADE",
            "TRUNCATE(12.345, 2)",
            "DROP TABLE", "DROP VIEW IF EXISTS", "DROP TABLE a,", "DROP TABLE a RESTRICT CASCADE",
            "DROP TABLE IF t", "TRUNCATE TABLE", "TRUNCATE TABLE a,"
        ]);
    }

    [Fact]
    public async Task PostgreSQL_only_table_modifier_matches_upstream()
    {
        // ONLY is a PostgreSQL keyword; in base it becomes a command fallback.
        await Compare(["TRUNCATE ONLY public.t * RESTART IDENTITY", "TRUNCATE TABLE ONLY t, ONLY u CASCADE"], ["postgres"]);
    }

    [Fact]
    public async Task External_corpus_is_read_only_and_ordinary_routes_match_live_Python()
    {
        // Harvest inputs only; recorded tokenizer/AST/SQL answers are not an oracle.
        var path = CorpusReader.ResolveFile(Path.Combine(CorpusReader.RootDirectory, "v30.27.0"), "L0.base.jsonl");
        const string name = "(?:[a-zA-Z_][a-zA-Z_0-9]*|\"[^\"]+\")(?:\\.(?:[a-zA-Z_][a-zA-Z_0-9]*|\"[^\"]+\"))*";
        var ordinary = new Regex($"^(?:DROP (?:TABLE|VIEW)(?: IF EXISTS)? {name}(?:, *{name})*(?: CASCADE| RESTRICT)?|TRUNCATE(?: TABLE)? {name}(?:, *{name})*(?: (?:RESTART|CONTINUE) IDENTITY)?(?: CASCADE| RESTRICT)?)$", RegexOptions.IgnoreCase);
        var samples = new HashSet<string>();
        foreach (var line in File.ReadLines(path))
        {
            if (!line.Contains("DROP", StringComparison.OrdinalIgnoreCase) && !line.Contains("TRUNCATE", StringComparison.OrdinalIgnoreCase)) continue;
            using var doc = JsonDocument.Parse(line);
            var sql = doc.RootElement.GetProperty("sql").GetString()!;
            if (ordinary.IsMatch(sql.Trim())) samples.Add(sql);
        }
        Assert.NotEmpty(samples);
        await Compare(samples.ToArray());
    }

    // Formerly an explicit NotPorted boundary; the full _parse_drop/drop_sql are ported and compared to Python.
    [Fact]
    public async Task Every_drop_kind_and_modifier_matches_upstream()
    {
        await Compare(new[] {
            "DROP INDEX t", "DROP INDEX CONCURRENTLY IF EXISTS t", "DROP SCHEMA t CASCADE", "DROP FUNCTION f(INT)",
            "DROP SEQUENCE t", "DROP MATERIALIZED VIEW v", "DROP TEMPORARY TABLE t", "DROP ICEBERG TABLE t",
            "DROP TABLE t ON CLUSTER c", "DROP TABLE t CASCADE CONSTRAINTS", "DROP TABLE t PURGE", "DROP TABLE t SYNC",
            "DROP TABLE t FORCE", "DROP TRIGGER tr ON t", "DROP DATABASE d" });
    }

    [Theory]
    [InlineData("TRUNCATE DATABASE d", "TRUNCATE DATABASE d")]
    [InlineData("TRUNCATE TABLE t ON CLUSTER c", "TRUNCATE TABLE t ON CLUSTER c")]
    [InlineData("TRUNCATE TABLE t PARTITION (x = 1)", "TRUNCATE TABLE t PARTITION(x = 1)")]
    [InlineData("TRUNCATE TABLE t RESTART", "TRUNCATE TABLE t RESTART")] // unsupported tail: falls back to a Command
    [InlineData("TRUNCATE TABLE t CASCADE RESTRICT", "TRUNCATE TABLE t CASCADE RESTRICT")]
    public void Truncate_branches_match_upstream_even_when_errors_are_ignored(string sql, string expected)
    {
        foreach (var read in new[] { "", "postgres" })
            Assert.Equal(expected, Assert.Single(SqlGlot.transpile(sql, read: read, write: read,
                error_level: errors.ErrorLevel.IGNORE)));
    }

    [Fact]
    public void Truncate_database_flag_is_generated()
    {
        foreach (var read in new[] { "", "postgres" })
        {
            var d = dialects.dialect.Dialect.get_or_raise(read);
            generator.Generator g = read == "postgres" ? new generators.postgres.PostgresGenerator() : new generator.Generator();
            g.dialect = d;
            var e = Assert.Single(d.parse("TRUNCATE t"))!;
            e.set("is_database", true);
            Assert.Equal("TRUNCATE DATABASE t", g.generate(e)); // oracle value
        }
    }

    private static object Order(exp.Expr? e) => e is null ? Array.Empty<object>() :
        new { type = e.GetType().Name, keys = e.args.Keys.ToArray(), children = e.iter_expressions().Select(Order).ToArray() };

    private static async Task Compare(string[] samples, string[]? reads = null)
    {
        var cases = samples.SelectMany(sql => (reads ?? ["", "postgres"]).SelectMany(read =>
            new[] { false, true }.Select(pretty => new Case(sql, read, pretty)))).ToArray();
        const string script = """
            import sys,json
            sys.path.insert(0,'python')
            import sqlglot
            from sqlglot.errors import ParseError
            def order(e):
                if e is None: return []
                return dict(type=type(e).__name__,keys=list(e.args),children=[order(x) for x in e.iter_expressions()])
            out=[]
            for c in json.load(sys.stdin):
                try:
                    es=sqlglot.parse(c['sql'],read=c['read'])
                    out.append(dict(ast=[e.dump() if e else None for e in es],order=[order(e) for e in es],
                        sql=[e.sql(dialect=c['read'],pretty=c['pretty']) if e else None for e in es],error=None))
                except ParseError as e:
                    out.append(dict(ast=None,order=None,sql=None,error=dict(message=str(e),errors=e.errors)))
            print(json.dumps(out))
            """;
        var expected = JsonNode.Parse(await Python(script, JsonSerializer.Serialize(cases)))!.AsArray();
        for (var i = 0; i < cases.Length; i++)
        {
            var c = cases[i];
            var d = dialects.dialect.Dialect.get_or_raise(c.read);
            object actual;
            try
            {
                var es = d.parse(c.sql);
                generator.Generator g = c.read == "postgres" ? new generators.postgres.PostgresGenerator() : new generator.Generator();
                g.dialect = d; g.pretty = c.pretty;
                actual = new { ast = es.Select(e => e is null ? null : serde.dump(e)).ToArray(), order = es.Select(Order).ToArray(),
                    sql = es.Select(e => e is null ? null : g.generate(e)).ToArray(), error = (object?)null };
            }
            catch (errors.ParseError e) { actual = new { ast = (object?)null, order = (object?)null, sql = (object?)null, error = new { message = e.Message, e.errors } }; }
            catch (NotPortedException e) { Assert.Fail($"Unexpected boundary {c}: {e.Message}"); throw; }
            var observed = JsonSerializer.SerializeToNode(actual);
            Assert.True(JsonNode.DeepEquals(expected[i], observed), $"{c}\nPython: {expected[i]}\nC#: {observed}");
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
        using var p = Process.Start(start)!;
        var stdout = p.StandardOutput.ReadToEndAsync(); var stderr = p.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await p.StandardInput.WriteAsync(input.AsMemory(), timeout.Token); p.StandardInput.Close(); await p.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { p.Kill(true); throw; }
        Assert.True(p.ExitCode == 0, await stderr);
        return await stdout;
    }
}
