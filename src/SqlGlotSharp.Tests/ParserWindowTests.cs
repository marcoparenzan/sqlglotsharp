using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Parser = SqlGlotSharp.parser.Parser;
using Dialect = SqlGlotSharp.dialects.dialect.Dialect;
using Token = SqlGlotSharp.tokenizer_core.Token;

namespace SqlGlotSharp.Tests;

public class ParserWindowTests
{
    private sealed record Case(string sql, string dialect = "", string level = "IMMEDIATE",
        string mode = "expression", int max_nodes = -1);

    private static Parser Start(Case c, List<Token> input)
    {
        var p = Dialect.get_or_raise(c.dialect).parser();
        if (c.dialect == "postgres") Assert.IsType<parsers.postgres.PostgresParser>(p);
        p.error_level = Enum.Parse<errors.ErrorLevel>(c.level); p.max_nodes = c.max_nodes;
        p.sql = c.sql; p._tokens = input; p._tokens_size = input.Count; p._index = -1; p._advance();
        return p;
    }
    private static object Snapshot(Token t) => new
    { type = t.token_type.ToString(), t.text, t.line, t.col, t.start, t.end, comments = t.comments.ToArray() };
    private static object? Invoke(Parser p, string mode) => mode switch
    {
        "expression" => p._parse_expression(), "select" => p._parse_select(),
        "parse" => p.parse(p._tokens, p.sql),
        "named" => p._parse_named_window(), "clause" => p._parse_window_clause(),
        "spec" => p._parse_window_spec(), "partition" => p._parse_partition_by(),
        "window" => p._parse_window(new exp.Sum(@this: new exp.Column(@this: new exp.Identifier(@this: "a", quoted: false)))),
        _ => throw new ArgumentException(mode)
    };

