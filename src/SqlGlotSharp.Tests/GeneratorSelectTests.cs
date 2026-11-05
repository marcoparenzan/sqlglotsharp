using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Pg = SqlGlotSharp.generators.postgres.PostgresGenerator;

namespace SqlGlotSharp.Tests;

public class GeneratorSelectTests
{
    private sealed record Node(string type, Dictionary<string, object?> args, List<string>? comments = null);
    private sealed record Case(Node node, bool postgres, bool pretty, bool comments, bool leading,
        string mode = "ordinary", string method = "sql");
    private static Node N(string type, object? value = null, List<string>? comments = null,
        params (string key, object? value)[] extras)
    {
        var args = new Dictionary<string, object?> { ["this"] = value };
        foreach (var (key, item) in extras) args[key] = item;
        return new(type, args, comments);
    }
    private static Node I(string value) => N("Identifier", value);
    private static Node C(string value = "a") => N("Column", I(value), ["column */ /*"]);
    private static Node L(string value = "2") => N("Literal", value, extras: [("is_string", false)]);
    private static Node S(params (string key, object? value)[] extras) =>
        N("Select", comments: ["select\ncomment"], extras: [("expressions", new object?[] { C(), L() }), .. extras]);
    private static object? BuildValue(object? value) => value switch
    {
        Node node => Build(node),
        object?[] list => list.Select(BuildValue).ToList(),
        _ => value
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
    private const string Oracle = """
        import sys,json
        sys.path.insert(0,'python')
        from sqlglot import exp
        from sqlglot.generator import Generator
        from sqlglot.generators.postgres import PostgresGenerator
        def build(v):
            if isinstance(v,list): return [build(x) for x in v]
            if not isinstance(v,dict) or 'type' not in v: return v
            n=getattr(exp,v['type'])(**{k:build(x) for k,x in v['args'].items()})
            n.comments=v['comments']
            return n
        out=[]
        for c in json.load(sys.stdin):
            base=PostgresGenerator if c['postgres'] else Generator
            class G(base): pass
            if c['mode']=='top': G.LIMIT_IS_TOP=True
            if c['mode']=='fetch': G.LIMIT_FETCH='FETCH'
            if c['mode']=='limit': G.LIMIT_FETCH='LIMIT'
            if c['mode']=='literals': G.LIMIT_ONLY_LITERALS=True
            if c['mode']=='exclude': G.STAR_EXCLUDE_REQUIRES_DERIVED_TABLE=False
            g=G(dialect='postgres' if c['postgres'] else '',pretty=c['pretty'],comments=c['comments'],leading_comma=c['leading'],max_text_width=12)
            n=build(c['node'])
            out.append(getattr(g,c['method'])(n))
        print(json.dumps(out))
        """;

