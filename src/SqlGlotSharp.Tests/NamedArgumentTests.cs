using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dialect = SqlGlotSharp.dialects.dialect.Dialect;
using Token = SqlGlotSharp.tokenizer_core.Token;

namespace SqlGlotSharp.Tests;

public class NamedArgumentTests
{
    private static readonly string[] Queries =
    [
        "SELECT create_hypertable('m','ts',if_not_exists=>TRUE)",
        "SELECT create_hypertable('m',by_range('ts',INTERVAL '1 day'),if_not_exists=>TRUE,migrate_data=>FALSE)",
        "SELECT add_retention_policy('m',INTERVAL '30 days',if_not_exists=>TRUE)",
        "SELECT time_bucket_gapfill(INTERVAL '1 hour',ts,start=>'2024-01-01',finish=>'2024-01-02') FROM m",
        "SELECT time_bucket_gapfill(INTERVAL '1 hour',ts,start=>bounds(which=>1),finish=>bounds(which=>2))",
        "SELECT create_hypertable('m','ts',if_not_exists:=TRUE)",
        "SELECT add_retention_policy('m',INTERVAL '30 days',if_not_exists:=TRUE)",
        "SELECT mystery(1, Foo=>nested(x=>2,y:=3), \"MiXeD\"=>FALSE, Foo=>4, 5)",
        "SELECT mystery(/* open */ 1 /* pos */, /* comma */ name /* key */ => /* arrow */ nested(x=>2 /* rhs */) /* inner */) /* close */",
        "SELECT mystery((one,two)=>3, (\"MiXeD\")=>4)",
        "SELECT mystery(name=>1+2*3, flag=>a AND b, value=>CASE WHEN a THEN 1 ELSE 2 END)",
        "SELECT mystery(name:=(other:=2), value=>other(name=>3))"
    ];

    [Fact]
    public async Task Production_Postgres_and_Timescale_parse_generate_reparse_match_live_Python()
    {
        var cases = Queries.SelectMany(sql => new[] { "postgres", "timescale" }
            .SelectMany(dialect => new[] { false, true }.Select(pretty => new { sql, dialect, pretty }))).ToArray();
        const string script = """
            import sys, json
            sys.path.insert(0, 'python'); sys.path.insert(0, 'src/tools')
            from oracle_plugins.timescale import Timescale
            from sqlglot.dialects.dialect import Dialect
            from sqlglot.serde import dump
            results = []
            for c in json.load(sys.stdin):
                d = Dialect.get_or_raise(c['dialect'])
                ast = d.parse(c['sql'])
                sql = [d.generate(n, pretty=c['pretty']) for n in ast]
                results.append(dict(ast=[dump(n) for n in ast], sql=sql,
                    reparsed=[[dump(n) for n in d.parse(s)] for s in sql]))
            print(json.dumps(results))
            """;
        var expected = JsonNode.Parse(await Python(script, JsonSerializer.Serialize(cases)))!.AsArray();
        for (var i = 0; i < cases.Length; i++)
        {
            var c = cases[i];
            var d = Dialect.get_or_raise(c.dialect);
            var ast = d.parse(c.sql);
            var sql = ast.Select(n => d.generator(c.pretty).generate(n!)).ToArray();
            var actual = JsonSerializer.SerializeToNode(new
            {
                ast = ast.Select(serde.dump).ToArray(), sql,
                reparsed = sql.Select(s => d.parse(s).Select(serde.dump).ToArray()).ToArray()
            }, serde.JsonOptions);
            Assert.True(JsonNode.DeepEquals(expected[i], actual), $"{c}\nPython: {expected[i]}\nC#: {actual}");
        }
    }

    private sealed record Case(string sql, string method, string dialect, string level, bool alias = false, int max_nodes = -1);
    private static object Snapshot(Token t) => new
    {
        type = t.token_type.ToString(), t.text, t.line, t.col, t.start, t.end, comments = t.comments.ToArray()
    };

