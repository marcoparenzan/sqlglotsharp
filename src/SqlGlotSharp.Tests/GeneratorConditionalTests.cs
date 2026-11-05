using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Pg = SqlGlotSharp.generators.postgres.PostgresGenerator;

namespace SqlGlotSharp.Tests;

public class GeneratorConditionalTests
{
    private sealed record Node(string type, Dictionary<string, object?> args, List<string>? comments = null);
    private sealed record Sample(Node node, bool postgres, Dictionary<string, object?> options,
        string mode = "sql", bool comment = true, bool copy = true);
    private static Node N(string type, object? value = null, List<string>? comments = null,
        params (string key, object? value)[] extras)
    {
        var args = new Dictionary<string, object?>();
        if (value is not null) args["this"] = value;
        foreach (var (key, item) in extras) args[key] = item;
        return new(type, args, comments);
    }
    private static Node L(string value = "1", bool text = false) => N("Literal", value, extras: [("is_string", text)]);
    private static Node C(string name = "x") => N("Column", N("Identifier", name, extras: [("quoted", false)]));
    private static Node If(object? condition, object? yes, object? no = null, List<string>? comments = null) =>
        N("If", condition, comments, ("true", yes), ("false", no));
    private static Node Case(object? value, object? otherwise, params object?[] branches) =>
        N("Case", value, ["case */ /*", "line\nbreak", ""], ("ifs", branches), ("default", otherwise));

    private static exp.Expr Build(Node node, Dictionary<exp.Expr, string> paths, string path = "root")
    {
        object? Value(object? value, string p) => value switch
        {
            Node child => Build(child, paths, p),
            object?[] list => list.Select((v, i) => Value(v, $"{p}[{i}]")).ToList(),
            _ => value
        };
        var args = new PyDict<string, object?>();
        foreach (var (key, value) in node.args) args[key] = Value(value, path + "." + key);
        var type = exp.EXPR_CLASSES.Values.Single(t => t.Name == node.type);
        var expression = (exp.Expr)Activator.CreateInstance(type, [args])!;
        expression.comments = node.comments?.ToList();
        paths[expression] = path;
        return expression;
    }

    private static string Snapshot(Dictionary<exp.Expr, string> paths) => JsonSerializer.Serialize(
        paths.OrderBy(p => p.Value, StringComparer.Ordinal).Select(p => new
        {
            path = p.Value, type = p.Key.GetType().Name,
            keys = p.Key.args.Keys.Order(StringComparer.Ordinal).ToArray(), comments = p.Key.comments,
            parent = p.Key.parent is null ? null : paths.TryGetValue(p.Key.parent, out var parent) ? parent : "temporary:" + p.Key.parent.GetType().Name,
            key = p.Key.arg_key, index = p.Key.index,
            parent_keys = p.Key.parent?.args.Keys.Order(StringComparer.Ordinal).ToArray()
        }));

