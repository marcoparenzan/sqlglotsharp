using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Parser = SqlGlotSharp.parser.Parser;
using Dialect = SqlGlotSharp.dialects.dialect.Dialect;
using Token = SqlGlotSharp.tokenizer_core.Token;

namespace SqlGlotSharp.Tests;

public class ParserGroupTests
{
    private sealed record Case(string sql, string dialect = "", string level = "IMMEDIATE",
        string mode = "group", int max_nodes = -1);

    private static Parser Start(Case c, List<Token> input)
    {
        var p = Dialect.get_or_raise(c.dialect).parser();
        p.error_level = Enum.Parse<errors.ErrorLevel>(c.level); p.max_nodes = c.max_nodes;
        p.sql = c.sql; p._tokens = input; p._tokens_size = input.Count; p._index = -1; p._advance();
        return p;
    }
    private static object Snapshot(Token t) => new
    { type = t.token_type.ToString(), t.text, t.line, t.col, t.start, t.end, comments = t.comments.ToArray() };
    private static exp.Expr? Invoke(Parser p, string mode) => mode switch
    {
        "group" => p._parse_group(), "group_skip" => p._parse_group(skip_group_by_token: true),
        "having" => p._parse_having(), "having_skip" => p._parse_having(skip_having_token: true),
        "cube" => p._parse_cube_or_rollup(), "cube_prefix" => p._parse_cube_or_rollup(with_prefix: true),
        "sets" => p._parse_grouping_sets(), "set" => p._parse_grouping_set(),
        "select" => p._parse_select(), "statement" => p._parse_statement(),
        _ => throw new ArgumentException(mode)
    };

    [Fact]
    public async Task Grouping_and_having_match_live_Python_AST_comments_errors_and_cursor()
    {
        var samples = new List<(string sql, string mode)>();
        foreach (var sql in new[]
        {
            "", "x", "HAVING TRUE", "GROUP BY a", "GROUP BY 1, 2", "GROUP BY ALL",
            "GROUP BY DISTINCT a, b", "GROUP BY ALL a + 1, b * 2", "GROUP BY ()",
            "GROUP BY (a)", "GROUP BY (a, b), c", "GROUP BY a AND b OR c",
            "GROUP BY a,", "GROUP BY", "GROUP BY ALL HAVING TRUE", "GROUP BY DISTINCT LIMIT 1",
            "GROUP BY LIMIT 1", "GROUP BY OFFSET 2", "GROUP BY WINDOW w AS (a)",
            "GROUP BY a LIMIT 1", "GROUP BY a HAVING a > 0", "GROUP BY a ORDER BY a",
            "GROUP BY CUBE(a, b)", "GROUP BY ROLLUP(a + 1, (b, c))", "GROUP BY CUBE()",
            "GROUP BY ROLLUP(a), CUBE(b), c", "GROUP BY GROUPING SETS (a, (b, c), ())",
            "GROUP BY GROUPING SETS (ROLLUP(a, b), CUBE(c), GROUPING SETS ((), (a)))",
            "GROUP BY a, GROUPING SETS ((b, c), ()), ROLLUP(d)",
            "GROUP BY a, b GROUPING SETS ((a), ()) HAVING TRUE",
            "GROUP BY a WITH ROLLUP", "GROUP BY a WITH CUBE", "GROUP BY a WITH ROLLUP WITH CUBE",
            "GROUP BY a WITH ROLLUP, b", "GROUP BY a WITH", "GROUP BY a WITH foo",
            "GROUP BY a WITH TOTALS", "GROUP BY a TOTALS", "GROUP BY a WITH ROLLUP WITH TOTALS",
            "GROUP BY /* group */ a /* column */, b -- last\n HAVING TRUE",
            "GROUP BY CUBE /* cube */ (a /* arg */, b)", "GROUP BY GROUPING SETS /* sets */ ((), (a))",
            "GROUP BY ROLLUP a", "GROUP BY CUBE(a", "GROUP BY GROUPING SETS a",
            "GROUP BY GROUPING SETS ((a, b)", "GROUP BY CUBE(a,)", "GROUP BY GROUPING SETS ()",
            "GROUP BY CUBE(a = 1)", "GROUP BY \"LIMIT\", \"window\""
        }) samples.Add((sql, "group"));
        foreach (var sql in new[]
        {
            "", "x", "GROUP BY a", "HAVING TRUE", "HAVING a > 1 AND b < 3 OR c IS NULL",
            "HAVING a BETWEEN 1 AND 3", "HAVING a IN (1, 2)", "HAVING NOT (a = 1)",
            "HAVING MIN(a) > 1", "HAVING SUM(a) > 1", "HAVING a > 1 ORDER BY a",
            "HAVING /* having */ a /* lhs */ > /* gt */ 1", "HAVING", "HAVING a >",
            "HAVING (a > 1", "HAVING a > 0 AND", "HAVING a IN (SELECT a FROM t GROUP BY a)"
        }) samples.Add((sql, "having"));
        foreach (var sql in new[] { "a, b", "ALL a", "DISTINCT a", "LIMIT 1", "", "CUBE(a)", "a WITH ROLLUP" })
            samples.Add((sql, "group_skip"));
        foreach (var sql in new[] { "a > 0", "", "a AND", "SUM(a) > 1" }) samples.Add((sql, "having_skip"));
        foreach (var sql in new[] { "CUBE(a, b)", "ROLLUP()", "ROLLUP((a, b), c)", "CUBE(a", "CUBE a", "x" })
            samples.Add((sql, "cube"));
        foreach (var sql in new[] { "CUBE", "ROLLUP", "CUBE(a)", "x" }) samples.Add((sql, "cube_prefix"));
        foreach (var sql in new[] { "GROUPING SETS ((a, b), ())", "GROUPING SETS ()", "GROUPING SETS a", "x" })
            samples.Add((sql, "sets"));
        foreach (var sql in new[] { "()", "(a, b)", "a + 1", "ROLLUP(a)", "GROUPING SETS (())", "a = 1", "" })
            samples.Add((sql, "set"));

        List<Case> cases = [];
        foreach (var dialect in new[] { "", "postgres" })
            foreach (var (sql, mode) in samples)
                foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
                    cases.Add(new(sql, dialect, level, mode));
        foreach (var max in new[] { 0, 1, 2, 3, 5, 8 })
            foreach (var mode in new[] { "group", "having" })
                foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
                    cases.Add(new(mode == "group" ? "GROUP BY GROUPING SETS ((a, b), ())" : "HAVING a > 1 AND b < 2",
                        level: level, mode: mode, max_nodes: max));
        await Compare(cases);
    }

