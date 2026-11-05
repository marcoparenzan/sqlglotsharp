using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pg = SqlGlotSharp.generators.postgres.PostgresGenerator;

namespace SqlGlotSharp.Tests;

public class PostgresPipelineTests
{
    private sealed record Node(string type, Dictionary<string, object?> args, List<string>? comments = null);
    private sealed record Case(Node node, bool pretty, bool copy, bool identify);
    private sealed record Result(string sql, Node ast);
    private static Node N(string type, params (string key, object? value)[] args)
    {
        var values = new Dictionary<string, object?>();
        foreach (var (key, value) in args) values[key] = value;
        return new(type, values);
    }
    private static Node I(string name) => N("Identifier", ("this", name));
    private static Node C(string name) => N("Column", ("this", I(name)));
    private static Node L(string value) => N("Literal", ("this", value), ("is_string", false));
    private static Node T(string name) => N("Table", ("this", I(name)));
    private static Node Eq(string name, string value) => N("EQ", ("this", C(name)), ("expression", L(value)));
    private static Node Select(params (string key, object? value)[] extras) => N("Select", [
        ("expressions", new object?[] { C("A"), N("Alias", ("this", C("B")), ("alias", I("Result"))) }),
        ("from_", N("From", ("this", T("tbl")))), .. extras]);
    private static object? BuildValue(object? value) => value switch
    {
        Node n => Build(n), object?[] values => values.Select(BuildValue).ToList(), _ => value
    };
    private static exp.Expr Build(Node n)
    {
        var args = new PyDict<string, object?>();
        foreach (var (key, value) in n.args) args[key] = BuildValue(value);
        var type = exp.EXPR_CLASSES.Values.Single(t => t.Name == n.type);
        var e = (exp.Expr)Activator.CreateInstance(type, [args])!;
        e.comments = n.comments?.ToList();
        return e;
    }
    private static object? SnapshotValue(object? value) => value switch
    {
        exp.Expr e => Snapshot(e), System.Collections.IList values => values.Cast<object?>().Select(SnapshotValue).ToArray(), _ => value
    };
    private static Node Snapshot(exp.Expr e) => new(e.GetType().Name,
        e.args.ToDictionary(x => x.Key, x => SnapshotValue(x.Value)), e.comments);
    private static void SameAst(object expected, object actual) => Assert.True(
        JsonNode.DeepEquals(JsonSerializer.SerializeToNode(expected), JsonSerializer.SerializeToNode(actual)),
        $"Expected {JsonSerializer.Serialize(expected)}\nActual {JsonSerializer.Serialize(actual)}");
    private static Pg Generator(bool pretty = false, bool identify = false) => new()
    {
        dialect = dialects.dialect.Dialect.get_or_raise("postgres"), pretty = pretty,
        identify = identify, normalize = true, max_text_width = 18
    };

    private const string Oracle = """
        import sys,json
        sys.path.insert(0,'python')
        from sqlglot import exp
        from sqlglot.generators.postgres import PostgresGenerator
        def build(v):
            if isinstance(v,list): return [build(x) for x in v]
            if not isinstance(v,dict) or 'type' not in v: return v
            e=getattr(exp,v['type'])(**{k:build(x) for k,x in v['args'].items()})
            e.comments=v['comments']
            return e
        def snapshot(v):
            if isinstance(v,exp.Expr): return dict(type=type(v).__name__,args={k:snapshot(x) for k,x in v.args.items()},comments=v.comments)
            if isinstance(v,list): return [snapshot(x) for x in v]
            return v
        out=[]
        for c in json.load(sys.stdin):
            e=build(c['node'])
            g=PostgresGenerator(dialect='postgres',pretty=c['pretty'],identify=c['identify'],normalize=True,max_text_width=18)
            sql=g.generate(e,copy=c['copy'])
            out.append(dict(sql=sql,ast=snapshot(e)))
        print(json.dumps(out))
        """;

