using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dialect = SqlGlotSharp.dialects.dialect.Dialect;
using Token = SqlGlotSharp.tokenizer_core.Token;

namespace SqlGlotSharp.Tests;

public class ParserCreateTableTests
{
    private sealed record Case(string sql, string dialect, string level, int max_nodes = -1, string mode = "statement");
    private static object Snapshot(Token t) => new
    { type = t.token_type.ToString(), t.text, t.line, t.col, t.start, t.end, comments = t.comments.ToArray() };

    [Fact]
    public async Task Create_and_shared_typed_schemas_match_live_Python_state()
    {
        string[] samples = [
            "CREATE TABLE t(a INT,b TEXT)", "CREATE TABLE IF NOT EXISTS t (a INT, b TEXT)",
            "CREATE TABLE db.t (a BIGINT, b VARCHAR(20), c DECIMAL(10, 2))",
            "CREATE TABLE \"catalog\".\"db\".\"t\" (\"a\" BOOLEAN, \"b\" DATE)",
            "CREATE TABLE t (a TIMESTAMP WITH TIME ZONE, b DOUBLE PRECISION, c SMALLINT)",
            "CREATE TABLE t (a INT DEFAULT 1 NOT NULL, b TEXT NULL DEFAULT 'x')",
            "CREATE TABLE t (a INT NOT NULL DEFAULT -2 + 3, b BOOLEAN DEFAULT TRUE)",
            "CREATE TABLE t (a INT DEFAULT NULL, b TEXT DEFAULT 'x' || 'y')",
            "CREATE TABLE t (a, b)", "CREATE TABLE t", "CREATE TABLE t ()", "CREATE TABLE t (a,)",
            "CREATE TABLE t (a INT,)", "CREATE TABLE t (a INT,,b TEXT)", "CREATE TABLE t (a INT",
            "CREATE TABLE t (a INT DEFAULT)", "CREATE TABLE t (a INT NOT NULL DEFAULT)",
            "CREATE TABLE", "CREATE TABLE IF NOT EXISTS", "CREATE TABLE t (",
            "CREATE OR REPLACE TABLE t (a INT)", "CREATE OR ALTER TABLE t (a INT)",
            "CREATE UNIQUE TABLE t (a INT)",
            "CREATE TABLE t AS SELECT 1", "CREATE TABLE t (a INT) AS SELECT a FROM s",
            "CREATE TABLE t AS s",
            "CREATE /* create */ TABLE /* table */ db.t /* name */ (a /* a */ INT /* type */ DEFAULT /* default */ 1 /* value */ NOT NULL /* null */, b TEXT) /* schema */",
            "INSERT INTO t (a INT, b TEXT) VALUES (1, 'x')",
            "INSERT INTO t (a DEFAULT 1) VALUES (1)",
            "INSERT INTO t (a INT NOT NULL DEFAULT 2) SELECT 1",
            "WITH q(a INT) AS (SELECT 1) SELECT * FROM q",
            "WITH q(a DEFAULT 1) AS (SELECT 1) SELECT * FROM q"
        ];
        List<Case> cases = [];
        foreach (var dialect in new[] { "", "postgres" })
            foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
            {
                cases.AddRange(samples.Select(s => new Case(s, dialect, level)));
                foreach (var max in new[] { 0, 1, 2, 4, 8, 16, 32 })
                    cases.Add(new("CREATE TABLE t (a INT DEFAULT 1 NOT NULL, b TEXT)", dialect, level, max));
                cases.Add(new("(a INT, b TEXT DEFAULT 'x' NOT NULL)", dialect, level, mode: "schema"));
                cases.Add(new("(SELECT 1)", dialect, level, mode: "schema"));
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
            try { node = c.mode == "schema" ? p._parse_schema() : p._parse_statement(); }
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
                try: node = p._parse_schema() if c['mode'] == 'schema' else p._parse_statement()
                except ParseError as e: failure = e.errors
                results.append(dict(ast=dump(node), failure=failure, errors=[e.errors for e in p.errors],
                    index=p._index, curr=snapshot(p._curr), next=snapshot(p._next), prev=snapshot(p._prev),
                    prev_comments=p._prev_comments[:], token_comments=[t.comments[:] for t in tokens], node_count=p._node_count))
            print(json.dumps(results))
            """;
        var expected = JsonNode.Parse(await Python(script, JsonSerializer.Serialize(input)))!.AsArray();
        var observed = JsonSerializer.SerializeToNode(actual, serde.JsonOptions)!.AsArray();
        for (var i = 0; i < cases.Count; i++)
            Assert.True(JsonNode.DeepEquals(expected[i], observed[i]), $"Case {i}: {cases[i]}\nPython: {expected[i]}\nC#: {observed[i]}");
    }

    [Theory]
    [InlineData("CREATE TABLE t(a INT,b TEXT)")]
    [InlineData("CREATE TABLE IF NOT EXISTS db.t (a BIGINT, b VARCHAR(20))")]
    [InlineData("CREATE TABLE t (a INT DEFAULT 1 NOT NULL, b TEXT NULL)")]
    [InlineData("CREATE TABLE t (a INT) AS SELECT 1")]
    public async Task Public_Postgres_parse_matches_live_Python(string sql)
    {
        var node = Assert.IsType<exp.Create>(Assert.Single(new dialects.postgres.Postgres().parse(sql)));
        Assert.Equal(new[] { "this", "kind", "replace", "refresh", "unique", "expression", "exists", "properties",
            "indexes", "no_schema_binding", "begin", "clone", "concurrently", "clustered" }, node.args.Keys);
        var expected = JsonNode.Parse(await Python("""
            import sys, json
            sys.path.insert(0, 'python')
            from sqlglot.dialects.postgres import Postgres
            from sqlglot.serde import dump
            print(json.dumps(dump(Postgres().parse(json.load(sys.stdin))[0])))
            """, JsonSerializer.Serialize(sql)));
        Assert.True(JsonNode.DeepEquals(expected, JsonSerializer.SerializeToNode(serde.dump(node), serde.JsonOptions)),
            $"Python: {expected}\nC#: {JsonSerializer.Serialize(serde.dump(node), serde.JsonOptions)}");
    }

    private static string Source([CallerFilePath] string source = "") => source;
    private static async Task<string> Python(string script, string input)
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
            await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
            process.StandardInput.Close(); await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) { process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await stderr); return await stdout;
    }
}
