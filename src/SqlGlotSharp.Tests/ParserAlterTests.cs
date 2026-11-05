using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dialect = SqlGlotSharp.dialects.dialect.Dialect;
using Token = SqlGlotSharp.tokenizer_core.Token;

namespace SqlGlotSharp.Tests;

public class ParserAlterTests
{
    private sealed record Case(string sql, string dialect, string level, int max_nodes = -1, bool public_parse = false);
    private static object Snapshot(Token t) => new
    { type = t.token_type.ToString(), t.text, t.line, t.col, t.start, t.end, comments = t.comments.ToArray() };

    private static readonly string[] Samples = [
        "ALTER TABLE t ADD COLUMN a INT", "ALTER TABLE t ADD a TEXT DEFAULT 'x' NOT NULL",
        "ALTER TABLE IF EXISTS ONLY db.t ADD COLUMN IF NOT EXISTS a DECIMAL(10, 2)",
        "ALTER TABLE t ADD a INT, ADD b TEXT", "ALTER TABLE t ADD COLUMN a TIMESTAMPTZ, ADD COLUMN b INT[]",
        "ALTER TABLE t DROP COLUMN a", "ALTER TABLE t DROP a", "ALTER TABLE t DROP IF EXISTS a CASCADE",
        "ALTER TABLE t DROP COLUMN IF EXISTS a RESTRICT, DROP COLUMN b",
        "ALTER TABLE t DROP COLUMN any", "ALTER TABLE t DROP COLUMN charset",
        "ALTER TABLE t ALTER COLUMN a TYPE BIGINT", "ALTER TABLE t ALTER a SET DATA TYPE VARCHAR(30)",
        "ALTER TABLE t ALTER COLUMN a TYPE TEXT COLLATE \"C\" USING a::TEXT",
        "ALTER TABLE t ALTER COLUMN IF EXISTS a TYPE TIMESTAMP WITH TIME ZONE",
        "ALTER TABLE t ALTER a TYPE INT[] USING a::INT[]",
        "ALTER TABLE t ALTER COLUMN a SET DEFAULT 1 + 2", "ALTER TABLE t ALTER a SET DEFAULT NULL",
        "ALTER TABLE t ALTER COLUMN a SET DEFAULT TRUE OR FALSE", "ALTER TABLE t ALTER a DROP DEFAULT",
        "ALTER TABLE t ALTER COLUMN a SET NOT NULL", "ALTER TABLE t ALTER a DROP NOT NULL",
        "ALTER TABLE t ALTER COLUMN IF EXISTS a SET NOT NULL",
        "ALTER TABLE t RENAME COLUMN a TO b", "ALTER TABLE db.t RENAME COLUMN IF EXISTS \"a\" TO \"b\"",
        "ALTER TABLE t RENAME TO u", "ALTER TABLE t RENAME TO db.u",
        "ALTER TABLE t SET (fillfactor=70)", "ALTER TABLE t SET (autovacuum_enabled=false, fillfactor=80)",
        "ALTER TABLE t SET (timescaledb.compress=true, timescaledb.compress_segmentby='device_id', timescaledb.compress_orderby='ts DESC')",
        "ALTER TABLE t SET (tsdb.hypertable=true, tsdb.partition_column='ts')",
        "ALTER TABLE t ADD COLUMN a INT, ALTER COLUMN a SET DEFAULT 2, DROP COLUMN b",
        "ALTER TABLE t ADD CONSTRAINT pk PRIMARY KEY (a)", "ALTER TABLE t ADD CONSTRAINT ck CHECK (a > 0)",
        "ALTER TABLE t ADD a INT DEFAULT 1 CHECK (a > 0)",
        "ALTER /* alter */ TABLE /* table */ db.t /* target */ ADD /* add */ COLUMN a /* name */ INT /* type */ DEFAULT /* default */ 1 /* value */ NOT NULL /* null */",
        "ALTER TABLE t DROP /* drop */ COLUMN /* col */ a /* name */ CASCADE /* cascade */",
        "ALTER TABLE t ALTER /* alter */ COLUMN a /* col */ TYPE /* type */ TEXT /* dtype */ USING /* using */ a /* value */::TEXT",
        "ALTER TABLE t RENAME /* rename */ COLUMN a /* old */ TO /* to */ b /* new */",
        "ALTER TABLE t SET /* set */ (timescaledb.compress /* key */ = /* eq */ true /* value */, fillfactor=80) /* end */",
        "ALTER TABLE t ADD COLUMN a INT; ALTER TABLE t DROP COLUMN a",
        "ALTER TABLE t ADD COLUMN a INT DEFAULT", "ALTER TABLE t ALTER COLUMN a SET DEFAULT",
        "ALTER TABLE t SET (fillfactor=70", "ALTER TABLE t SET (fillfactor=)", "ALTER TABLE t SET ()"
    ];

