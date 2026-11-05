using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using SqlGlotSharp.Corpus;

namespace SqlGlotSharp.Tests;

public class TimescaleReplayTests
{
    private static readonly string Artifacts = Path.Combine(RepoPaths.Scratch, "timescale-replay-agent");
    private const string Contract = "oracle_plugins.timebucket_contract.TimeBucket";

    private const string RecorderScript = """
        import sys,json
        sys.path[:0]=['python','src/tools']
        import sqlglot,extract_corpus as corpus
        from oracle_plugins.timescale import Timescale
        from sqlglot.errors import ErrorLevel
        from tests.dialects.test_dialect import Validator
        from unittest.mock import patch
        r=corpus.Recorder(); r.install()
        try:
            for pretty in (False,True):
                with patch.object(sqlglot,'pretty',pretty):
                    for read in ('timescale','postgres'):
                        for write in ('timescale','postgres'):
                            for opts in ({},{'pretty':None},{'identify':True,'normalize':True,'comments':False},
                                         {'pretty':True,'leading_comma':True,'pad':4,'indent':3}):
                                for sql in ('SELECT A, B FROM x -- comment', "SELECT E'it\\'s'", 'SELECT 1; SELECT 2'):
                                    sqlglot.transpile(sql,read=read,write=write,**opts)
                    for level in (ErrorLevel.IMMEDIATE,ErrorLevel.WARN,ErrorLevel.IGNORE):
                        try: sqlglot.transpile('SELECT a +',read='timescale',write='timescale',error_level=level)
                        except sqlglot.errors.SqlglotError: pass
                    for sql in ("SELECT time_bucket(INTERVAL '5 minutes', time)",
                                "SELECT time_bucket(INTERVAL '1 day', time, INTERVAL '1 hour')",
                                "SELECT time_bucket(INTERVAL '1 day', time, 'Europe/Berlin')",
                                'SELECT first(value, time), last(value, time)',
                                "SELECT time_bucket_gapfill(INTERVAL '1 hour', time), locf(value)"):
                        ast=sqlglot.parse_one(sql,read='timescale')
                        ast.sql('timescale',pretty=None)
                        Timescale().generator(pretty=pretty,identify=True).generate(ast,copy=False)
                        sqlglot.transpile(sql,read='timescale',write='timescale',pretty=None)
                    v=Validator(); v.dialect='timescale'
                    v.validate_identity('SELECT 1')
                    v.validate_transpile('SELECT a FROM x','SELECT\n  a\nFROM x' if pretty else 'SELECT a FROM x','postgres')
                    v.validate_all('SELECT a FROM x',read={'postgres':'SELECT a FROM x'},
                                   write={'timescale':'SELECT a FROM x','postgres':'SELECT a FROM x'})
                    # parse_one wraps multiple statements in Block; native block_sql is unported.
                    if not pretty: v.validate_transpile('SELECT 1; SELECT 2','SELECT 1; SELECT 2','timescale')
                    v.parse_one('SELECT a FROM x',error_level=ErrorLevel.IGNORE).sql('timescale')
            # Generate-boundary oracle still emits TIME_BUCKET for PostgreSQL: stale relative
            # to the native proven DATE_BIN contract. Keep those actual payloads intact.
            for sql in ("SELECT time_bucket(INTERVAL '5 minutes', CAST(time AS TIMESTAMP))",
                        "SELECT time_bucket(INTERVAL '1 day', CAST(time AS TIMESTAMPTZ), CAST('2024-01-01 00:00:00+00:00' AS TIMESTAMPTZ))",
                        "SELECT time_bucket(INTERVAL '1 day', time)",
                        "SELECT time_bucket(INTERVAL '1 day', CAST(time AS TIMESTAMP), 'UTC')",
                        "SELECT time_bucket(INTERVAL '1 day', CAST(time AS TIMESTAMP), INTERVAL '1 hour')"):
                sqlglot.parse_one(sql,read='timescale').sql('postgres')
        finally: r.uninstall()
        cases=[c for c in r.cases.values() if c['level'] in ('L2','L3')]
        # Malformed-SQL probes used to produce six nested L2 AST serialization failures (the
        # recorder now captures them, 0 skips); any remaining skips must stay ParseErrors.
        # Their actual L3 errors are still captured and replayed below.
        assert cases and sum(r.skipped.values()) <= 6,dict(r.skipped)
        assert all(k.startswith('ParseError:') for k in r.skipped),dict(r.skipped)
        from pathlib import Path
        evidence=Path(__import__('tempfile').gettempdir())/'timescale-replay-agent'
        evidence.mkdir(parents=True,exist_ok=True)
        (evidence/'recorder-skips.json').write_text(json.dumps(dict(r.skipped),indent=2))
        print(json.dumps(cases))
        """;

