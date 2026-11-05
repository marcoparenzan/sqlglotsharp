using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Pg = SqlGlotSharp.generators.postgres.PostgresGenerator;

namespace SqlGlotSharp.Tests;

public class GeneratorIntervalTests
{
    private sealed record Node(string type, Dictionary<string, object?> args, List<string>? comments = null);
    private sealed record Case(Node node, bool postgres, bool pretty, bool comments, bool copy, string level);
    private sealed record Result(string? sql, string[] messages, string? error, Node state, bool[] attached);
    private static Node N(string type, params (string key, object? value)[] args) =>
        new(type, args.ToDictionary(x => x.key, x => x.value), ["interval */ /*", "line\ncomment"]);
    private static Node L(string value, bool str = true) => N("Literal", ("this", value), ("is_string", str));
    private static Node V(string value) => N("Var", ("this", value));
    private static Node I(object? value = null, object? unit = null) => N("Interval", ("this", value), ("unit", unit));
    private static Node Span(object? start, object? end) => N("IntervalSpan", ("this", start), ("expression", end));
    private static exp.Expr Build(Node node)
    {
        object? Convert(object? v) => v is Node child ? Build(child) : v is List<object?> list ? list.Select(Convert).ToList() : v;
        var args = new PyDict<string, object?>();
        foreach (var (key, value) in node.args) args[key] = Convert(value);
        var type = exp.EXPR_CLASSES.Values.Single(t => t.Name == node.type);
        var expression = (exp.Expr)Activator.CreateInstance(type, [args])!;
        expression.comments = node.comments?.ToList();
        return expression;
    }
    private static Node Snapshot(exp.Expr expression)
    {
        object? Convert(object? value) => value is exp.Expr child ? Snapshot(child) :
            value is List<object?> list ? list.Select(Convert).ToList() : value;
        return new(expression.GetType().Name, expression.args.ToDictionary(x => x.Key, x => Convert(x.Value)), expression.comments);
    }

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
        files=[p for p in corpus.rglob('*') if p.is_file()]
        assert files, 'empty corpus'
        with files[0].open('rb') as f: f.read(1)
        def build(v):
            if isinstance(v,list): return [build(x) for x in v]
            if not isinstance(v,dict) or 'type' not in v: return v
            node=getattr(exp,v['type'])(**{k:build(x) for k,x in v['args'].items()})
            node.comments=v['comments']
            return node
        def snap(v):
            if isinstance(v,exp.Expr): return dict(type=type(v).__name__,args={k:snap(x) for k,x in v.args.items()},comments=v.comments)
            if isinstance(v,list): return [snap(x) for x in v]
            return v
        results=[]
        for c in json.load(sys.stdin):
            root=build(c['node'])
            old=list(root.walk())
            g=(PostgresGenerator if c['postgres'] else Generator)(dialect='postgres' if c['postgres'] else None,
                pretty=c['pretty'],comments=c['comments'],unsupported_level=ErrorLevel[c['level']])
            sql=error=None
            try: sql=g.generate(root,copy=c['copy'])
            except Exception as e: error=str(e)
            results.append(dict(sql=sql,error=error,messages=g.unsupported_messages,state=snap(root),attached=[x.parent is not None for x in old]))
        print(json.dumps(results))
        """;

    [Fact]
    public async Task Production_generate_matches_Python_intervals_spans_additions_errors_and_mutations()
    {
        var nodes = new List<Node> { I(), I(L("")), I(null, V("DAY")), I(L("a'\\b\nend"), V("days")),
            I(N("Column", ("this", N("Identifier", ("this", "amount"), ("quoted", true)))), V("DAY")),
            I(N("Neg", ("this", L("2", false))), V("DAY")),
            I(N("Add", ("this", L("1", false)), ("expression", L("2", false))), V("DAY")),
            I(N("Paren", ("this", L("2", false))), V("DAY")),
            I(L("1-2"), Span(V("YEAR"), V("MONTH"))), I(L("3 04:05:06"), Span(V("DAY"), V("SECOND"))),
            Span(V("YEAR"), V("MONTH")), Span(null, V("SECOND")), I(L(""), Span(V("DAY"), null)) };
        foreach (var unit in new Node?[] { null, V("DAY"), V("days"), L("hour"), L("4", false),
            N("Identifier", ("this", "Odd Unit"), ("quoted", true)), Span(V("YEAR"), V("MONTH")) })
        foreach (var value in new[] { "", "1", "-2", "1.5", "1e3", "0", "a'\\b\nend" })
        foreach (var str in new[] { true, false }) nodes.Add(I(L(value, str), unit));
        foreach (var unit in new[] { "QUARTER", "quarters", "QuArTeRly" })
        foreach (var value in new[] { "2", "-2", "+2", "2.0", "2.5", "1e3", "1e-3", "1e1000000", "0e1000000",
            "NaN", "Infinity", "bad", "", " 2 ", "2_0", "١٢", "1e30" })
        {
            nodes.Add(I(L(value), V(unit)));
            if (value is "2" or "-2" or "2.0" or "2.5" or "1e3" or "1e-3" or "NaN" or "Infinity" or "bad" or "" or "1e1000000")
                nodes.Add(I(L(value, false), V(unit)));
        }
        nodes.Add(I(L("2"), L("QUARTER")));
        nodes.Add(I(L("2"), N("Identifier", ("this", "quarters"), ("quoted", true))));
        nodes.Add(N("Add", ("this", I(L("2"), V("QUARTER"))), ("expression", I(L("3"), V("DAY")))));
        nodes.Add(N("Select", ("expressions", new List<object?> { I(L("2"), V("QUARTER")), I(L("1-2"), Span(V("YEAR"), V("MONTH"))) })));
        var cases = new List<Case>();
        foreach (var postgres in new[] { true, false })
        foreach (var pretty in new[] { false, true })
        foreach (var comments in new[] { false, true })
        foreach (var copy in new[] { false, true })
        foreach (var level in new[] { "IGNORE", "WARN", "RAISE", "IMMEDIATE" })
        foreach (var node in nodes) cases.Add(new(node, postgres, pretty, comments, copy, level));
        var expected = JsonSerializer.Deserialize<Result[]>(await Python(Oracle, JsonSerializer.Serialize(cases)))!;
        Assert.Equal(cases.Count, expected.Length);
        for (var i = 0; i < cases.Count; i++)
        {
            var c = cases[i];
            var root = Build(c.node);
            var old = root.walk().ToList();
            generator.Generator g = c.postgres ? new Pg() : new generator.Generator();
            g.dialect = dialects.dialect.Dialect.get_or_raise(c.postgres ? "postgres" : null);
            g.pretty = c.pretty; g.comments = c.comments;
            g.unsupported_level = Enum.Parse<errors.ErrorLevel>(c.level);
            string? sql = null, error = null;
            try { sql = g.generate(root, copy: c.copy); }
            catch (errors.UnsupportedError e) { error = e.Message; }
            catch (FormatException e) { error = e.Message; }
            var actual = new Result(sql, g.unsupported_messages.ToArray(), error, Snapshot(root), old.Select(x => x.parent is not null).ToArray());
            Assert.True(JsonSerializer.Serialize(expected[i]) == JsonSerializer.Serialize(actual),
                $"Case {i}: {JsonSerializer.Serialize(c)}\nPython: {JsonSerializer.Serialize(expected[i])}\nC#: {JsonSerializer.Serialize(actual)}");
        }
    }

    private sealed class BareGenerator : generator.Generator
    {
        public bool single;
        public override bool AUTO_REFRESH_BARE_INTERVALS => true;
        public override bool INTERVAL_ALLOWS_PLURAL_FORM => false;
        public override bool SINGLE_STRING_INTERVAL => single;
    }

    [Fact]
    public async Task Base_single_string_bare_plural_and_ancestor_options_match_Python()
    {
        const string script = """
            import sys,json
            sys.path.insert(0,'python')
            from sqlglot import exp
            from sqlglot.generator import Generator
            def build(v):
                if not isinstance(v,dict) or 'type' not in v: return v
                n=getattr(exp,v['type'])(**{k:build(x) for k,x in v['args'].items()})
                n.comments=v['comments']
                return n
            out=[]
            for c in json.load(sys.stdin):
                class G(Generator):
                    AUTO_REFRESH_BARE_INTERVALS=True
                    INTERVAL_ALLOWS_PLURAL_FORM=False
                    SINGLE_STRING_INTERVAL=c['single']
                n=build(c['node'])
                if c['context']==1: parent=exp.AutoRefreshProperty(this=n)
                elif c['context']==2: parent=exp.AutoRefreshProperty(this=exp.Select(expressions=[n]))
                out.append(G(pretty=c['pretty'],comments=c['comments']).sql(n))
            print(json.dumps(out))
            """;
        var cases = new List<object>();
        var actual = new List<string>();
        foreach (var single in new[] { false, true })
        foreach (var pretty in new[] { false, true })
        foreach (var comments in new[] { false, true })
        foreach (var context in new[] { 0, 1, 2 })
        foreach (var value in new Node?[] { null, L(""), L("2"), L("-2", false),
            N("Add", ("this", L("1", false)), ("expression", L("2", false))) })
        foreach (var unit in new Node?[] { null, V("DAYS"), Span(V("YEAR"), V("MONTH")) })
        {
            var node = I(value, unit);
            cases.Add(new { node, single, pretty, comments, context });
            var interval = Build(node);
            if (context == 1) _ = new exp.AutoRefreshProperty(@this: interval);
            if (context == 2) _ = new exp.AutoRefreshProperty(@this: new exp.Select(expressions: new List<object?> { interval }));
            actual.Add(new BareGenerator { single = single, pretty = pretty, comments = comments }.sql(interval));
        }
        Assert.Equal(JsonSerializer.Deserialize<string[]>(await Python(script, JsonSerializer.Serialize(cases))), actual);
    }
    [Fact]
    public void Base_options_and_nearest_ancestor_boundary_are_preserved()
    {
        var g = new BareGenerator();
        var interval = new exp.Interval(exp.Literal.@string("2"), new exp.Var("DAYS"));
        _ = new exp.AutoRefreshProperty(@this: interval);
        Assert.Equal("2 DAY", g.sql(interval));
        _ = new exp.Select(expressions: new List<object?> { interval });
        Assert.Equal("INTERVAL '2' DAY", g.sql(interval));
        Assert.Equal("INTERVAL (1 + 2) DAY", g.sql(new exp.Interval(new exp.Add(exp.Literal.number(1), exp.Literal.number(2)), new exp.Var("DAYS"))));
    }

    [Fact]
    public void Missing_rewrites_and_runtime_numeric_boundaries_are_explicit()
    {
        var pg = new Pg();
        Assert.Equal("INTERVAL", pg.generate(new exp.Interval()));
        foreach (var node in new exp.Expr[] { new exp.Pivot() })
            Assert.Throws<NotPortedException>(() => pg.generate(node));
        Assert.Equal("INTERVAL WEEK", pg.generate(new exp.Interval(new exp.Select(), new exp.WeekStart(@this: new exp.Var("MONDAY"))))); // oracle value
        Assert.Contains("arbitrary-precision", Assert.Throws<NotPortedException>(() => pg.generate(
            new exp.Interval(exp.Literal.number("9223372036854775808"), new exp.Var("QUARTER")))).Message);
    }

    [Fact]
    public async Task Interval_port_anchors_match_upstream_and_keep_numeric_boundaries_partial()
    {
        const string script = """
            import sys,ast
            from pathlib import Path
            sys.path.insert(0,'src/tools')
            import port_status as p
            tree=ast.parse(Path('python/sqlglot/generator.py').read_text())
            cls=next(n for n in tree.body if isinstance(n,ast.ClassDef) and n.name=='Generator')
            node=next(n for n in cls.body if isinstance(n,ast.FunctionDef) and n.name=='interval_sql')
            h=p.normalized_hash([node])
            anchors=p.scan_anchors(Path('src/SqlGlotSharp'))
            assert anchors['sqlglot/generator.py::Generator.interval_sql'][0]['hash']==h, h
            assert anchors['sqlglot/generators/postgres.py::PostgresGenerator.interval_sql'][0]['hash']=='0000000000'
            print('OK')
            """;
        Assert.Equal("OK", (await Python(script, "")).Trim());
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
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
            process.StandardInput.Close(); await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, await stderr);
        return await stdout;
    }
}
