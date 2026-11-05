using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Parser = SqlGlotSharp.parser.Parser;
using Token = SqlGlotSharp.tokenizer_core.Token;

namespace SqlGlotSharp.Tests;

public class ParserEntrypointTests
{
    private sealed record Case(string sql, string level = "IMMEDIATE", string method = "parse",
        int max_nodes = -1, bool boundary = false);

    private static object Snapshot(Token t) => new
    { type = t.token_type.ToString(), t.text, t.line, t.col, t.start, t.end, comments = t.comments.ToArray() };

    [Fact]
    public async Task Entrypoints_match_Python_AST_errors_cursor_chunkification_and_reset()
    {
        List<Case> cases = [];
        foreach (var sql in new[]
        {
            "", ";", ";;", ";;;", "1", "1;", ";1", "1;;2;", ";SELECT 1;;SELECT 2;",
            "1 + 2 * 3; a IS NOT NULL; b BETWEEN 1 AND 3; c IN (1,2)",
            "SELECT 1; SELECT a AS x FROM db.t WHERE a>2 ORDER BY a DESC LIMIT 3 OFFSET 1",
            "SELECT (SELECT b FROM u) AS a; (SELECT 2); (1,2)",
            "PRAGMA foo=1; PRAGMA bar", "SELECT a; END; SELECT b", "SELECT a; ELSE SELECT b; SELECT c",
            "END", "ELSE SELECT 1", "SELECT 1; END x", "SELECT 1; END; END",
            "SELECT /* s */ a /* a */ AS x /* x */; /* semi */ SELECT b; /* tail */",
            "; /* leading */ SELECT 1; /* middle */ ; /* trailing */",
            "SELECT\n a\n WHERE a > 2;\nSELECT b + 3", "SELECT ; SELECT 2", "SELECT a +; SELECT 3",
            "SELECT a WHERE; SELECT 3", "1 2; SELECT 3", "SELECT a ,; SELECT 2", "1 +; 2 +; 3",
            "SELECT a WHERE a >; SELECT b LIMIT; SELECT c", "SELECT 1 SELECT 2; SELECT 3"
        })
            foreach (var level in new[] { "IMMEDIATE", "RAISE", "WARN", "IGNORE" }) cases.Add(new(sql, level));
        foreach (var sql in new[] { "", ";", "1 + 2; 3", "1 2; 3", "1 +; 2", ";1;;2;", "1; /* comment */ 2" })
            foreach (var level in new[] { "IMMEDIATE", "RAISE", "WARN", "IGNORE" })
                cases.Add(new(sql, level, "_parse"));
        foreach (var n in new[] { 0, 1, 2, 4, 10 })
            foreach (var level in new[] { "IMMEDIATE", "RAISE", "WARN", "IGNORE" })
                cases.Add(new("SELECT a + 2; SELECT b + 3; SELECT c", level, max_nodes: n));
        await Compare(cases);
    }

    [Fact]
    public async Task Special_statements_stop_at_exact_upstream_dispatch_boundary()
    {
        var statements = new[]
        {
            "REFRESH t"
        };
        await Compare(statements.SelectMany(sql => new[]
        {
            new Case(sql, boundary: true), new Case("SELECT 1; /* dispatch */ " + sql + "; SELECT 2", boundary: true)
        }).ToList());
    }