    [Fact]
    public async Task Postgres_and_Timescale_match_live_AST_errors_comments_and_cursor()
    {
        var cases = new List<Case>();
        foreach (var d in new[] { "postgres", "timescale" })
            foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
            {
                cases.AddRange(Samples.Where(s => !s.Contains(';')).Select(s => new Case(s, d, level)));
                foreach (var max in new[] { 0, 1, 2, 4, 8, 16, 32 })
                    cases.Add(new("ALTER TABLE t ADD a INT DEFAULT 1 NOT NULL, ALTER a TYPE BIGINT USING a::BIGINT", d, level, max));
                cases.AddRange(Samples.Select(s => new Case(s, d, level, public_parse: true)));
            }
        var prepared = cases.Select(c => tokens.Tokenizer.ForDialect(c.dialect).tokenize(c.sql)).ToArray();
        var input = cases.Select((c, i) => new { c.sql, c.dialect, c.level, c.max_nodes, c.public_parse,
            tokens = prepared[i].Select(Snapshot).ToArray() });
        var expected = JsonNode.Parse(await Python("""
            import sys,json
            sys.path.insert(0,'python'); sys.path.insert(0,'src/tools')
            from oracle_plugins.timescale import Timescale
            from sqlglot.dialects import Dialect
            from sqlglot.tokens import Token,TokenType
            from sqlglot.errors import ParseError,ErrorLevel
            from sqlglot.serde import dump
            def snap(t):
                return dict(type=t.token_type.name,text=t.text,line=t.line,col=t.col,start=t.start,end=t.end,comments=t.comments)
            results=[]
            for c in json.load(sys.stdin):
                tokens=[Token(TokenType[t['type']],t['text'],line=t['line'],col=t['col'],start=t['start'],end=t['end'],comments=t['comments']) for t in c['tokens']]
                p=Dialect.get_or_raise(c['dialect']).parser(error_level=ErrorLevel[c['level']],max_nodes=c['max_nodes'])
                ast=None; failure=[]
                try:
                    if c['public_parse']: ast=[dump(e) for e in p.parse(tokens,c['sql'])]
                    else:
                        p.sql=c['sql']; p._tokens,p._tokens_size,p._index=tokens,len(tokens),-1; p._advance()
                        ast=dump(p._parse_statement())
                except ParseError as e: failure=e.errors
                results.append(dict(ast=ast,failure=failure,errors=[e.errors for e in p.errors],index=p._index,
                    curr=snap(p._curr),next=snap(p._next),prev=snap(p._prev),prev_comments=p._prev_comments[:],
                    token_comments=[t.comments[:] for t in tokens],node_count=p._node_count))
            print(json.dumps(results))
            """, JsonSerializer.Serialize(input)))!.AsArray();
        for (var i = 0; i < cases.Count; i++)
        {
            var c = cases[i];
            var p = Dialect.get_or_raise(c.dialect).parser();
            p.error_level = Enum.Parse<errors.ErrorLevel>(c.level); p.max_nodes = c.max_nodes;
            object? ast = null; List<PyDict<string, object?>> failure = [];
            try
            {
                if (c.public_parse) ast = p.parse(prepared[i], c.sql).Select(serde.dump).ToArray();
                else
                {
                    p.sql = c.sql; p._tokens = prepared[i]; p._tokens_size = prepared[i].Count;
                    p._index = -1; p._advance(); ast = serde.dump(p._parse_statement());
                }
            }
            catch (errors.ParseError e) { failure = e.errors; }
            catch (NotPortedException e)
            {
                // Upstream speculative action parsing may hit Command even for node
                // limits or malformed input. That fallback is explicitly unavailable.
                if (expected[i]!["ast"]?.ToJsonString().Contains("sqlglot.expressions.ddl.Command", StringComparison.Ordinal) == true)
                {
                    Assert.Contains("_parse_as_command", e.Message);
                    continue;
                }
                Assert.Fail($"Unexpected boundary {i}: {c}: {e.Message}\nPython: {expected[i]}");
            }
            var actual = JsonSerializer.SerializeToNode(new { ast, failure, errors = p.errors.Select(e => e.errors).ToArray(),
                index = p._index, curr = Snapshot(p._curr), next = Snapshot(p._next), prev = Snapshot(p._prev),
                prev_comments = p._prev_comments.ToArray(), token_comments = prepared[i].Select(t => t.comments.ToArray()).ToArray(),
                node_count = p._node_count }, serde.JsonOptions);
            Assert.True(JsonNode.DeepEquals(expected[i], actual), $"Case {i}: {c}\nPython: {expected[i]}\nC#: {actual}");
        }
    }

    [Theory]
    [InlineData("ALTER TABLE t ADD a INT", "ColumnDef")]
    [InlineData("ALTER TABLE t DROP COLUMN a", "Drop")]
    [InlineData("ALTER TABLE t ALTER a TYPE BIGINT", "AlterColumn")]
    [InlineData("ALTER TABLE t RENAME COLUMN a TO b", "RenameColumn")]
    [InlineData("ALTER TABLE t RENAME TO u", "AlterRename")]
    [InlineData("ALTER TABLE t SET (timescaledb.compress=true)", "AlterSet")]
    public void Ordinary_ALTER_is_routed_in_production(string sql, string action)
    {
        foreach (var d in new[] { "postgres", "timescale" })
        {
            var node = Assert.IsType<exp.Alter>(Assert.Single(Dialect.get_or_raise(d).parse(sql)));
            Assert.Equal(new[] { "this", "kind", "exists", "actions", "only", "options", "cluster", "not_valid", "check", "cascade", "iceberg" }, node.args.Keys);
            Assert.Equal(action, Assert.IsAssignableFrom<exp.Expr>(Assert.Single((List<object?>)node.args["actions"]!)).GetType().Name);
        }
    }