    [Fact]
    public async Task Windows_match_live_Python_AST_errors_cursor_comments_for_base_and_Postgres()
    {
        List<(string sql, string mode)> samples = [];
        foreach (var sql in new[]
        {
            "ROW_NUMBER() OVER ()", "SUM(a) OVER (PARTITION BY b ORDER BY a)",
            "RANK() OVER w", "SUM(a) OVER \"Window Name\"", "SUM(a) OVER (w)",
            "SUM(a) OVER (w PARTITION BY b ORDER BY a DESC NULLS LAST)",
            "SUM(a) OVER (ORDER BY a ASC NULLS FIRST, b DESC)", "SUM(a) OVER () OVER ()",
            "SUM(a) KEEP (FIRST ORDER BY b) OVER (PARTITION BY c)",
            "SUM(a) KEEP (LAST ORDER BY b)", "SUM(a) OVER (FIRST ORDER BY b)",
            "SUM(a) OVER (LAST PARTITION BY b)", "(SUM(a)) OVER ()", "(SUM(a))",
            "SUM(a) FILTER (WHERE a > 0) OVER (PARTITION BY b)",
            "SUM(a) FILTER (a > 0)", "SUM(a) FILTER (WHERE)",
            "ABS(1) WITHIN GROUP (ORDER BY a DESC) OVER ()",
            "SUM(a) IGNORE NULLS OVER ()", "SUM(a) RESPECT NULLS OVER w",
            "ABS(1) RESPECT NULLS", "SUM(a) PER (b, c + 1)", "SUM(a) OVER () PER (b)",
            "SUM(a) OVER (PARTITION BY EXCLUDING b, c ORDER BY a)",
            "SUM(a) /* function */ OVER /* over */ (PARTITION BY /* partition */ b /* b */ ORDER BY /* order */ a)",
            "SUM(a) /* function */ OVER /* over */ w /* name */",
            "ROW_NUMBER() OVER /* window */ () /* close */", "SUM(a) FILTER /* filter */ (WHERE /* where */ b) OVER ()",
            "SUM(a) OVER (PARTITION BY a + 1, b * 2 ORDER BY c - 3)",
            "SUM(a) OVER", "SUM(a) OVER (", "SUM(a) OVER (PARTITION BY)",
            "SUM(a) OVER (ORDER BY)", "SUM(a) OVER (PARTITION BY b ORDER BY a",
            "SUM(a) OVER (w x)", "SUM(a) OVER (PARTITION BY a,)",
            "SUM(a) WITHIN GROUP ORDER BY a", "SUM(a) FILTER (WHERE a >)",
            "NTH_VALUE(a, 2) OVER (ORDER BY b)", "LAG(a, 1, 0) OVER (ORDER BY b)",
            "COUNT(*) OVER ()", "COUNT(DISTINCT a) OVER (PARTITION BY b)"
        }) samples.Add((sql, "expression"));
        foreach (var kind in new[] { "ROWS", "RANGE", "GROUPS", "rows", "range" })
            foreach (var frame in new[]
            {
                "UNBOUNDED PRECEDING", "CURRENT ROW", "2 PRECEDING", "3 FOLLOWING",
                "BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW",
                "BETWEEN 2 PRECEDING AND 3 FOLLOWING", "BETWEEN CURRENT ROW AND UNBOUNDED FOLLOWING",
                "BETWEEN a + 1 PRECEDING AND b * 2 FOLLOWING", "BETWEEN -2 PRECEDING AND 0 FOLLOWING",
                "BETWEEN INTERVAL '1' DAY PRECEDING AND CURRENT ROW",
                "BETWEEN BETWEEN 2 PRECEDING AND BETWEEN 3 FOLLOWING",
                "BETWEEN 2 preceding AND 3 following", "", "BETWEEN", "BETWEEN AND CURRENT ROW",
                "BETWEEN 2 PRECEDING AND", "CURRENT", "UNBOUNDED", "2",
                "BETWEEN 2 /* bound */ PRECEDING AND /* end */ CURRENT ROW"
            }) samples.Add(($"SUM(a) OVER (ORDER BY b {kind} {frame})", "expression"));
        foreach (var exclude in new[] { "CURRENT ROW", "NO OTHERS", "GROUP", "TIES", "no others", "BAD", "NO", "CURRENT", "", "\"GROUP\"" })
            samples.Add(($"SUM(a) OVER (ROWS BETWEEN 2 PRECEDING AND CURRENT ROW EXCLUDE {exclude})", "expression"));
        foreach (var sql in new[] { "w AS ()", "w AS (PARTITION BY b ORDER BY a)", "w AS (other ROWS 2 PRECEDING)",
            "w (ORDER BY a)", "w AS other", "w AS (", "w AS (ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW)",
            "w /* name */ AS /* as */ (ORDER BY a)", "\"w\" AS (\"other\")", "" }) samples.Add((sql, "named"));
        foreach (var sql in new[] { "WINDOW w AS (), x AS (w ORDER BY a)", "WINDOW w AS (ROWS 2 PRECEDING), x AS ()",
            "WINDOW /* window */ w /* name */ AS (PARTITION BY b)", "WINDOW", "WINDOW w AS () ,", "", "x" }) samples.Add((sql, "clause"));
        foreach (var sql in new[] { "", "UNBOUNDED PRECEDING", "CURRENT ROW", "2 FOLLOWING", "BETWEEN BETWEEN 2 PRECEDING",
            "a * 2 + 1 preceding", "INTERVAL '1' DAY PRECEDING", "AND", "CURRENT", "2 /* bound */ PRECEDING" }) samples.Add((sql, "spec"));
        foreach (var sql in new[] { "", "PARTITION BY a, b", "PARTITION BY EXCLUDING a, b", "PARTITION BY", "PARTITION BY a ORDER BY b" }) samples.Add((sql, "partition"));
        foreach (var sql in new[] { "", "OVER ()", "OVER w", "IGNORE NULLS OVER ()", "KEEP (FIRST ORDER BY b) OVER ()", "PER (b)" }) samples.Add((sql, "window"));
        foreach (var sql in new[]
        {
            "SELECT ROW_NUMBER() OVER ()", "SELECT SUM(a) OVER (PARTITION BY b ORDER BY a) FROM t",
            "SELECT ROW_NUMBER() OVER (ORDER BY a DESC), SUM(a) OVER w FROM t WHERE b > 0",
            "SELECT SUM(a) OVER (ORDER BY b RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS total FROM t",
            "SELECT SUM(a) FILTER (WHERE a > 0) OVER () FROM t", "SELECT (SUM(a)) OVER ()",
            "SELECT COUNT(*) OVER ()", "SELECT SUM(a) OVER (ORDER BY b ROWS 2 PRECEDING) FROM t ORDER BY b"
        }) samples.Add((sql, "select"));
        foreach (var sql in new[]
        {
            "SELECT ROW_NUMBER() OVER ()", "SELECT SUM(a) OVER (PARTITION BY b ORDER BY a) FROM t",
            "SELECT SUM(a) OVER (ORDER BY b ROWS BETWEEN 2 PRECEDING AND CURRENT ROW) AS total FROM t",
            "SELECT SUM(a) OVER w", "SELECT SUM(a) OVER (", "SELECT SUM(a) OVER (ROWS BETWEEN AND CURRENT ROW)",
            "SELECT COUNT(*) FILTER (WHERE b > 0) OVER (); SELECT RANK() OVER (ORDER BY a DESC)"
        }) samples.Add((sql, "parse"));
        List<Case> cases = [];
        foreach (var dialect in new[] { "", "postgres" })
            foreach (var (sql, mode) in samples)
                foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" }) cases.Add(new(sql, dialect, level, mode));
        foreach (var dialect in new[] { "", "postgres" })
            foreach (var max in new[] { 0, 1, 2, 3, 5, 8, 12 })
                foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
                    cases.Add(new("SUM(a) /* f */ OVER (PARTITION BY b ORDER BY a ROWS BETWEEN 2 PRECEDING AND CURRENT ROW)", dialect, level, max_nodes: max));
        await Compare(cases);
    }

