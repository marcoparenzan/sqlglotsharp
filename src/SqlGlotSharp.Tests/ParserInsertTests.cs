using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dialect = SqlGlotSharp.dialects.dialect.Dialect;
using Token = SqlGlotSharp.tokenizer_core.Token;

namespace SqlGlotSharp.Tests;

public class ParserInsertTests
{
    private sealed record Case(string sql, string dialect, string level, string mode = "statement", int max_nodes = -1);
    private static object Snapshot(Token t) => new
    { type = t.token_type.ToString(), t.text, t.line, t.col, t.start, t.end, comments = t.comments.ToArray() };

    [Fact]
    public async Task Ordinary_insert_matches_live_Python_AST_errors_comments_and_cursor()
    {
        var samples = new[]
        {
            "INSERT INTO t VALUES (1)", "INSERT INTO t (a, b) VALUES (1, 'x'), (2, 'y')",
            "INSERT INTO db.t (\"a\", b) VALUES (DEFAULT, NULL), (TRUE, FALSE)",
            "INSERT INTO t SELECT a, b FROM s WHERE a > 1 ORDER BY b LIMIT 2",
            "INSERT INTO t (a) SELECT a FROM s", "INSERT INTO t (SELECT a FROM s)",
            "INSERT INTO t AS dest (a, b) VALUES (1, 2)", "INSERT INTO t AS dest SELECT a FROM s",
            "INSERT INTO ONLY t * (a) VALUES (1)", "INSERT INTO t DEFAULT VALUES",
            "INSERT INTO t DEFAULT VALUES RETURNING *", "INSERT INTO t (a) VALUES (1) RETURNING a, a + 1 AS b",
            "INSERT INTO t SELECT a FROM s RETURNING t.a", "INSERT INTO t RETURNING a VALUES (1)",
            "INSERT INTO t VALUES (1) RETURNING a INTO x", "INSERT t VALUE (1), (2)",
            "INSERT OVERWRITE TABLE t VALUES (1)", "INSERT IGNORE INTO t VALUES (1)",
            "INSERT OR REPLACE INTO t VALUES (1)", "INSERT INTO t BY NAME SELECT a FROM s",
            "INSERT INTO t IF EXISTS VALUES (1)", "INSERT INTO t TABLE s",
            "WITH c AS (SELECT a FROM s) INSERT INTO t SELECT a FROM c",
            "INSERT INTO t WITH c AS (SELECT a FROM s) SELECT a FROM c",
            "INSERT INTO t VALUES (a + 1, CASE WHEN a THEN b ELSE c END)",
            "INSERT INTO t SELECT 1 UNION ALL SELECT 2",
            "INSERT /* insert */ INTO /* into */ db.t /* table */ (a /* a */, b) /* schema */ VALUES /* values */ (1 /* cell */, 2) /* row */ RETURNING /* returning */ a /* end */",
            "INSERT INTO t /* table */ AS /* as */ dest /* alias */ (a) VALUES (1)",
            "INSERT", "INSERT INTO", "INSERT INTO t", "INSERT INTO t VALUES",
            "INSERT INTO t VALUES (", "INSERT INTO t VALUES (1", "INSERT INTO t VALUES (1,)",
            "INSERT INTO t VALUES (,)", "INSERT INTO t VALUES (1),", "INSERT INTO t RETURNING",
            "INSERT INTO t (a", "INSERT INTO t () VALUES (1)", "INSERT INTO t (a,) VALUES (1)",
            "INSERT INTO t (a,,b) VALUES (1)", "INSERT INTO t DEFAULT VALUES RETURNING",
            "INSERT INTO t (a + 1) VALUES (1)",
            "INSERT INTO t (\"CHECK\", \"CONSTRAINT\") VALUES (1, 2)",
            "INSERT INTO t (CHECK (a > 0)) VALUES (1)",
            "INSERT INTO t (CONSTRAINT ck CHECK (a > 0)) VALUES (1)",
            "INSERT INTO \"catalog\".\"db\".\"t\" (a) VALUES (1) RETURNING a"
        };
        List<Case> cases = [];
        foreach (var dialect in new[] { "", "postgres" })
            foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
            {
                cases.AddRange(samples.Select(sql => new Case(sql, dialect, level)));
                foreach (var max in new[] { 0, 1, 2, 4, 8, 16 })
                    cases.Add(new("INSERT INTO t (a, b) VALUES (DEFAULT, 2), (3, 4) RETURNING a", dialect, level, max_nodes: max));
                cases.Add(new("INTO t (a) VALUES (1) RETURNING a", dialect, level, "insert"));
            }
        var prepared = cases.Select(c => tokens.Tokenizer.ForDialect(c.dialect).tokenize(c.sql)).ToArray();
        var input = cases.Select((c, i) => new { c.sql, c.dialect, c.level, c.mode, c.max_nodes,
            tokens = prepared[i].Select(Snapshot).ToArray() });
        var actual = cases.Select((c, i) =>
        {
            var p = Dialect.get_or_raise(c.dialect).parser();
            p.sql = c.sql; p.error_level = Enum.Parse<errors.ErrorLevel>(c.level); p.max_nodes = c.max_nodes;
            p._tokens = prepared[i]; p._tokens_size = prepared[i].Count; p._index = -1; p._advance();
            exp.Expr? node = null; List<PyDict<string, object?>> failure = [];
            try { node = c.mode == "insert" ? p._parse_insert() : p._parse_statement(); }
            catch (errors.ParseError e) { failure = e.errors; }
            catch (NotPortedException e) { Assert.Fail($"Unexpected boundary {c}: {e.Message}"); }
            return new { ast = serde.dump(node), failure, errors = p.errors.Select(e => e.errors).ToArray(),
                index = p._index, curr = Snapshot(p._curr), next = Snapshot(p._next), prev = Snapshot(p._prev),
                prev_comments = p._prev_comments.ToArray(), token_comments = prepared[i].Select(t => t.comments.ToArray()).ToArray(),
                node_count = p._node_count };
        }).ToArray();
        const string script = """
            import sys, json
            sys.path.insert(0, 'python')
            from sqlglot.dialects import Dialect
            from sqlglot.tokens import Token, TokenType
            from sqlglot.errors import ParseError, ErrorLevel
            from sqlglot.serde import dump
            def snapshot(t):
                return dict(type=t.token_type.name, text=t.text, line=t.line, col=t.col,
                    start=t.start, end=t.end, comments=t.comments)
            results = []
            for c in json.load(sys.stdin):
                tokens = [Token(TokenType[t['type']], t['text'], line=t['line'], col=t['col'],
                    start=t['start'], end=t['end'], comments=t['comments']) for t in c['tokens']]
                p = Dialect.get_or_raise(c['dialect']).parser(error_level=ErrorLevel[c['level']], max_nodes=c['max_nodes'])
                p.sql = c['sql']; p._tokens, p._tokens_size, p._index = tokens, len(tokens), -1; p._advance()
                node, failure = None, []
                try: node = p._parse_insert() if c['mode'] == 'insert' else p._parse_statement()
                except ParseError as e: failure = e.errors
                results.append(dict(ast=dump(node), failure=failure, errors=[e.errors for e in p.errors],
                    index=p._index, curr=snapshot(p._curr), next=snapshot(p._next), prev=snapshot(p._prev),
                    prev_comments=p._prev_comments[:], token_comments=[t.comments[:] for t in tokens], node_count=p._node_count))
            print(json.dumps(results))
            """;
        var expected = JsonNode.Parse(await Python(script, JsonSerializer.Serialize(input)))!.AsArray();
        var observed = JsonSerializer.SerializeToNode(actual, serde.JsonOptions)!.AsArray();
        for (var i = 0; i < cases.Count; i++)
        {
            expected[i]!["ast"] = AstTree(expected[i]!["ast"]!.AsArray());
            observed[i]!["ast"] = AstTree(observed[i]!["ast"]!.AsArray());
            Assert.True(JsonNode.DeepEquals(expected[i], observed[i]), $"Case {i}: {cases[i]}\nPython: {expected[i]}\nC#: {observed[i]}");
        }
    }

