using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pg = SqlGlotSharp.generators.postgres.PostgresGenerator;

namespace SqlGlotSharp.Tests;

public class GeneratorJsonOperatorTests
{
    private sealed record Node(string type, Dictionary<string, object?> args, List<string>? comments = null);
    private sealed record Case(Node node, bool pretty, bool comments, bool copy, string level);
    private static Node N(string type, params (string key, object? value)[] args) =>
        new(type, args.ToDictionary(x => x.key, x => x.value));
    private static Node L(string text, bool str = true) => N("Literal", ("this", text), ("is_string", str));
    private static Node V(string text) => N("Var", ("this", text));
    private static Node P(params Node[] parts) => N("JSONPath", ("expressions", parts.Cast<object?>().ToList()));
    private static Node K(string key) => N("JSONPathKey", ("this", key));
    private static Node S(object value) => N("JSONPathSubscript", ("this", value));
    private static Node E(string kind, object? path, bool only = false, object? source = null, List<object?>? more = null) =>
        N(kind, ("this", source ?? V("doc")), ("expression", path), ("only_json_types", only), ("expressions", more));

    private static exp.Expr Build(Node node)
    {
        object? Convert(object? value) => value is Node child ? Build(child) :
            value is List<object?> list ? list.Select(Convert).ToList() : value;
        var args = new PyDict<string, object?>();
        foreach (var (key, value) in node.args) args[key] = Convert(value);
        var expression = (exp.Expr)Activator.CreateInstance(exp.EXPR_CLASSES.Values.Single(t => t.Name == node.type), [args])!;
        expression.comments = node.comments?.ToList();
        return expression;
    }
    private static object? Snapshot(object? value) => value switch
    {
        exp.Expr node => new Node(node.GetType().Name, node.args.ToDictionary(x => x.Key, x => Snapshot(x.Value)), node.comments),
        List<object?> list => list.Select(Snapshot).ToList(),
        Enum e => e.ToString(),
        _ => value
    };

    private const string Oracle = """
        import sys,json,enum
        from pathlib import Path
        sys.path.insert(0,'python')
        from sqlglot import exp
        from sqlglot.generators.postgres import PostgresGenerator
        from sqlglot.errors import ErrorLevel
        corpus=Path(r'D:\clones\corpus')
        assert corpus.is_dir(), 'external corpus unavailable'
        first=next(p for p in corpus.rglob('*') if p.is_file())
        with first.open('rb') as f: f.read(1)
        def build(v):
            if isinstance(v,list): return [build(x) for x in v]
            if not isinstance(v,dict) or 'type' not in v: return v
            node=getattr(exp,v['type'])(**{k:build(x) for k,x in v['args'].items()})
            node.comments=v['comments']
            return node
        def snap(v):
            if isinstance(v,exp.Expr): return dict(type=type(v).__name__,args={k:snap(x) for k,x in v.args.items()},comments=v.comments)
            if isinstance(v,list): return [snap(x) for x in v]
            if isinstance(v,enum.Enum): return v.name
            return v
        results=[]
        for c in json.load(sys.stdin):
            root=build(c['node']); old=list(root.walk())
            g=PostgresGenerator(dialect='postgres',pretty=c['pretty'],comments=c['comments'],unsupported_level=ErrorLevel[c['level']])
            sql=error=None
            try: sql=g.generate(root,copy=c['copy'])
            except Exception as e: error=str(e)
            results.append(dict(sql=sql,error=error,messages=g.unsupported_messages,state=snap(root),attached=[x.parent is not None for x in old]))
        print(json.dumps(results))
        """;

    [Fact]
    public async Task Production_generate_matches_live_Python_SQL_errors_comments_and_mutation()
    {
        var nodes = new List<Node>();
        foreach (var kind in new[] { "JSONExtract", "JSONExtractScalar", "JSONBExtract", "JSONBExtractScalar" })
        {
            foreach (var path in new Node[] { L("key"), L("0"), L("-1", false), V("key_column"),
                N("Add", ("this", V("idx")), ("expression", L("1", false))),
                N("Not", ("this", V("flag"))),
                N("EQ", ("this", V("k")), ("expression", L("x"))),
                N("Paren", ("this", V("key_column"))),
                P(N("JSONPathRoot")), P(N("JSONPathRoot"), K("a")),
                P(N("JSONPathRoot"), K("0"), S(-1), K("a.b[0]")),
                P(K("..leading"), K("a'\\b\"\nend")), P(K("")), P(S("last")),
                P(S(0), S(1234567890123L)), P(), N("Variadic", ("this", L("{}"))),
                P(K("commented") with { comments = ["key */ /*", "line\ncomment"] },
                    S(-2) with { comments = ["index"] }),
                N("Add", ("this", V("idx")), ("expression", L("1", false))) with { comments = ["computed path"] } })
            foreach (var only in new[] { false, true })
            {
                nodes.Add(E(kind, path, only));
                nodes.Add(E(kind, path, only, L("{\"a\":1}")));
            }
            foreach (var path in new[] { V("dynamic"), L("first"), N("Variadic", ("this", L("{}"))) })
                nodes.Add(E(kind, path, more: [L("second"), V("third")]));
        }
        // Direct part dispatch, exact key/subscript rendering and variadic transform.
        nodes.AddRange([P(N("JSONPathRoot"), K("a"), S(2)), K("name"), S("a\"b"), S(-2), N("JSONPathRoot")]);
        nodes = nodes.Select(n => n with { comments = ["json */ /*", "line\ncomment"] }).ToList();
        var cases = (from node in nodes from pretty in new[] { false, true }
                     from copy in new[] { false, true } from comments in new[] { false, true }
                     from level in new[] { "IGNORE", "RAISE", "IMMEDIATE" }
                     select new Case(node, pretty, comments, copy, level)).ToArray();
        var expected = JsonNode.Parse(await Python(Oracle, JsonSerializer.Serialize(cases)))!.AsArray();
        for (var i = 0; i < cases.Length; i++)
        {
            var c = cases[i]; var root = Build(c.node); var old = root.walk().ToArray();
            var g = new Pg { dialect = dialects.dialect.Dialect.get_or_raise("postgres"), pretty = c.pretty,
                comments = c.comments, unsupported_level = Enum.Parse<errors.ErrorLevel>(c.level) };
            string? sql = null, error = null;
            try { sql = g.generate(root, copy: c.copy); }
            catch (Exception e) { error = e.Message; }
            var actual = JsonSerializer.SerializeToNode(new { sql, error, messages = g.unsupported_messages,
                state = Snapshot(root), attached = old.Select(n => n.parent is not null).ToArray() });
            Assert.True(JsonNode.DeepEquals(expected[i], actual),
                $"Case {i}: {JsonSerializer.Serialize(c)}\nPython: {expected[i]}\nC#: {actual}");
        }
    }

