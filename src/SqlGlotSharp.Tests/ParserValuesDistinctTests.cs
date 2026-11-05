using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Parser = SqlGlotSharp.parser.Parser;
using Dialect = SqlGlotSharp.dialects.dialect.Dialect;
using Token = SqlGlotSharp.tokenizer_core.Token;

namespace SqlGlotSharp.Tests;

public class ParserValuesDistinctTests
{
    private sealed record Case(string sql, string dialect = "", string level = "IMMEDIATE",
        string mode = "values", int max_nodes = -1);

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
        "value" => p._parse_value(), "value_false" => p._parse_value(values: false),
        "values" => p._parse_derived_table_values(),
        "synonym" => p._parse_derived_table_values(allow_value_synonym: true),
        "select" => p._parse_select(), "statement" => p._parse_statement(),
        "table" => p._parse_table(),
        _ => throw new ArgumentException(mode)
    };

    [Fact]
    public async Task Values_and_distinct_match_live_Python_AST_errors_cursor_and_comments()
    {
        var samples = new List<(string sql, string mode)>();
        foreach (var sql in new[]
        {
            "", "x", "()", "(1)", "(a, b)", "(1, 'x', NULL, TRUE)",
            "(DEFAULT, 1)", "DEFAULT", "1", "a + 1", "(a AS x, b y)",
            "((a, b), (c))", "(a = 1, b AND c)", "(CASE WHEN a THEN b ELSE c END)",
            "(MIN(a), ABS(-1))", "(SELECT a FROM t)",
            "(", "(a", "(a,", "(,)", "(a,,b)", "(a +)", "(a) tail",
            "/* before */ ( /* open */ a /* arg */, b /* last */) /* close */ tail"
        })
        {
            samples.Add((sql, "value")); samples.Add((sql, "value_false"));
        }
        foreach (var sql in new[]
        {
            "", "x", "VALUE (1)", "FORMAT VALUES (1, 2), (3, 4)",
            "VALUES (1)", "VALUES 1, 2", "VALUES (1, 2), (3, 4)",
            "VALUES ()", "VALUES (DEFAULT, NULL), (1, 'two')", "VALUES ((1, 2), 3)",
            "VALUES (a + 1, b AND c)", "VALUES (1 AS x, 2 y)",
            "VALUES (1) AS t(a)", "VALUES (1, 2) t(a, b)", "VALUES (1) \"t\"(\"x\")",
            "(VALUES (1), (2)) AS t(x)", "(VALUES (1) AS t(x))", "(VALUES (1) t(x)) outer_t(y)",
            "VALUES (1) ORDER BY 1", "VALUES (1) LIMIT 1", "VALUES (1) OFFSET 1",
            "VALUES", "VALUES (", "VALUES (1", "VALUES (1,)", "VALUES (,)",
            "VALUES (1),", "VALUES (1),, (2)", "VALUES (1) AS", "VALUES (1) t(",
            "(VALUES (1)", "(VALUES)", "VALUES (1) t()", "VALUES (1) t(x,)",
            "VALUES /* values */ ( /* row */ 1 /* cell */, 2), (3) /* end */ AS /* as */ t(x, y)"
        }) samples.Add((sql, "values"));
        foreach (var sql in new[] { "VALUE (1), (2)", "VALUE 1, 2", "VALUE", "FORMAT VALUES (1)", "(VALUE (1))" })
            samples.Add((sql, "synonym"));
        foreach (var sql in new[]
        {
            "SELECT DISTINCT ON (a) a FROM t", "SELECT DISTINCT ON (a, b) a, b FROM t ORDER BY a",
            "SELECT DISTINCT ON () * FROM t", "SELECT DISTINCT ON a b FROM t", "SELECT DISTINCT a FROM t",
            "SELECT DISTINCT ON ((a, b), c + 1) a FROM t", "SELECT DISTINCT ON (DEFAULT, 1) a",
            "SELECT DISTINCT ON (a AS x, b) a", "SELECT TOP 2 DISTINCT ON (a, b) a FROM t",
            "SELECT ALL DISTINCT ON (a) a", "SELECT DISTINCT ON", "SELECT DISTINCT ON (",
            "SELECT DISTINCT ON (a", "SELECT DISTINCT ON (a,) a", "SELECT DISTINCT ON (,a) a",
            "SELECT DISTINCT ON (a +) a", "SELECT DISTINCT ON (a) FROM t",
            "SELECT /* select */ DISTINCT /* distinct */ ON /* on */ (a /* arg */, b) /* tuple */ a FROM t",
            "VALUES (1, 2), (3, 4)", "SELECT * FROM (VALUES (1, 2), (3, 4)) AS t(a, b)",
            "SELECT t.a FROM (VALUES (1)) t(a)", "SELECT * FROM VALUES (1, 2) t(a, b)",
            "SELECT * FROM (VALUES (DEFAULT, 1)) t(a, b)", "SELECT * FROM (VALUES (1) AS t(a))",
            "SELECT * FROM (VALUES (1), (2)) t(a) WHERE a > 1 ORDER BY a",
            "SELECT DISTINCT ON (t.a) t.a FROM (VALUES (1), (2)) t(a)",
            "SELECT * FROM (VALUES (1", "SELECT * FROM (VALUES (1)) AS t(a,)",
            "VALUES /* keyword */ (1 /* cell */), (2) /* rows */",
            "SELECT * FROM ( /* open */ VALUES (1 /* cell */), (2)) /* close */ t(a)"
        }) samples.Add((sql, "select"));
        foreach (var sql in new[] { "(VALUES (1), (2)) t(a)", "VALUES (1) t(a)", "(VALUES (1) AS t(a))" })
            samples.Add((sql, "table"));
        foreach (var sql in new[] { "VALUES (1), (2)", "(VALUES (1), (2))", "SELECT DISTINCT ON (a, b) a FROM t" })
            samples.Add((sql, "statement"));

        List<Case> cases = [];
        foreach (var dialect in new[] { "", "postgres" })
            foreach (var (sql, mode) in samples)
                foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
                    cases.Add(new(sql, dialect, level, mode));
        foreach (var dialect in new[] { "", "postgres" })
            foreach (var max in new[] { 0, 1, 2, 3, 5, 8 })
                foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
                {
                    cases.Add(new("VALUES (DEFAULT, 1), (2, 3)", dialect, level, max_nodes: max));
                    cases.Add(new("SELECT DISTINCT ON (a, b) a FROM t", dialect, level, "select", max));
                }
        await Compare(cases);
    }

    [Theory]
    [InlineData("SELECT DISTINCT ON (a, b) a FROM t", "Select")]
    [InlineData("VALUES (1, 'x'), (2, 'y')", "Values")]
    [InlineData("SELECT * FROM (VALUES (1, 2), (3, 4)) AS t(a, b)", "Select")]
    public void Public_Postgres_parse_uses_production_path(string sql, string type)
    {
        var parsed = new dialects.postgres.Postgres().parse(sql);
        var node = Assert.Single(parsed);
        Assert.Equal(type, node!.GetType().Name);
        if (sql.StartsWith("SELECT DISTINCT"))
            Assert.Equal(2, Assert.IsType<exp.Tuple>(Assert.IsType<exp.Distinct>(node.args["distinct"]).args["on"]).expressions.Count);
        if (sql.StartsWith("VALUES")) Assert.Equal(2, Assert.IsType<exp.Values>(node).expressions.Count);
        if (sql.Contains("FROM (VALUES"))
        {
            var values = Assert.IsType<exp.Values>(Assert.IsType<exp.From>(node.args["from_"]).@this);
            var alias = Assert.IsType<exp.TableAlias>(values.args["alias"]);
            Assert.Equal("t", alias.name);
            Assert.Equal(2, ((System.Collections.IEnumerable)alias.args["columns"]!).Cast<object>().Count());
        }
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
                    if mode == 'value': node = p._parse_value()
                    elif mode == 'value_false': node = p._parse_value(values=False)
                    elif mode == 'values': node = p._parse_derived_table_values()
                    elif mode == 'synonym': node = p._parse_derived_table_values(allow_value_synonym=True)
                    elif mode == 'select': node = p._parse_select()
                    elif mode == 'statement': node = p._parse_statement()
                    elif mode == 'table': node = p._parse_table()
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
            // Compare AST trees without imposing serialized argument insertion order.
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
    public async Task Partial_anchors_are_unique_in_values_file()
    {
        const string script = """
            import sys, json
            from pathlib import Path
            sys.path.insert(0, 'src/tools')
            import port_status
            anchors = port_status.scan_anchors(Path('src/SqlGlotSharp'))
            for name in ('_parse_value', '_parse_derived_table_values'):
                matches = anchors['sqlglot/parser.py::Parser.' + name]
                assert len(matches) == 1 and matches[0]['file'] == 'parser.values.cs', matches
                assert matches[0]['hash'] == '0000000000', matches
            print('ok')
            """;
        Assert.Equal("ok", (await Python(script)).Trim());
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