    [Theory]
    [InlineData("INSERT INTO t (a, b) VALUES (1, 'x'), (2, 'y')", "Values")]
    [InlineData("INSERT INTO t SELECT a FROM s", "Select")]
    [InlineData("INSERT INTO t DEFAULT VALUES RETURNING *", null)]
    [InlineData("WITH c AS (SELECT 1) INSERT INTO t SELECT * FROM c", "Select")]
    public async Task Actual_Postgres_parse_matches_live_Python(string sql, string? sourceType)
    {
        var insert = Assert.IsType<exp.Insert>(Assert.Single(new dialects.postgres.Postgres().parse(sql)));
        Assert.Equal(sourceType, (insert.args.get("expression") as exp.Expr)?.GetType().Name);
        var expected = JsonNode.Parse(await Python("""
            import sys, json
            sys.path.insert(0, 'python')
            from sqlglot.dialects.postgres import Postgres
            from sqlglot.serde import dump
            print(json.dumps(dump(Postgres().parse(json.load(sys.stdin))[0])))
            """, JsonSerializer.Serialize(sql)))!.AsArray();
        Assert.True(JsonNode.DeepEquals(AstTree(expected), AstTree(JsonSerializer.SerializeToNode(serde.dump(insert), serde.JsonOptions)!.AsArray())));
    }

