using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Pg = SqlGlotSharp.generators.postgres.PostgresGenerator;

namespace SqlGlotSharp.Tests;

public class GeneratorSetOperationsTests
{
    private sealed record Node(string type, Dictionary<string, object?> args, List<string>? comments = null);
    private sealed record Case(Node node, bool postgres, bool pretty, bool comments, bool leading,
        string mode = "ordinary", string method = "sql", bool immediate = false);
    private sealed record Result(string? sql, string[] messages, string? error);
    private static Node N(string type, object? value = null, List<string>? comments = null,
        params (string key, object? value)[] extras)
    {
        var args = new Dictionary<string, object?> { ["this"] = value };
        foreach (var (key, item) in extras) args[key] = item;
        return new(type, args, comments);
    }
    private static Node L(string value = "1") => N("Literal", value, extras: [("is_string", false)]);
    private static Node S(string value = "1", params (string key, object? value)[] extras) =>
        N("Select", comments: ["select comment"], extras: [("expressions", new object?[] { L(value) }), .. extras]);
    private static Node Op(string type = "Union", Node? left = null, Node? right = null,
        object? distinct = null, params (string key, object? value)[] extras) =>
        N(type, left ?? S(), ["operator\ncomment", "*/ /*"], extras: [("expression", right ?? S("2")), ("distinct", distinct), .. extras]);
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
    private sealed class NoAll : generator.Generator { public override bool EXCEPT_INTERSECT_SUPPORT_ALL_CLAUSE => false; }
    private sealed class NoParens : generator.Generator { public override bool SET_OP_PARENTHESIZED_OPERANDS => false; }
    private sealed class NoModifiers : generator.Generator { public override object? SET_OP_MODIFIERS => false; }
    private sealed class Limits : generator.Generator { public override bool SET_OP_LIMITS => true; }
    private sealed class Tight : dialects.dialect.Dialect { public override bool INTERSECT_BINDS_TIGHTER_THAN_UNION_AND_EXCEPT => true; }
    private sealed class Loose : dialects.dialect.Dialect { public override bool INTERSECT_BINDS_TIGHTER_THAN_UNION_AND_EXCEPT => false; }
    private sealed class DefaultAll : dialects.dialect.Dialect
    {
        public override PyDict<Type, bool> SET_OP_DISTINCT_BY_DEFAULT => new()
        { [typeof(exp.Union)] = false, [typeof(exp.Intersect)] = false, [typeof(exp.Except)] = false };
    }
    private static generator.Generator G(Case c)
    {
        generator.Generator g = c.mode switch
        {
            "noall" => new NoAll(), "noparens" or "tight" or "loose" => new NoParens(),
            "nomodifiers" => new NoModifiers(), "limits" => new Limits(),
            _ => c.postgres ? new Pg() : new generator.Generator()
        };
        g.dialect = c.mode switch
        {
            "tight" => new Tight(), "loose" => new Loose(), "defaultall" => new DefaultAll(),
            _ => dialects.dialect.Dialect.get_or_raise(c.postgres ? "postgres" : "")
        };
        g.pretty = c.pretty; g.comments = c.comments; g.leading_comma = c.leading;
        g.unsupported_level = c.immediate ? errors.ErrorLevel.IMMEDIATE : errors.ErrorLevel.WARN;
        return g;
    }
    private const string Oracle = """
        import sys,json
        sys.path.insert(0,'python')
        from sqlglot import exp
        from sqlglot.generator import Generator
        from sqlglot.generators.postgres import PostgresGenerator
        from sqlglot.dialects.dialect import Dialect
        from sqlglot.errors import ErrorLevel
        def build(v):
            if isinstance(v,list): return [build(x) for x in v]
            if not isinstance(v,dict) or 'type' not in v: return v
            n=getattr(exp,v['type'])(**{k:build(x) for k,x in v['args'].items()})
            n.comments=v['comments']
            return n
        out=[]
        for c in json.load(sys.stdin):
            class D(Dialect): pass
            class G(PostgresGenerator if c['postgres'] else Generator): pass
            mode=c['mode']
            if mode=='noall': G.EXCEPT_INTERSECT_SUPPORT_ALL_CLAUSE=False
            if mode in ('noparens','tight','loose'): G.SET_OP_PARENTHESIZED_OPERANDS=False
            if mode=='tight': D.INTERSECT_BINDS_TIGHTER_THAN_UNION_AND_EXCEPT=True
            if mode=='loose': D.INTERSECT_BINDS_TIGHTER_THAN_UNION_AND_EXCEPT=False
            if mode=='nomodifiers': G.SET_OP_MODIFIERS=False
            if mode=='limits': G.SET_OP_LIMITS=True
            if mode=='defaultall': D.SET_OP_DISTINCT_BY_DEFAULT={exp.Union:False,exp.Intersect:False,exp.Except:False}
            dialect=D() if mode in ('tight','loose','defaultall') else ('postgres' if c['postgres'] else '')
            g=G(dialect=dialect,pretty=c['pretty'],comments=c['comments'],leading_comma=c['leading'],
                unsupported_level=ErrorLevel.IMMEDIATE if c['immediate'] else ErrorLevel.WARN)
            try: out.append(dict(sql=getattr(g,c['method'])(build(c['node'])),messages=g.unsupported_messages,error=None))
            except Exception as e: out.append(dict(sql=None,messages=g.unsupported_messages,error=type(e).__name__+': '+str(e)))
        print(json.dumps(out))
        """;
    private static async Task Compare(List<Case> cases)
    {
        var expected = JsonSerializer.Deserialize<Result[]>(await Python(Oracle, JsonSerializer.Serialize(cases, new JsonSerializerOptions { MaxDepth = 1024 })))!;
        Assert.Equal(cases.Count, expected.Length);
        for (var i = 0; i < cases.Count; i++)
        {
            var c = cases[i]; var g = G(c); var node = Build(c.node);
            string? actual = null, error = null;
            try { actual = c.method switch
            {
                "set_operation" => g.set_operation((exp.SetOperation)node),
                "set_operations" => g.set_operations((exp.SetOperation)node),
                "subquery_sql" => g.subquery_sql((exp.Subquery)node), _ => g.sql(node)
            }; }
            catch (errors.UnsupportedError e) { error = "UnsupportedError: " + e.Message; }
            Assert.True(expected[i].sql == actual && expected[i].error == error && expected[i].messages.SequenceEqual(g.unsupported_messages),
                $"Case {i}: {JsonSerializer.Serialize(c, new JsonSerializerOptions { MaxDepth = 1024 })}\nPython: {JsonSerializer.Serialize(expected[i])}\nC#: {actual}, {error}, {string.Join(',', g.unsupported_messages)}");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Operator_keywords_and_errors_match_live_Python(bool postgres)
    {
        var cases = new List<Case>();
        foreach (var pretty in new[] { false, true })
        foreach (var comments in new[] { false, true })
        foreach (var type in new[] { "Union", "Intersect", "Except" })
        foreach (var distinct in new object?[] { null, true, false })
        foreach (var mode in postgres ? new[] { "ordinary" } : new[] { "ordinary", "noall", "defaultall" })
        foreach (var immediate in new[] { false, true })
        {
            cases.Add(new(Op(type, distinct: distinct), postgres, pretty, comments, false, mode, "set_operation", immediate));
            cases.Add(new(Op(type, distinct: distinct, extras: [("side", "left"), ("kind", "outer"),
                ("by_name", true), ("on", new object?[] { N("Identifier", "a"), N("Identifier", "b") })]),
                postgres, pretty, comments, false, mode, "set_operation", immediate));
        }
        await Compare(cases);
    }

    [Fact]
    public async Task Base_query_dispatch_nesting_precedence_modifiers_and_comments_match_Python()
    {
        var cases = new List<Case>();
        foreach (var pretty in new[] { false, true })
        foreach (var comments in new[] { false, true })
        foreach (var leading in new[] { false, true })
        {
            void Add(Node n, string mode = "ordinary", string method = "sql") =>
                cases.Add(new(n, false, pretty, comments, leading, mode, method));
            var order = N("Order", extras: [("expressions", new object?[] { N("Ordered", L(), extras: [("desc", true), ("nulls_first", false)]) })]);
            var limit = N("Limit", extras: [("expression", L("5"))]);
            var offset = N("Offset", extras: [("expression", L("2"))]);
            foreach (var type in new[] { "Union", "Intersect", "Except" })
            foreach (var other in new[] { "Union", "Intersect", "Except" })
            foreach (var distinct in new object?[] { null, true, false })
            {
                var inner = Op(other, distinct: distinct);
                Add(Op(type, distinct: distinct));
                Add(Op(type, inner, S("3"), distinct));
                Add(Op(type, S("3"), inner, distinct));
                Add(Op(type, N("Subquery", inner), S("3"), distinct));
                Add(Op(type, S("3"), N("Subquery", inner), distinct));
                Add(Op(type, N("Subquery", N("Subquery", inner)), S("3"), distinct), "noparens");
                Add(Op(type, N("Subquery", inner), S("3"), distinct), "tight");
                Add(Op(type, N("Subquery", inner, ["loose wrapper comment"]), S("3"), distinct), "loose");
                Add(Op(type, N("Subquery", inner, ["wrapper comment"]), S("3"), distinct));
            }
            foreach (var mode in new[] { "ordinary", "limits", "noparens" })
            {
                Add(Op(left: S("1", ("limit", limit)), right: S("2", ("limit", limit))), mode);
                Add(Op(extras: [("order", order), ("limit", limit), ("offset", offset),
                    ("options", new object?[] { N("Var", "OPTION_ONE"), N("Var", "OPTION_TWO") })]), mode);
                Add(Op(left: N("Subquery", Op(extras: [("order", order), ("limit", limit)])), right: S("3")), mode);
                Add(Op(right: N("Subquery", Op())), mode);
            }
            Add(Op(), "nomodifiers"); // no rewrite needed
            Add(N("Subquery", Op(), ["subquery"], ("alias", N("TableAlias", N("Identifier", "q")))), method: "subquery_sql");
            Node deep = Op("Intersect");
            for (var i = 0; i < 100; i++) deep = Op("Intersect", N("Subquery", deep), S("3"));
            Add(deep);
        }
        await Compare(cases);
    }

    [Fact]
    public async Task Postgres_supported_operand_dispatch_matches_Python_without_faking_Select_transform()
    {
        // Literal operands are intentionally non-query AST fixtures: they exercise
        // the real set-operation dispatch/formatting, not valid PostgreSQL queries.
        // Real SELECT operands remain blocked and are tested separately below.
        var cases = new List<Case>();
        foreach (var pretty in new[] { false, true })
        foreach (var comments in new[] { false, true })
        foreach (var type in new[] { "Union", "Intersect", "Except" })
        foreach (var distinct in new object?[] { null, true, false })
        {
            var inner = Op(type, L(), L("2"), distinct);
            foreach (var node in new[] { inner, Op(type, N("Subquery", inner), L("3")),
                Op(type, L("3"), N("Subquery", inner)),
                Op(type, L(), L("2"), distinct, ("limit", N("Limit", extras: [("expression", L("4"))])),
                    ("offset", N("Offset", extras: [("expression", L())]))) })
                cases.Add(new(node, true, pretty, comments, false));
        }
        await Compare(cases);
    }

    [Fact]
    public void Missing_dependencies_remain_explicit()
    {
        var g = new generator.Generator();
        Assert.Contains("UNION", g.generate(Build(Op())));
        Assert.Contains("UNION", new Pg().sql(Build(Op())));
        foreach (var key in new[] { "limit", "order", "offset" })
            Assert.Contains("_move_ctes_to_top_level", Assert.Throws<NotPortedException>(() =>
                new NoModifiers().sql(Build(Op(extras: [(key, N("Var", "x"))])))).Message);
        foreach (var (key, child) in new[] { ("locks", "Get"), ("sample", "Get"), ("for_", "Get") })
        {
            object value = key == "locks" ? new object?[] { N(child) } : N(child);
            Assert.Throws<NotPortedException>(() => g.sql(Build(Op(extras: [(key, value)]))));
        }
        Assert.Throws<NotPortedException>(() => g.sql(Build(N("Subquery", Op(), extras: [("pivots", new object?[] { N("Get") })]))));
        Assert.Throws<NotPortedException>(() => g.sql(Build(N("Subquery", Op(), extras: [("sample", N("Get"))]))));
    }

    [Fact]
    public async Task Anchors_are_current_and_partial_assembly_is_honest()
    {
        const string script = """
            import sys,json
            from pathlib import Path
            sys.path.insert(0,'src/tools')
            import port_status as p
            ms=p.inventory(Path('python'),json.loads(Path('src/tools/scope.json').read_text()))
            entries=[x for x in p.report(ms,p.scan_anchors(Path('src/SqlGlotSharp')))['members'] if any(a['file']=='generator.setoperations.cs' for a in x['csharp'])]
            assert len(entries)==3,len(entries)
            errors=[x for x in entries if x['status']!=('stub' if x['anchor'].endswith('.set_operations') else 'ported')]
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
