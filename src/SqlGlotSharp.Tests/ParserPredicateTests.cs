using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Parser = SqlGlotSharp.parser.Parser;
using Token = SqlGlotSharp.tokenizer_core.Token;

namespace SqlGlotSharp.Tests;

public class ParserPredicateTests
{
    private sealed record Case(string sql, string method = "_parse_disjunction", string precedence = "HIGH",
        string dialect = "", string level = "IMMEDIATE", int max_nodes = -1, bool alias = false);
    private sealed class OracleParser : Parser
    {
        public parser.IsPredicatePrecedence precedence;
        public override parser.IsPredicatePrecedence IS_PREDICATE_PRECEDENCE => precedence;
    }
    private static Parser Start(Case c, List<Token> input)
    {
        var p = new OracleParser { precedence = Enum.Parse<parser.IsPredicatePrecedence>(c.precedence),
            dialect = parser._resolve_dialect(c.dialect), error_level = Enum.Parse<errors.ErrorLevel>(c.level), max_nodes = c.max_nodes };
        p.sql = c.sql; p._tokens = input; p._tokens_size = input.Count; p._index = -1; p._advance();
        return p;
    }
    private static object Snapshot(Token t) => new
    { type = t.token_type.ToString(), t.text, t.line, t.col, t.start, t.end, comments = t.comments.ToArray() };
    private static exp.Expr? Invoke(Parser p, Case c) => c.method switch
    {
        "_parse_disjunction" => p._parse_disjunction(), "_parse_select" => p._parse_select(),
        "_parse_null" => p._parse_null(), "_parse_is_rhs" => p._parse_is_rhs(),
        "_parse_select_or_expression" => p._parse_select_or_expression(c.alias),
        _ => throw new ArgumentException(c.method)
    };

    [Fact]
    public async Task Is_null_boolean_distinct_json_and_retreat_match_python()
    {
        var sqls = new[] { "a IS NULL", "a IS UNKNOWN", "a IS TRUE", "a IS FALSE", "a IS ?", "a IS :v",
            "a IS b + 2", "a IS NOT NULL", "a IS NOT UNKNOWN", "a IS NOT TRUE", "a IS NOT b",
            "a IS DISTINCT FROM b", "a IS NOT DISTINCT FROM b", "a IS NULL IS TRUE", "a IS NOT NULL IS FALSE",
            "a = b IS NULL", "a > b IS DISTINCT FROM c", "NOT a IS NULL", "a IS DISTINCT FROM b = c",
            "a IS JSON", "a IS NOT JSON", "a IS JSON ARRAY", "a IS JSON OBJECT WITH UNIQUE KEYS",
            "a IS JSON SCALAR WITHOUT UNIQUE KEYS", "a IS JSON VALUE WITH KEYS", "a IS JSON WITH",
            "a IS", "a IS NOT", "a IS DISTINCT", "a IS NOT DISTINCT FROM", "a IS DISTINCT FROM",
            "a IS )", "a IS DISTINCT FROM b IS NULL", "a IS NULL = FALSE OR b IS NOT TRUE",
            "a /* lhs */ IS /* is */ NOT /* not */ NULL /* null */", "a IS JSON /* json */ ARRAY /* kind */ WITH UNIQUE KEYS /* keys */" };
        await Compare((from sql in sqls from prec in new[] { "HIGH", "LOW", "EQUALITY", "COMPARISON" }
            from dialect in new[] { "", "postgres" } from level in new[] { "IMMEDIATE", "IGNORE" }
            select new Case(sql, precedence: prec, dialect: dialect, level: level)).ToList());
    }

    [Fact]
    public async Task Between_and_not_preserve_bounds_grouping_and_precedence()
    {
        var sqls = new[] { "a BETWEEN 1 AND 2", "a NOT BETWEEN 1 AND 2", "NOT a BETWEEN 1 AND 2",
            "a BETWEEN SYMMETRIC 1 AND 2", "a BETWEEN ASYMMETRIC 1 AND 2", "a BETWEEN 1+2 AND 3*4",
            "a BETWEEN b & 2 AND c | 3", "a BETWEEN 1 AND 2 AND b = 3 OR NOT c",
            "a IS NULL BETWEEN FALSE AND TRUE", "a IS DISTINCT FROM b BETWEEN 1 AND 2",
            "a IS NOT NULL BETWEEN FALSE AND TRUE", "a NOT BETWEEN 1 AND 2 NOT BETWEEN 3 AND 4",
            "a BETWEEN", "a BETWEEN 1", "a BETWEEN 1 2", "a BETWEEN AND 2", "a BETWEEN 1 AND",
            "a BETWEEN /* between */ SYMMETRIC /* sym */ 1 /* low */ AND /* and */ 2 /* high */",
            "a /* lhs */ NOT /* not */ BETWEEN 1 AND 2", "a NOT NULL", "a ISNULL", "a NOTNULL" };
        await Compare((from sql in sqls from prec in new[] { "HIGH", "LOW", "EQUALITY", "COMPARISON" }
            from dialect in new[] { "", "postgres" } from level in new[] { "IMMEDIATE", "RAISE", "IGNORE" }
            // With two absent bounds, Python's required_args set has no stable
            // first error. RAISE compares the complete diagnostic set instead.
            select new Case(sql, precedence: prec, dialect: dialect, level: sql == "a BETWEEN" && level == "IMMEDIATE" ? "RAISE" : level)).ToList());
    }