    // Formerly NotPorted boundaries (ANALYZE, CACHE, UNCACHE, LOAD DATA, DESCRIBE, TABLESAMPLE).
    // Expected values come from the Python oracle (read="postgres").
    [Theory]
    [InlineData("ANALYZE t", "Analyze", "ANALYZE t")]
    [InlineData("ANALYZE VERBOSE TABLE a, b", "Analyze", "ANALYZE VERBOSE TABLE a, b")]
    [InlineData("ANALYZE COMPUTE STATISTICS NOSCAN", "Analyze", "ANALYZE COMPUTE STATISTICS NOSCAN")]
    [InlineData("CACHE TABLE x AS SELECT 1", "Cache", "CACHE TABLE x AS SELECT 1")]
    [InlineData("CACHE LAZY TABLE x OPTIONS('k' = 'v') AS SELECT 1", "Cache", "CACHE LAZY TABLE x OPTIONS('k' = 'v') AS SELECT 1")]
    [InlineData("UNCACHE TABLE IF EXISTS x", "Uncache", "UNCACHE TABLE IF EXISTS x")]
    [InlineData("LOAD DATA INPATH 'x' INTO TABLE y", "LoadData", "LOAD DATA INPATH 'x' INTO TABLE y")]
    [InlineData("LOAD DATA LOCAL INPATH 'x' OVERWRITE INTO TABLE y PARTITION(ds = '1')", "LoadData", "LOAD DATA LOCAL INPATH 'x' OVERWRITE INTO TABLE y PARTITION(ds = '1')")]
    [InlineData("DESCRIBE t", "Describe", "DESCRIBE t")]
    [InlineData("DESCRIBE FORMATTED a.b", "Describe", "DESCRIBE FORMATTED a.b")]
    [InlineData("SELECT * FROM t TABLESAMPLE (10 PERCENT)", "Select", "SELECT * FROM t TABLESAMPLE (10)")]
    [InlineData("SELECT * FROM t TABLESAMPLE SYSTEM (10)", "Select", "SELECT * FROM t TABLESAMPLE SYSTEM (10)")]
    public void Admin_statements_match_upstream(string sql, string node, string expected)
    {
        Assert.Equal(node, Assert.Single(SqlGlot.Parse(sql, "postgres"))!.GetType().Name);
        Assert.Equal(expected, Assert.Single(SqlGlot.Transpile(sql, "postgres")));
    }

    // Expected values come from the Python oracle (sqlglot.parse_one(sql, read="postgres").sql("postgres")).
    [Theory]
    [InlineData("SET x=1", "SET x = 1", typeof(exp.Set))]
    [InlineData("SET LOCAL variable = value", "SET LOCAL variable = value", typeof(exp.Set))]
    [InlineData("SET SESSION x TO 'y'", "SET SESSION x = 'y'", typeof(exp.Set))]
    [InlineData("SET GLOBAL x = 1, y = 2", "SET GLOBAL x = 1, y = 2", typeof(exp.Set))]
    [InlineData("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE, READ ONLY", "SET TRANSACTION ISOLATION LEVEL SERIALIZABLE, READ ONLY", typeof(exp.Set))]
    [InlineData("SET SESSION TRANSACTION READ WRITE", "SET TRANSACTION READ WRITE", typeof(exp.Set))]
    [InlineData("SET x", "SET x", typeof(exp.Command))]
    public void Set_statements_match_upstream(string sql, string expected, Type node)
    {
        var parsed = SqlGlot.parse_one(sql, "postgres");
        Assert.IsType(node, parsed);
        Assert.Equal(expected, Assert.Single(SqlGlot.Transpile(sql, "postgres")));
    }

    [Theory]
    [InlineData("COPY t FROM 'file.csv'", "COPY t FROM 'file.csv'")]
    [InlineData("COPY t FROM 'file.csv' WITH (FORMAT csv, HEADER true)", "COPY t FROM 'file.csv' WITH (FORMAT csv, HEADER TRUE)")]
    [InlineData("COPY (SELECT 1) TO 'out.csv'", "COPY (SELECT 1) TO 'out.csv'")]
    [InlineData("COPY INTO t FROM 's3://x' CREDENTIALS = (AWS_KEY_ID = 'a')", "COPY t FROM 's3://x' CREDENTIALS = (AWS_KEY_ID='a')")]
    public void Copy_statements_match_upstream(string sql, string expected)
    {
        Assert.IsType<exp.Copy>(SqlGlot.parse_one(sql, "postgres"));
        Assert.Equal(expected, Assert.Single(SqlGlot.Transpile(sql, "postgres")));
    }

