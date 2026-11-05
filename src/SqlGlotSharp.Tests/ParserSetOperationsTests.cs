using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Parser = SqlGlotSharp.parser.Parser;
using Dialect = SqlGlotSharp.dialects.dialect.Dialect;
using Token = SqlGlotSharp.tokenizer_core.Token;

namespace SqlGlotSharp.Tests;

public class ParserSetOperationsTests
{
    private sealed record Case(string sql, string dialect = "", string level = "IMMEDIATE",
        string mode = "select", bool tighter = true, bool modifiers = true, int max_nodes = -1);
    private sealed class LooseDialect : Dialect
    {
        public override bool INTERSECT_BINDS_TIGHTER_THAN_UNION_AND_EXCEPT => false;
    }
    private sealed class MissingDefaultsDialect : Dialect
    {
        public override PyDict<Type, bool> SET_OP_DISTINCT_BY_DEFAULT => new();
    }
    private sealed class OperandModifiersParser : Parser
    {
        public override bool MODIFIERS_ATTACHED_TO_SET_OP => false;
    }
    private static Parser Start(Case c, List<Token> input)
    {
        var dialect = Dialect.get_or_raise(c.dialect);
        var p = !c.modifiers ? new OperandModifiersParser() : !c.tighter
            ? new Parser(dialect: new LooseDialect()) : dialect.parser();
        p.error_level = Enum.Parse<errors.ErrorLevel>(c.level); p.max_nodes = c.max_nodes;
        p.sql = c.sql; p._tokens = input; p._tokens_size = input.Count; p._index = -1; p._advance();
        return p;
    }
    private static object Snapshot(Token t) => new
    { type = t.token_type.ToString(), t.text, t.line, t.col, t.start, t.end, comments = t.comments.ToArray() };
    private static exp.Expr? Invoke(Parser p, string mode) => mode switch
    {
        "select" => p._parse_select(), "statement" => p._parse_statement(),
        "intersect_only" => p.parse_set_operation(new exp.Select(), intersect_only: true),
        "alias" => p._parse_set_operations(new exp.Alias(@this: new exp.Subquery(@this: new exp.Select()),
            alias: new exp.Identifier(@this: "q", quoted: false))),
        "existing" => p._parse_set_operations(new exp.Union(@this: new exp.Select(), expression: new exp.Select(), distinct: true)),
        _ => throw new ArgumentException(mode)
    };

    [Fact]
    public async Task Base_and_Postgres_match_live_Python_AST_errors_comments_metadata_and_cursor()
    {
        List<Case> cases = [];
        var queries = new List<string>
        {
            "SELECT 1", "SELECT a FROM t WHERE a > 0 UNION SELECT b FROM u WHERE b < 2",
            "SELECT 1 UNION SELECT 2 UNION SELECT 3", "SELECT 1 EXCEPT SELECT 2 UNION SELECT 3",
            "SELECT 1 UNION SELECT 2 INTERSECT SELECT 3 INTERSECT SELECT 4 EXCEPT SELECT 5",
            "SELECT 1 INTERSECT SELECT 2 UNION SELECT 3 INTERSECT SELECT 4",
            "SELECT 1 EXCEPT SELECT 2 INTERSECT SELECT 3 UNION SELECT 4",
            "SELECT 1 UNION (SELECT 2)", "SELECT 1 UNION ((SELECT 2))",
            "(SELECT 1 UNION SELECT 2) INTERSECT SELECT 3",
            "SELECT 1 UNION (SELECT 2 EXCEPT SELECT 3) INTERSECT SELECT 4",
            "SELECT (SELECT 1 UNION SELECT 2) AS x",
            "SELECT a FROM (SELECT 1 AS a UNION ALL SELECT 2) q WHERE a > 0",
            "SELECT 1 WHERE 1 IN (SELECT 1 UNION SELECT 2)",
            "SELECT 1 UNION SELECT 2 ORDER BY 1", "SELECT 1 EXCEPT SELECT 2 LIMIT 3 OFFSET 1",
            "SELECT 1 UNION SELECT 2 INTERSECT SELECT 3 ORDER BY 1 LIMIT 2 OFFSET 1",
            "SELECT 1 UNION (SELECT 2) ORDER BY 1 LIMIT 2 OFFSET 1",
            "SELECT 1 UNION (SELECT 2 ORDER BY 1 LIMIT 3) ORDER BY 1 LIMIT 2",
            "SELECT TOP 1 1 UNION SELECT TOP 2 2 LIMIT 1",
            "SELECT 1 UNION SELECT TOP 2 2 ORDER BY 1 LIMIT 1",
            "SELECT 1 UNION SELECT 2 FETCH FIRST 1 ROW ONLY",
            "SELECT 1 UNION SELECT 2 BY NAME", "SELECT 1 UNION BY NAME SELECT 2",
            "SELECT a UNION CORRESPONDING SELECT b", "SELECT a INTERSECT STRICT CORRESPONDING SELECT b",
            "SELECT a EXCEPT BY NAME ON (a, b) SELECT b",
            "SELECT a UNION CORRESPONDING BY (a) SELECT b",
            "SELECT a LEFT OUTER UNION BY NAME SELECT b",
            "SELECT /* left */ 1 /* literal */ UNION /* operation */ ALL SELECT /* right */ 2 /* end */",
            "SELECT 1 UNION", "SELECT 1 INTERSECT SELECT", "SELECT 1 EXCEPT ALL",
            "SELECT 1 UNION ALL DISTINCT SELECT 2", "SELECT 1 UNION SELECT 2 ORDER BY",
            "SELECT 1 UNION (SELECT 2", "SELECT 1 UNION SELECT 2 LIMIT 1 LIMIT 2",
            "SELECT 1 UNION BY NAME ON a SELECT 2", "SELECT 1 UNION CORRESPONDING BY () SELECT 2"
        };
        foreach (var operation in new[] { "UNION", "INTERSECT", "EXCEPT" })
            foreach (var qualifier in new[] { "", " DISTINCT", " ALL" })
                queries.Add($"SELECT 1 {operation}{qualifier} SELECT 2");
        foreach (var dialect in new[] { "", "postgres" })
            foreach (var sql in queries)
                foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
                    cases.Add(new(sql, dialect, level, sql.StartsWith('(') ? "statement" : "select"));
        foreach (var sql in new[] { "SELECT 1 UNION SELECT 2 INTERSECT SELECT 3", "SELECT 1 EXCEPT SELECT 2 INTERSECT SELECT 3 INTERSECT SELECT 4",
            "SELECT 1 UNION SELECT 2 INTERSECT SELECT 3 ORDER BY 1 LIMIT 2" })
        {
            cases.Add(new(sql, tighter: false));
            cases.Add(new(sql, modifiers: false));
        }
        foreach (var sql in new[] { "UNION SELECT 2", "EXCEPT SELECT 2", "LEFT UNION SELECT 2", "INTERSECT ALL SELECT 2", "x" })
            cases.Add(new(sql, mode: "intersect_only"));
        cases.Add(new("UNION SELECT 2", mode: "alias"));
        cases.Add(new("ORDER BY 1 LIMIT 2", mode: "existing"));
        foreach (var max in new[] { 0, 1, 2, 3, 5, 8, 12 })
            foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
                cases.Add(new("SELECT 1 UNION SELECT 2 INTERSECT SELECT 3", level: level, max_nodes: max));
        await Compare(cases);
    }