    [Theory]
    [InlineData("SUM(a) OVER (ORDER BY b WITH FILL)", "expression", "_parse_ordered")]


    public void Missing_dependency_closures_stay_explicit(string sql, string mode, string boundary)
    {
        foreach (var dialect in new[] { "", "postgres" })
        {
            var c = new Case(sql, dialect, "IGNORE", mode);
            var p = Start(c, tokens.Tokenizer.ForDialect(dialect).tokenize(sql));
            Assert.Contains(boundary, Assert.Throws<NotPortedException>(() => Invoke(p, mode)).Message);
        }
    }

    [Fact]
    public void Nested_null_wrapper_relocation_is_an_explicit_scope_boundary()
    {
        var c = new Case(""); var p = Start(c, []);
        var inner = new exp.IgnoreNulls(@this: new exp.Column(@this: new exp.Identifier(@this: "a")));
        Assert.Contains("find_in_scope", Assert.Throws<NotPortedException>(() =>
            p._parse_window(new exp.Sum(@this: inner))).Message);
    }

    private static JsonNode? Dump(object? node) => node switch
    {
        exp.Expr e => AstTree(JsonSerializer.SerializeToNode(serde.dump(e), serde.JsonOptions)!.AsArray()),
        List<exp.Expr> list => new JsonArray(list.Select(e => Dump(e)).ToArray()),
        PyDict<string, object?> d => new JsonObject(d.Select(kv => KeyValuePair.Create(kv.Key,
            kv.Value is exp.Expr e ? Dump(e) : JsonSerializer.SerializeToNode(kv.Value, serde.JsonOptions)))),
        null => null,
        _ => throw new ArgumentException(node.GetType().Name)
    };

