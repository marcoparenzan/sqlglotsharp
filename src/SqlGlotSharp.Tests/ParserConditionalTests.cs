using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Parser = SqlGlotSharp.parser.Parser;
using Token = SqlGlotSharp.tokenizer_core.Token;
using Postgres = SqlGlotSharp.dialects.postgres.Postgres;

namespace SqlGlotSharp.Tests;

public class ParserConditionalTests
{
    private sealed record Fixture(string sql, string method = "_parse_expression", string dialect = "",
        string level = "IMMEDIATE", int max_nodes = -1, bool consume_keyword = false);
    private static object Snapshot(Token t) => new { type = t.token_type.ToString(), t.text, t.line,
        t.col, t.start, t.end, comments = t.comments.ToArray() };

    [Fact]
    public async Task Live_Python_matches_AST_comments_errors_cursor_and_validation()
    {
        List<Fixture> cases = [];
        var queries = new[] {
            "CASE WHEN a THEN b END", "CASE a WHEN 1 THEN 'one' WHEN 2 THEN 'two' ELSE 'other' END",
            "CASE WHEN a > 1 AND b IS NOT NULL THEN ABS(a + 1) WHEN b IN (1,2) THEN (a*2) ELSE -3 END + 4",
            "CASE WHEN a THEN CASE b WHEN 1 THEN 2 ELSE 3 END ELSE CASE WHEN c THEN 4 END END",
            "CASE WHEN CASE WHEN a THEN b ELSE c END = 1 THEN mystery(2) END",
            "mystery(CASE WHEN a THEN 1 ELSE 2 END, IF(a, CASE b WHEN 1 THEN 2 END, 3))",
            "CASE /* case */ WHEN a /* condition */ THEN 1 /* result */ ELSE 2 /* default */ END /* end */ ; next",
            "/* lead */ CASE x /* operand */ WHEN 1 THEN /* then */ 2 END",
            "CASE END", "CASE a END", "CASE WHEN a 1 END", "CASE WHEN THEN 1 END",
            "CASE WHEN a THEN END", "CASE WHEN a THEN 1", "CASE WHEN a THEN 1 ELSE", "CASE",
            "CASE WHEN a THEN 1 ELSE 2 END END", "case.*", "case.a", "\"case\"",
            "CASE WHEN a THEN INTERVAL '1 day' ELSE INTERVAL '2 days' END",
            "IF(a, 1, 2)", "IF(a, 1)", "IF(a)", "IF(a,1,2,3)", "IF(a,1,)",
            "IF(a,,2)", "IF(a,1", "IF(a +,1,2)", "IF(a AS x,1 AS y,2 AS z)",
            "IF(/* open */ a /* a */,1 /* b */,2 /* c */) /* close */ ;",
            "IF(a, IF(b,1,2), CASE WHEN c THEN 3 ELSE 4 END)",
            "IF a THEN 1 ELSE 2 END", "IF a THEN 1", "IF a", "IF THEN 1",
            "CASE WHEN a THEN IF b THEN 1 ELSE 2 END ELSE 3 END"
        };
        foreach (var dialect in new[] { "", "postgres" })
            foreach (var sql in queries)
                foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
                    // Top-level unparenthesized IF is an unavailable command path.
                    if (!sql.StartsWith("IF ", StringComparison.Ordinal)) cases.Add(new(sql, dialect: dialect, level: level));
        foreach (var method in new[] { "_parse_case", "_parse_if" })
            foreach (var sql in method == "_parse_case"
                ? new[] { "CASE WHEN a THEN 1 END", "CASE a WHEN 1 THEN 2 END", "CASE.*", "CASE WHEN a THEN 1" }
                : new[] { "IF(a,1,2)", "IF(a,1", "IF a THEN 1 ELSE 2 END", "IF a", "IF THEN 1" })
                foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
                    cases.Add(new(sql, method, level: level, consume_keyword: true));
        // Start after an unrelated token to exercise rollback and optional END,
        // without entering the top-level NO_PAREN_IF_COMMANDS branch.
        foreach (var sql in new[] { "x IF a THEN 1 ELSE 2 END", "x IF a THEN 1", "x IF a", "x IF THEN 1" })
            cases.Add(new(sql, "_parse_if", consume_keyword: true));
        // Remove direct top-level command cases, which are tested as explicit boundaries below.
        cases.RemoveAll(c => c.method == "_parse_if" && c.sql.StartsWith("IF ", StringComparison.Ordinal));
        foreach (var limit in new[] { 0, 1, 2, 3, 4, 8, 20 })
            foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
                foreach (var sql in new[] { "CASE a WHEN 1 THEN 2 WHEN 3 THEN 4 ELSE 5 END", "IF(a, CASE WHEN b THEN 1 END, 2)" })
                    cases.Add(new(sql, max_nodes: limit, level: level));

        var input = cases.Select(c => new { c.sql, c.method, c.dialect, c.level, c.max_nodes, c.consume_keyword }).ToArray();
        var actual = cases.Select(c =>
        {
            var dialect = dialects.dialect.Dialect.get_or_raise(c.dialect);
            var tokens = dialect.tokenize(c.sql);
            var p = c.dialect == "" ? new Parser(error_level: Enum.Parse<errors.ErrorLevel>(c.level), max_nodes: c.max_nodes)
                : dialect.parser(Enum.Parse<errors.ErrorLevel>(c.level));
            p.max_nodes = c.max_nodes;
            p.sql = c.sql; p._tokens = tokens; p._tokens_size = tokens.Count; p._index = -1; p._advance();
            if (c.consume_keyword) p._advance(c.sql.StartsWith("x ", StringComparison.Ordinal) ? 2 : 1);
            exp.Expr? node = null; List<PyDict<string, object?>> failure = [];
            try { node = c.method switch { "_parse_case" => p._parse_case(), "_parse_if" => p._parse_if(), _ => p._parse_expression() }; }
            catch (errors.ParseError e) { failure = e.errors; }
            catch (NotPortedException e) { throw new InvalidOperationException(JsonSerializer.Serialize(c), e); }
            return new { ast = serde.dump(node), failure, errors = p.errors.Select(e => e.errors).ToArray(),
                index = p._index, curr = Snapshot(p._curr), next = Snapshot(p._next), prev = Snapshot(p._prev),
                prev_comments = p._prev_comments.ToArray(), token_comments = tokens.Select(t => t.comments.ToArray()).ToArray(),
                consumed = tokens.Take((int)p._index).Select(Snapshot).ToArray(), node_count = p._node_count };
        }).ToArray();
        const string script = """
            import sys, json
            sys.path.insert(0, 'python')
            from sqlglot.parser import Parser
            from sqlglot.dialects.dialect import Dialect
            from sqlglot.errors import ParseError, ErrorLevel
            from sqlglot.serde import dump
            def snap(t):
                return dict(type=t.token_type.name,text=t.text,line=t.line,col=t.col,start=t.start,end=t.end,comments=t.comments)
            results = []
            for c in json.load(sys.stdin):
                d = Dialect.get_or_raise(c['dialect'])
                tokens = d.tokenize(c['sql'])
                p = (Parser if not c['dialect'] else d.parser_class)(error_level=ErrorLevel[c['level']], max_nodes=c['max_nodes'], dialect=d)
                p.sql = c['sql']; p._tokens = tokens; p._tokens_size = len(tokens); p._index = -1; p._advance()
                if c['consume_keyword']: p._advance(2 if c['sql'].startswith('x ') else 1)
                node, failure = None, []
                try: node = getattr(p, c['method'])()
                except ParseError as e: failure = e.errors
                results.append(dict(ast=dump(node),failure=failure,errors=[e.errors for e in p.errors],index=p._index,
                    curr=snap(p._curr),next=snap(p._next),prev=snap(p._prev),prev_comments=p._prev_comments[:],
                    token_comments=[t.comments[:] for t in tokens],consumed=[snap(t) for t in tokens[:p._index]],node_count=p._node_count))
            print(json.dumps(results))
            """;
        Compare(await Python(script, input), actual, input);
    }