    [Fact]
    public async Task Unsupported_paths_are_explicit_not_approximate_SQL()
    {
        var parts = new[] { "JSONPathWildcard", "JSONPathRecursive", "JSONPathFilter", "JSONPathScript",
            "JSONPathSelector", "JSONPathSlice", "JSONPathUnion" };
        var nodes = (from part in parts from kind in new[] { "JSONExtract", "JSONExtractScalar", "JSONBExtract", "JSONBExtractScalar" }
                     select E(kind, P(N("JSONPathRoot"), N(part)), only: true)).ToList();
        var concat_nodes = nodes.Take(0).ToList(); // CONCAT is ported: oracle-only sample of "Python can emit SQL"
        foreach (var kind in new[] { "JSONExtract", "JSONExtractScalar", "JSONBExtract", "JSONBExtractScalar" })
        {
            nodes.Add(E(kind, P(N("JSONPathRoot"), N("JSONPathKey", ("this", N("JSONPathWildcard"))))));
            concat_nodes.Add(E(kind, N("Concat", ("expressions", new List<object?> { V("a"), V("b") }))));
        }
        // Run the production Python oracle even for branches intentionally outside this port.
        var oracle = JsonNode.Parse(await Python(Oracle, JsonSerializer.Serialize(
            nodes.Concat(concat_nodes).Select(n => new Case(n, false, true, false, "IMMEDIATE")))))!.AsArray();
        Assert.Equal(nodes.Count + concat_nodes.Count, oracle.Count);
        Assert.Contains(oracle, r => r!["error"] is not null); // live production errors, not just SQL fixtures
        Assert.Contains(oracle, r => r!["sql"] is not null); // Python can emit SQL for intentionally unported branches
        // The first parts.Length*4 nodes hold a bare unsupported JSONPath part. PostgreSQL drops those
        // transforms, so upstream raises ValueError("Unsupported expression type ...") from sql().
        var bare = parts.Length * 4;
        Assert.All(oracle.Take(bare), r => Assert.NotNull(r!["error"]));
        foreach (var node in nodes.Take(bare))
        foreach (var level in new[] { errors.ErrorLevel.IGNORE, errors.ErrorLevel.RAISE, errors.ErrorLevel.IMMEDIATE })
            Assert.Throws<ArgumentException>(() => new Pg { dialect = dialects.dialect.Dialect.get_or_raise("postgres"), unsupported_level = level }.generate(Build(node)));
        // JSONPath wildcard keys now follow upstream: json_path_key_only_name reports an unsupported message and renders node.name.
        foreach (var node in nodes.Skip(bare))
            _ = new Pg { dialect = dialects.dialect.Dialect.get_or_raise("postgres"), unsupported_level = errors.ErrorLevel.IGNORE }.generate(Build(node));
        foreach (var payload in new object?[] { null, true, 1.5, V("dynamic") })
            Assert.Throws<NotPortedException>(() => new Pg { dialect = dialects.dialect.Dialect.get_or_raise("postgres") }.generate(Build(E("JSONExtract", P(S(payload!))))));
    }

    [Fact]
    public void Real_Postgres_dispatch_does_not_use_function_fallback()
    {
        var g = new Pg { dialect = dialects.dialect.Dialect.get_or_raise("postgres") };
        Assert.Equal("doc -> 'key'", g.generate(Build(E("JSONExtract", L("key")))));
        Assert.Equal("doc ->> 0", g.generate(Build(E("JSONExtractScalar", L("0", false)))));
        Assert.Equal("doc #> '{a,0}'", g.generate(Build(E("JSONBExtract", L("{a,0}")))));
        Assert.Equal("doc #>> '{a,0}'", g.generate(Build(E("JSONBExtractScalar", L("{a,0}")))));
        Assert.Equal("JSON_EXTRACT_PATH(doc, 'a', '0')", g.generate(Build(E("JSONExtract", P(K("a"), S(0))))));
        Assert.Equal("doc -> 'a' -> 0", g.generate(Build(E("JSONExtract", P(K("a"), S(0)), only: true))));
        Assert.Equal("JSON_EXTRACT(doc, 'key')", new generator.Generator().generate(Build(E("JSONExtract", L("key")))));
    }

    private static string SourcePath([CallerFilePath] string path = "") => path;
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