    [Fact]
    public void Unsupported_query_paths_never_become_simplified_statements()
    {
        foreach (var sql in new[] {
            "SELECT 1 |> SELECT 2" })
        {
            var p = new Parser();
            // Pipe syntax is ported: it parses (compared with the Python oracle in Statement_and_clause_branches_match_upstream).
            Assert.Single(p.parse(tokens.Tokenizer.ForDialect("").tokenize(sql), sql));
            var result = p.parse(tokens.Tokenizer.ForDialect("").tokenize("SELECT 2; 3"), "SELECT 2; 3");
            Assert.Equal(2, result.Count);
            Assert.Empty(p.errors);
        }
    }

    private sealed class OverriddenParser : Parser
    {
        public int calls;
        public override exp.Expr? _parse_statement() { calls++; return base._parse_statement(); }
    }

    [Fact]
    public void Parse_uses_virtual_statement_dispatch_and_optional_SQL_resets_to_empty()
    {
        var p = new OverriddenParser();
        var result = p.parse(tokens.Tokenizer.ForDialect("").tokenize("1;2"), "1;2");
        Assert.Equal(2, p.calls);
        Assert.Equal(2, result.Count);
        p.sql = "stale"; p._pipe_cte_counter = 9;
        result = p._parse(q => q._parse_expression(), tokens.Tokenizer.ForDialect("").tokenize("3"));
        Assert.Single(result);
        Assert.Equal("", p.sql);
        Assert.Equal(0, p._pipe_cte_counter);
    }