    [Fact]
    public async Task Production_generate_SQL_and_mutation_match_Python()
    {
        var predicate = N("Or", ("this", Eq("A", "1")), ("expression", Eq("B", "2")));
        var basic = Select(("where", N("Where", ("this", predicate))),
            ("group", N("Group", ("expressions", new object?[] { C("A"), C("B") }))),
            ("limit", N("Limit", ("expression", L("10")))), ("offset", N("Offset", ("expression", L("2")))));
        Node Join(string kind, string table = "other", Node? on = null) => N("Join", ("this", T(table)), ("kind", kind), ("on", on ?? Eq("A", "3")));
        var nodes = new[]
        {
            Select(), basic,
            Select(("joins", new object?[] { Join("SEMI") })),
            Select(("joins", new object?[] { Join("anti") })),
            Select(("where", N("Where", ("this", predicate))), ("joins", new object?[] { Join("SEMI"), Join("ANTI", "third") })),
            basic with { comments = ["query */ /*", "line\ncomment"] },
            N("Select", ("expressions", new object?[] { C("A") }),
                ("from_", N("From", ("this", N("Subquery", ("this", basic),
                    ("alias", N("TableAlias", ("this", I("sub")))))))))
        };
        var cases = nodes.SelectMany(n => new[] { false, true }.SelectMany(pretty =>
            new[] { false, true }.SelectMany(copy => new[] { false, true }.Select(identify => new Case(n, pretty, copy, identify))))).ToArray();
        var expected = JsonSerializer.Deserialize<Result[]>(await Python(Oracle, JsonSerializer.Serialize(cases)))!;
        for (var i = 0; i < cases.Length; i++)
        {
            var c = cases[i]; var e = Build(c.node); var g = Generator(c.pretty, c.identify);
            g.unsupported_messages.Add("stale message");
            Assert.Equal(expected[i].sql, g.generate(e, c.copy));
            Assert.Empty(g.unsupported_messages);
            SameAst(expected[i].ast, Snapshot(e));
            foreach (var child in e.walk().Skip(1))
            {
                Assert.NotNull(child.parent);
                Assert.NotNull(child.arg_key);
                var slot = child.parent.args[child.arg_key];
                Assert.Same(child, child.index is int index ? ((System.Collections.IList)slot!)[index] : slot);
            }
        }
    }

    [Fact]
    public void Ordinary_Cte_generation_is_available()
    {
        var node = Select(("with_", N("With", ("expressions", new object?[] {
            N("CTE", ("this", Select()), ("alias", N("TableAlias", ("this", I("cte")))))
        }))));
        Assert.Contains("WITH cte AS (SELECT", Generator().generate(Build(node)));
    }

    [Theory]
    [InlineData("SELECT 1", "SELECT 1")]
    [InlineData("SELECT a AS x FROM tbl WHERE a > 2 GROUP BY a LIMIT 3", "SELECT a AS x FROM tbl WHERE a > 2 GROUP BY a LIMIT 3")]
    [InlineData("SELECT a FROM tbl ORDER BY a DESC LIMIT 3 OFFSET 1", "SELECT a FROM tbl ORDER BY a DESC LIMIT 3 OFFSET 1")]
    public void Parsed_ordinary_SELECT_uses_real_Postgres_generation(string input, string expected)
    {
        var dialect = dialects.dialect.Dialect.get_or_raise("postgres");
        var expression = Assert.Single(dialect.parse(input))!;
        Assert.Equal(expected, Generator().generate(expression));
    }

    private sealed class SelectOverride : Pg
    {
        public override string select_sql(exp.Select expression) => "VIRTUAL SELECT";
    }
    private sealed class BoolGenerator : generator.Generator { public override bool ENSURE_BOOLS => true; }
    private sealed class RestrictedCteGenerator : generator.Generator
    {
        public override HashSet<Type> EXPRESSIONS_WITHOUT_NESTED_CTES => [typeof(exp.Select)];
    }

    [Fact]
    public void Missing_rewrites_throw_and_the_transform_keeps_handler_precedence()
    {
        var pg = Generator();
        Assert.Equal("VIRTUAL SELECT", new SelectOverride().generate(Build(Select())));
        Assert.Same(pg.preprocess(Build(Select())).GetType(), typeof(exp.Select));
        foreach (var level in Enum.GetValues<errors.ErrorLevel>())
        {
            pg.unsupported_level = level;
            var qualify = Build(Select(("qualify", N("Qualify", ("this", Eq("A", "1"))))));
            Assert.Contains("_t", pg.generate(qualify));
        }
        Assert.Contains("CURRENT_DATE", pg.generate(Build(Select(("expressions", new object?[] { N("CurrentDate") })))));
        Assert.IsType<exp.Select>(new BoolGenerator().preprocess(Build(Select()))); // ensure_bools is ported
        var inner = Select(("with_", N("With", ("expressions", new object?[] { N("CTE", ("this", Select()), ("alias", N("TableAlias", ("this", I("cte"))))) }))));
        var outer = Build(N("Select", ("expressions", new object?[] { N("Subquery", ("this", inner)) })));
        Assert.Same(outer, pg.preprocess(outer)); // PostgreSQL permits nested CTEs; no rewrite needed.
        var moved = new RestrictedCteGenerator().preprocess(outer);
        Assert.IsType<exp.With>(moved.args.get("with_")); // nested CTEs are hoisted to the top level
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
        await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
        process.StandardInput.Close();
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, await stderr);
        return await stdout;
    }
}