    [Fact]
    public void Prepared_VALUES_operands_cannot_bypass_canonicalization_boundary()
    {
        foreach (var operand in new exp.Expr[] { new exp.Values(), new exp.Subquery(@this: new exp.Values()),
            new exp.Subquery(@this: new exp.Subquery(@this: new exp.Values())) })
        {
            var c = new Case("UNION SELECT 2");
            var p = Start(c, tokens.Tokenizer.ForDialect("").tokenize(c.sql));
            var result = Assert.IsType<exp.Union>(p.parse_set_operation(operand));
            Assert.NotNull(result.find(typeof(exp.Select)));
        }
    }

    private static async Task Compare(List<Case> cases)
    {
        var prepared = cases.Select(c => tokens.Tokenizer.ForDialect(c.dialect).tokenize(c.sql)).ToArray();
        var input = cases.Select((c, i) => new { c.sql, c.dialect, c.level, c.mode, c.tighter, c.modifiers, c.max_nodes,
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
            from sqlglot import exp
            from sqlglot.dialects import Dialect
            from sqlglot.parser import Parser
            from sqlglot.tokens import Token, TokenType
            from sqlglot.errors import ParseError, ErrorLevel
            from sqlglot.serde import dump
            class LooseDialect(Dialect): INTERSECT_BINDS_TIGHTER_THAN_UNION_AND_EXCEPT = False
            def snapshot(t):
                return dict(type=t.token_type.name, text=t.text, line=t.line, col=t.col,
                    start=t.start, end=t.end, comments=t.comments)
            results = []
            for c in json.load(sys.stdin):
                tokens = [Token(TokenType[t['type']], t['text'], line=t['line'], col=t['col'],
                    start=t['start'], end=t['end'], comments=t['comments']) for t in c['tokens']]
                d = Dialect.get_or_raise(c['dialect']) if c['tighter'] else LooseDialect()
                p = d.parser(error_level=ErrorLevel[c['level']], max_nodes=c['max_nodes'])
                p.MODIFIERS_ATTACHED_TO_SET_OP = c['modifiers']
                p.sql = c['sql']; p._tokens, p._tokens_size, p._index = tokens, len(tokens), -1; p._advance()
                node, failure = None, []
                try:
                    mode = c['mode']
                    if mode == 'select': node = p._parse_select()
                    elif mode == 'statement': node = p._parse_statement()
                    elif mode == 'intersect_only': node = p.parse_set_operation(exp.Select(), intersect_only=True)
                    elif mode == 'alias': node = p._parse_set_operations(exp.Alias(this=exp.Subquery(this=exp.Select()), alias=exp.Identifier(this='q', quoted=False)))
                    elif mode == 'existing': node = p._parse_set_operations(exp.Union(this=exp.Select(), expression=exp.Select(), distinct=True))
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
            // SET_OP_MODIFIERS is a Python set: dictionary insertion order may vary
            // between processes. Compare full AST contents as trees, not dump indices.
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
    public async Task Anchors_are_unique_and_partial_hash_is_honest()
    {
        const string script = """
            import sys, json
            from pathlib import Path
            sys.path.insert(0, 'src/tools')
            import port_status
            rows = port_status.inventory(Path('python'), json.loads(Path('src/tools/scope.json').read_text()))
            anchors = port_status.scan_anchors(Path('src/SqlGlotSharp'))
            names = ('parse_set_operation', '_parse_set_operations', '_parse_wrapped_csv', '_parse_wrapped')
            hashes = {}
            mismatches = []
            for name in names:
                anchor = 'sqlglot/parser.py::Parser.' + name
                matches = anchors[anchor]
                assert len(matches) == 1 and matches[0]['file'] == 'parser.setoperations.cs', matches
                actual = next(r['hash'] for r in rows if r['anchor'] == anchor)
                if matches[0]['hash'] != ('0000000000' if name == 'parse_set_operation' else actual): mismatches.append(name)
                hashes[name] = actual
            assert not mismatches, hashes
            print(json.dumps(hashes))
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