    private static async Task Compare(List<Case> cases)
    {
        var prepared = cases.Select(c =>
        {
            var inputTokens = tokens.Tokenizer.ForDialect("").tokenize(c.sql);
            // Some statement keywords (e.g. DECLARE) are dialect-only. Exercise the
            // dispatch itself with the same prepared TokenType on both implementations.
            if (c.boundary)
                foreach (var token in inputTokens)
                    if (Enum.TryParse<tokenizer_core.TokenType>(token.text.ToUpperInvariant(), out var type)
                        && Parser.STATEMENT_PARSERS_.ContainsKey(type)) token.token_type = type;
            return inputTokens;
        }).ToArray();
        var input = cases.Select((c, i) => new { c.sql, c.level, c.method, c.max_nodes, c.boundary,
            tokens = prepared[i].Select(Snapshot).ToArray() }).ToArray();
        // Deliberately reuse one instance across successes, failures, empty input and boundaries.
        var p = new Parser();
        var actual = cases.Select((c, i) =>
        {
            p.error_level = Enum.Parse<errors.ErrorLevel>(c.level); p.max_nodes = c.max_nodes;
            p.max_errors = 2; p.error_message_context = 12;
            List<exp.Expr?>? nodes = null;
            List<PyDict<string, object?>> failure = []; string? message = null; var unavailable = false;
            try
            {
                nodes = c.method == "parse" ? p.parse(prepared[i], c.sql)
                    : p._parse(q => q._parse_expression(), prepared[i], c.sql);
            }
            catch (errors.ParseError e) { failure = e.errors; message = e.ToString(); }
            catch (NotPortedException) { unavailable = true; }
            Assert.True(c.boundary == unavailable, $"Boundary mismatch: {c.sql}; expected {c.boundary}, got {unavailable}, error {message}");
            return new
            {
                ast = nodes?.Select(serde.dump).ToArray(), failure, message, unavailable, p.sql,
                errors = p.errors.Select(e => e.errors).ToArray(), error_messages = p.errors.Select(e => e.ToString()).ToArray(),
                index = p._index, curr = Snapshot(p._curr), next = Snapshot(p._next), prev = Snapshot(p._prev),
                prev_comments = p._prev_comments.ToArray(), node_count = p._node_count, pipe_cte_counter = p._pipe_cte_counter,
                chunk_index = p._chunk_index, tokens_size = p._tokens_size,
                chunks = p._chunks.Select(chunk => chunk.Select(Snapshot).ToArray()).ToArray(),
                token_comments = prepared[i].Select(t => t.comments.ToArray()).ToArray()
            };
        }).ToArray();
        const string script = """
            import sys, json
            sys.path.insert(0, 'python')
            from sqlglot.parser import Parser
            from sqlglot.tokens import Token, TokenType
            from sqlglot.errors import ParseError, ErrorLevel
            from sqlglot.serde import dump
            class Unavailable(Exception): pass
            def fail(*a, **k): raise Unavailable()
            class Boundary(Parser):
                STATEMENT_PARSERS = {
                    k: v if k in (TokenType.PRAGMA, TokenType.SEMICOLON) else fail
                    for k, v in Parser.STATEMENT_PARSERS.items()
                }
                _parse_command = fail
                _parse_whileblock = fail
            def snapshot(t):
                return dict(type=t.token_type.name, text=t.text, line=t.line, col=t.col,
                    start=t.start, end=t.end, comments=t.comments)
            # Same reuse pattern as C#; injected boundaries only replace unported special bodies.
            p = Boundary(max_errors=2, error_message_context=12)
            results = []
            for c in json.load(sys.stdin):
                tokens = [Token(TokenType[t['type']], t['text'], line=t['line'], col=t['col'],
                    start=t['start'], end=t['end'], comments=t['comments']) for t in c['tokens']]
                p.error_level = ErrorLevel[c['level']]; p.max_nodes = c['max_nodes']
                nodes, failure, message, unavailable = None, [], None, False
                try:
                    nodes = p.parse(tokens, c['sql']) if c['method'] == 'parse' else p._parse(Parser._parse_expression, tokens, c['sql'])
                except ParseError as e: failure, message = e.errors, str(e)
                except Unavailable: unavailable = True
                results.append(dict(ast=None if nodes is None else [dump(n) for n in nodes],
                    failure=failure, message=message, unavailable=unavailable, sql=p.sql,
                    errors=[e.errors for e in p.errors], error_messages=[str(e) for e in p.errors],
                    index=p._index, curr=snapshot(p._curr), next=snapshot(p._next), prev=snapshot(p._prev),
                    prev_comments=p._prev_comments, node_count=p._node_count, pipe_cte_counter=p._pipe_cte_counter,
                    chunk_index=p._chunk_index, tokens_size=p._tokens_size,
                    chunks=[[snapshot(t) for t in chunk] for chunk in p._chunks],
                    token_comments=[t.comments for t in tokens]))
            print(json.dumps(results))
            """;
        var expected = JsonNode.Parse(await Python(script, JsonSerializer.Serialize(input)))!.AsArray();
        var observed = JsonSerializer.SerializeToNode(actual)!.AsArray();
        Assert.Equal(observed.Count, expected.Count);
        for (var i = 0; i < expected.Count; i++)
            Assert.True(JsonNode.DeepEquals(expected[i], observed[i]),
                $"Case {i}: {JsonSerializer.Serialize(input[i])}\nPython: {expected[i]}\nC#: {observed[i]}");
    }

    [Fact]
    public async Task Entrypoint_anchors_are_unique_and_honest_about_incomplete_dispatch()
    {
        const string script = """
            import sys, json
            from pathlib import Path
            sys.path.insert(0, 'src/tools')
            import port_status
            members = port_status.inventory(Path('python'), json.loads(Path('src/tools/scope.json').read_text()))
            report = port_status.report(members, port_status.scan_anchors(Path('src/SqlGlotSharp')))
            rows = [r for r in report['members'] if any(c['file'] == 'parser.entrypoints.cs' for c in r.get('csharp', []))]
            partial = ('_parse_statement', 'STATEMENT_PARSERS', 'EXPRESSION_PARSERS')
            assert len(rows) == 7, rows
            assert all(r['status'] in ('ported', 'stub') for r in rows), rows
            hashes = [(r['anchor'], r['hash']) for r in rows]
            assert all(r['status'] == 'ported' for r in rows if r['anchor'].split('.')[-1] not in partial), hashes
            for r in rows:
                assert len(r['csharp']) == 1, r
                expected = 'stub' if r['anchor'].split('.')[-1] in partial else 'ported'
                assert r['status'] == expected, (r['anchor'], r['hash'], r['status'])
            print('ok')
            """;
        Assert.Contains("ok", await Python(script));
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