    private static async Task Compare(List<Case> cases)
    {
        var prepared = cases.Select(c => tokens.Tokenizer.ForDialect(c.dialect).tokenize(c.sql)).ToArray();
        var input = cases.Select((c, i) => new { c.sql, c.dialect, c.level, c.mode, c.max_nodes,
            tokens = prepared[i].Select(Snapshot).ToArray() }).ToArray();
        var actual = cases.Select((c, i) =>
        {
            var p = Start(c, prepared[i]); object? node = null;
            List<PyDict<string, object?>> failure = [];
            try { node = Invoke(p, c.mode); }
            catch (errors.ParseError e) { failure = e.errors; }
            catch (NotPortedException e) { Assert.Fail($"Unexpected boundary: {c}: {e.Message}"); }
            return new { ast = Dump(node), failure, errors = p.errors.Select(e => e.errors).ToArray(),
                index = p._index, curr = Snapshot(p._curr), next = Snapshot(p._next), prev = Snapshot(p._prev),
                prev_comments = p._prev_comments.ToArray(), token_comments = prepared[i].Select(t => t.comments.ToArray()).ToArray(),
                node_count = p._node_count };
        }).ToArray();
        const string script = """
            import sys, json
            sys.path.insert(0, 'python')
            from sqlglot import exp
            from sqlglot.dialects import Dialect
            from sqlglot.tokens import Token, TokenType
            from sqlglot.errors import ParseError, ErrorLevel
            from sqlglot.serde import dump
            def snapshot(t):
                return dict(type=t.token_type.name, text=t.text, line=t.line, col=t.col,
                    start=t.start, end=t.end, comments=t.comments)
            def tree(node):
                if isinstance(node, list): return [tree(n) for n in node]
                if isinstance(node, dict): return {k: tree(v) for k,v in node.items()}
                if not isinstance(node, exp.Expr): return node
                nodes = []
                for p in dump(node):
                    if 'c' in p:
                        n = dict(c=p['c'], args={})
                        for k in ('o','m','t'):
                            if k in p: n[k] = p[k]
                    else: n = p.get('v')
                    nodes.append(n)
                    if 'i' in p:
                        args = nodes[p['i']]['args']; key = p['k']
                        if p.get('a'): args.setdefault(key, []).append(n)
                        else: args[key] = n
                return nodes[0] if nodes else None
            results = []
            for c in json.load(sys.stdin):
                tokens = [Token(TokenType[t['type']], t['text'], line=t['line'], col=t['col'],
                    start=t['start'], end=t['end'], comments=t['comments']) for t in c['tokens']]
                p = Dialect.get_or_raise(c['dialect']).parser(error_level=ErrorLevel[c['level']], max_nodes=c['max_nodes'])
                p.sql = c['sql']; p._tokens, p._tokens_size, p._index = tokens, len(tokens), -1; p._advance()
                node, failure = None, []
                try:
                    mode = c['mode']
                    if mode == 'expression': node = p._parse_expression()
                    elif mode == 'select': node = p._parse_select()
                    elif mode == 'parse': node = p.parse(tokens, sql=c['sql'])
                    elif mode == 'named': node = p._parse_named_window()
                    elif mode == 'clause': node = p._parse_window_clause()
                    elif mode == 'spec': node = p._parse_window_spec()
                    elif mode == 'partition': node = p._parse_partition_by()
                    elif mode == 'window': node = p._parse_window(exp.Sum(this=exp.Column(this=exp.Identifier(this='a', quoted=False))))
                except ParseError as e: failure = e.errors
                results.append(dict(ast=tree(node), failure=failure, errors=[e.errors for e in p.errors],
                    index=p._index, curr=snapshot(p._curr), next=snapshot(p._next), prev=snapshot(p._prev),
                    prev_comments=p._prev_comments[:], token_comments=[t.comments[:] for t in tokens], node_count=p._node_count))
            print(json.dumps(results))
            """;
        var expected = JsonNode.Parse(await Python(script, JsonSerializer.Serialize(input)))!.AsArray();
        var observed = JsonSerializer.SerializeToNode(actual, serde.JsonOptions)!.AsArray();
        Assert.Equal(cases.Count, expected.Count);
        for (var i = 0; i < cases.Count; i++)
            Assert.True(JsonNode.DeepEquals(expected[i], observed[i]),
                $"Case {i}: {cases[i]}\nPython: {expected[i]}\nC#: {observed[i]}");
    }

    // Normalize only dump parent indices/argument insertion order, not AST values.
    private static JsonNode? AstTree(JsonArray dump)
    {
        var nodes = new List<JsonNode?>();
        foreach (var payload in dump)
        {
            var p = payload!.AsObject(); JsonNode? n;
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

    [Fact]
    public void External_corpus_is_read_only_and_not_copied_into_workspace()
    {
        const string corpus = @"D:\clones\corpus";
        var workspace = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Source())!, "..", ".."));
        Assert.False(Path.GetFullPath(corpus).StartsWith(workspace, StringComparison.OrdinalIgnoreCase));
        using var stream = File.OpenRead(Path.Combine(corpus, "L0.base.jsonl"));
        Assert.True(stream.Length > 0);
        Assert.NotEqual(-1, stream.ReadByte());
        Assert.Empty(Directory.EnumerateFiles(workspace, "L0.base.jsonl", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateDirectories(workspace, "corpus", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Partial_anchors_are_unique_and_zero()
    {
        const string script = """
            import sys, json
            from pathlib import Path
            sys.path.insert(0, 'src/tools')
            import port_status
            anchors = port_status.scan_anchors(Path('src/SqlGlotSharp'))
            names = ('_parse_window_clause', '_parse_named_window', '_parse_respect_or_ignore_nulls',
                '_parse_per', '_parse_window', '_parse_partition_and_order', '_parse_partition_by', '_parse_window_spec')
            for name in names:
                matches = anchors['sqlglot/parser.py::Parser.' + name]
                assert len(matches) == 1 and matches[0]['file'] == 'parser.windows.cs', matches
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
