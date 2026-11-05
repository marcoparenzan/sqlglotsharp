using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Pg = SqlGlotSharp.generators.postgres.PostgresGenerator;

namespace SqlGlotSharp.Tests;

public class GeneratorJoinCteTests
{
    private sealed record Node(string type, Dictionary<string, object?> args, List<string>? comments = null);
    private sealed record Case(Node node, bool pretty, bool comments, bool leading, bool copy = true,
        string method = "generate", string mode = "postgres", string? join_op = null);
    private sealed record State(string type, List<string>? comments);
    private sealed record Result(string sql, List<State> state, List<string> messages);
    private static Node N(string type, object? value = null, List<string>? comments = null,
        params (string key, object? value)[] extras)
    {
        var args = new Dictionary<string, object?> { ["this"] = value };
        foreach (var (key, item) in extras) args[key] = item;
        return new(type, args, comments);
    }
    private static Node I(string name) => N("Identifier", name);
    private static Node C(string name = "a") => N("Column", I(name), ["column */ /*"]);
    private static Node L(string value = "1") => N("Literal", value, extras: [("is_string", false)]);
    private static Node T(string name = "t") => N("Table", I(name), ["table"],
        ("alias", N("TableAlias", I("source"), ["table alias"])));
    private static Node S(params (string key, object? value)[] extras) => N("Select", comments: ["select\ncomment"],
        extras: [("expressions", new object?[] { C(), L() }), .. extras]);
    private static Node J(params (string key, object? value)[] extras) => N("Join", T(), ["join\ncomment"], extras);
    private static Node Cte(Node? body = null, object? materialized = null, bool alias = true) =>
        N("CTE", body ?? S(), ["cte */ /*", "second\ncomment"],
            ("alias", alias ? N("TableAlias", I("cte_name"), ["existing alias"],
                ("columns", new object?[] { I("a"), I("b") })) : null), ("materialized", materialized));
    private static object? BuildValue(object? value) => value switch
    {
        Node node => Build(node), object?[] list => list.Select(BuildValue).ToList(), _ => value
    };
    private static exp.Expr Build(Node node)
    {
        var args = new PyDict<string, object?>();
        foreach (var (key, value) in node.args) args[key] = BuildValue(value);
        var type = exp.EXPR_CLASSES.Values.Single(t => t.Name == node.type);
        var expression = (exp.Expr)Activator.CreateInstance(type, [args])!;
        expression.comments = node.comments?.ToList();
        return expression;
    }
    private static List<State> Snapshot(exp.Expr expression)
    {
        var states = new List<State>();
        void Visit(exp.Expr node)
        {
            states.Add(new(node.GetType().Name, node.comments?.ToList()));
            foreach (var value in node.args.Values)
            {
                if (value is exp.Expr child) Visit(child);
                else if (value is List<object?> list)
                    foreach (var item in list.OfType<exp.Expr>()) Visit(item);
            }
        }
        Visit(expression); return states;
    }
    private sealed class NoSide : generator.Generator { public override bool SEMI_ANTI_JOIN_WITH_SIDE => false; }
    private sealed class Directed : generator.Generator { public override bool DIRECTED_JOINS => true; }
    private sealed class NoRecursive : generator.Generator { public override bool CTE_RECURSIVE_KEYWORD_REQUIRED => false; }
    private sealed class NoHints : generator.Generator { public override bool QUERY_HINTS => false; }
    private sealed class BoolBoundary : generator.Generator { public override bool ENSURE_BOOLS => true; }
    private sealed class NestedBoundary : generator.Generator
    {
        public override bool ENSURE_BOOLS => false;
        public override HashSet<Type> EXPRESSIONS_WITHOUT_NESTED_CTES => [typeof(exp.Select)];
    }
    private static generator.Generator Generator(Case c)
    {
        generator.Generator g = c.mode switch
        {
            "base" => new(), "noside" => new NoSide(), "directed" => new Directed(),
            "norecursive" => new NoRecursive(), "nohints" => new NoHints(), _ => new Pg()
        };
        g.dialect = dialects.dialect.Dialect.get_or_raise(c.mode == "postgres" ? "postgres" : "");
        g.pretty = c.pretty; g.comments = c.comments; g.leading_comma = c.leading; g.max_text_width = 16;
        return g;
    }
    private const string Oracle = """
        import sys,json
        sys.path.insert(0,'python')
        from sqlglot import exp
        from sqlglot.dialects.postgres import Postgres
        from sqlglot.generator import Generator
        def build(v):
            if isinstance(v,list): return [build(x) for x in v]
            if not isinstance(v,dict) or 'type' not in v: return v
            n=getattr(exp,v['type'])(**{k:build(x) for k,x in v['args'].items()})
            n.comments=list(v['comments']) if v['comments'] is not None else None
            return n
        def state(n):
            out=[dict(type=type(n).__name__,comments=n.comments)]
            for v in n.args.values():
                if isinstance(v,exp.Expr): out.extend(state(v))
                elif isinstance(v,list):
                    for x in v:
                        if isinstance(x,exp.Expr): out.extend(state(x))
            return out
        out=[]
        for c in json.load(sys.stdin):
            options=dict(pretty=c['pretty'],comments=c['comments'],leading_comma=c['leading'],max_text_width=16)
            n=build(c['node'])
            if c['mode']=='postgres':
                # Real production dialect entry point; never override preprocess or SELECT.
                dialect=Postgres()
                g=dialect.generator(**options)
                text=dialect.generate(n,copy=c['copy'],**options) if c['method']=='generate' else g.sql(n)
                if c['method']=='generate':
                    # The dialect creates its own generator; capture warnings using an
                    # equivalent production generator on a fresh fixture, not the mutated n.
                    g.generate(build(c['node']),copy=c['copy'])
            else:
                class G(Generator): pass
                if c['mode']=='noside': G.SEMI_ANTI_JOIN_WITH_SIDE=False
                if c['mode']=='directed': G.DIRECTED_JOINS=True
                if c['mode']=='norecursive': G.CTE_RECURSIVE_KEYWORD_REQUIRED=False
                if c['mode']=='nohints': G.QUERY_HINTS=False
                g=G(**options)
                text=g.join_sql(n,join_op=c['join_op']) if c['method']=='join_sql' else g.sql(n)
            out.append(dict(sql=text,state=state(n),messages=g.unsupported_messages))
        print(json.dumps(out))
        """;