    [Fact]
    public async Task Production_Postgres_parse_matches_live_Python()
    {
        var queries = new[] { "SELECT CASE WHEN a > 1 THEN 2 ELSE 3 END AS v FROM t WHERE CASE a WHEN 1 THEN TRUE ELSE FALSE END",
            "SELECT CASE a WHEN 1 THEN 2 WHEN 3 THEN CASE WHEN b THEN 4 END ELSE 5 END FROM t",
            "SELECT IF(a, CASE WHEN b THEN ABS(1) ELSE 2 END, 3), CASE WHEN c THEN IF(d,1,2) END",
            "SELECT /* before */ CASE /* case */ WHEN a THEN 1 ELSE 2 END /* after */ AS x; SELECT 2",
            "SELECT CASE WHEN a THEN (SELECT 1) ELSE 2 END", "SELECT CASE WHEN a THEN 1 END + 2",
            "SELECT IF a THEN 1 ELSE 2 END", "SELECT IF a THEN 1", "SELECT IF a", "SELECT case.* FROM t",
            "SELECT CASE WHEN a THEN 1", "SELECT CASE WHEN a THEN END", "SELECT IF(a,1", "SELECT IF(a)" };
        var cases = queries.SelectMany(sql => new[] { "IMMEDIATE", "RAISE", "IGNORE" }.Select(level => new { sql, level })).ToArray();
        var actual = cases.Select(c =>
        {
            List<exp.Expr?>? nodes = null; List<PyDict<string, object?>> failure = []; string? message = null;
            try { nodes = new Postgres().parse(c.sql, Enum.Parse<errors.ErrorLevel>(c.level)); }
            catch (errors.ParseError e) { failure = e.errors; message = e.ToString(); }
            return new { ast = nodes?.Select(serde.dump).ToArray(), failure, message };
        }).ToArray();
        const string script = """
            import sys,json
            sys.path.insert(0,'python')
            from sqlglot.dialects.postgres import Postgres
            from sqlglot.errors import ErrorLevel,ParseError
            from sqlglot.serde import dump
            results=[]
            for c in json.load(sys.stdin):
                nodes,failure,message=None,[],None
                try: nodes=Postgres().parse(c['sql'],error_level=ErrorLevel[c['level']])
                except ParseError as e: failure,message=e.errors,str(e)
                results.append(dict(ast=None if nodes is None else [dump(n) for n in nodes],failure=failure,message=message))
            print(json.dumps(results))
            """;
        Compare(await Python(script, cases), actual, cases);
    }

    [Theory]
    [InlineData("IF a THEN 1 ELSE 2 END", "_parse_as_command")]


    public void Missing_dependencies_remain_explicit(string sql, string dependency)
    {
        foreach (var level in new[] { errors.ErrorLevel.IMMEDIATE, errors.ErrorLevel.IGNORE })
            Assert.Contains(dependency, Assert.Throws<NotPortedException>(() => new Postgres().parse(sql, level)).Message);
    }

    private static void Compare<T, C>(string json, T[] actual, C[] cases)
    {
        var expected = JsonNode.Parse(json)!.AsArray();
        var observed = JsonSerializer.SerializeToNode(actual, serde.JsonOptions)!.AsArray();
        Assert.Equal(expected.Count, observed.Count);
        for (var i = 0; i < cases.Length; i++) Assert.True(JsonNode.DeepEquals(expected[i], observed[i]),
            $"Case {i}: {JsonSerializer.Serialize(cases[i])}\nPython: {expected[i]}\nC#: {observed[i]}");
    }
    private static string Source([CallerFilePath] string source = "") => source;
    private static async Task<string> Python<T>(string script, T input)
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
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(input).AsMemory(), timeout.Token);
            process.StandardInput.Close(); await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) { process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await stderr); return await stdout;
    }
}
