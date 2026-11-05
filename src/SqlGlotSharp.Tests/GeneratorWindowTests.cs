using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Pg = SqlGlotSharp.generators.postgres.PostgresGenerator;

namespace SqlGlotSharp.Tests;

public class GeneratorWindowTests
{
    private sealed record Node(string type, Dictionary<string, object?> args, List<string>? comments = null);
    private sealed record Case(Node node, bool postgres, bool pretty, bool comments, bool copy, string level, int width, bool identify);
    private sealed record Result(string? sql, string[] messages, string? error, Node state, string[] links);
    private static Node N(string type, params (string key, object? value)[] args) =>
        new(type, args.ToDictionary(x => x.key, x => x.value), ["window */ /*", "line\ncomment"]);
    private static Node Id(string value) => N("Identifier", ("this", value), ("quoted", false));
    private static Node Col(string value) => N("Column", ("this", Id(value)));
    private static Node Num(string value) => N("Literal", ("this", value), ("is_string", false));
    private static Node Sum() => N("Sum", ("this", Col("amount")));
    private static Node Spec(object? start = null, string? startSide = null, object? end = null, string? endSide = null,
        string? kind = "ROWS", object? exclude = null) => N("WindowSpec", ("kind", kind), ("start", start),
            ("start_side", startSide), ("end", end), ("end_side", endSide), ("exclude", exclude));
    private static Node Win(Node? spec = null, object? alias = null, object? first = null, string? over = null) =>
        N("Window", ("this", Sum()), ("partition_by", new List<object?> { Col("department"), Col("team") }),
            ("order", N("Order", ("expressions", new List<object?> {
                N("Ordered", ("this", Col("day")), ("desc", false), ("nulls_first", false)),
                N("Ordered", ("this", Col("sequence")), ("desc", true), ("nulls_first", true)) }))),
            ("spec", spec), ("alias", alias), ("first", first), ("over", over));
    private static Node Select(Node window, List<object?>? windows = null) =>
        N("Select", ("expressions", new List<object?> { window, N("Window", ("this", N("RowNumber"))) }),
            ("from_", N("From", ("this", N("Table", ("this", Id("events")))))), ("windows", windows));

    private static exp.Expr Build(Node node)
    {
        object? Convert(object? v) => v is Node n ? Build(n) : v is List<object?> list ? list.Select(Convert).ToList() : v;
        var args = new PyDict<string, object?>();
        foreach (var (key, value) in node.args) args[key] = Convert(value);
        var type = exp.EXPR_CLASSES.Values.Single(t => t.Name == node.type);
        var result = (exp.Expr)Activator.CreateInstance(type, [args])!;
        result.comments = node.comments?.ToList();
        return result;
    }
    private static Node Snapshot(exp.Expr node)
    {
        object? Convert(object? value) => value is exp.Expr child ? Snapshot(child) :
            value is List<object?> list ? list.Select(Convert).ToList() : value;
        return new(node.GetType().Name, node.args.ToDictionary(x => x.Key, x => Convert(x.Value)), node.comments);
    }
    private static string Link(exp.Expr node) => $"{node.parent?.GetType().Name ?? ""}:{node.arg_key ?? ""}:{node.index?.ToString() ?? ""}";

    private const string Oracle = """
        import sys,json
        from pathlib import Path
        sys.path.insert(0,'python')
        from sqlglot import exp
        from sqlglot.generator import Generator
        from sqlglot.generators.postgres import PostgresGenerator
        from sqlglot.errors import ErrorLevel
        corpus=Path(r'D:\clones\corpus')
        assert corpus.is_dir(), 'external corpus unavailable'
        sample=next(p for p in corpus.rglob('*') if p.is_file())
        with sample.open('rb') as f: f.read(4096)
        def build(v):
            if isinstance(v,list): return [build(x) for x in v]
            if not isinstance(v,dict) or 'type' not in v: return v
            n=getattr(exp,v['type'])(**{k:build(x) for k,x in v['args'].items()})
            n.comments=v['comments']
            return n
        def snap(v):
            if isinstance(v,exp.Expr): return dict(type=type(v).__name__,args={k:snap(x) for k,x in v.args.items()},comments=v.comments)
            if isinstance(v,list): return [snap(x) for x in v]
            return v
        def link(n):
            return f'{type(n.parent).__name__ if n.parent is not None else ""}:{n.arg_key or ""}:{n.index if n.index is not None else ""}'
        out=[]
        for c in json.load(sys.stdin):
            n=build(c['node']); old=list(n.walk())
            g=(PostgresGenerator if c['postgres'] else Generator)(dialect='postgres' if c['postgres'] else None,
                pretty=c['pretty'],comments=c['comments'],unsupported_level=ErrorLevel[c['level']],
                max_text_width=c['width'],identify=c['identify'],normalize_functions='lower')
            sql=error=None
            try: sql=g.generate(n,copy=c['copy'])
            except Exception as e: error=str(e)
            out.append(dict(sql=sql,messages=g.unsupported_messages,error=error,state=snap(n),links=[link(x) for x in old]))
        print(json.dumps(out))
        """;