    [Fact]
    public async Task Fresh_recorder6_actual_TS_L2_L3_and_helpers_match_with_exact_diagnostics()
    {
        var cases = await Fresh();
        var blocks = cases.Where(c => (c.Payload.TryGetProperty("pipeline", out _) && c.Payload.GetProperty("sql").GetString() == "SELECT 1; SELECT 2")
            || (c.Level == "L2" && c.Payload.GetProperty("ast").GetRawText().Contains(".Block\"", StringComparison.Ordinal))).ToArray();
        Assert.NotEmpty(blocks);
        Assert.All(blocks, c => CorpusVerifier.Verify(c)); // block_sql is ported: these records now verify
        var parity = cases.Except(blocks).Where(c => !(c.Payload.GetProperty("write").GetString() == "postgres"
            && c.Payload.TryGetProperty("ast", out var ast) && ast.GetRawText().Contains(Contract, StringComparison.Ordinal))).ToArray();
        Assert.True(parity.Length > 80);
        Assert.Contains(parity, c => c.Level == "L2" && c.Payload.GetProperty("boundary").GetString() == "generate");
        Assert.Contains(parity, c => c.Level == "L3" && c.Payload.TryGetProperty("pipeline", out _));
        Assert.Contains(parity, c => c.Payload.GetProperty("warnings").GetArrayLength() > 0);
        Assert.Contains(parity, c => c.Payload.GetProperty("expected").TryGetProperty("error", out _));
        Assert.Contains(parity, c => c.Payload.TryGetProperty("ast", out var ast) && ast.GetRawText().Contains(Contract, StringComparison.Ordinal));
        foreach (var c in parity)
        {
            CorpusVerifier.Verify(c);
            var wrong = Copy(c); wrong["expected"] = new JsonObject { ["result"] = "invented" };
            Assert.Throws<InvalidOperationException>(() => CorpusVerifier.Verify(Item(wrong)));
            wrong = Copy(c); wrong["warnings"] = new JsonArray(new JsonObject { ["level"] = "WARNING", ["message"] = "invented" });
            Assert.Throws<InvalidOperationException>(() => CorpusVerifier.Verify(Item(wrong)));
            if (c.Payload.GetProperty("expected").TryGetProperty("error", out _))
                foreach (var field in new[] { "class", "module", "message" })
                {
                    wrong = Copy(c); wrong["expected"]!["error"]![field] = "invented";
                    Assert.Throws<InvalidOperationException>(() => CorpusVerifier.Verify(Item(wrong)));
                }
        }
        var prettyCase = parity.First(c => c.Level == "L3" && c.Payload.GetProperty("boundary").GetString() == "transpile"
            && c.Payload.GetProperty("sql").GetString() == "SELECT A, B FROM x -- comment"
            && !c.Payload.GetProperty("options").TryGetProperty("pretty", out _));
        var context = Copy(prettyCase);
        context["runtime_context"]!["sqlglot.pretty"] = !prettyCase.Payload.GetProperty("runtime_context").GetProperty("sqlglot.pretty").GetBoolean();
        Assert.Throws<InvalidOperationException>(() => CorpusVerifier.Verify(Item(context)));
        foreach (var helper in parity.Where(c => c.Payload.TryGetProperty("pipeline", out _)))
        {
            var wrong = Copy(helper); wrong["expected"] = new JsonObject { ["result"] = new JsonArray(helper.Payload.GetProperty("expected").GetProperty("result").GetString()) };
            Assert.Throws<InvalidOperationException>(() => CorpusVerifier.Verify(Item(wrong)));
        }
    }

