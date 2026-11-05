using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Pg = SqlGlotSharp.generators.postgres.PostgresGenerator;

namespace SqlGlotSharp.Tests;

public class GeneratorCollectionTests
{
    private sealed record Node(string type, Dictionary<string, object?> args, List<string>? comments = null);
    private sealed record Case(Node node, bool postgres, Dictionary<string, object?> options, bool comment = true, bool exclude = false);
    private static Node N(string type, object? value = null, List<string>? comments = null,
        params (string key, object? value)[] extras)
    {
        var args = new Dictionary<string, object?> { ["this"] = value };
        foreach (var (key, item) in extras) args[key] = item;
        return new(type, args, comments);
    }
    private static Node I(string name, bool quoted = false) => N("Identifier", name, extras: [("quoted", quoted)]);
    private static Node C(string name = "value") => N("Column", I(name), ["column */ /*"], ("table", I("t")));
    private static Node L(string value = "2", bool text = false) => N("Literal", value, extras: [("is_string", text)]);
    private static Node T(params object?[] items) => N("Tuple", comments: ["tuple\ncomment"], extras: [("expressions", items)]);
    private static Node S(params object?[] items) => N("Select", comments: ["select comment"], extras: [("expressions", items)]);
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
    private sealed class Exclude : generator.Generator { public override string STAR_EXCEPT => "EXCLUDE"; }
    private const string Oracle = """
        import sys,json
        sys.path.insert(0,'python')
        from sqlglot import exp
        from sqlglot.generator import Generator
        from sqlglot.generators.postgres import PostgresGenerator
        class Exclude(Generator): STAR_EXCEPT='EXCLUDE'
        def build(v):
            if isinstance(v,list): return [build(x) for x in v]
            if not isinstance(v,dict) or 'type' not in v: return v
            n=getattr(exp,v['type'])(**{k:build(x) for k,x in v['args'].items()})
            n.comments=v['comments']
            return n
        out=[]
        for c in json.load(sys.stdin):
            G=Exclude if c['exclude'] else PostgresGenerator if c['postgres'] else Generator
            out.append(G(**c['options']).sql(build(c['node']),comment=c['comment']))
        print(json.dumps(out))
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stars_and_tuples_match_Python_production_dispatch(bool postgres)
    {
        var cases = new List<Case>();
        foreach (var pretty in new[] { false, true })
        foreach (var comments in new[] { false, true })
        foreach (var leading in new[] { false, true })
        foreach (var identify in new object[] { false, true, "safe" })
        foreach (var pad in new long[] { 0, 2 })
        foreach (var width in new long[] { 12, 80 })
        {
            var options = new Dictionary<string, object?> { ["dialect"] = postgres ? "postgres" : "",
                ["pretty"] = pretty, ["comments"] = comments, ["leading_comma"] = leading,
                ["identify"] = identify, ["pad"] = pad, ["max_text_width"] = width };
            void Add(Node n, bool comment = true) => cases.Add(new(n, postgres, options, comment));
            var star = N("Star", comments: ["star */ /*", "line\nbreak"], extras: [
                ("except_", new object?[] { I("a"), C("CamelCase"), "", null }),
                ("replace", new object?[] { N("Alias", N("Add", L(), ["add"], ("expression", L("3"))), ["replacement"], ("alias", I("new", true))) }),
                ("rename", new object?[] { N("Alias", C("old"), ["rename"], ("alias", I("new_name"))) }),
                ("ilike", L("ab'%\n", true))]);
            Add(N("Star")); Add(N("Star", comments: ["plain star"])); Add(star); Add(star, false);
            Add(N("Star", extras: [("except_", Array.Empty<object?>()), ("replace", null), ("rename", new object?[] { "", null }), ("ilike", "")]));
            foreach (var key in new[] { "except_", "replace", "rename" })
                Add(N("Star", extras: [(key, new object?[] { I("Only", true) })]));
            Add(N("Star", extras: [("ilike", L("%name%", true))]));
            Add(N("Column", star, ["qualified"], ("table", I("t"))));
            Add(T()); Add(T(L())); Add(T(L(), C(), L("a very long string", true)));
            Add(T(null, "", L(), null)); Add(T(T(L(), L("4")), N("Paren", T(C(), star))));
            Add(N("Alias", T(C(), L()), ["tuple alias"], ("alias", I("row", true))));
            if (!postgres)
            {
                // Real nested SELECT dispatch, not direct select_sql or a fake child renderer.
                Add(S(star, T(C(), L())));
                Add(S(N("Paren", S(N("Star"))), T(N("Paren", S(T(L(), L("3")))))));
                Add(T(S(N("Star")), T(S(L()))));
                cases.Add(new(star, false, options, exclude: true));
            }
        }
        var expected = JsonSerializer.Deserialize<string[]>(await Python(Oracle, JsonSerializer.Serialize(cases)))!;
        Assert.Equal(cases.Count, expected.Length);
        for (var i = 0; i < cases.Count; i++)
        {
            var c = cases[i]; var o = c.options;
            generator.Generator g = c.exclude ? new Exclude() : postgres ? new Pg() : new generator.Generator(dialect: "");
            if (postgres) g.dialect = dialects.dialect.Dialect.get_or_raise("postgres");
            g.pretty = o["pretty"]!; g.comments = (bool)o["comments"]!; g.leading_comma = (bool)o["leading_comma"]!;
            g.identify = o["identify"]!; g.pad = (long)o["pad"]!; g.max_text_width = (long)o["max_text_width"]!;
            var actual = g.sql(Build(c.node), comment: c.comment);
            Assert.True(expected[i] == actual, $"Case {i}: {JsonSerializer.Serialize(c)}\nPython: {expected[i]}\nC#: {actual}");
        }
    }

    [Fact]
    public void Missing_children_and_Postgres_Select_remain_explicit_failures()
    {
        foreach (var g in new generator.Generator[] { new generator.Generator(), new Pg() })
        {
            foreach (var key in new[] { "except_", "replace", "rename", "ilike" })
            {
                var child = N("Anonymous", "rewrite");
                var value = key == "ilike" ? (object)child : new object?[] { child };
                Assert.Contains("REWRITE()", g.sql(Build(N("Star", extras: [(key, value)]))));
            }
            Assert.Equal("(REWRITE()) /* tuple\ncomment */", g.sql(Build(T(N("Anonymous", "rewrite")))));
            // Except is a set-operation transform, not a star list-modifier helper.
            var setOperationModifier = Build(N("Star", extras: [("except_", new object?[] { N("Except", S(L()), extras: [("expression", S(L()))]) })]));
            Assert.Contains("EXCEPT", g.sql(setOperationModifier));
            Assert.Equal("*", g.generate(new exp.Star()));
        }
        var pg = new Pg();
        foreach (var node in new[] { S(N("Star")), T(S(L())), N("Paren", S(T(L(), L("3")))) })
        {
            Assert.Contains("SELECT", pg.sql(Build(node)));
        }
        Assert.Equal("*", new generator.Generator().sql(new exp.Star()));
        Assert.Equal("()", new Pg().sql(Build(T()), comment: false));
    }

    [Fact]
    public async Task Collection_anchors_are_current_and_complete_using_port_status()
    {
        const string script = """
            import sys,json
            from pathlib import Path
            sys.path.insert(0,'src/tools')
            import port_status as p
            ms=p.inventory(Path('python'),json.loads(Path('src/tools/scope.json').read_text()))
            names={'sqlglot/generator.py::Generator.star_sql','sqlglot/generator.py::Generator.tuple_sql'}
            selected=[x for x in ms if x['anchor'] in names]
            entries=[x for x in p.report(ms,p.scan_anchors(Path('src/SqlGlotSharp')))['members'] if x['anchor'] in names]
            assert len(entries)==2 and all(x['status']=='ported' and len(x['csharp'])==1 and x['csharp'][0]['file']=='generator.collections.cs' for x in entries),json.dumps(selected)
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
