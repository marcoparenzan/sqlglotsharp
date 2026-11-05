using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Pg = SqlGlotSharp.generators.postgres.PostgresGenerator;

namespace SqlGlotSharp.Tests;

public class GeneratorValuesTests
{
    private sealed record Node(string type, Dictionary<string, object?> args, List<string>? comments = null);
    private sealed record Case(Node node, bool postgres = false, bool pretty = false, bool comments = true,
        bool leading = false, long width = 80, string mode = "ordinary", string method = "generate",
        bool copy = true, string level = "WARN");
    private sealed record Result(string? sql, string[] messages, string? error, JsonElement state);
    private static Node N(string type, object? value = null, List<string>? comments = null,
        params (string key, object? value)[] extras)
    {
        var args = new Dictionary<string, object?>();
        if (value is not null) args["this"] = value;
        foreach (var (key, item) in extras) args[key] = item;
        return new(type, args, comments);
    }
    private static Node L(string value = "1", bool text = false) => N("Literal", value, extras: [("is_string", text)]);
    private static Node I(string value, bool quoted = false) => N("Identifier", value, extras: [("quoted", quoted)]);
    private static Node Row(params object?[] cells) => N("Tuple", comments: ["row\ncomment", "*/ /*"], extras: [("expressions", cells)]);
    private static Node Alias(string? name = "v", int columns = 2) => N("TableAlias", name is null ? null : I(name),
        ["alias comment"], ("columns", Enumerable.Range(0, columns).Select(i => (object?)I(i == 0 ? "column one" : "b", i == 0)).ToArray()));
    private static Node Values(Node? alias = null, params (string key, object? value)[] extras) =>
        N("Values", comments: ["values\ncomment"], extras: [("expressions", new object?[] {
            Row(L(), L("long 'string' cell", true)), Row(N("Null"), L("2")), Row(N("Boolean", true), L("3")) }),
            ("alias", alias), .. extras]);
    private static Node Select(Node from) => N("Select", extras: [("expressions", new object?[] { N("Star") }), ("from_", N("From", from))]);
    private static object? BuildValue(object? value) => value switch
    {
        Node node => Build(node), object?[] list => list.Select(BuildValue).ToList(), _ => value
    };
    private static exp.Expr Build(Node node)
    {
        var args = new PyDict<string, object?>();
        foreach (var (key, value) in node.args) args[key] = BuildValue(value);
        var expression = (exp.Expr)Activator.CreateInstance(exp.EXPR_CLASSES.Values.Single(t => t.Name == node.type), [args])!;
        expression.comments = node.comments?.ToList();
        return expression;
    }
    private sealed class NoWrap : generator.Generator { public override bool WRAP_DERIVED_VALUES => false; }
    private sealed class NoColumns : generator.Generator { public override bool SUPPORTS_TABLE_ALIAS_COLUMNS => false; }
    private sealed class NoTable : generator.Generator { public override bool VALUES_AS_TABLE => false; }
    private static generator.Generator G(Case c)
    {
        generator.Generator g = c.mode switch
        {
            "nowrap" => new NoWrap(), "nocolumns" => new NoColumns(), "notable" => new NoTable(),
            _ => c.postgres ? new Pg() : new generator.Generator()
        };
        g.dialect = dialects.dialect.Dialect.get_or_raise(c.postgres ? "postgres" : "");
        g.pretty = c.pretty; g.comments = c.comments; g.leading_comma = c.leading; g.max_text_width = c.width;
        g.unsupported_level = Enum.Parse<errors.ErrorLevel>(c.level);
        return g;
    }
    // Snapshot the ORIGINAL nodes, including reparenting to synthetic SELECTs in
    // the fallback branch. generate(copy=True) must leave these nodes untouched.
    private static List<exp.Expr> Nodes(exp.Expr root)
    {
        var result = new List<exp.Expr>();
        void Visit(exp.Expr node)
        {
            result.Add(node);
            foreach (var value in node.args.Values)
                if (value is exp.Expr child) Visit(child);
                else if (value is List<object?> list) foreach (var item in list) if (item is exp.Expr entry) Visit(entry);
        }
        Visit(root); return result;
    }
    private static object? Dump(object? value) => value switch
    {
        exp.Expr node => new { type = node.GetType().Name, args = node.args.ToDictionary(p => p.Key, p => Dump(p.Value)), comments = node.comments },
        List<object?> list => list.Select(Dump).ToArray(), _ => value
    };
    private static object State(List<exp.Expr> original) => original.Select(n => new
    {
        node = Dump(n), parent = n.parent?.GetType().Name, key = n.arg_key, index = n.index
    }).ToArray();
    private const string Oracle = """
        import sys,json
        sys.path.insert(0,'python')
        from sqlglot import exp
        from sqlglot.generator import Generator
        from sqlglot.generators.postgres import PostgresGenerator
        from sqlglot.errors import ErrorLevel
        def build(v):
            if isinstance(v,list): return [build(x) for x in v]
            if not isinstance(v,dict) or 'type' not in v: return v
            n=getattr(exp,v['type'])(**{k:build(x) for k,x in v['args'].items()})
            n.comments=v['comments']
            return n
        def nodes(n):
            out=[n]
            for v in n.args.values():
                if isinstance(v,exp.Expr): out.extend(nodes(v))
                elif isinstance(v,list):
                    for x in v:
                        if isinstance(x,exp.Expr): out.extend(nodes(x))
            return out
        def dump(n):
            if isinstance(n,list): return [dump(x) for x in n]
            if not isinstance(n,exp.Expr): return n
            return dict(type=type(n).__name__,args={k:dump(v) for k,v in n.args.items()},comments=n.comments)
        out=[]
        for c in json.load(sys.stdin):
            class G(PostgresGenerator if c['postgres'] else Generator): pass
            if c['mode']=='nowrap': G.WRAP_DERIVED_VALUES=False
            if c['mode']=='nocolumns': G.SUPPORTS_TABLE_ALIAS_COLUMNS=False
            if c['mode']=='notable': G.VALUES_AS_TABLE=False
            g=G(dialect='postgres' if c['postgres'] else '',pretty=c['pretty'],comments=c['comments'],leading_comma=c['leading'],
                max_text_width=c['width'],unsupported_level=getattr(ErrorLevel,c['level']))
            n=build(c['node']); original=nodes(n); sql=None; error=None
            try:
                if c['method']=='generate': sql=g.generate(n,copy=c['copy'])
                elif c['method']=='values_false': sql=g.values_sql(next(x for x in original if isinstance(x,exp.Values)),values_as_table=False)
                else: sql=g.sql(n)
            except Exception as e: error=type(e).__name__+': '+str(e)
            state=[dict(node=dump(x),parent=type(x.parent).__name__ if x.parent is not None else None,key=x.arg_key,index=x.index) for x in original]
            out.append(dict(sql=sql,error=error,messages=g.unsupported_messages,state=state))
        print(json.dumps(out))
        """;
    private static bool JsonEqual(JsonElement left, JsonElement right) =>
        left.ValueKind == right.ValueKind && (left.ValueKind switch
        {
            JsonValueKind.Object => left.EnumerateObject().Count() == right.EnumerateObject().Count() &&
                left.EnumerateObject().All(p => right.TryGetProperty(p.Name, out var r) && JsonEqual(p.Value, r)),
            JsonValueKind.Array => left.GetArrayLength() == right.GetArrayLength() && left.EnumerateArray().Zip(right.EnumerateArray()).All(p => JsonEqual(p.First, p.Second)),
            _ => left.ToString() == right.ToString()
        });
    private static async Task Compare(List<Case> cases)
    {
        var expected = JsonSerializer.Deserialize<Result[]>(await Python(Oracle, JsonSerializer.Serialize(cases)))!;
        for (var i = 0; i < cases.Count; i++)
        {
            var c = cases[i]; var g = G(c); var node = Build(c.node); var original = Nodes(node);
            string? actual = null, error = null;
            try { actual = c.method switch
            {
                "generate" => g.generate(node, copy: c.copy),
                "values_false" => g.values_sql(original.OfType<exp.Values>().First(), values_as_table: false),
                _ => g.sql(node)
            }; }
            catch (errors.UnsupportedError e) { error = "UnsupportedError: " + e.Message; }
            catch (InvalidOperationException e) when (e.Message == "reduce() of empty iterable with no initial value") { error = "TypeError: " + e.Message; }
            var state = JsonSerializer.SerializeToElement(State(original));
            Assert.True(expected[i].sql == actual && expected[i].error == error && expected[i].messages.SequenceEqual(g.unsupported_messages) && JsonEqual(expected[i].state, state),
                $"Case {i}: {JsonSerializer.Serialize(c)}\nPython: {JsonSerializer.Serialize(expected[i])}\nC#: {actual}, {error}, {string.Join(',', g.unsupported_messages)}\nState: {state}");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Production_generate_rows_aliases_wrappers_and_layout_match_live_Python(bool postgres)
    {
        var cases = new List<Case>();
        foreach (var pretty in new[] { false, true })
        foreach (var comments in new[] { false, true })
        foreach (var leading in new[] { false, true })
        foreach (var width in new long[] { 12, 80 })
        foreach (var copy in new[] { false, true })
        {
            void Add(Node n) => cases.Add(new(n, postgres, pretty, comments, leading, width, copy: copy));
            Add(Values()); Add(Values(Alias())); Add(Values(Alias(null))); Add(Values(Alias("v", 0)));
            Add(N("Values", extras: [("expressions", Array.Empty<object?>())]));
            Add(N("Values", extras: [("expressions", new object?[] { Row() })]));
            Add(Select(Values())); Add(Select(Values(Alias())));
            Add(N("From", N("Table", Values(), extras: [("alias", Alias("t"))])));
            Add(Select(N("Table", Values(Alias()), extras: [("alias", Alias("t"))])));
            Add(Select(N("Subquery", Values(), ["wrapper comment"], ("alias", Alias("q")))));
            Add(Select(N("Subquery", Values(Alias()), extras: [("alias", Alias("q"))])));
            Add(N("Subquery", N("Subquery", Values(), extras: [("alias", Alias("inner"))]), extras: [("alias", Alias("outer"))]));
            Add(N("Join", Values(Alias()), extras: [("kind", "CROSS")]));
            Add(N("Tuple", extras: [("expressions", new object?[] { Values(), L("9") })]));
            Add(Values(Alias(), ("order", N("Order", extras: [("expressions", new object?[] {
                N("Ordered", L(), extras: [("desc", true), ("nulls_first", false)]) })])),
                ("limit", N("Limit", extras: [("expression", L("2"))])),
                ("offset", N("Offset", extras: [("expression", L())]))));
            Add(N("Subquery", Values(), extras: [("alias", Alias()), ("limit", N("Limit", extras: [("expression", L("2"))])),
                ("order", N("Order", extras: [("expressions", new object?[] { N("Ordered", L(), extras: [("nulls_first", true)]) })]))]));
        }
        await Compare(cases);
    }

    [Fact]
    public async Task Alias_errors_configuration_and_values_to_select_mutations_match_live_Python()
    {
        var cases = new List<Case>();
        foreach (var pretty in new[] { false, true })
        foreach (var copy in new[] { false, true })
        {
            foreach (var level in new[] { "WARN", "IGNORE", "IMMEDIATE", "RAISE" })
            {
                cases.Add(new(Values(Alias()), pretty: pretty, mode: "nocolumns", copy: copy, level: level));
                cases.Add(new(Select(N("Subquery", Values(), extras: [("alias", Alias())])), pretty: pretty, mode: "nocolumns", copy: copy, level: level));
            }
            cases.Add(new(Select(Values(Alias())), pretty: pretty, mode: "nowrap", copy: copy));
            cases.Add(new(Values(Alias()), pretty: pretty, mode: "notable", copy: copy)); // root still VALUES
            foreach (var alias in new Node?[] { null, Alias(), Alias(null), Alias("v", 1), Alias("v", 4) })
            {
                cases.Add(new(Select(Values(alias)), pretty: pretty, mode: "notable", copy: copy));
                cases.Add(new(Select(Values(alias)), pretty: pretty, method: "values_false", copy: copy));
                cases.Add(new(Select(Values(alias)), postgres: true, pretty: pretty, method: "values_false", copy: copy));
            }
            cases.Add(new(Select(N("Values", extras: [("expressions", Array.Empty<object?>())])), pretty: pretty, mode: "notable", copy: copy));
            cases.Add(new(Select(N("Values", extras: [("expressions", new object?[] { Row(L()) })])), pretty: pretty, mode: "notable", copy: copy));
            // Existing Alias cells follow alias_'s in-place-on-copy path, not a nested Alias.
            cases.Add(new(Select(N("Values", extras: [("expressions", new object?[] { Row(N("Alias", L(), extras: [("alias", I("old"))])) }),
                ("alias", Alias("v", 1))])), pretty: pretty, mode: "notable", copy: copy));
        }
        await Compare(cases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task All_supported_modifiers_use_production_child_dispatch(bool postgres)
    {
        var cases = new List<Case>();
        var extras = new (string, object?)[] {
            ("where", N("Where", N("Boolean", true))), ("group", N("Group", extras: [("expressions", new object?[] { L() })])),
            ("having", N("Having", N("Boolean", true))), ("joins", new object?[] { N("Join", N("Table", I("t")), extras: [("kind", "CROSS")]) }),
            ("options", new object?[] { N("Var", "OPTION_A"), N("Var", "OPTION_B") }),
            ("limit", N("Fetch", extras: [("direction", "FIRST"), ("count", L("2")), ("limit_options", N("LimitOptions", extras: [("rows", true)]))])),
            ("offset", N("Offset", extras: [("expression", L())]))
        };
        foreach (var pretty in new[] { false, true })
        foreach (var copy in new[] { false, true })
        {
            foreach (var extra in extras) cases.Add(new(Values(Alias(), extra), postgres, pretty, copy: copy));
            cases.Add(new(Values(Alias(), extras), postgres, pretty, copy: copy));
        }
        await Compare(cases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unsupported_children_are_explicit_not_simplified(bool postgres)
    {
        generator.Generator g = postgres ? new Pg() : new generator.Generator();
        foreach (var (key, value) in new (string, object?)[] {
            ("locks", new object?[] { N("Get") }), ("sample", N("Get")), ("for_", N("Get")),
            ("match", N("MatchRecognize"))
        }) Assert.Throws<NotPortedException>(() => g.generate(Build(Values(Alias(), (key, value)))));
        foreach (var key in new[] { "pivots", "sample" })
        {
            object value = key == "pivots" ? new object?[] { N("Get") } : N("Get");
            Assert.Throws<NotPortedException>(() => g.generate(Build(N("Subquery", Values(), extras: [(key, value)]))));
        }
        var transform = postgres ? "Pivot" : "Get";
        Assert.Contains("TRANSFORMS[" + transform + "]", Assert.Throws<NotPortedException>(() => g.generate(Build(
            N("Values", extras: [("expressions", new object?[] { Row(N(transform, L())) })])))).Message);
        Assert.Throws<NotPortedException>(() => g.generate(Build(N("Table", Values(), extras: [("sample", N("Get"))]))));
        Assert.Contains("maybe_parse", Assert.Throws<NotPortedException>(() => new NoTable().generate(Build(
            Select(N("Values", extras: [("expressions", new object?[] { Row("x") }), ("alias", Alias("v", 1))]))))).Message);
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