    // Formerly NotPorted boundaries; expected values come from the Python oracle (read="postgres").
    [Theory]
    [InlineData("INSERT INTO t SET a = 1", "Insert", "INSERT INTO t (a) VALUES (1)")]
    [InlineData("INSERT ALL INTO t VALUES (1) SELECT 1", "MultitableInserts", "INSERT ALL INTO t VALUES (1) SELECT 1")]
    [InlineData("INSERT INTO t PARTITION (p = 1) VALUES (1)", "Insert", "INSERT INTO t PARTITION(p = 1) VALUES (1)")]
    [InlineData("INSERT INTO t STORED AS PARQUET SELECT 1", "Insert", "INSERT INTO t USING PARQUET SELECT 1")]
    [InlineData("SELECT a FOR UPDATE", "Select", "SELECT a FOR UPDATE")]
    [InlineData("SELECT * FROM t FOR SHARE NOWAIT", "Select", "SELECT * FROM t FOR SHARE NOWAIT")]
    [InlineData("KILL 1", "Kill", "KILL 1")]
    [InlineData("WITH RECURSIVE q AS (SELECT 1) SEARCH DEPTH FIRST BY a SET b SELECT * FROM q", "Select", "WITH RECURSIVE q AS (SELECT 1) SEARCH DEPTH FIRST BY a SET b SELECT * FROM q")]
    [InlineData("SELECT a INTO t", "Select", "SELECT a INTO t")]
    [InlineData("SELECT /*+ hint */ a", "Select", "/* + hint */ SELECT a")]
    [InlineData("SELECT 1 UNION SELECT 2 |> SELECT 3", "Select", "WITH __tmp1 AS (SELECT 3 FROM (SELECT 1 UNION SELECT 2)) SELECT * FROM __tmp1")]
    [InlineData("SELECT a |> SELECT b", "Select", "WITH __tmp1 AS (SELECT b FROM (SELECT a)) SELECT * FROM __tmp1")]
    [InlineData("SELECT * FROM t AT (TIMESTAMP => 1)", "Select", "SELECT * FROM t AT (TIMESTAMP => 1)")]
    [InlineData("SELECT * FROM t FOR SYSTEM_TIME AS OF 1", "Select", "SELECT * FROM t FOR TIMESTAMP AS OF 1")]
    [InlineData("FROM x |> WHERE a > 1 |> WHERE b < 2", "Select", "SELECT * FROM x WHERE a > 1 AND b < 2")]
    [InlineData("FROM x |> AS t |> JOIN y ON t.id = y.id", "Select", "WITH t AS (SELECT * FROM x) SELECT * FROM t JOIN y ON t.id = y.id")]
    [InlineData("FROM x |> UNION ALL (SELECT 1)", "Select", "WITH __tmp1 AS (SELECT * FROM x), __tmp2 AS (SELECT * FROM __tmp1 UNION ALL SELECT 1) SELECT * FROM __tmp2")]
    [InlineData("INSERT INTO t VALUES (1) ON DUPLICATE KEY UPDATE a = 2", "Insert", "INSERT INTO t VALUES (1) ON DUPLICATE KEY UPDATE SET a = 2")]
    [InlineData("SELECT CASE WHEN a THEN 1 ELSE INTERVAL END", "Select", "SELECT CASE WHEN a THEN 1 ELSE interval END")]
    [InlineData("SELECT FROM x", "Select", "SELECT FROM x")]
    [InlineData("SELECT DISTINCT ON (a) FROM x", "Select", "SELECT DISTINCT ON (a) FROM x")]
    public void Statement_and_clause_branches_match_upstream(string sql, string node, string expected)
    {
        Assert.Equal(node, Assert.Single(SqlGlot.Parse(sql, "postgres"))!.GetType().Name);
        Assert.Equal(expected, Assert.Single(SqlGlot.Transpile(sql, "postgres")));
    }

