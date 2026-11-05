using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dialect = SqlGlotSharp.dialects.dialect.Dialect;
using Token = SqlGlotSharp.tokenizer_core.Token;

namespace SqlGlotSharp.Tests;

public class ParserUpdateDeleteTests
{
    private sealed record Case(string sql, string dialect, string level, string mode = "statement", int max_nodes = -1);
    private static object Snapshot(Token t) => new
    { type = t.token_type.ToString(), t.text, t.line, t.col, t.start, t.end, comments = t.comments.ToArray() };

    [Fact]
    public async Task Mutations_match_live_Python_AST_errors_comments_cursor_and_argument_order()
    {
        string[] samples =
        [
            "UPDATE t SET a = 1", "UPDATE db.t AS dest SET a = a + 1, b = 'x' WHERE dest.id = 2 RETURNING *",
            "UPDATE ONLY t * SET a = DEFAULT RETURNING a, a + 1 AS b",
            "UPDATE t SET a = s.a FROM s WHERE t.id = s.id RETURNING t.a",
            "UPDATE t SET a = 1 FROM s, u WHERE t.id = s.id",
            "UPDATE t SET a = s.a FROM s JOIN u ON s.id = u.id WHERE t.id = s.id",
            "UPDATE t SET a = s.a FROM (SELECT a FROM s) AS s WHERE t.id = s.a",
            "UPDATE t SET a = s.a FROM (SELECT a FROM s) AS s JOIN u ON s.a = u.a",
            "UPDATE t SET (a, b) = (1, 2)", "UPDATE t SET a = CASE WHEN b > 0 THEN 1 ELSE 2 END",
            "UPDATE t SET a = b > 1 AND c < 2", "UPDATE t SET a = NULL, b = TRUE, c = FALSE",
            "UPDATE t SET a = 1 RETURNING a WHERE b = 2 FROM s",
            "UPDATE t WHERE b = 2 SET a = 1 RETURNING a SET a = 2",
            "UPDATE t SET a = 1 ORDER BY b DESC LIMIT 2", "UPDATE t SET a = 1 RETURNING a INTO x",
            "WITH c AS (SELECT 1 AS a) UPDATE t SET a = c.a FROM c",
            "UPDATE /* update */ t /* table */ AS /* as */ dest /* alias */ SET /* set */ a /* lhs */ = /* eq */ 1 /* rhs */ WHERE /* where */ b = 2 RETURNING /* returning */ a /* end */",
            "UPDATE", "UPDATE t", "UPDATE t SET", "UPDATE t SET a", "UPDATE t SET a =",
            "UPDATE t SET a = 1,", "UPDATE t SET a = 1 WHERE", "UPDATE t SET a = 1 RETURNING",
            "UPDATE t SET a = 1 FROM", "UPDATE t SET a = (1", "UPDATE t SET a = 1 stray",
            "DELETE FROM t", "DELETE FROM db.t AS dest WHERE dest.a > 1 RETURNING *",
            "DELETE FROM ONLY t * WHERE a IS NULL RETURNING a, a + 1 AS b",
            "DELETE FROM t USING s WHERE t.id = s.id RETURNING t.a",
            "DELETE FROM t USING s, u WHERE t.id = s.id",
            "DELETE FROM t USING s JOIN u ON s.id = u.id WHERE t.id = s.id",
            "DELETE FROM t USING (SELECT a FROM s) AS s WHERE t.a = s.a",
            "DELETE FROM t JOIN s ON t.id = s.id WHERE t.a = 1",
            "DELETE FROM t WHERE a = 1 ORDER BY b LIMIT 2", "DELETE FROM t RETURNING a INTO x",
            "WITH c AS (SELECT 1 AS a) DELETE FROM t USING c WHERE t.a = c.a",
            "DELETE /* delete */ FROM /* from */ t /* table */ USING /* using */ s /* source */ WHERE /* where */ t.a = /* eq */ s.a RETURNING /* returning */ t.a /* end */",
            "DELETE", "DELETE FROM", "DELETE FROM t WHERE", "DELETE FROM t USING",
            "DELETE FROM t RETURNING", "DELETE FROM t USING s,", "DELETE FROM t WHERE a =",
            "DELETE FROM t WHERE (a = 1", "DELETE FROM t WHERE a = 1 stray"
        ];
        List<Case> cases = [];
        foreach (var dialect in new[] { "", "postgres" })
            foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
            {
                cases.AddRange(samples.Select(sql => new Case(sql, dialect, level)));
                if (dialect == "postgres")
                    cases.AddRange(new[] { "UPDATE /*+ INDEX(t ix) */ t SET a = 1", "DELETE /*+ INDEX(t ix) */ FROM t" }
                        .Select(sql => new Case(sql, dialect, level)));
                foreach (var max in new[] { 0, 1, 2, 4, 8, 16 })
                    foreach (var sql in new[] { "UPDATE t SET a = 1, b = 2 FROM s WHERE t.id = s.id RETURNING a", "DELETE FROM t USING s WHERE t.id = s.id RETURNING a" })
                        cases.Add(new(sql, dialect, level, max_nodes: max));
                cases.Add(new("t SET a = 1 WHERE b = 2 RETURNING a", dialect, level, "update"));
                cases.Add(new("FROM t USING s WHERE t.a = s.a RETURNING t.a", dialect, level, "delete"));
                cases.Add(new("a = b > 1 AND c < 2", dialect, level, "assignment"));
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
            try { node = c.mode switch { "update" => p._parse_update(), "delete" => p._parse_delete(),
                "assignment" => p._parse_update_assignment(), _ => p._parse_statement() }; }
            catch (errors.ParseError e) { failure = e.errors; }
            catch (NotPortedException e) { Assert.Fail($"Unexpected boundary {c}: {e.Message}"); }
            return new { ast = serde.dump(node), keys = node?.args.Keys.ToArray() ?? [], failure,
                errors = p.errors.Select(e => e.errors).ToArray(), index = p._index,
                curr = Snapshot(p._curr), next = Snapshot(p._next), prev = Snapshot(p._prev),
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
                try:
                    node = {'update': p._parse_update, 'delete': p._parse_delete,
                        'assignment': p._parse_update_assignment, 'statement': p._parse_statement}[c['mode']]()
                except ParseError as e: failure = e.errors
                results.append(dict(ast=dump(node), keys=list(node.args) if node is not None else [], failure=failure,
                    errors=[e.errors for e in p.errors], index=p._index, curr=snapshot(p._curr), next=snapshot(p._next),
                    prev=snapshot(p._prev), prev_comments=p._prev_comments[:], token_comments=[t.comments[:] for t in tokens],
                    node_count=p._node_count))
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
    [InlineData("UPDATE t SET a = 1 WHERE b = 2 RETURNING a", "Update")]
    [InlineData("UPDATE t AS d SET a = s.a FROM s WHERE d.id = s.id RETURNING d.a", "Update")]
    [InlineData("DELETE FROM t WHERE a = 1 RETURNING *", "Delete")]
    [InlineData("DELETE FROM t USING s WHERE t.id = s.id RETURNING t.a", "Delete")]
    [InlineData("WITH c AS (SELECT 1 AS a) UPDATE t SET a = c.a FROM c", "Update")]
    [InlineData("WITH c AS (SELECT 1 AS a) DELETE FROM t USING c WHERE t.a = c.a", "Delete")]
    public async Task Production_Postgres_parse_matches_live_Python(string sql, string kind)
    {
        var node = Assert.Single(new dialects.postgres.Postgres().parse(sql));
        Assert.Equal(kind, node!.GetType().Name);
        var expected = JsonNode.Parse(await Python("""
            import sys, json
            sys.path.insert(0, 'python')
            from sqlglot.dialects.postgres import Postgres
            from sqlglot.serde import dump
            node = Postgres().parse(json.load(sys.stdin))[0]
            print(json.dumps(dict(ast=dump(node), keys=list(node.args))))
            """, JsonSerializer.Serialize(sql)))!;
        Assert.True(JsonNode.DeepEquals(expected["keys"], JsonSerializer.SerializeToNode(node.args.Keys.ToArray())));
        Assert.True(JsonNode.DeepEquals(AstTree(expected["ast"]!.AsArray()),
            AstTree(JsonSerializer.SerializeToNode(serde.dump(node), serde.JsonOptions)!.AsArray())));
    }

    [Theory]
    [InlineData("DELETE t FROM t JOIN s ON t.id = s.id", "DELETE t FROM t JOIN s ON t.id = s.id")]
    [InlineData("DELETE FROM t ON CLUSTER cluster WHERE a = 1", "DELETE FROM t ON CLUSTER")]
    public void Multiple_table_and_cluster_variations_match_upstream(string sql, string expected)
    {
        foreach (var dialect in new[] { "", "postgres" })
            Assert.Equal(expected, Assert.Single(SqlGlot.transpile(sql, read: dialect, write: dialect,
                error_level: errors.ErrorLevel.IGNORE)));
    }

    // Optimizer hints are ported: the base dialect keeps them (expected values from the Python oracle).
    [Theory]
    [InlineData("UPDATE /*+ INDEX(t ix) */ t SET a = 1", "UPDATE /*+ INDEX(t ix) */ t SET a = 1")]
    [InlineData("DELETE /*+ INDEX(t ix) */ FROM t", "DELETE /*+ INDEX(t ix) */ FROM t")]
    [InlineData("SELECT /*+ SOME_HINT(foo) */ 1", "SELECT /*+ SOME_HINT(foo) */ 1")]
    public void Base_optimizer_hints_match_upstream(string sql, string expected) =>
        Assert.Equal(expected, Assert.Single(SqlGlot.Transpile(sql, "")));

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