    [Fact]
    public async Task Production_generate_matches_live_Python_windows_frames_named_declarations_and_mutations()
    {
        var nodes = new List<Node> {
            N("Window", ("this", N("RowNumber"))),
            N("Window", ("this", Sum()), ("alias", Id("w"))),
            N("Window", ("this", Sum()), ("first", true)),
            N("Window", ("this", Sum()), ("first", false)),
            Win(), Win(alias: Id("base_window"), first: true, over: "OVER"), Win(first: false, over: "KEEP"),
            N("PartitionByExcluding", ("expressions", new List<object?> { Col("a"), Col("b") })),
            N("PartitionByExcluding"), Spec(), Spec(kind: null), Spec(end: Num("2"), endSide: "FOLLOWING"),
            Spec(startSide: "PRECEDING", endSide: "FOLLOWING"),
        };
        foreach (var kind in new[] { "ROWS", "RANGE", "GROUPS" })
        foreach (var bounds in new[] {
            Spec("UNBOUNDED", "PRECEDING", "CURRENT ROW", kind: kind),
            Spec(Num("2"), "PRECEDING", Num("3"), "FOLLOWING", kind),
            Spec("CURRENT ROW", end: "UNBOUNDED", endSide: "FOLLOWING", kind: kind),
            Spec(Num("0"), "PRECEDING", kind: kind),
            Spec("CURRENT ROW", kind: kind),
            Spec("UNBOUNDED", "PRECEDING", exclude: "CURRENT ROW", kind: kind),
            Spec("UNBOUNDED", "PRECEDING", exclude: N("Var", ("this", "TIES")), kind: kind),
            Spec("UNBOUNDED", "PRECEDING", exclude: "GROUP", kind: kind),
            Spec("UNBOUNDED", "PRECEDING", exclude: "NO OTHERS", kind: kind) })
        {
            nodes.Add(bounds); nodes.Add(Win(bounds)); nodes.Add(Select(Win(bounds)));
        }
        var declaration = Win(Spec("UNBOUNDED", "PRECEDING"), alias: Id("base_window"));
        declaration.args["this"] = Id("w");
        var inherited = N("Window", ("this", Id("w2")), ("alias", Id("w")));
        nodes.Add(Select(N("Window", ("this", Sum()), ("alias", Id("w2"))), [declaration, inherited]));
        nodes.Add(Select(Win(Spec(Num("1"), "PRECEDING")), []));
        // PostgreSQL's interval conversion mutates a RANGE bound with copy=False.
        // Compare original AST state and parent links, not just rendered SQL.
        nodes.Add(Win(Spec(N("Interval", ("this", N("Literal", ("this", "2"), ("is_string", true))),
            ("unit", N("Var", ("this", "QUARTER")))), "PRECEDING", kind: "RANGE")));
        var pgOnly = new[] { "IgnoreNulls", "RespectNulls" }.Select(type =>
            N("Window", ("this", N(type, ("this", N("RowNumber")))))).ToArray();
        var cases = new List<Case>();
        foreach (var postgres in new[] { false, true })
        foreach (var pretty in new[] { false, true })
        foreach (var comments in new[] { false, true })
        foreach (var copy in new[] { false, true })
        foreach (var level in new[] { "IGNORE", "WARN", "RAISE", "IMMEDIATE" })
        foreach (var width in new[] { 24, 80 })
        foreach (var node in nodes.Concat(postgres ? pgOnly : []))
            cases.Add(new(node, postgres, pretty, comments, copy, level, width, width == 24));
        var expected = JsonSerializer.Deserialize<Result[]>(await Python(Oracle, JsonSerializer.Serialize(cases)))!;
        Assert.Equal(cases.Count, expected.Length);
        for (var i = 0; i < cases.Count; i++)
        {
            var c = cases[i]; var root = Build(c.node); var old = root.walk().ToList();
            generator.Generator g = c.postgres ? new Pg() : new generator.Generator();
            g.dialect = dialects.dialect.Dialect.get_or_raise(c.postgres ? "postgres" : null);
            g.pretty = c.pretty; g.comments = c.comments; g.max_text_width = c.width; g.identify = c.identify;
            g.normalize_functions = "lower"; g.unsupported_level = Enum.Parse<errors.ErrorLevel>(c.level);
            string? sql = null, error = null;
            try { sql = g.generate(root, copy: c.copy); }
            catch (errors.UnsupportedError e) { error = e.Message; }
            var actual = new Result(sql, g.unsupported_messages.ToArray(), error, Snapshot(root), old.Select(Link).ToArray());
            Assert.True(JsonSerializer.Serialize(expected[i]) == JsonSerializer.Serialize(actual),
                $"Case {i}: {JsonSerializer.Serialize(c)}\nPython: {JsonSerializer.Serialize(expected[i])}\nC#: {JsonSerializer.Serialize(actual)}");
        }
    }