    [Fact]
    public async Task In_lists_brackets_fields_not_and_malformed_input_match_python()
    {
        var sqls = new[] { "a IN (1, 2)", "a NOT IN (1,2)", "NOT a IN (1,2)", "a IN [1, 2]",
            "a IN ()", "a IN []", "a IN (1,)", "a IN (,)", "a IN (1", "a IN [1", "a IN [1)",
            "a IN (1]", "a IN", "a IN b", "a IN t.b", "a IN mystery(1)",
            "a IN ((1+2), (3,4))", "a IN (1 IS NULL, 2 BETWEEN 1 AND 3)",
            "a IN (1,2) IN (TRUE,FALSE)", "a NOT IN [1] BETWEEN FALSE AND TRUE",
            "a IS NULL IN (FALSE,TRUE)", "a IN [1] AND b NOT IN (2,3)",
            "a /* lhs */ NOT /* not */ IN /* in */ (1 /* one */, /* comma */ 2 /* two */) /* close */",
            "a IN /* in */ [1, 2] /* close */" };
        await Compare((from sql in sqls from level in new[] { "IMMEDIATE", "RAISE", "IGNORE" }
            select new Case(sql, level: level)).ToList());
    }

    [Fact]
    public async Task Predicate_dependencies_and_select_integration_match_python()
    {
        List<Case> cases = [];
        foreach (var sql in new[] { "NULL", "UNKNOWN", "?", ":name", "@name", "", "true", "b" })
            cases.Add(new(sql, "_parse_null"));
        foreach (var sql in new[] { "NULL", "UNKNOWN", "true", "b+2", "?", "" })
            cases.Add(new(sql, "_parse_is_rhs"));
        foreach (var sql in new[] { "1", "1 AS x", "SELECT a WHERE a IS NOT NULL", "SELECT a WHERE a IN (1,2) AND a BETWEEN 1 AND 2" })
            foreach (var alias in new[] { false, true }) cases.Add(new(sql, "_parse_select_or_expression", alias: alias));
        foreach (var sql in new[] { "SELECT a IS NULL", "SELECT a WHERE d IS NULL", "SELECT a WHERE b BETWEEN 1 AND 3",
            "SELECT a FROM t WHERE a NOT IN [1,2] ORDER BY a", "SELECT a WHERE NOT a IS DISTINCT FROM b" })
            cases.Add(new(sql, "_parse_select"));
        foreach (var n in new[] { 0, 1, 2, 3, 4, 5, 6, 8, 12 })
            foreach (var sql in new[] { "a IS NOT NULL", "a BETWEEN 1 AND 2", "a NOT IN [1,2]" })
                foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" }) cases.Add(new(sql, max_nodes: n, level: level));
        await Compare(cases);
    }

    [Fact]
    public async Task In_select_subqueries_match_python_on_available_parser_paths()
    {
        var sqls = new[] { "a IN (SELECT b)", "a NOT IN (SELECT b FROM tbl WHERE b > 1)",
            "NOT a IN (SELECT b)", "a IN [SELECT b]", "a IN ((SELECT b))",
            "a IN (SELECT b LIMIT 2)", "a IN (SELECT b ORDER BY b DESC LIMIT 2 OFFSET 1)",
            "a IN (SELECT b WHERE b IN (SELECT c))", "a IN (SELECT b) AND c = 1",
            "a IN (SELECT b", "a IN [SELECT b)",
            "a /* lhs */ IN /* in */ (SELECT /* select */ b /* projection */) /* close */" };
        await Compare((from sql in sqls from dialect in new[] { "", "postgres" }
            from level in new[] { "IMMEDIATE", "RAISE", "IGNORE" }
            select new Case(sql, dialect: dialect, level: level)).ToList());
        await Compare(new[] { "SELECT a IN (SELECT b)", "SELECT a FROM tbl WHERE a IN (SELECT b FROM other_tbl)" }
            .Select(sql => new Case(sql, "_parse_select")).ToList());
        await Compare((from n in new[] { 0, 1, 2, 3, 4, 5, 8, 12 }
            from level in new[] { "IMMEDIATE", "RAISE", "IGNORE" }
            select new Case("a IN (SELECT b)", max_nodes: n, level: level)).ToList());
    }