    [Fact]
    public async Task Production_PostgreSQL_and_bounded_helpers_match_live_Python_AST_fixtures()
    {
        var cases = new List<Case>();
        foreach (var pretty in new[] { false, true })
        foreach (var comments in new[] { false, true })
        foreach (var leading in new[] { false, true })
        {
            void Add(Node n, string method = "generate", string mode = "postgres", string? op = null) =>
                cases.Add(new(n, pretty, comments, leading, method: method, mode: mode, join_op: op));
            var predicate = N("And", N("EQ", C(), extras: [("expression", L())]), ["and"],
                ("expression", N("EQ", C("b"), extras: [("expression", L("2"))])));
            Add(J()); Add(J(("on", predicate))); Add(J(("using", new object?[] { I("a"), I("b") })));
            // Python selects USING formatting when both ON and USING are present.
            Add(J(("on", predicate), ("using", new object?[] { I("ignored") })));
            Add(J(("using", Array.Empty<object?>())));
            foreach (var kind in new[] { "inner", "outer", "cross", "semi", "anti", "straight_join" })
            foreach (var side in new[] { "", "left", "right", "full" })
            {
                var join = J(("kind", kind), ("side", side), ("method", "natural"),
                    ("hint", "hash"), ("global_", true), ("directed", true), ("on", predicate));
                Add(join); Add(join, "sql", "noside"); Add(join, "sql", "directed");
            }
            Add(J(("kind", "straight_join"))); Add(J(("kind", "cross")));
            Add(J(("on", predicate), ("match_condition", N("GT", C(), extras: [("expression", L())])),
                ("expressions", new object?[] { T("extra"), T("more") })));
            Add(J(("kind", "left"), ("on", predicate)), "join_sql", "base", "OVERRIDE");
            Add(J(), "join_sql", "base", "IGNORED_FOR_COMMA");
            Add(N("Hint", extras: [("expressions", new object?[] {
                N("JoinHint", N("Var", "HASH_JOIN"), extras: [("expressions", new object?[] { I("a"), I("b") })]),
                N("Var", "ORDERED") })]));
            Add(N("Hint", extras: [("expressions", new object?[] { N("Property") })]), "sql", "nohints");
            var joins = new object?[] { J(("kind", "left"), ("on", predicate)), J(("using", new object?[] { I("a"), I("b") })) };
            var query = S(("from_", N("From", T("base"))), ("joins", joins));
            Add(query); Add(N("Table", I("nested"), extras: [("joins", joins)]));
            foreach (var materialized in new object?[] { null, false, true })
            foreach (var alias in new[] { false, true })
            {
                var cte = Cte(query, materialized, alias);
                Add(cte);
                cases.Add(new(cte, pretty, comments, leading, copy: false));
                Add(cte, "sql");
                var with = N("With", comments: ["with comment"], extras: [("expressions", new object?[] { cte, Cte() }), ("recursive", true)]);
                Add(with); Add(with, "sql", "norecursive");
                Add(S(("with_", with), ("from_", N("From", T("cte_name"))), ("joins", joins)));
            }
            Add(N("With", extras: [("recursive", true)]));
            var keyed = Cte(); keyed.args["key_expressions"] = new object?[] { I("a"), I("b") }; Add(keyed);
            Add(Cte(S(("with_", N("With", extras: [("expressions", new object?[] { Cte() })])))));
        }
        var expected = JsonSerializer.Deserialize<Result[]>(await Python(Oracle, JsonSerializer.Serialize(cases)))!;
        Assert.Equal(cases.Count, expected.Length);
        for (var i = 0; i < cases.Count; i++)
        {
            var c = cases[i]; var g = Generator(c); var n = Build(c.node);
            var actual = c.method switch { "generate" => g.generate(n, c.copy), "join_sql" => g.join_sql((exp.Join)n, c.join_op), _ => g.sql(n) };
            Assert.True(expected[i].sql == actual, $"Case {i}: {JsonSerializer.Serialize(c)}\nPython: {expected[i].sql}\nC#: {actual}");
            Assert.Equal(JsonSerializer.Serialize(expected[i].state), JsonSerializer.Serialize(Snapshot(n)));
            Assert.Equal(expected[i].messages, g.unsupported_messages);
        }
    }

