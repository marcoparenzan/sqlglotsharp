using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Pg = SqlGlotSharp.generators.postgres.PostgresGenerator;

namespace SqlGlotSharp.Tests;

public class GeneratorCreateTableTests
{
    private sealed record Node(string type, Dictionary<string, object?> args, List<string>? comments = null);
    private sealed record Case(Node node, bool postgres = false, bool pretty = false, bool comments = true,
        bool copy = true, bool leading = false, long width = 80, bool identify = false, bool normalize = false,
        string method = "generate");
    private sealed record Result(string sql, JsonElement state);
    private static Node N(string type, object? value = null, List<string>? comments = null,
        params (string key, object? value)[] extras)
    {
        var args = new Dictionary<string, object?>();
        if (value is not null) args["this"] = value;
        foreach (var (key, item) in extras) args[key] = item;
        return new(type, args, comments);
    }
    private static Node I(string name, bool quoted = false) => N("Identifier", name, extras: [("quoted", quoted)]);
    private static Node L(string value, bool text = false) => N("Literal", value, extras: [("is_string", text)]);
    private static Node T(bool qualified = true) => N("Table", I("Target"), ["table comment"],
        ("db", qualified ? I("My Schema", true) : null), ("catalog", qualified ? I("Catalog") : null));
    private static Node D(string type, params Node[] parameters) => N("DataType", type,
        extras: [("expressions", parameters.Select(p => (object?)N("DataTypeParam", p)).ToArray())]);
    private static Node C(Node kind, string? name = null) => N("ColumnConstraint", name is null ? null : I(name),
        ["constraint comment"], ("kind", kind));
    private static Node Column(string name, Node? type, params Node[] constraints) => N("ColumnDef", I(name),
        ["column\ncomment", "*/ /*"], ("kind", type), ("constraints", constraints.Cast<object?>().ToArray()));
    private static Node Schema(bool reverse = false, bool qualified = true) => N("Schema", T(qualified), ["schema comment"],
        ("expressions", new object?[] {
            Column("id", D("INT"), C(N("NotNullColumnConstraint"))),
            Column("select", D("VARCHAR", L("24")),
                [.. (reverse ? new[] { C(N("NotNullColumnConstraint", extras: [("allow_null", true)])), C(N("DefaultColumnConstraint", L("a 'quoted'\ntext", true)), "default name") }
                : new[] { C(N("DefaultColumnConstraint", L("a 'quoted'\ntext", true)), "default name"), C(N("NotNullColumnConstraint", extras: [("allow_null", true)])) })]),
            Column("amount", D("DECIMAL", L("10"), L("2")), C(N("DefaultColumnConstraint", N("Neg", L("1"))))) }));
    private static Node Create(Node? target = null, params (string key, object? value)[] extras) =>
        N("Create", target ?? Schema(), ["create\ncomment"], extras: [("kind", "TABLE"), .. extras]);
    private static Node Select() => N("Select", extras: [("expressions", new object?[] { L("1") })]);
    private static Node With() => N("With", extras: [("expressions", new object?[] {
        N("CTE", Select(), ["cte comment"], ("alias", N("TableAlias", I("source")))) })]);