    private sealed class Top : generator.Generator { public override bool LIMIT_IS_TOP => true; }
    private sealed class Fetch : generator.Generator { public override string LIMIT_FETCH => "FETCH"; }
    private sealed class Limit : generator.Generator { public override string LIMIT_FETCH => "LIMIT"; }
    private sealed class Literals : generator.Generator { public override bool LIMIT_ONLY_LITERALS => true; }
    private sealed class Exclude : generator.Generator { public override bool STAR_EXCLUDE_REQUIRES_DERIVED_TABLE => false; }
    private static generator.Generator Generator(Case c)
    {
        generator.Generator g = c.mode switch
        {
            "top" => new Top(), "fetch" => new Fetch(), "limit" => new Limit(),
            "literals" => new Literals(), "exclude" => new Exclude(),
            _ => c.postgres ? new Pg() : new generator.Generator()
        };
        g.dialect = dialects.dialect.Dialect.get_or_raise(c.postgres ? "postgres" : "");
        g.pretty = c.pretty; g.comments = c.comments; g.leading_comma = c.leading; g.max_text_width = 12;
        return g;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Queries_and_helpers_match_Python_actual_nested_dispatch(bool postgres)
    {
        var cases = new List<Case>();
        foreach (var pretty in new[] { false, true })
        foreach (var comments in new[] { false, true })
        foreach (var leading in new[] { false, true })
        {
            void Add(Node node, string mode = "ordinary", string method = "sql") => cases.Add(new(node, postgres, pretty, comments, leading, mode, method));
            var table = N("Table", I("t"), ["table"], ("alias", N("TableAlias", I("source"))));
            var from = N("From", table, ["from comments intentionally suppressed"]);
            var predicate = N("And", N("GT", C(), extras: [("expression", L())]), ["and"],
                ("expression", N("EQ", C("b"), extras: [("expression", L("5"))])));
            var where = N("Where", predicate, ["where"]);
            var group = N("Group", comments: ["group"], extras: [("expressions", new object?[] { C(), C("b") })]);
            var having = N("Having", N("GT", C(), extras: [("expression", L("1"))]), ["having"]);
            var order = N("Order", comments: ["order"], extras: [("expressions", new object?[] {
                N("Ordered", C(), ["ordered"], ("desc", true), ("nulls_first", false)),
                N("Ordered", C("b"), extras: [("desc", false), ("nulls_first", true)]) })]);
            Add(S()); Add(S(("from_", from))); Add(S(("where", where))); Add(S(("order", order)));
            var query = S(("from_", from), ("where", where), ("group", group), ("having", having), ("order", order),
                ("qualify", N("Qualify", predicate, ["qualify"])), ("limit", N("Limit", extras: [("expression", L("10"))])),
                ("offset", N("Offset", extras: [("expression", L("3"))])));
            Add(query);
            Add(N("Paren", query, ["nested query"]));
            Add(S(("expressions", new object?[] { N("Alias", N("Add", C(), extras: [("expression", L())]),
                ["alias"], ("alias", I("result"))), C("other") }), ("from_", from)));
            Add(S(("kind", "VALUE"), ("operation_modifiers", new object?[] { N("Var", "ALL") })));
            Add(S(("kind", "UNRECOGNIZED")));
            Add(S(("exclude", new object?[] { C("b") })), "exclude");
            Add(query, method: "query_modifiers");
            Add(S(("options", new object?[] { N("Var", "OPTION_ONE"), N("Var", "OPTION_TWO") })), method: "query_modifiers");
            foreach (var all in new object?[] { null, false, true })
            foreach (var totals in new[] { false, true })
            {
                Add(N("Group", extras: [("expressions", new object?[] { C(), L() }), ("all", all), ("totals", totals)]));
                Add(N("Group", extras: [("all", all)]));
                foreach (var grouping in new[] { "Cube", "Rollup", "GroupingSets" })
                foreach (var empty in new[] { false, true })
                    Add(N("Group", extras: [("expressions", new object?[] { C() }), ("all", all),
                        (grouping == "GroupingSets" ? "grouping_sets" : grouping.ToLowerInvariant(),
                            new object?[] { N(grouping, extras: [("expressions", empty ? Array.Empty<object?>() : new object?[] { C(), L() })]) })]));
            }
            foreach (var percent in new[] { false, true })
            foreach (var rows in new[] { false, true })
            foreach (var ties in new[] { false, true })
            {
                var options = N("LimitOptions", extras: [("percent", percent), ("rows", rows), ("with_ties", ties)]);
                Add(options);
                Add(N("Fetch", extras: [("direction", "NEXT"), ("count", L("4")), ("limit_options", options)]));
                Add(N("Limit", extras: [("expression", L()), ("limit_options", options)]));
            }
            foreach (var mode in new[] { "ordinary", "top", "fetch", "limit", "literals" })
            foreach (var count in new object?[] { null, L("7") })
            {
                Add(S(("limit", N("Fetch", comments: ["fetch comment not copied to converted limit"],
                    extras: [("direction", "FIRST"), ("count", count)])), ("offset", N("Offset", extras: [("expression", L())]))), mode);
                Add(S(("limit", N("Limit", comments: ["limit"], extras: [("expression", L("8"))])),
                    ("offset", N("Offset", extras: [("expression", L())]))), mode);
            }
            Add(N("Limit", C(), ["limit"], ("offset", L("1")), ("expression", L("2")), ("expressions", new object?[] { C("b") })));
            Add(N("Offset", C(), ["offset"], ("expression", N("Add", L(), extras: [("expression", L())])),
                ("expressions", new object?[] { C("b") })));
            Add(S(("limit", N("Limit", extras: [("expression", C())]))), "top");
            Add(S(("limit", N("Limit", extras: [("expression", N("Var", "all"))]))));
            Add(S(("limit", N("Limit", extras: [("expression", N("Var", "ALL"))]))), "fetch");
        }
        // PostgreSQL SELECT runs an unported transform, not inherited base assembly.
        if (postgres) cases = cases.Where(c => c.method == "sql" && c.node.type is not ("Select" or "Paren")).ToList();
        var expected = JsonSerializer.Deserialize<string[]>(await Python(Oracle, JsonSerializer.Serialize(cases)))!;
        Assert.Equal(cases.Count, expected.Length);
        for (var i = 0; i < cases.Count; i++)
        {
            var c = cases[i]; var g = Generator(c); var node = Build(c.node);
            var actual = c.method == "sql" ? g.sql(node) : g.query_modifiers(node);
            Assert.True(expected[i] == actual, $"Case {i}: {JsonSerializer.Serialize(c)}\nPython: {expected[i]}\nC#: {actual}");
        }
    }

    [Fact]
    public async Task Conversion_copies_count_and_TOP_mutation_match_upstream()
    {
        const string script = """
            import sys,json
            sys.path.insert(0,'python')
            from sqlglot import exp
            from sqlglot.generator import Generator
            class Top(Generator): LIMIT_IS_TOP=True
            n=exp.Select(expressions=[exp.Literal.number(1)],limit=exp.Limit(expression=exp.Literal.number(2)))
            original=n.args['limit']
            text=Top().sql(n)
            print(json.dumps([text,n.args.get('limit') is None,original.parent is None]))
            """;
        var expected = JsonSerializer.Deserialize<JsonElement[]>(await Python(script))!;
        var limit = new exp.Limit(expression: exp.Literal.number(2));
        var query = new exp.Select(expressions: new List<object?> { exp.Literal.number(1) }, limit: limit);
        var copy = query.copy();
        Assert.Equal(expected[0].GetString(), new Top().sql(query));
        Assert.Equal(expected[1].GetBoolean(), query.args.get("limit") is null);
        Assert.Equal(expected[2].GetBoolean(), limit.parent is null);
        Assert.NotNull(copy.args.get("limit"));
        foreach (var g in new generator.Generator[] { new Limit(), new Fetch() })
        {
            var count = exp.Literal.number(4);
            exp.Expr original = g is Limit ? new exp.Fetch(count: count) : new exp.Limit(expression: count);
            var select = new exp.Select(expressions: new List<object?> { exp.Literal.number(1) }, limit: original);
            var parent = count.parent;
            g.sql(select);
            Assert.Same(original, select.args.get("limit"));
            Assert.Same(parent, count.parent);
            Assert.Same(count, original.args.get(g is Limit ? "count" : "expression"));
        }
    }

    [Fact]
    public void Unsupported_query_paths_and_pipeline_are_explicit()
    {
        var g = new generator.Generator();
        foreach (var node in new[] {
            S(("locks", new object?[] { N("Get") })),
            S(("sample", N("Get"))),
            S(("for_", N("Get"))),
            S(("expressions", new object?[] { N("Get") }))
        }) Assert.Throws<NotPortedException>(() => g.sql(Build(node)));
        var arithmetic = new exp.Add(@this: exp.Literal.number(1), expression: exp.Literal.number(2));
        Assert.Throws<NotPortedException>(() => new Literals().sql(new exp.Limit(expression: arithmetic)));
        Assert.Throws<NotPortedException>(() => new Literals().sql(new exp.Offset(expression: arithmetic)));
        Assert.Contains("SELECT", g.generate(Build(S())));
        Assert.Contains("SELECT", new Pg().sql(Build(S())));
        Assert.Equal("*", g.sql(new exp.Star()));
        Assert.Equal("1 # 2", new Pg().sql(new exp.BitwiseXor(@this: exp.Literal.number(1), expression: exp.Literal.number(2))));
        // Empty WITH renders no prefix; SELECT comment mutation remains upstream-compatible.
        var with = Build(S(("with_", N("With"))));
        Assert.Contains("SELECT", g.sql(with));
        Assert.Null(with.comments);
    }

    [Fact]
    public async Task Anchors_are_current_and_partial_algorithm_is_not_marked_complete()
    {
        const string script = """
            import sys,json
            from pathlib import Path
            sys.path.insert(0,'src/tools')
            import port_status as p
            ms=p.inventory(Path('python'),json.loads(Path('src/tools/scope.json').read_text()))
            entries=[x for x in p.report(ms,p.scan_anchors(Path('src/SqlGlotSharp')))['members'] if any(a['file']=='generator.select.cs' for a in x['csharp'])]
            partial={'select_sql','limit_sql','offset_sql','_simplify_unless_literal'}
            errors=[x for x in entries if x['status']!=('stub' if x['anchor'].split('.')[-1] in partial else 'ported')]
            assert len(entries)==16,len(entries)
            assert not errors,json.dumps(errors)
            print('OK')
            """;
        Assert.Equal("OK", (await Python(script)).Trim());
    }

    private static string SourcePath([CallerFilePath] string source = "") => source;
    private static async Task<string> Python(string script, string? input = null)
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
            if (input is not null) await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
            process.StandardInput.Close(); await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, await stderr); return await stdout;
    }
}