    [Fact]
    public async Task Select_and_subquery_dispatch_match_live_Python_for_base_and_Postgres()
    {
        List<Case> cases = [];
        foreach (var dialect in new[] { "", "postgres" })
            foreach (var sql in new[]
            {
                "SELECT a FROM t GROUP BY a", "SELECT a FROM t HAVING a > 0",
                "SELECT a, SUM(a) FROM t WHERE a > 0 GROUP BY a HAVING SUM(a) > 1 ORDER BY a LIMIT 2 OFFSET 1",
                "SELECT a FROM t GROUP BY ALL a", "SELECT a GROUP BY DISTINCT a",
                "SELECT a FROM t GROUP BY CUBE(a, b), ROLLUP(c)",
                "SELECT a GROUP BY GROUPING SETS ((a, b), (), CUBE(c)) HAVING TRUE",
                "SELECT a GROUP BY a WITH ROLLUP WITH CUBE HAVING a > 1",
                "SELECT a GROUP BY a, b GROUPING SETS ((a), ()) ORDER BY a",
                "SELECT a GROUP BY a WITH TOTALS", "SELECT a GROUP BY ALL LIMIT 1",
                "SELECT /* select */ a GROUP BY /* group */ a HAVING /* having */ a > 1",
                "SELECT a GROUP BY a UNION SELECT b GROUP BY b HAVING b > 0 ORDER BY 1",
                "SELECT a FROM (SELECT a FROM t GROUP BY a HAVING a > 0) q",
                "SELECT (SELECT a FROM t GROUP BY a) AS x", "(SELECT a GROUP BY a) UNION SELECT b HAVING b > 0",
                "SELECT a GROUP BY", "SELECT a HAVING", "SELECT a GROUP BY CUBE(a", "SELECT a HAVING a >",
                "SELECT a GROUP BY a GROUP BY b", "SELECT a HAVING TRUE HAVING FALSE"
            })
                foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
                    cases.Add(new(sql, dialect, level, sql.StartsWith('(') ? "statement" : "select"));
        await Compare(cases);
    }