    [Theory]
    [InlineData("INSERT INTO t OVERRIDING SYSTEM VALUE VALUES (1)")]
    [InlineData("INSERT INTO t OVERRIDING SYSTEM VALUE VALUES (1) ON CONFLICT DO NOTHING")]
    public void Overriding_is_a_parse_error_like_upstream(string sql) =>
        Assert.Throws<errors.ParseError>(() => SqlGlot.Parse(sql, "postgres"));


    [Fact]
    public async Task Partial_insert_anchors_are_unique_and_zero_hash()
    {
        Assert.Equal("ok", (await Python("""
            import sys
            from pathlib import Path
            sys.path.insert(0, 'src/tools')
            import port_status
            anchors = port_status.scan_anchors(Path('src/SqlGlotSharp'))
            for name in ('_parse_insert', '_parse_insert_table', '_parse_schema', '_parse_field_def', '_parse_ddl_select', '_parse_on_conflict', '_parse_returning'):
                matches = anchors['sqlglot/parser.py::Parser.' + name]
                assert len(matches) == 1 and matches[0]['file'] == {'_parse_on_conflict': 'parser.conflict.cs', '_parse_insert': 'parser.clauses.cs'}.get(name, 'parser.insert.cs'), matches
                assert matches[0]['hash'] == '0000000000', matches
            print('ok')
            """)).Trim());
    }

    private static JsonNode? AstTree(JsonArray dump)
    {
        List<JsonNode?> nodes = [];
        foreach (var payload in dump)
        {
            var p = payload!.AsObject(); JsonNode? n;
            if (p.ContainsKey("c"))
            {
                var obj = new JsonObject { ["c"] = p["c"]!.DeepClone(), ["args"] = new JsonObject() };
                foreach (var key in new[] { "o", "m", "t" }) if (p.ContainsKey(key)) obj[key] = p[key]?.DeepClone();
                n = obj;
            }
            else n = p["v"]?.DeepClone();
            nodes.Add(n);
            if (p["i"] is { } index)
            {
                var args = nodes[index.GetValue<int>()]!["args"]!.AsObject(); var key = p["k"]!.GetValue<string>();
                if (p["a"]?.GetValue<bool>() == true)
                {
                    if (!args.ContainsKey(key)) args[key] = new JsonArray();
                    args[key]!.AsArray().Add(n);
                }
                else args[key] = n;
            }
        }
        return nodes.FirstOrDefault();
    }
    private static string Source([CallerFilePath] string source = "") => source;
    private static async Task<string> Python(string script, string? input = null)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SQLGLOT_PYTHON") ?? "python")
        {
            WorkingDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Source())!, "..", "..")),
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-B"); start.ArgumentList.Add("-c"); start.ArgumentList.Add(script);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            if (input is not null) await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
            process.StandardInput.Close(); await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) { process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await stderr); return await stdout;
    }
}