    private static object? BuildValue(object? value) => value switch
    {
        Node node => Build(node), object?[] list => list.Select(BuildValue).ToList(), _ => value
    };
    private static exp.Expr Build(Node node)
    {
        var args = new PyDict<string, object?>();
        foreach (var (key, value) in node.args)
            args[key] = node.type == "DataType" && key == "this" ? Enum.Parse<exp.DType>((string)value!) : BuildValue(value);
        var expression = (exp.Expr)Activator.CreateInstance(exp.EXPR_CLASSES.Values.Single(t => t.Name == node.type), [args])!;
        expression.comments = node.comments?.ToList();
        return expression;
    }
    private static generator.Generator G(Case c)
    {
        generator.Generator g = c.postgres ? new Pg() : new generator.Generator();
        g.dialect = dialects.dialect.Dialect.get_or_raise(c.postgres ? "postgres" : "");
        g.pretty = c.pretty; g.comments = c.comments; g.leading_comma = c.leading; g.max_text_width = c.width;
        g.identify = c.identify; g.normalize = c.normalize;
        return g;
    }
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
        exp.DType type => type.ToString(), List<object?> list => list.Select(Dump).ToArray(), _ => value
    };
    private static object State(List<exp.Expr> nodes) => nodes.Select(n => new
    {
        node = Dump(n), parent = n.parent?.GetType().Name, key = n.arg_key, index = n.index
    }).ToArray();
    private const string Oracle = """
        import sys,json
        sys.path.insert(0,'python')
        from sqlglot import exp
        from sqlglot.generator import Generator
        from sqlglot.generators.postgres import PostgresGenerator
        def build(v):
            if isinstance(v,list): return [build(x) for x in v]
            if not isinstance(v,dict) or 'type' not in v: return v
            args={k:build(x) for k,x in v['args'].items()}
            if v['type']=='DataType': args['this']=getattr(exp.DType,args['this'])
            n=getattr(exp,v['type'])(**args); n.comments=v['comments']; return n
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
            if isinstance(n,exp.DType): return n.name
            if not isinstance(n,exp.Expr): return n
            return dict(type=type(n).__name__,args={k:dump(v) for k,v in n.args.items()},comments=n.comments)
        out=[]
        for c in json.load(sys.stdin):
            g=(PostgresGenerator if c['postgres'] else Generator)(dialect='postgres' if c['postgres'] else '',
                pretty=c['pretty'],comments=c['comments'],leading_comma=c['leading'],max_text_width=c['width'],
                identify=c['identify'],normalize=c['normalize'])
            n=build(c['node']); original=nodes(n)
            if c['method']=='generate': sql=g.generate(n,copy=c['copy'])
            elif c['method']=='column': sql=g.columndef_sql(n,sep=': ')
            else: sql=g.sql(n)
            state=[dict(node=dump(x),parent=type(x.parent).__name__ if x.parent is not None else None,key=x.arg_key,index=x.index) for x in original]
            out.append(dict(sql=sql,state=state))
        print(json.dumps(out))
        """;
    private static bool JsonEqual(JsonElement a, JsonElement b) => a.ValueKind == b.ValueKind && (a.ValueKind switch
    {
        JsonValueKind.Object => a.EnumerateObject().Count() == b.EnumerateObject().Count() &&
            a.EnumerateObject().All(p => b.TryGetProperty(p.Name, out var r) && JsonEqual(p.Value, r)),
        JsonValueKind.Array => a.GetArrayLength() == b.GetArrayLength() && a.EnumerateArray().Zip(b.EnumerateArray()).All(p => JsonEqual(p.First, p.Second)),
        _ => a.ToString() == b.ToString()
    });
    private static async Task Compare(List<Case> cases)
    {
        var expected = JsonSerializer.Deserialize<Result[]>(await Python(JsonSerializer.Serialize(cases)))!;
        for (var i = 0; i < cases.Count; i++)
        {
            var c = cases[i]; var g = G(c); var node = Build(c.node); var original = Nodes(node);
            var sql = c.method switch { "generate" => g.generate(node, copy: c.copy), "column" => g.columndef_sql((exp.ColumnDef)node, sep: ": "), _ => g.sql(node) };
            var state = JsonSerializer.SerializeToElement(State(original));
            Assert.True(expected[i].sql == sql && JsonEqual(expected[i].state, state),
                $"Case {i}: {JsonSerializer.Serialize(c)}\nPython: {JsonSerializer.Serialize(expected[i])}\nC#: {sql}\nState: {state}");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Production_create_table_matches_live_Python_SQL_comments_pretty_and_copy(bool postgres)
    {
        var cases = new List<Case>();
        foreach (var pretty in new[] { false, true })
        foreach (var comments in new[] { false, true })
        foreach (var copy in new[] { false, true })
        foreach (var exists in new[] { false, true })
        foreach (var reverse in new[] { false, true })
        {
            cases.Add(new(Create(Schema(reverse), ("exists", exists)), postgres, pretty, comments, copy));
            cases.Add(new(Create(Schema(reverse, false), ("exists", exists)), postgres, pretty, comments, copy,
                leading: true, width: 10, identify: true, normalize: true));
            cases.Add(new(Create(T(), ("exists", exists)), postgres, pretty, comments, copy));
        }
        await Compare(cases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Clause_order_and_column_helpers_match_live_Python(bool postgres)
    {
        var cases = new List<Case>();
        foreach (var pretty in new[] { false, true })
        foreach (var clustered in new object?[] { null, true, false })
        {
            cases.Add(new(Create(Schema(), ("clustered", clustered), ("replace", true), ("refresh", true),
                ("unique", true), ("concurrently", true), ("exists", true), ("expression", Select()), ("with_", With())), postgres, pretty));
            cases.Add(new(Create(N("Schema", T(), extras: [("expressions", Array.Empty<object?>())])), postgres, pretty));
            cases.Add(new(Column("untyped", null), postgres, pretty, method: "column"));
            cases.Add(new(N("ColumnDef", I("c"), extras: [("kind", D("INT")), ("exists", true)]), postgres, pretty, method: "column"));
        }
        await Compare(cases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Parameter_mode_override_preserves_Python_copy_and_mutation(bool postgres)
    {
        var cases = new List<Case>();
        foreach (var input in new[] { false, true })
        foreach (var output in new[] { false, true })
        foreach (var variadic in new[] { false, true })
        foreach (var copy in new[] { false, true })
        foreach (var method in new[] { "generate", "sql", "column" })
            cases.Add(new(Column("parameter", D("INT"), C(N("InOutColumnConstraint", extras: [
                ("input_", input), ("output", output), ("variadic", variadic)])), C(N("NotNullColumnConstraint"))),
                postgres, pretty: true, copy: copy, method: method));
        await Compare(cases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Active_unported_options_fail_instead_of_disappearing(bool postgres)
    {
        var schema = N("Schema", T(), extras: [("expressions", new object?[] { N("PrimaryKey", extras: [("expressions", new object?[] { I("id") })]) })]);
        var tableConstraint = Create(schema);
        Assert.Contains("PRIMARY KEY (id)", G(new(tableConstraint, postgres)).generate(Build(tableConstraint)));
        if (postgres)
            foreach (var serial in new[] { "SERIAL", "SMALLSERIAL", "BIGSERIAL" })
            {
                var node = Column("c", D(serial));
                Assert.Contains("GENERATED BY DEFAULT AS IDENTITY", G(new(node, true)).generate(Build(node)));
            }
    }

    private static string SourcePath([CallerFilePath] string source = "") => source;
    private static async Task<string> Python(string input)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SQLGLOT_PYTHON") ?? "python")
        {
            WorkingDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath())!, "..", "..")),
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-B"); start.ArgumentList.Add("-c"); start.ArgumentList.Add(Oracle);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token); process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, await stderr); return await stdout;
    }
}