    [Fact]
    public void Missing_children_and_generic_preprocess_remain_explicit_boundaries()
    {
        var g = new Pg();
        foreach (var (node, method) in new[] {
            (J(("on", N("Get"))), "TRANSFORMS[Get]"),
            (J(("using", new object?[] { N("Get") })), "TRANSFORMS[Get]"),
            (J(("kind", "left"), ("pivots", new object?[] { N("Pivot") })), "Pivot"),
        })
        {
            var error = Record.Exception(() => g.generate(Build(node)));
            Assert.True(error is NotPortedException, $"Expected boundary {method}: {JsonSerializer.Serialize(node)}; got {error}");
            Assert.Contains(method, error.Message);
        }
        var cte = Build(Cte(N("Get")));
        Assert.Contains("TRANSFORMS[Get]", Assert.Throws<NotPortedException>(() => g.sql(cte)).Message);
        Assert.Null(cte.comments);
        Assert.Contains("cte */ /*", ((exp.Expr)cte.args["alias"]!).comments!);
        Assert.NotNull(new BoolBoundary().generate(Build(S()))); // ensure_bools is ported
        var nested = Build(S(("expressions", new object?[] { N("Subquery", S(("with_", N("With", extras: [("expressions", new object?[] { Cte() })])))) })));
        Assert.StartsWith("WITH", new NestedBoundary().generate(nested)); // nested CTEs are hoisted
    }

    [Fact]
    public async Task Handler_anchors_match_current_upstream_algorithms()
    {
        const string script = """
            import sys,json
            from pathlib import Path
            sys.path.insert(0,'src/tools')
            import port_status as p
            names={'join_sql','with_sql','cte_sql','hint_sql','joinhint_sql'}
            ms=p.inventory(Path('python'),json.loads(Path('src/tools/scope.json').read_text()))
            entries=[x for x in p.report(ms,p.scan_anchors(Path('src/SqlGlotSharp')))['members'] if x['anchor'].startswith('sqlglot/generator.py::Generator.') and x['anchor'].split('.')[-1] in names]
            print(json.dumps({x['anchor']:x['hash'] for x in entries if x['status']!='ported'}))
            """;
        var errors = (await Python(script, "")).Trim();
        Assert.True(errors == "{}", errors);
    }

    private static string SourcePath([CallerFilePath] string source = "") => source;
    private static async Task<string> Python(string script, string input)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SQLGLOT_PYTHON") ?? "python")
        {
            WorkingDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath())!, "..", "..")),
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
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
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, await stderr); return await stdout;
    }
}