    [Fact]
    public async Task Lambda_and_assignment_helpers_match_AST_errors_comments_validation_and_cursor()
    {
        List<Case> cases = [];
        foreach (var dialect in new[] { "postgres", "timescale", "" })
        foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
        {
            foreach (var sql in new[]
            {
                "a=>1", "\"MiXeD\"=>nested(x=>TRUE)", "(a)=>1", "(a,b)=>2",
                "a /* key */ => /* arrow */ nested(1,x=>2 /* value */) /* after */ , next",
                "a=>", "a=>)", "a=>1+", "a=>nested(x=>)", "a=>SELECT 1", "a=>1:=2",
                "a:=1", "a:=b:=2", "a:=nested(x=>2)", "a.b:=2", "a:=", "a:=)",
                "1", "(1+2)", "other(1,a=>2)", "ALL other(x=>1)", "1 AS alias"
            })
                foreach (var alias in new[] { false, true }) cases.Add(new(sql, "_parse_lambda", dialect, level, alias));
            foreach (var sql in new[] { "a:=1", "a:=b:=2", "a.b:=2", "a:=", "a:=)", "SELECT:=2", "1:=2" })
                cases.Add(new(sql, "_parse_assignment", dialect, level));
            foreach (var sql in new[] { "1,a=>TRUE,b=>nested(x=>2),", "a=>1, a=>2, 3", "a:=1,b=>2", "a=>)" })
                cases.Add(new(sql, "_parse_function_args", dialect, level));
        }
        foreach (var max in new[] { 0, 1, 2, 3, 4, 5, 10 })
        foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
            cases.Add(new("key=>nested(other=>1)", "_parse_lambda", "postgres", level, max_nodes: max));
        var prepared = cases.Select(c => tokens.Tokenizer.ForDialect(c.dialect).tokenize(c.sql)).ToArray();
        var input = cases.Select((c, i) => new { c.sql, c.method, c.dialect, c.level, c.alias, c.max_nodes,
            tokens = prepared[i].Select(Snapshot).ToArray() }).ToArray();
        const string script = """
            import sys, json
            sys.path.insert(0, 'python'); sys.path.insert(0, 'src/tools')
            from oracle_plugins.timescale import Timescale
            from sqlglot import exp
            from sqlglot.dialects.dialect import Dialect
            from sqlglot.tokens import Token, TokenType
            from sqlglot.errors import ParseError, ErrorLevel
            from sqlglot.serde import dump
            def snapshot(t):
                return dict(type=t.token_type.name,text=t.text,line=t.line,col=t.col,start=t.start,end=t.end,comments=t.comments)
            results = []
            for c in json.load(sys.stdin):
                ts = [Token(TokenType[t['type']],t['text'],line=t['line'],col=t['col'],start=t['start'],end=t['end'],comments=t['comments']) for t in c['tokens']]
                p = Dialect.get_or_raise(c['dialect']).parser(error_level=ErrorLevel[c['level']],max_nodes=c['max_nodes'])
                p.sql=c['sql']; p._tokens=ts; p._tokens_size=len(ts); p._index=-1; p._advance()
                node, failure = None, []
                try:
                    if c['method']=='_parse_assignment': node=p._parse_assignment()
                    elif c['method']=='_parse_function_args': node=exp.Tuple(expressions=p._parse_function_args())
                    else: node=p._parse_lambda(alias=c['alias'])
                except ParseError as e: failure=e.errors
                results.append(dict(ast=dump(node),failure=failure,errors=[e.errors for e in p.errors],index=p._index,
                    curr=snapshot(p._curr),next=snapshot(p._next),prev=snapshot(p._prev),
                    prev_comments=p._prev_comments[:],token_comments=[t.comments[:] for t in ts],
                    consumed=[snapshot(t) for t in ts[:p._index]],node_count=p._node_count))
            print(json.dumps(results))
            """;
        var expected = JsonNode.Parse(await Python(script, JsonSerializer.Serialize(input)))!.AsArray();
        for (var i = 0; i < cases.Count; i++)
        {
            var c = cases[i];
            var p = Dialect.get_or_raise(c.dialect).parser(Enum.Parse<errors.ErrorLevel>(c.level));
            p.max_nodes = c.max_nodes;
            var ts = prepared[i];
            p.sql = c.sql; p._tokens = ts; p._tokens_size = ts.Count; p._index = -1; p._advance();
            exp.Expr? node = null; List<PyDict<string, object?>> failure = [];
            try
            {
                node = c.method switch
                {
                    "_parse_assignment" => p._parse_assignment(),
                    "_parse_function_args" => new exp.Tuple(expressions: p._parse_function_args().Cast<object?>().ToList()),
                    _ => p._parse_lambda(c.alias)
                };
            }
            catch (errors.ParseError e) { failure = e.errors; }
            var actual = JsonSerializer.SerializeToNode(new
            {
                ast = serde.dump(node), failure, errors = p.errors.Select(e => e.errors).ToArray(), index = p._index,
                curr = Snapshot(p._curr), next = Snapshot(p._next), prev = Snapshot(p._prev),
                prev_comments = p._prev_comments.ToArray(), token_comments = ts.Select(t => t.comments.ToArray()).ToArray(),
                consumed = ts.Take((int)p._index).Select(Snapshot).ToArray(), node_count = p._node_count
            }, serde.JsonOptions);
            Assert.True(JsonNode.DeepEquals(expected[i], actual), $"Case {i}: {c}\nPython: {expected[i]}\nC#: {actual}");
        }
    }

    [Fact]
    public void Kwarg_names_and_argument_order_are_not_normalized_or_dropped()
    {
        var d = Dialect.get_or_raise("timescale");
        var ast = Assert.Single(d.parse("SELECT mystery(1, Foo=>2, \"MiXeD\"=>3, Foo=>4, 5)"))!;
        var call = Assert.Single(ast.walk().OfType<exp.Anonymous>());
        Assert.Equal(5, call.expressions.Count);
        var kwargs = call.expressions.OfType<exp.Kwarg>().ToArray();
        Assert.Equal(new[] { "Foo", "MiXeD", "Foo" }, kwargs.Select(k => Assert.IsType<exp.Var>(k.@this).name));
        Assert.Equal("SELECT MYSTERY(1, Foo => 2, MiXeD => 3, Foo => 4, 5)", d.generator().generate(ast));
    }

    private static async Task<string> Python(string script, string input, [CallerFilePath] string source = "")
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SQLGLOT_PYTHON") ?? "python")
        {
            WorkingDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..")),
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
