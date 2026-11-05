using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Parser = SqlGlotSharp.parser.Parser;
using Token = SqlGlotSharp.tokenizer_core.Token;
using Postgres = SqlGlotSharp.dialects.postgres.Postgres;

namespace SqlGlotSharp.Tests;

public class ParserPatternTests
{
    private sealed record Case(string sql, string dialect = "", string precedence = "HIGH",
        string level = "IMMEDIATE", int max_nodes = -1, string method = "range", bool reverse = false);
    private sealed class OracleParser : Parser
    {
        public parser.IsPredicatePrecedence precedence;
        public override parser.IsPredicatePrecedence IS_PREDICATE_PRECEDENCE => precedence;
    }
    private static Parser Start(Case c, List<Token> ts)
    {
        var p = new OracleParser { precedence = Enum.Parse<parser.IsPredicatePrecedence>(c.precedence),
            dialect = parser._resolve_dialect(c.dialect), error_level = Enum.Parse<errors.ErrorLevel>(c.level), max_nodes = c.max_nodes,
            sql = c.sql, _tokens = ts, _tokens_size = ts.Count, _index = -1 };
        p._advance(); return p;
    }
    private static object Snapshot(Token t) => new
    { type = t.token_type.ToString(), t.text, t.line, t.col, t.start, t.end, comments = t.comments.ToArray() };

    [Fact]
    public async Task Patterns_not_escape_precedence_comments_and_cursor_match_live_python()
    {
        string[] sqls = ["a LIKE 'x%'", "a ILIKE 'x%'", "a SIMILAR TO '(a|b)%'", "a NOT LIKE 'x'",
            "a NOT ILIKE 'x'", "a NOT SIMILAR TO 'x'", "NOT a LIKE 'x'", "NOT a ILIKE 'x'",
            "NOT a SIMILAR TO 'x'", "a LIKE 'x' ESCAPE '!'", "a NOT LIKE 'x' ESCAPE '!'",
            "a NOT ILIKE 'x' ESCAPE NULL", "a NOT SIMILAR TO 'x' ESCAPE ?", "a LIKE 'x' ESCAPE :esc",
            "a ILIKE 'x' ESCAPE UNKNOWN", "a SIMILAR TO 'x' ESCAPE NULL", "a LIKE ?", "a LIKE :pattern",
            "a LIKE b + 2 * 3", "a LIKE b || 'x'", "a LIKE b & 2", "a = b LIKE c", "a > b ILIKE c",
            "a LIKE b = c", "a LIKE b > c", "a IS NULL LIKE b", "a LIKE b IS NULL",
            "a LIKE b IS DISTINCT FROM c", "a IS DISTINCT FROM b LIKE c", "a IS NOT NULL NOT LIKE b",
            "a NOT LIKE b LIKE c", "a NOT ILIKE b NOT SIMILAR TO c", "a NOT SIMILAR TO b ILIKE c",
            "a LIKE b ESCAPE '!' AND c ILIKE d OR NOT e SIMILAR TO f", "a LIKE b BETWEEN 1 AND 2",
            "a BETWEEN 1 AND 2 LIKE b", "a IN (1,2) NOT LIKE b", "a LIKE b IN (TRUE,FALSE)",
            "a LIKE (b || 'x')", "a LIKE (SELECT b)", "a LIKE b NOT", "a NOT xyz",
            "a LIKE b::INT", "a ILIKE INTERVAL '1' DAY",
            "a /* lhs */ NOT /* not */ LIKE /* like */ 'x' /* rhs */ ESCAPE /* esc */ '!' /* char */",
            "a /* lhs */ SIMILAR TO /* similar */ 'x' /* rhs */ ESCAPE NULL /* null */",
            "a ILIKE 'a' 'b' ESCAPE '!'", "a LIKE b ESCAPE '!' ESCAPE '?'", "a LIKE b ESCAPE '!' + 1"];
        await Compare((from sql in sqls from dialect in new[] { "", "postgres" }
            from prec in new[] { "HIGH", "LOW", "EQUALITY", "COMPARISON" }
            select new Case(sql, dialect, prec)).ToList());
    }

    [Fact]
    public async Task Errors_missing_operands_escape_and_node_limits_match_live_python()
    {
        string[] sqls = ["a LIKE", "a ILIKE", "a SIMILAR TO", "a NOT LIKE", "a NOT SIMILAR TO",
            "a LIKE )", "a LIKE b ESCAPE", "a NOT LIKE b ESCAPE", "a SIMILAR TO b ESCAPE 1",
            "a ILIKE b ESCAPE identifier", "a LIKE b ESCAPE TRUE", "a LIKE b ESCAPE :",
            "a LIKE b ESCAPE ?", "a NOT ILIKE b ESCAPE '!'", "a NOT SIMILAR TO b ESCAPE NULL"];
        await Compare((from sql in sqls from dialect in new[] { "", "postgres" }
            from level in new[] { "IMMEDIATE", "RAISE", "IGNORE" } select new Case(sql, dialect, level: level)).ToList());
        await Compare((from sql in new[] { "a LIKE 'x'", "a NOT ILIKE 'x' ESCAPE '!'", "a NOT SIMILAR TO 'x' ESCAPE NULL" }
            from n in new[] { 0, 1, 2, 3, 4, 5, 6, 8, 12 } from level in new[] { "IMMEDIATE", "RAISE", "IGNORE" }
            select new Case(sql, max_nodes: n, level: level)).ToList());
    }