    private static async Task Compare(List<Case> cases)
    {
        var prepared = cases.Select(c => tokens.Tokenizer.ForDialect(c.dialect).tokenize(c.sql)).ToArray();
        var input = cases.Select((c, i) => new { c.sql, c.dialect, c.level, c.mode, c.max_nodes,
            tokens = prepared[i].Select(Snapshot).ToArray() }).ToArray();
        var actual = cases.Select((c, i) =>
        {
            var p = Start(c, prepared[i]); exp.Expr? node = null;
            List<PyDict<string, object?>> failure = [];
            try { node = Invoke(p, c.mode); }
            catch (errors.ParseError e) { failure = e.errors; }
            catch (NotPortedException e) { Assert.Fail($"Unexpected boundary: {c}: {e.Message}"); }
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
                try:
                    mode = c['mode']
                    if mode == 'group': node = p._parse_group()
                    elif mode == 'group_skip': node = p._parse_group(skip_group_by_token=True)
                    elif mode == 'having': node = p._parse_having()
                    elif mode == 'having_skip': node = p._parse_having(skip_having_token=True)
                    elif mode == 'cube': node = p._parse_cube_or_rollup()
                    elif mode == 'cube_prefix': node = p._parse_cube_or_rollup(with_prefix=True)
                    elif mode == 'sets': node = p._parse_grouping_sets()
                    elif mode == 'set': node = p._parse_grouping_set()
                    elif mode == 'select': node = p._parse_select()
                    elif mode == 'statement': node = p._parse_statement()
                except ParseError as e: failure = e.errors
                results.append(dict(ast=dump(node), failure=failure, errors=[e.errors for e in p.errors],
                    index=p._index, curr=snapshot(p._curr), next=snapshot(p._next), prev=snapshot(p._prev),
                    prev_comments=p._prev_comments[:], token_comments=[t.comments[:] for t in tokens], node_count=p._node_count))
            print(json.dumps(results))
            """;
        var expected = JsonNode.Parse(await Python(script, JsonSerializer.Serialize(input)))!.AsArray();
        var observed = JsonSerializer.SerializeToNode(actual, serde.JsonOptions)!.AsArray();
        Assert.Equal(cases.Count, expected.Count);
        for (var i = 0; i < cases.Count; i++)
        {
            // defaultdict argument insertion and set-operation modifier order are not semantic.
            // Preserve all AST values, comments and metadata while comparing trees instead of dump indices.
            expected[i]!["ast"] = AstTree(expected[i]!["ast"]!.AsArray());
            observed[i]!["ast"] = AstTree(observed[i]!["ast"]!.AsArray());
            Assert.True(JsonNode.DeepEquals(expected[i], observed[i]),
                $"Case {i}: {cases[i]}\nPython: {expected[i]}\nC#: {observed[i]}");
        }
    }

    private static JsonNode? AstTree(JsonArray dump)
    {
        var nodes = new List<JsonNode?>();
        foreach (var payload in dump)
        {
            var p = payload!.AsObject();
            JsonNode? n;
            if (p.ContainsKey("c"))
            {
                var obj = new JsonObject { ["c"] = p["c"]!.DeepClone(), ["args"] = new JsonObject() };
                foreach (var key in new[] { "o", "m", "t" })
                    if (p.ContainsKey(key)) obj[key] = p[key]?.DeepClone();
                n = obj;
            }
            else n = p["v"]?.DeepClone();
            nodes.Add(n);
            if (p["i"] is { } index)
            {
                var args = nodes[index.GetValue<int>()]!["args"]!.AsObject();
                var key = p["k"]!.GetValue<string>();
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

    [Fact]
    public async Task Anchors_are_unique_and_zero_for_partial_dependency_closures()
    {
        const string script = """
            import sys, json
            from pathlib import Path
            sys.path.insert(0, 'src/tools')
            import port_status
            rows = port_status.inventory(Path('python'), json.loads(Path('src/tools/scope.json').read_text()))
            anchors = port_status.scan_anchors(Path('src/SqlGlotSharp'))
            names = ('_parse_group', '_parse_cube_or_rollup', '_parse_grouping_sets', '_parse_grouping_set', '_parse_having')
            for name in names:
                anchor = 'sqlglot/parser.py::Parser.' + name
                assert any(r['anchor'] == anchor for r in rows), anchor
                matches = anchors[anchor]
                assert len(matches) == 1 and matches[0]['file'] == 'parser.group.cs', matches
                assert matches[0]['hash'] == '0000000000', matches
            print(json.dumps(names))
            """;
        Assert.NotEmpty(await Python(script));
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