    private sealed class NoNullOrdering : generator.Generator
    {
        public override bool? NULL_ORDERING_SUPPORTED => false;
    }

    [Fact]
    public void Missing_dependencies_are_explicit_not_silently_omitted()
    {
        var pg = new Pg();
        foreach (var (key, child) in new (string, exp.Expr)[] {
            ("spec", new exp.MatchRecognize()), ("partition_by", new exp.MatchRecognize()),
            ("alias", new exp.DynamicIdentifier()), ("this", new exp.MatchRecognize()) })
        {
            var w = Build(Win());
            w.set(key, key == "partition_by" ? new List<object?> { child } : child);
            Assert.Throws<NotPortedException>(() => pg.generate(w));
        }
        var ordered = new exp.Ordered(@this: new exp.Column(@this: new exp.Identifier("x")), nulls_first: false);
        var window = new exp.Window(@this: new exp.RowNumber(), order: new exp.Order(expressions: new List<object?> { ordered }));
        Assert.Equal("ROW_NUMBER() OVER (ORDER BY x NULLS LAST)", new NoNullOrdering().generate(window)); // oracle value
        Assert.Equal("ROWS BETWEEN  AND CURRENT ROW", new generator.Generator().generate(new exp.WindowSpec(kind: "ROWS")));
        Assert.True(pg._dispatch.ContainsKey(typeof(exp.Window)));
        Assert.True(pg._dispatch.ContainsKey(typeof(exp.WindowSpec)));
    }

    [Fact]
    public async Task Window_anchor_dependency_boundaries_match_upstream()
    {
        const string script = """
            import sys,ast,json
            from pathlib import Path
            sys.path.insert(0,'src/tools')
            import port_status as p
            tree=ast.parse(Path('python/sqlglot/generator.py').read_text())
            cls=next(n for n in tree.body if isinstance(n,ast.ClassDef) and n.name=='Generator')
            names=['partition_by_sql','partitionbyexcluding_sql','windowspec_sql']
            hashes={n.name:p.normalized_hash([n]) for n in cls.body if isinstance(n,ast.FunctionDef) and n.name in names}
            anchors=p.scan_anchors(Path('src/SqlGlotSharp'))
            assert anchors['sqlglot/generator.py::Generator.window_sql'][0]['hash']=='0000000000'
            for name,h in hashes.items():
                assert anchors['sqlglot/generator.py::Generator.'+name][0]['hash']==h, json.dumps(hashes)
            print('OK')
            """;
        Assert.Equal("OK", (await Python(script, "")).Trim());
    }

    private static string SourcePath([CallerFilePath] string source = "") => source;
    private static async Task<string> Python(string script, string input)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SQLGLOT_PYTHON") ?? "python") {
            WorkingDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath())!, "..", "..")),
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        start.ArgumentList.Add("-B"); start.ArgumentList.Add("-c"); start.ArgumentList.Add(script);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(input); process.StandardInput.Close();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await error);
        return await output;
    }
}
