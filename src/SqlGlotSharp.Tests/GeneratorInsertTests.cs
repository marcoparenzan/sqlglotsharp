using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Pg = SqlGlotSharp.generators.postgres.PostgresGenerator;

namespace SqlGlotSharp.Tests;

public class GeneratorInsertTests
{
    private sealed record Node(string type, Dictionary<string, object?> args, List<string>? comments = null);
    private sealed record Case(Node node, bool postgres = false, bool pretty = false, bool comments = true,
        bool leading = false, long width = 80, bool copy = true, bool identify = false, bool normalize = false,
        string mode = "ordinary", string method = "generate", string level = "WARN");
    private sealed record Result(string? sql, string[] messages, string? error, JsonElement state);
    private static Node N(string type, object? value = null, List<string>? comments = null,
        params (string key, object? value)[] extras)
    {
        var args = new Dictionary<string, object?>();
        if (value is not null) args["this"] = value;
        foreach (var (key, item) in extras) args[key] = item;
        return new(type, args, comments);
    }
    private static Node I(string name) => N("Identifier", name, extras: [("quoted", false)]);
    private static Node L(string value = "1", bool text = false) => N("Literal", value, extras: [("is_string", text)]);
    private static Node Col(string name, string? table = null) => N("Column", I(name), extras: [("table", table is null ? null : I(table))]);
    private static Node Table(string name = "Target") => N("Table", I(name));
    private static Node Schema(int count = 2, bool target = true) => N("Schema", target ? Table() : null,
        ["schema\ncomment"], ("expressions", Enumerable.Range(0, count).Select(i => (object?)I("c" + i)).ToArray()));
    private static Node Values() => N("Values", comments: ["values\ncomment"], extras: [("expressions", new object?[] {
        N("Tuple", comments: ["row comment"], extras: [("expressions", new object?[] { L(), L("long 'string'\ncell", true) })]),
        N("Tuple", extras: [("expressions", new object?[] { N("Null"), L("2") })]) })]);
    private static Node Select() => N("Select", comments: ["select comment"], extras: [
        ("expressions", new object?[] { Col("c0"), Col("c1") }), ("from_", N("From", Table("Source"))),
        ("where", N("Where", N("GT", Col("c0"), extras: [("expression", L("0"))]))) ]);
    private static Node Returning() => N("Returning", comments: ["return comment"], extras: [
        ("expressions", new object?[] { N("Star"), Col("c1") })]);
    private static Node Conflict(bool update = false, bool constraint = false, bool predicates = false, bool duplicate = false) =>
        N("OnConflict", comments: ["conflict comment"], extras: [
            ("duplicate", duplicate), ("constraint", constraint ? I("unique_target") : null),
            ("conflict_keys", constraint ? Array.Empty<object?>() : new object?[] { Col("c0"), Col("c1") }),
            ("action", N("Var", duplicate ? "UPDATE" : update ? "DO UPDATE" : "DO NOTHING")),
            ("expressions", update ? new object?[] { N("EQ", Col("c1"), extras: [("expression", Col("c1", "excluded"))]) } : Array.Empty<object?>()),
            ("index_predicate", predicates ? N("Where", N("GT", Col("c0"), extras: [("expression", L("0"))])) : null),
            ("where", predicates ? N("Where", N("GT", Col("c1"), extras: [("expression", L("0"))])) : null)]);
    private static Node Ctes() => N("With", extras: [("expressions", new object?[] {
        N("CTE", Select(), ["cte\ncomment"], ("alias", N("TableAlias", I("src"), ["alias comment"]))) })]);
    private static Node Insert(Node? target = null, Node? body = null, params (string key, object? value)[] extras) =>
        N("Insert", target ?? Schema(), ["insert\ncomment", "*/ /*"], extras: [("expression", body), .. extras]);

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
    private sealed class EarlyReturning : generator.Generator { public override bool RETURNING_END => false; }
    private sealed class NoSet : generator.Generator { public override bool DUPLICATE_KEY_UPDATE_WITH_SET => false; }
    private static generator.Generator G(Case c)
    {
        generator.Generator g = c.mode switch
        {
            "early" => new EarlyReturning(), "noset" => new NoSet(),
            _ => c.postgres ? new Pg() : new generator.Generator()
        };
        g.dialect = dialects.dialect.Dialect.get_or_raise(c.postgres ? "postgres" : "");
        g.pretty = c.pretty; g.comments = c.comments; g.leading_comma = c.leading; g.max_text_width = c.width;
        g.identify = c.identify; g.normalize = c.normalize;
        g.unsupported_level = Enum.Parse<errors.ErrorLevel>(c.level);
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
            if c['mode']=='early': G.RETURNING_END=False
            if c['mode']=='noset': G.DUPLICATE_KEY_UPDATE_WITH_SET=False
            g=G(dialect='postgres' if c['postgres'] else '',pretty=c['pretty'],comments=c['comments'],leading_comma=c['leading'],
                max_text_width=c['width'],identify=c['identify'],normalize=c['normalize'],unsupported_level=getattr(ErrorLevel,c['level']))
            n=build(c['node']); original=nodes(n); sql=None; error=None
            try:
                if c['method']=='generate': sql=g.generate(n,copy=c['copy'])
                elif c['method']=='columns': sql=g.schema_columns_sql(n)
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
            try { actual = c.method switch { "generate" => g.generate(node, copy: c.copy), "columns" => g.schema_columns_sql(node), _ => g.sql(node) }; }
            catch (errors.UnsupportedError e) { error = "UnsupportedError: " + e.Message; }
            catch (ArgumentException e) when (e.Message.StartsWith("Unsupported expression type")) { error = "ValueError: " + e.Message; }
            var state = JsonSerializer.SerializeToElement(State(original));
            Assert.True(expected[i].sql == actual && expected[i].error == error && expected[i].messages.SequenceEqual(g.unsupported_messages) && JsonEqual(expected[i].state, state),
                $"Case {i}: {JsonSerializer.Serialize(c)}\nPython: {JsonSerializer.Serialize(expected[i])}\nC#: {actual}, {error}, {string.Join(',', g.unsupported_messages)}\nState: {state}");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Production_insert_values_select_default_conflict_returning_match_live_Python(bool postgres)
    {
        var cases = new List<Case>();
        foreach (var pretty in new[] { false, true })
        foreach (var comments in new[] { false, true })
        foreach (var copy in new[] { false, true })
        foreach (var body in new Node?[] { Values(), Select(), null })
        foreach (var target in new[] { Table(), Schema(), Schema(0) })
        {
            cases.Add(new(Insert(target, body, ("default", body is null)), postgres, pretty, comments, copy: copy));
            cases.Add(new(Insert(target, body, ("default", body is null), ("returning", Returning()),
                ("conflict", Conflict(true, true, true)), ("with_", Ctes())), postgres, pretty, comments,
                leading: pretty, width: 14, copy: copy, identify: true, normalize: true));
            cases.Add(new(Insert(target, body, ("default", body is null), ("returning", Returning()),
                ("conflict", Conflict(false))), postgres, pretty, comments, copy: copy));
            cases.Add(new(Insert(target, body, ("default", body is null), ("conflict", Conflict(true, false, true))),
                postgres, pretty, comments, copy: copy));
        }
        await Compare(cases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Options_helpers_and_clause_order_match_live_Python(bool postgres)
    {
        var cases = new List<Case>();
        foreach (var pretty in new[] { false, true })
        {
            foreach (var target in new[] { Schema(), Schema(0), Schema(3, false), Schema(0, false) })
                foreach (var method in new[] { "generate", "columns" })
                    cases.Add(new(target, postgres, pretty, leading: true, width: 5, method: method));
            foreach (var duplicate in new[] { false, true })
                foreach (var update in new[] { false, true })
                    cases.Add(new(Conflict(update, predicates: true, duplicate: duplicate), postgres, pretty));
            cases.Add(new(Returning(), postgres, pretty));
            cases.Add(new(N("OnConflict", extras: [("action", N("Var", "DO NOTHING"))]), postgres, pretty));
            cases.Add(new(Insert(Table(), Values(), ("overwrite", true), ("stored", "STORED AS TEXTFILE"),
                ("alternative", "REPLACE"), ("ignore", true), ("is_function", true), ("exists", true), ("by_name", true),
                ("where", L("c0 > 0")), ("using", new object?[] { Col("c0"), Col("c1") }),
                ("partition", "PARTITION(c0=1)"), ("settings", "SETTINGS x=1"), ("source", Table("src")),
                ("hint", N("Hint", extras: [("expressions", new object?[] { N("Var", "test") })]))), postgres, pretty));
            // String children use upstream sql's literal-string boundary. Actual missing
            // storage/settings AST handlers are tested separately as explicit failures.
            cases.Add(new(Insert(Table(), null, ("default", true), ("returning", Returning())), pretty: pretty, mode: "early"));
            cases.Add(new(Insert(Table(), Values(), ("conflict", Conflict(true, duplicate: true)), ("returning", Returning())), pretty: pretty, mode: "noset"));
        }
        await Compare(cases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Live_Python_errors_and_mutations_match(bool postgres)
    {
        var cases = new List<Case>();
        foreach (var copy in new[] { false, true })
        foreach (var comments in new[] { false, true })
        foreach (var level in new[] { "WARN", "IGNORE", "RAISE", "IMMEDIATE" })
        {
            // Base Hint emits unsupported; Postgres supports it. CTE comment transfer
            // tests mutations even with comments disabled and generate(copy=False).
            cases.Add(new(Insert(Table(), Values(), ("hint", N("Hint", extras: [("expressions", new object?[] { N("Var", "test") })])),
                ("with_", Ctes())), postgres, pretty: true, comments: comments, copy: copy, level: level));
            cases.Add(new(Insert(Table(), N("Select", extras: [("expressions", new object?[] {
                N("Column", I("c0"), extras: [("join_mark", true)]) })]), ("with_", Ctes())),
                postgres, comments: comments, copy: copy, level: level));
        }
        await Compare(cases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_child_handlers_are_explicit_not_omitted(bool postgres)
    {
        foreach (var (key, node, handler) in new[] {
            ("returning", N("Returning", extras: [("expressions", new object?[] { N("Get") })]), "TRANSFORMS[Get]") })
        {
            var ast = Insert(Table(), Values(), (key, node));
            var error = Assert.Throws<NotPortedException>(() => G(new(ast, postgres)).generate(Build(ast)));
            Assert.Contains(handler, error.Message, StringComparison.OrdinalIgnoreCase);
        }
        var unported = postgres ? "Pivot" : "Get";
        var insert = Insert(Table(), N("Select", extras: [("expressions", new object?[] { N(unported, L()) })]));
        Assert.Contains("TRANSFORMS[" + unported + "]", Assert.Throws<NotPortedException>(() => G(new(insert, postgres)).generate(Build(insert))).Message);
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