    [Fact]
    public async Task Canonical_identity_is_explicit_and_unproved_PG_translations_are_rejected()
    {
        var cases = await Fresh();
        var bucket = cases.First(c => c.Level == "L2" && c.Payload.GetProperty("write").GetString() == "timescale"
            && c.Payload.GetProperty("ast").GetRawText().Contains(Contract, StringComparison.Ordinal));
        CorpusVerifier.Verify(bucket);
        var loaded = Assert.IsAssignableFrom<exp.Expr>(serde.load(bucket.Payload.GetProperty("ast")));
        Assert.Contains(loaded.walk(), e => e.GetType() == typeof(exp.TimeBucket));
        foreach (var identity in new[] { "TimeBucket", "custom.TimeBucket", "oracle_plugins.timescale.TimeBucket", "sqlglot.expressions.TimeBucket", "sqlglot.expressions.custom.TimeBucket" })
        {
            var wrong = Copy(bucket);
            foreach (var node in wrong["ast"]!.AsArray().OfType<JsonObject>())
                if (node["c"]?.GetValue<string>() == Contract) node["c"] = identity;
            wrong["expected"] = new JsonObject { ["result"] = "invented" };
            Assert.Throws<NotPortedException>(() => CorpusVerifier.Verify(Item(wrong)));
        }
        var pg = cases.Where(c => c.Level == "L2" && c.Payload.GetProperty("write").GetString() == "postgres"
            && c.Payload.GetProperty("ast").GetRawText().Contains(Contract, StringComparison.Ordinal)).ToArray();
        Assert.Equal(5, pg.Length);
        // Classify these fixed fixture contracts from the input AST, never expected SQL.
        var safe = pg.Where(c =>
        {
            var ast = (exp.Expr)serde.load(c.Payload.GetProperty("ast"))!;
            var bucketNode = ast.walk().OfType<exp.TimeBucket>().Single();
            if (bucketNode.@this is not exp.Cast) return false;
            return bucketNode.expressions.Count == 0 || bucketNode.expressions.Single() is exp.Cast;
        }).ToArray();
        Assert.Equal(2, safe.Length);
        foreach (var c in pg.Except(safe)) Assert.Contains("TimeBucket", Assert.Throws<NotPortedException>(() => CorpusVerifier.Verify(c)).Message);
        foreach (var c in safe)
        {
            // Actual oracle TIME_BUCKET is stale, not a replay fallback or reason to skip.
            Assert.Contains("DATE_BIN", Assert.Throws<InvalidOperationException>(() => CorpusVerifier.Verify(c)).Message);
            var p = Copy(c);
            var zoned = c.Payload.GetProperty("expected").GetProperty("result").GetString()!.Contains("TIMESTAMPTZ", StringComparison.Ordinal);
            p["expected"] = new JsonObject { ["result"] = zoned
                // PostgresGenerator.interval_sql folds the unit into the literal (verified against Python 3.11).
                ? "SELECT DATE_BIN(INTERVAL '86400000000 MICROSECONDS', CAST(time AS TIMESTAMPTZ), CAST('2024-01-01 00:00:00+00:00' AS TIMESTAMPTZ))"
                : "SELECT DATE_BIN(INTERVAL '300000000 MICROSECONDS', CAST(time AS TIMESTAMP), CAST('2000-01-03 00:00:00' AS TIMESTAMP))" };
            CorpusVerifier.Verify(Item(p)); // Test-only native contract; external recorded payload remains unchanged.
        }
        File.WriteAllText(Path.Combine(Artifacts, "stale-pg-timebucket-expectations.json"), JsonSerializer.Serialize(safe.Select(c => c.Payload)));
    }

    [Fact]
    public void Unknown_helper_contexts_classes_and_options_are_NotPorted_before_comparison()
    {
        var valid = JsonNode.Parse("""{"level":"L3","boundary":"validate_transpile","pipeline":"parse_one_then_sql","sql":"SELECT 1","read":"timescale","write":"postgres","options":{"parse":{},"generate":{}},"runtime_context":{"sqlglot.pretty":false},"expected":{"result":"SELECT 1"},"warnings":[]}""")!.AsObject();
        CorpusVerifier.Verify(Item(valid));
        foreach (var patch in new[] {
            """{"pipeline":"unknown"}""", """{"boundary":"custom_helper"}""",
            """{"runtime_context":{"unknown":false}}""", """{"parser":"custom::Parser"}""",
            """{"generator":"custom::Generator"}""", """{"generator":"oracle_plugins.timescale::TimescaleGenerator"}""",
            """{"logger_context":{}}""", """{"options":{"parse":{"into":{"$type":"sqlglot.expressions::Select"}},"generate":{}}}""",
            """{"options":{"parse":{"max_errors":1},"generate":{}}}""", """{"options":{"parse":{},"generate":{"unknown":true}}}""",
            """{"options":{"parse":{},"generate":{},"unknown":{}}}""", """{"write":"timescale,version=2"}""" })
        {
            var p = (JsonObject)valid.DeepClone();
            foreach (var entry in JsonNode.Parse(patch)!.AsObject()) p[entry.Key] = entry.Value?.DeepClone();
            p["expected"] = new JsonObject { ["result"] = "invented" };
            Assert.Throws<NotPortedException>(() => CorpusVerifier.Verify(Item(p)));
        }
        var level = (JsonObject)valid.DeepClone();
        level["options"]!["parse"]!["error_level"] = new JsonObject { ["$enum"] = "sqlglot.errors::ErrorLevel", ["value"] = "IGNORE" };
        CorpusVerifier.Verify(Item(level));
    }

    private static JsonObject Copy(CorpusCase c) => JsonNode.Parse(c.Payload.GetRawText())!.AsObject();
    private static CorpusCase Item(JsonObject p) => Item(JsonSerializer.SerializeToElement(p));
    private static CorpusCase Item(JsonElement p) => new(p.TryGetProperty("id", out var id) ? id.GetString()! : "timescale-test",
        p.GetProperty("level").GetString()!, p.GetProperty("write").GetString()!, [], p.Clone());

    private static async Task<CorpusCase[]> Fresh([CallerFilePath] string source = "")
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SQLGLOT_PYTHON") ?? "python")
        {
            WorkingDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..")),
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-B"); start.ArgumentList.Add("-c"); start.ArgumentList.Add(RecorderScript);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, await stderr);
        var json = await stdout;
        Directory.CreateDirectory(Artifacts);
        File.WriteAllText(Path.Combine(Artifacts, "fresh-recorder6-timescale-l2-l3.json"), json);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray().Select(Item).ToArray();
    }
}