    [Fact]
    public async Task Binary_helper_reversal_and_escape_direct_calls_match_live_python()
    {
        await Compare((from sql in new[] { "b + 2", "b ESCAPE '!'", "b ESCAPE NULL", "b ESCAPE ?", "b ESCAPE", "" }
            from reverse in new[] { false, true } from level in new[] { "IMMEDIATE", "RAISE", "IGNORE" }
            select new Case(sql, level: level, method: "binary", reverse: reverse)).ToList());
        await Compare(new[] { "", "other", "ESCAPE '!'", "ESCAPE NULL", "ESCAPE UNKNOWN", "ESCAPE ?", "ESCAPE :esc",
            "ESCAPE 1", "ESCAPE", "ESCAPE 'a' 'b'" }.SelectMany(sql => new[] { "IMMEDIATE", "RAISE", "IGNORE" }
                .Select(level => new Case(sql, method: "escape", level: level))).ToList());
    }

    [Fact]
    public async Task Production_Postgres_parse_matches_live_python()
    {
        string[] sqls = ["SELECT a LIKE 'x%', a ILIKE 'X%', a SIMILAR TO '(x|y)%' FROM tbl",
            "SELECT a FROM tbl WHERE a NOT LIKE 'x!_%' ESCAPE '!' AND a NOT ILIKE 'Y%'",
            "SELECT a NOT SIMILAR TO '(x|y)%' ESCAPE NULL FROM tbl",
            "SELECT NOT a LIKE $1, a ILIKE :pattern, a SIMILAR TO ?",
            "SELECT /* select */ a /* lhs */ NOT LIKE /* like */ 'x' ESCAPE /* escape */ '!' /* end */",
            "SELECT a FROM tbl WHERE a IS NOT NULL AND a LIKE 'x' ORDER BY a LIMIT 2",
            "SELECT a LIKE 'x' ESCAPE NULL IS FALSE", "SELECT a NOT ILIKE b ILIKE c",
            "SELECT a LIKE (SELECT b FROM other_tbl)", "SELECT a LIKE 'x'; SELECT a ILIKE 'y'",
            "SELECT a LIKE", "SELECT a LIKE 'x' ESCAPE", "SELECT a SIMILAR TO 'x' ESCAPE 1"];
        var cases = sqls.SelectMany(sql => new[] { "IMMEDIATE", "RAISE", "IGNORE" }.Select(level => new { sql, level })).ToArray();
        var actual = cases.Select(c =>
        {
            List<exp.Expr?>? nodes = null; List<PyDict<string, object?>> failure = [];
            try { nodes = new Postgres().parse(c.sql, Enum.Parse<errors.ErrorLevel>(c.level)); }
            catch (errors.ParseError e) { failure = e.errors; }
            return new { ast = nodes?.Select(serde.dump).ToArray(), failure };
        }).ToArray();
        const string script = """
            import sys,json
            sys.path.insert(0,'python')
            from sqlglot.dialects.postgres import Postgres
            from sqlglot.errors import ErrorLevel,ParseError
            from sqlglot.serde import dump
            out=[]
            for c in json.load(sys.stdin):
                nodes=None; failure=[]
                try: nodes=Postgres().parse(c['sql'],error_level=ErrorLevel[c['level']])
                except ParseError as e: failure=e.errors
                out.append(dict(ast=None if nodes is None else [dump(n) for n in nodes],failure=failure))
            print(json.dumps(out))
            """;
        Equal(cases.Select(c => c.sql + " " + c.level).ToArray(), await Python(script, JsonSerializer.Serialize(cases)), actual);
    }

    // Formerly NotPorted range callables; expected values come from the Python oracle (read="postgres").
    [Theory]
    [InlineData("a GLOB b", "Glob", "a GLOB b")]
    [InlineData("a RLIKE b", "RegexpLike", "a ~ b")]
    [InlineData("a OPERATOR(pg_catalog.~) b", "Operator", "a OPERATOR(pg_catalog.~) b")]
    [InlineData("x ? y", "JSONBContainsTopKey", "x ? y")]
    [InlineData("a @> b", "ArrayContainsAll", "a @> b")]
    [InlineData("a <@ b", "ArrayContainedBy", "a <@ b")]
    [InlineData("a ?| b", "JSONBContainsAnyTopKeys", "a ?| b")]
    public void Range_callables_match_upstream(string sql, string node, string expected)
    {
        Assert.Equal(node, Assert.Single(SqlGlot.Parse(sql, "postgres"))!.GetType().Name);
        Assert.Equal(expected, Assert.Single(SqlGlot.Transpile(sql, "postgres")));
    }

