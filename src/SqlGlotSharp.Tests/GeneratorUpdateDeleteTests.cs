using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Pg = SqlGlotSharp.generators.postgres.PostgresGenerator;

namespace SqlGlotSharp.Tests;

public class GeneratorUpdateDeleteTests
{
    private sealed record Node(string type, Dictionary<string, object?> args, List<string>? comments = null);
    private sealed record Case(Node node, bool postgres = false, bool pretty = false, bool comments = true,
        bool copy = true, bool leading = false, long width = 80, bool identify = false, bool normalize = false,
        bool early = false, string method = "generate", string level = "WARN");
    private sealed record Result(string? sql, string? error, string[] messages, JsonElement state);
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
    private static Node C(string name, string? table = null) => N("Column", I(name), extras: [("table", table is null ? null : I(table))]);
    private static Node T(string name = "Target", bool alias = false) => N("Table", I(name), ["table\ncomment"],
        ("alias", alias ? N("TableAlias", I("t"), ["alias comment"]) : null));
    private static Node Eq(Node? left = null, Node? right = null) => N("EQ", left ?? C("c0"), ["assignment comment"], ("expression", right ?? L()));
    private static Node Where() => N("Where", N("And", Eq(C("c0", "t")), extras: [("expression", Eq(C("c1"), L("long 'text'\nvalue", true)))]), ["where\ncomment"]);
    private static Node Returning() => N("Returning", comments: ["return\ncomment"], extras: [("expressions", new object?[] { N("Star"), C("c0") })]);
    private static Node Select() => N("Select", comments: ["select\ncomment"], extras: [
        ("expressions", new object?[] { C("c0"), L() }), ("from_", N("From", T("Source"))), ("where", Where())]);
    private static Node With() => N("With", extras: [("expressions", new object?[] {
        N("CTE", Select(), ["cte\ncomment"], ("alias", N("TableAlias", I("src")))) })]);
    private static Node Mutation(string kind, bool full = true, params (string key, object? value)[] extras) =>
        N(kind, T(alias: true), [kind + "\ncomment", "*/ /*"], extras: [
            ("expressions", kind == "Update" ? new object?[] { Eq(), Eq(C("c1"), C("c1", "s")) } : null),
            ("from_", kind == "Update" && full ? N("From", T("Source")) : null),
            ("using", kind == "Delete" && full ? new object?[] { T("Source"), T("Other") } : null),
            ("where", full ? Where() : null), ("returning", full ? Returning() : null), .. extras]);
    private sealed class Early : generator.Generator { public override bool RETURNING_END => false; }
    private sealed class NoFrom : generator.Generator { public override bool UPDATE_STATEMENT_SUPPORTS_FROM => false; }
    private static generator.Generator G(Case c)
    {
        generator.Generator g = c.early ? new Early() : c.postgres ? new Pg() : new generator.Generator();
        g.dialect = dialects.dialect.Dialect.get_or_raise(c.postgres ? "postgres" : "");
        g.pretty = c.pretty; g.comments = c.comments; g.leading_comma = c.leading; g.max_text_width = c.width;
        g.identify = c.identify; g.normalize = c.normalize;
        g.unsupported_level = Enum.Parse<errors.ErrorLevel>(c.level);
        return g;
    }
    private static object? BuildValue(object? value) => value switch
    {
        Node node => Build(node), object?[] list => list.Select(BuildValue).ToList(),
        JsonElement json => BuildValue(ReadJson(json)), _ => value
    };
    private static object? ReadJson(JsonElement json) => json.ValueKind switch
    {
        JsonValueKind.Object => JsonSerializer.Deserialize<Node>(json.GetRawText()),
        JsonValueKind.Array => json.EnumerateArray().Select(ReadJson).ToArray(),
        JsonValueKind.String => json.GetString(), JsonValueKind.True => true, JsonValueKind.False => false,
        JsonValueKind.Number => json.GetInt64(), _ => null
    };
    private static exp.Expr Build(Node node)
    {
        var args = new PyDict<string, object?>();
        foreach (var (key, value) in node.args) args[key] = BuildValue(value);
        var result = (exp.Expr)Activator.CreateInstance(exp.EXPR_CLASSES.Values.Single(t => t.Name == node.type), [args])!;
        result.comments = node.comments?.ToList(); return result;
    }
    private static List<exp.Expr> Nodes(exp.Expr root)
    {
        var result = new List<exp.Expr>();
        void Visit(exp.Expr n)
        {
            result.Add(n);
            foreach (var v in n.args.Values)
                if (v is exp.Expr child) Visit(child);
                else if (v is List<object?> list) foreach (var item in list) if (item is exp.Expr entry) Visit(entry);
        }
        Visit(root); return result;
    }
    private static object? Dump(object? value) => value switch
    {
        exp.Expr n => new { type = n.GetType().Name, args = n.args.ToDictionary(p => p.Key, p => Dump(p.Value)), comments = n.comments },
        List<object?> list => list.Select(Dump).ToArray(), _ => value
    };
    private static object State(List<exp.Expr> original) => original.Select(n => new
    {
        node = Dump(n), parent = n.parent?.GetType().Name, key = n.arg_key, index = n.index
    }).ToArray();
    private const string Oracle = """
        import sys,json
        sys.path.insert(0,'python')
        from sqlglot import exp,parse_one
        from sqlglot.generator import Generator
        from sqlglot.generators.postgres import PostgresGenerator
        from sqlglot.errors import ErrorLevel
        def build(v):
            if isinstance(v,list): return [build(x) for x in v]
            if not isinstance(v,dict) or 'type' not in v: return v
            n=getattr(exp,v['type'])(**{k:build(x) for k,x in v['args'].items()})
            n.comments=v['comments']; return n
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
            if c['early']: G.RETURNING_END=False
            g=G(dialect='postgres' if c['postgres'] else '',pretty=c['pretty'],comments=c['comments'],
                leading_comma=c['leading'],max_text_width=c['width'],identify=c['identify'],normalize=c['normalize'],
                unsupported_level=getattr(ErrorLevel,c['level']))
            n=build(c['node'])
            original=nodes(n); sql=None; error=None
            try: sql=g.generate(n,copy=c['copy']) if c['method']=='generate' else g.sql(n)
            except Exception as e: error=type(e).__name__+': '+str(e)
            out.append(dict(sql=sql,error=error,messages=g.unsupported_messages,state=[
                dict(node=dump(x),parent=type(x.parent).__name__ if x.parent is not None else None,key=x.arg_key,index=x.index) for x in original]))
        print(json.dumps(out))
        """;
    private static bool JsonEqual(JsonElement a, JsonElement b) => a.ValueKind == b.ValueKind && (a.ValueKind switch
    {
        JsonValueKind.Object => a.EnumerateObject().Count() == b.EnumerateObject().Count() &&
            a.EnumerateObject().All(p => b.TryGetProperty(p.Name, out var v) && JsonEqual(p.Value, v)),
        JsonValueKind.Array => a.GetArrayLength() == b.GetArrayLength() && a.EnumerateArray().Zip(b.EnumerateArray()).All(p => JsonEqual(p.First, p.Second)),
        _ => a.ToString() == b.ToString()
    });
    private static async Task Compare(List<Case> cases)
    {
        var expected = JsonSerializer.Deserialize<Result[]>(await Python(JsonSerializer.Serialize(cases)))!;
        for (var i = 0; i < cases.Count; i++)
        {
            var c = cases[i]; var g = G(c);
            var n = Build(c.node);
            var original = Nodes(n); string? actual = null, error = null;
            try { actual = c.method == "generate" ? g.generate(n, copy: c.copy) : g.sql(n); }
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
    public async Task Production_AST_mutations_match_Python_SQL_state_and_comments(bool postgres)
    {
        var cases = new List<Case>();
        foreach (var kind in new[] { "Update", "Delete" })
        foreach (var pretty in new[] { false, true })
        foreach (var comments in new[] { false, true })
        foreach (var copy in new[] { false, true })
        foreach (var full in new[] { false, true })
        foreach (var method in new[] { "generate", "sql" })
        {
            cases.Add(new(Mutation(kind, full), postgres, pretty, comments, copy, method: method));
            cases.Add(new(Mutation(kind, full, ("with_", With())), postgres, pretty, comments, copy,
                leading: pretty, width: 12, identify: true, normalize: true, method: method));
            // Tuple assignment, scalar subquery, joins, and qualified table targets use real handlers.
            cases.Add(new(Mutation(kind, full, ("this", N("Table", I("Target"), extras: [("db", I("Db")),
                ("only", true), ("joins", new object?[] { N("Join", T("Joined"), extras: [("on", Eq(C("c0", "t"), C("c0", "j")))]) })]))),
                postgres, pretty, comments, copy, method: method));
            if (kind == "Update") cases.Add(new(Mutation(kind, full, ("expressions", new object?[] {
                Eq(N("Tuple", extras: [("expressions", new object?[] { C("c0"), C("c1") })]),
                    N("Subquery", Select())), Eq(C("c2"), N("Null")) })), postgres, pretty, comments, copy,
                    leading: true, width: 8, method: method));
        }
        await Compare(cases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Clause_order_empty_arguments_options_and_early_returning_match_Python(bool postgres)
    {
        var cases = new List<Case>();
        foreach (var kind in new[] { "Update", "Delete" })
        foreach (var pretty in new[] { false, true })
        {
            cases.Add(new(N(kind), postgres, pretty));
            cases.Add(new(Mutation(kind, extras: [("order", N("Order", extras: [("expressions", new object?[] {
                N("Ordered", C("c0"), extras: [("desc", true), ("nulls_first", true)]) })])),
                ("limit", N("Limit", extras: [("expression", L("5"))])),
                ("cluster", "ON CLUSTER test"), ("tables", new object?[] { T("a"), T("b") }),
                ("options", new object?[] { N("Var", "RECOMPILE"), N("Var", "MAXDOP 1") })]),
                postgres, pretty, leading: true, width: 5));
            cases.Add(new(Mutation(kind, extras: [("with_", With())]), pretty: pretty, early: true));
        }
        await Compare(cases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Diagnostic_levels_and_copy_mutations_match_Python(bool postgres)
    {
        var cases = new List<Case>();
        foreach (var kind in new[] { "Update", "Delete" })
        foreach (var level in new[] { "WARN", "IGNORE", "RAISE", "IMMEDIATE" })
        foreach (var copy in new[] { false, true })
        foreach (var comments in new[] { false, true })
        {
            cases.Add(new(Mutation(kind, extras: [("with_", With()), ("hint", N("Hint", extras: [
                ("expressions", new object?[] { N("Var", "test") })]))]), postgres, true, comments, copy, level: level));
            cases.Add(new(Mutation(kind, extras: [("where", N("Where", N("Column", I("c0"), extras: [("join_mark", true)]))),
                ("with_", With())]), postgres, true, comments, copy, level: level));
        }
        await Compare(cases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_children_and_FROM_rewrite_fail_explicitly(bool postgres)
    {
        foreach (var kind in new[] { "Update", "Delete" })
        foreach (var (key, child, handler) in new[] {
            ("where", N("Where", N("Get")), "TRANSFORMS[Get]"),
            ("returning", N("Returning", extras: [("expressions", new object?[] { N("Get") })]), "TRANSFORMS[Get]"),
            ("limit", N("Limit", extras: [("expression", N("Get"))]), "TRANSFORMS[Get]") })
        {
            var ast = Mutation(kind, extras: [(key, child)]);
            Assert.Contains(handler, Assert.Throws<NotPortedException>(() => G(new(ast, postgres)).generate(Build(ast))).Message);
        }
        foreach (var (kind, key, child) in new[] {
            ("Update", "from_", (object)N("From", N("Get"))),
            ("Update", "expressions", new object?[] { N("Get") }),
            ("Update", "options", new object?[] { N("Get") }),
            ("Delete", "using", new object?[] { N("Get") }),
            ("Delete", "tables", new object?[] { N("Get") }),
            ("Delete", "cluster", (object)N("OnCluster", L("test"))) })
            Assert.Throws<NotPortedException>(() => G(new(Mutation(kind, extras: [(key, child)]), postgres)).generate(Build(Mutation(kind, extras: [(key, child)]))));
        var update = (exp.Update)Build(Mutation("Update"));
        Assert.Contains("FROM-to-JOIN rewrite", Assert.Throws<NotPortedException>(() => new NoFrom().generate(update)).Message);
        Assert.Equal(("", ""), new NoFrom()._update_from_joins_sql((exp.Update)Build(Mutation("Update", false))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Production_SQL_parsed_by_Python_renders_identically_without_CSharp_parser_dependency(bool postgres)
    {
        const string parse = """
            import sys,json
            sys.path.insert(0,'python')
            from sqlglot import parse_one,exp
            def dump(n):
                if isinstance(n,list): return [dump(x) for x in n]
                if not isinstance(n,exp.Expr): return n
                return dict(type=type(n).__name__,args={k:dump(v) for k,v in n.args.items()},comments=n.comments)
            c=json.load(sys.stdin)
            print(json.dumps([dump(parse_one(s,read='postgres' if c['postgres'] else '')) for s in c['sql']]))
            """;
        string[] sql = [
            "UPDATE tbl SET a = 1, b = NULL",
            "UPDATE /* statement */ tbl AS t SET a = s.a, b = 'text' FROM src AS s WHERE t.id = s.id RETURNING t.*",
            "UPDATE tbl SET (a, b) = (SELECT a, b FROM src WHERE id = 1) WHERE id = 2 RETURNING a, b",
            "WITH src AS (SELECT id FROM other) UPDATE tbl SET a = 2 FROM src WHERE tbl.id = src.id RETURNING *",
            "DELETE FROM tbl",
            "DELETE /* statement */ FROM tbl AS t USING src AS s, other AS o WHERE t.id = s.id AND s.id = o.id RETURNING t.id, t.*",
            "WITH src AS (SELECT id FROM other) DELETE FROM tbl USING src WHERE tbl.id = src.id RETURNING *",
            "UPDATE tbl SET a = 1 WHERE id > 0 ORDER BY id LIMIT 5",
            "DELETE FROM tbl WHERE id > 0 RETURNING * ORDER BY id LIMIT 5"
        ];
        var nodes = JsonSerializer.Deserialize<Node[]>(await Python(JsonSerializer.Serialize(new { postgres, sql }), parse))!;
        var cases = new List<Case>();
        foreach (var node in nodes)
        foreach (var pretty in new[] { false, true })
        foreach (var comments in new[] { false, true })
        foreach (var copy in new[] { false, true })
            cases.Add(new(node, postgres, pretty, comments, copy, leading: pretty, width: 20));
        await Compare(cases);
    }

    private static string SourcePath([CallerFilePath] string source = "") => source;
    private static async Task<string> Python(string input, string? script = null)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SQLGLOT_PYTHON") ?? "python")
        {
            WorkingDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath())!, "..", "..")),
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        start.ArgumentList.Add("-B"); start.ArgumentList.Add("-c"); start.ArgumentList.Add(script ?? Oracle);
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