    private const string Oracle = """
        import sys,json
        sys.path.insert(0,'python')
        from sqlglot import exp
        from sqlglot.generator import Generator
        from sqlglot.generators.postgres import PostgresGenerator
        def build(v,paths,path='root'):
            if isinstance(v,list): return [build(x,paths,f'{path}[{i}]') for i,x in enumerate(v)]
            if not isinstance(v,dict) or 'type' not in v: return v
            n=getattr(exp,v['type'])(**{k:build(x,paths,path+'.'+k) for k,x in v['args'].items()})
            n.comments=v['comments']
            paths[id(n)]=(n,path)
            return n
        def snapshot(paths):
            return [dict(path=path,type=type(n).__name__,keys=sorted(n.args),comments=n.comments,
                parent=None if n.parent is None else paths[id(n.parent)][1] if id(n.parent) in paths else 'temporary:'+type(n.parent).__name__,
                key=n.arg_key,index=n.index,parent_keys=None if n.parent is None else sorted(n.parent.args))
                for n,path in sorted(paths.values(),key=lambda pair:pair[1])]
        out=[]
        for c in json.load(sys.stdin):
            paths={}
            n=build(c['node'],paths)
            g=(PostgresGenerator if c['postgres'] else Generator)(**c['options'])
            before=snapshot(paths)
            sql=g.generate(n,copy=c['copy']) if c['mode']=='generate' else g.sql(n,comment=c['comment'])
            out.append(dict(sql=sql,before=before,after=snapshot(paths),messages=g.unsupported_messages))
        print(json.dumps(out))
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Case_and_If_match_live_Python_SQL_comments_pretty_and_mutations(bool postgres)
    {
        var samples = new List<Sample>();
        foreach (var pretty in new[] { false, true })
        foreach (var comments in new[] { false, true })
        foreach (var width in new long[] { 0, 18, 19, 20, 80 })
        foreach (var pad in new long[] { 0, 2, 4 })
        {
            var options = new Dictionary<string, object?> { ["dialect"] = postgres ? "postgres" : "",
                ["pretty"] = pretty, ["comments"] = comments, ["max_text_width"] = width,
                ["pad"] = pad, ["indent"] = 3L, ["identify"] = true };
            var condition = N("GT", C(), ["predicate"], ("expression", L("0")));
            var result = N("Add", L(), ["result"], ("expression", L("2")));
            var nodes = new[]
            {
                Case(null, null), Case(null, null, If(L(), L())),
                Case(C("kind"), L("other", true), If(L("a", true), L("A", true)), If(L("b", true), L("B", true))),
                Case(null, N("Null", comments: ["default"]), If(condition, result, L("ignored"), ["branch comments must not render"])),
                Case(null, "", If(null, "")), Case("", "", If("", null)),
                If(condition, result, comments: ["if */ /*", "if\ncomment"]),
                If(condition, L("yes", true), L("no", true), ["if comment"]),
                If(N("Boolean", false), N("Null"), N("Boolean", true)),
                If(condition, Case(C(), L("9"), If(L(), L("2"))), If(condition, L("3"), L("4"))),
                Case(null, If(condition, L("5"), L("6")), If(condition, If(condition, L("7"), L("8")))),
                N("Alias", Case(null, L("x\ny'z", true), If(condition, result)), ["alias comment"], ("alias", N("Identifier", "label", extras: [("quoted", true)]))),
                N("Select", extras: [("expressions", new object?[] { C(), If(condition, result, L("3")), Case(C(), L("4"), If(L(), L("2"))) })])
            };
            foreach (var n in nodes)
            {
                samples.Add(new(n, postgres, options));
                samples.Add(new(n, postgres, options, comment: false));
                samples.Add(new(n, postgres, options, mode: "generate", copy: true));
                samples.Add(new(n, postgres, options, mode: "generate", copy: false));
            }
        }
        var expected = JsonDocument.Parse(await Python(Oracle, JsonSerializer.Serialize(samples)));
        Assert.Equal(samples.Count, expected.RootElement.GetArrayLength());
        for (var i = 0; i < samples.Count; i++)
        {
            var c = samples[i]; var o = c.options;
            generator.Generator g = postgres ? new Pg() : new generator.Generator();
            g.dialect = dialects.dialect.Dialect.get_or_raise(postgres ? "postgres" : "");
            g.pretty = o["pretty"]!; g.comments = (bool)o["comments"]!;
            g.max_text_width = (long)o["max_text_width"]!; g.pad = (long)o["pad"]!;
            g._indent = (long)o["indent"]!; g.identify = o["identify"]!;
            var paths = new Dictionary<exp.Expr, string>(ReferenceEqualityComparer.Instance);
            var n = Build(c.node, paths); var before = Snapshot(paths);
            var actual = c.mode == "generate" ? g.generate(n, copy: c.copy) : g.sql(n, comment: c.comment);
            var oracle = expected.RootElement[i];
            Assert.True(oracle.GetProperty("sql").GetString() == actual,
                $"Sample {i}: {JsonSerializer.Serialize(c)}\nPython: {oracle.GetProperty("sql")}\nC#: {actual}");
            Assert.Equal(Canonical(oracle.GetProperty("before")), Canonical(JsonDocument.Parse(before).RootElement));
            Assert.Equal(Canonical(oracle.GetProperty("after")), Canonical(JsonDocument.Parse(Snapshot(paths)).RootElement));
            Assert.Equal(oracle.GetProperty("messages").GetArrayLength(), g.unsupported_messages.Count);
        }
    }

    private static string Canonical(JsonElement element) => JsonSerializer.Serialize(element);

    private sealed class EnsureBools : generator.Generator { public override bool ENSURE_BOOLS => true; }

    [Fact]
    public void Missing_children_and_optimizer_are_explicit_NotPorted()
    {
        foreach (var g in new generator.Generator[] { new(), new Pg() })
        foreach (var node in new[]
        {
            Case(N("Get"), null), Case(null, N("Get")),
            Case(null, null, If(N("Get"), L())),
            Case(null, null, If(L(), N("Get"))), If(L(), L(), N("Get"))
        })
        {
            var error = Assert.Throws<NotPortedException>(() => g.sql(Build(node, new(ReferenceEqualityComparer.Instance))));
            Assert.Contains("TRANSFORMS[Get]", error.Message);
        }
        var expression = Build(If(L(), L(), L()), new(ReferenceEqualityComparer.Instance));
        Assert.Equal("CASE WHEN 1 <> 0 THEN 1 ELSE 1 END", new EnsureBools().generate(expression)); // ensure_bools rewrites the numeric predicate
        Assert.Equal("CASE WHEN 1 THEN 1 ELSE 1 END", new EnsureBools().sql(expression)); // sql() skips preprocess
    }

    [Fact]
    public async Task Conditional_anchors_are_current_and_dependencies_remain_honest()
    {
        const string script = """
            import sys,json
            from pathlib import Path
            sys.path.insert(0,'src/tools')
            import port_status as p
            ms=p.inventory(Path('python'),json.loads(Path('src/tools/scope.json').read_text()))
            names={'sqlglot/generator.py::Generator.case_sql','sqlglot/generator.py::Generator.if_sql'}
            entries=[x for x in p.report(ms,p.scan_anchors(Path('src/SqlGlotSharp')))['members'] if x['anchor'] in names]
            assert len(entries)==2 and all(x['status']=='ported' and len(x['csharp'])==1 and x['csharp'][0]['file']=='generator.conditionals.cs' for x in entries),json.dumps(entries)
            deps=[x for x in p.report(ms,p.scan_anchors(Path('src/SqlGlotSharp')))['members'] if x['anchor'] in {'sqlglot/generator.py::Generator.preprocess','sqlglot/generator.py::_build_dispatch'}]
            assert len(deps)==2 and all(x['status']=='stub' and x['csharp'][0]['hash']=='0000000000' for x in deps),json.dumps(deps)
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