    private static async Task Compare(List<Case> cases)
    {
        var prepared = cases.Select(c => tokens.Tokenizer.ForDialect(c.dialect).tokenize(c.sql)).ToArray();
        var input = cases.Select((c, i) => new { c.sql, c.dialect, c.precedence, c.level, c.max_nodes, c.method, c.reverse,
            tokens = prepared[i].Select(Snapshot).ToArray() }).ToArray();
        var actual = cases.Select((c, i) =>
        {
            var p = Start(c, prepared[i]); exp.Expr? node = null; List<PyDict<string, object?>> failure = [];
            try
            {
                node = c.method switch
                {
                    "binary" => parser.binary_range_parser(typeof(exp.Like), c.reverse)(p, new exp.Column(@this: new exp.Identifier(@this: "lhs", quoted: false))),
                    "escape" => p._parse_escape(new exp.Like(@this: new exp.Literal(@this: "a", is_string: true), expression: new exp.Literal(@this: "b", is_string: true))),
                    _ => p._parse_disjunction()
                };
            }
            catch (errors.ParseError e) { failure = e.errors; }
            return new { ast = serde.dump(node), failure, errors = p.errors.Select(e => e.errors).ToArray(), index = p._index,
                curr = Snapshot(p._curr), next = Snapshot(p._next), prev = Snapshot(p._prev), prev_comments = p._prev_comments.ToArray(),
                token_comments = prepared[i].Select(t => t.comments.ToArray()).ToArray(), node_count = p._node_count };
        }).ToArray();
        const string script = """
            import sys,json
            sys.path.insert(0,'python')
            from sqlglot import exp
            from sqlglot.parser import Parser,IsPredicatePrecedence,binary_range_parser
            from sqlglot.tokens import Token,TokenType
            from sqlglot.errors import ParseError,ErrorLevel
            from sqlglot.serde import dump
            def snap(t):
                return dict(type=t.token_type.name,text=t.text,line=t.line,col=t.col,start=t.start,end=t.end,comments=t.comments)
            out=[]
            for c in json.load(sys.stdin):
                class OracleParser(Parser):
                    IS_PREDICATE_PRECEDENCE=IsPredicatePrecedence[c['precedence']]
                p=OracleParser(dialect=c['dialect'],error_level=ErrorLevel[c['level']],max_nodes=c['max_nodes'])
                ts=[Token(TokenType[t['type']],t['text'],line=t['line'],col=t['col'],start=t['start'],end=t['end'],comments=t['comments']) for t in c['tokens']]
                p.sql=c['sql']; p._tokens=ts; p._tokens_size=len(ts); p._index=-1; p._advance()
                node=None; failure=[]
                try:
                    if c['method']=='binary': node=binary_range_parser(exp.Like,c['reverse'])(p,exp.Column(this=exp.Identifier(this='lhs',quoted=False)))
                    elif c['method']=='escape': node=p._parse_escape(exp.Like(this=exp.Literal(this='a',is_string=True),expression=exp.Literal(this='b',is_string=True)))
                    else: node=p._parse_disjunction()
                except ParseError as e: failure=e.errors
                out.append(dict(ast=dump(node),failure=failure,errors=[e.errors for e in p.errors],index=p._index,
                    curr=snap(p._curr),next=snap(p._next),prev=snap(p._prev),prev_comments=p._prev_comments,
                    token_comments=[t.comments for t in ts],node_count=p._node_count))
            print(json.dumps(out))
            """;
        Equal(cases.Select(c => c.ToString()).ToArray(), await Python(script, JsonSerializer.Serialize(input)), actual);
    }
    private static void Equal(string[] cases, string expectedJson, object actual)
    {
        var expected = JsonNode.Parse(expectedJson)!.AsArray(); var got = JsonSerializer.SerializeToNode(actual)!.AsArray();
        Assert.Equal(expected.Count, got.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            // Required args are a Python set. Sort diagnostics only, not AST/state.
            foreach (var n in new[] { expected[i]!, got[i]! })
                foreach (var key in new[] { "failure", "errors" })
                    if (n[key] is JsonArray a) n[key] = new JsonArray(a.Select(e => e!.DeepClone()).OrderBy(e => e.ToJsonString(), StringComparer.Ordinal).ToArray());
            Assert.True(JsonNode.DeepEquals(expected[i], got[i]), $"Case {cases[i]}\nPython: {expected[i]}\nC#: {got[i]}");
        }
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