    // Formerly explicit NotPorted boundaries. The full _parse_alter now follows upstream: an Alter node
    // or a Command fallback. Expected values come from the Python oracle (read="postgres").
    [Theory]
    [InlineData("ALTER TABLE t DROP PARTITION (a=1)", "Alter", "ALTER TABLE t DROP PARTITION(a = 1)")]
    [InlineData("ALTER TABLE t ADD a INT, RESET (fillfactor)", "Command", "ALTER TABLE t ADD a INT, RESET (fillfactor)")]
    [InlineData("ALTER TABLE t ENABLE TRIGGER ALL", "Command", "ALTER TABLE t ENABLE TRIGGER ALL")]
    [InlineData("ALTER TABLE t ATTACH PARTITION u FOR VALUES IN (1)", "Command", "ALTER TABLE t ATTACH PARTITION u FOR VALUES IN (1)")]
    [InlineData("ALTER INDEX i RENAME TO j", "Alter", "ALTER INDEX i RENAME TO j")]
    [InlineData("ALTER TABLE t SWAP WITH u", "Alter", "ALTER TABLE t SWAP WITH u")]
    [InlineData("ALTER TABLE t AS SELECT 1", "Alter", "ALTER TABLE t AS SELECT 1")]
    [InlineData("ALTER VIEW t RENAME TO u", "Alter", "ALTER VIEW t RENAME TO u")]
    [InlineData("ALTER TABLE t ALTER DISTKEY a", "Alter", "ALTER TABLE t ALTER DISTSTYLE KEY DISTKEY a")]
    [InlineData("ALTER TABLE t RESET (timescaledb.compress)", "Command", "ALTER TABLE t RESET (timescaledb.compress)")]
    [InlineData("ALTER TABLE t", "Command", "ALTER TABLE t")]
    [InlineData("ALTER TABLE t ALTER a SET STATISTICS 100", "Command", "ALTER TABLE t ALTER a SET STATISTICS 100")]
    [InlineData("ALTER TABLE t ALTER a SET STORAGE EXTENDED", "Command", "ALTER TABLE t ALTER a SET STORAGE EXTENDED")]
    [InlineData("ALTER ICEBERG TABLE t ADD a INT", "Alter", "ALTER ICEBERG TABLE t ADD COLUMN a INT")]
    [InlineData("ALTER TABLE t ADD a INT, ENABLE TRIGGER ALL", "Command", "ALTER TABLE t ADD a INT, ENABLE TRIGGER ALL")]
    [InlineData("ALTER TABLE t ADD PARTITION (a=1)", "Alter", "ALTER TABLE t ADD PARTITION(a = 1)")]
    [InlineData("ALTER TABLE t OWNER TO u", "Command", "ALTER TABLE t OWNER TO u")]
    [InlineData("ALTER TABLE t ALTER a COMMENT 'x'", "Alter", "ALTER TABLE t ALTER COLUMN a COMMENT 'x'")]
    [InlineData("ALTER TABLE t ADD a INT nonsense", "Command", "ALTER TABLE t ADD a INT nonsense")]
    [InlineData("ALTER TABLE t SET TABLESPACE s", "Alter", "ALTER TABLE t SET TABLESPACE s")]
    [InlineData("ALTER SESSION SET x=1", "Command", "ALTER SESSION SET x=1")]
    public void Alter_matches_upstream_including_command_fallback(string sql, string node, string expected)
    {
        foreach (var level in new[] { errors.ErrorLevel.IMMEDIATE, errors.ErrorLevel.RAISE, errors.ErrorLevel.IGNORE })
        {
            var parsed = Assert.Single(Dialect.get_or_raise("postgres").parse(sql, level))!;
            Assert.Equal(node, parsed.GetType().Name);
        }
        Assert.Equal(expected, Assert.Single(SqlGlot.Transpile(sql, "postgres")));
    }

    [Fact]
    public async Task RESET_is_an_upstream_Command_not_an_invented_reloptions_AST()
    {
        var expected = JsonNode.Parse(await Python("""
            import sys,json
            sys.path.insert(0,'python'); sys.path.insert(0,'src/tools')
            from oracle_plugins.timescale import Timescale
            from sqlglot.dialects import Dialect
            json.load(sys.stdin)  # Keep stdin open until the host has flushed its payload.
            print(json.dumps([type(Dialect.get_or_raise(d).parse('ALTER TABLE t RESET (timescaledb.compress)')[0]).__name__ for d in ['postgres','timescale']]))
            """, "null"));
        Assert.Equal("[\"Command\",\"Command\"]", expected!.ToJsonString());
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
        Assert.True(process.ExitCode == 0, await stderr); return await stdout;
    }
}