    [Fact]
    public async Task Anchors_match_existing_hash_tool()
    {
        const string script = """
            import sys, json
            from pathlib import Path
            sys.path.insert(0, 'src/tools')
            import port_status
            members = port_status.inventory(Path('python'), json.loads(Path('src/tools/scope.json').read_text()))
            report = port_status.report(members, port_status.scan_anchors(Path('src/SqlGlotSharp')))
            rows = [r for r in report['members'] if any(c['file'] == 'parser.predicates.cs' for c in r.get('csharp', []))]
            assert len(rows) == 6, rows
            for r in rows:
                assert len(r['csharp']) == 1, r
                assert r['status'] == 'ported', r
            print('verified')
            """;
        Assert.Equal("verified", (await Python(script)).Trim());
    }

    private static async Task Compare(List<Case> cases)
    {
        var prepared = cases.Select(c => tokens.Tokenizer.ForDialect(c.dialect).tokenize(c.sql)).ToArray();
        var input = cases.Select((c, i) => new { c.sql, c.method, c.precedence, c.dialect, c.level, c.max_nodes, c.alias,
            tokens = prepared[i].Select(Snapshot).ToArray() }).ToArray();
        var actual = cases.Select((c, i) =>
        {
            var p = Start(c, prepared[i]); exp.Expr? node = null;
            List<PyDict<string, object?>> failure = [];
            try { node = Invoke(p, c); } catch (errors.ParseError e) { failure = e.errors; }
            return new { ast = serde.dump(node), failure, errors = p.errors.Select(e => e.errors).ToArray(),
                index = p._index, curr = Snapshot(p._curr), next = Snapshot(p._next), prev = Snapshot(p._prev),
                prev_comments = p._prev_comments.ToArray(), token_comments = prepared[i].Select(t => t.comments.ToArray()).ToArray(),
                node_count = p._node_count };
        }).ToArray();
        const string script = """
            import sys, json
            sys.path.insert(0, 'python')
            from sqlglot.parser import Parser, IsPredicatePrecedence
            from sqlglot.tokens import Token, TokenType
            from sqlglot.errors import ParseError, ErrorLevel
            from sqlglot.serde import dump
            def snapshot(t):
                return dict(type=t.token_type.name,text=t.text,line=t.line,col=t.col,start=t.start,end=t.end,comments=t.comments)
            results=[]
            for c in json.load(sys.stdin):
                class OracleParser(Parser):
                    IS_PREDICATE_PRECEDENCE = IsPredicatePrecedence[c['precedence']]
                p=OracleParser(dialect=c['dialect'],error_level=ErrorLevel[c['level']],max_nodes=c['max_nodes'])
                ts=[Token(TokenType[t['type']],t['text'],line=t['line'],col=t['col'],start=t['start'],end=t['end'],comments=t['comments']) for t in c['tokens']]
                p.sql=c['sql']; p._tokens=ts; p._tokens_size=len(ts); p._index=-1; p._advance()
                node=None; failure=[]
                try:
                    method=getattr(p,c['method'])
                    node=method(alias=c['alias']) if c['method']=='_parse_select_or_expression' else method()
                except ParseError as e: failure=e.errors
                results.append(dict(ast=dump(node),failure=failure,errors=[e.errors for e in p.errors],index=p._index,
                    curr=snapshot(p._curr),next=snapshot(p._next),prev=snapshot(p._prev),prev_comments=p._prev_comments,
                    token_comments=[t.comments for t in ts],node_count=p._node_count))
            print(json.dumps(results,ensure_ascii=False))
            """;
        var expected = JsonNode.Parse(await Python(script, JsonSerializer.Serialize(input)))!.AsArray();
        Assert.Equal(cases.Count, expected.Count);
        for (var i = 0; i < cases.Count; i++)
        {
            var got = JsonSerializer.SerializeToNode(actual[i]);
            // Only diagnostic ordering is set-dependent; retain every complete
            // message and all AST/cursor/comment/position fields unchanged.
            SortDiagnostics(expected[i]!); SortDiagnostics(got!);
            Assert.True(JsonNode.DeepEquals(expected[i], got), $"Case {i}: {cases[i]}\nExpected: {expected[i]}\nActual: {got}");
        }
    }
    private static void SortDiagnostics(JsonNode node)
    {
        static JsonArray Sorted(JsonArray errors) => new(errors.Select(e => e!.DeepClone())
            .OrderBy(e => e.ToJsonString(), StringComparer.Ordinal).ToArray());
        node["failure"] = Sorted(node["failure"]!.AsArray());
        // Parser.errors stores one ParseError per diagnostic; its list order is
        // likewise unspecified when expression.required_args is a Python set.
        node["errors"] = Sorted(node["errors"]!.AsArray());
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
