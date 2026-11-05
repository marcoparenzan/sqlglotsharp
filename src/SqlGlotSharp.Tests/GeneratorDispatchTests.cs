using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace SqlGlotSharp.Tests;

public class GeneratorDispatchTests
{
    private sealed record Node(string type, Dictionary<string, object?> args, List<string>? comments = null);
    private sealed record Case(Node node, Dictionary<string, object?> options, string? key = null, bool comment = true);
    private static Node N(string type, object? value = null, List<string>? comments = null,
        params (string key, object? value)[] extras)
    {
        var args = new Dictionary<string, object?> { ["this"] = value };
        foreach (var (key, item) in extras) args[key] = item;
        return new(type, args, comments);
    }
    private static exp.Expr Build(Node node)
    {
        var args = new PyDict<string, object?>();
        foreach (var (key, value) in node.args) args[key] = value is Node child ? Build(child) : value;
        var type = exp.EXPR_CLASSES.Values.Single(t => t.Name == node.type);
        var expression = (exp.Expr)Activator.CreateInstance(type, [args])!;
        expression.comments = node.comments?.ToList();
        return expression;
    }

    private const string Oracle = """
        import sys, json
        sys.path.insert(0, 'python')
        from sqlglot import exp
        from sqlglot.generator import Generator
        def build(n):
            args = {k: build(v) if isinstance(v, dict) and 'type' in v else v for k,v in n['args'].items()}
            node = getattr(exp, n['type'])(**args)
            node.comments = n['comments']
            return node
        print(json.dumps([Generator(**c['options']).sql(build(c['node']), c['key'], c['comment']) for c in json.load(sys.stdin)]))
        """;

    [Theory]
    [InlineData("")]
    [InlineData("postgres")]
    public async Task Nested_supported_ASTs_match_production_Python_sql(string dialect)
    {
        var cases = new List<Case>();
        foreach (var pretty in new[] { false, true })
        foreach (var comments in new[] { false, true })
        foreach (var identify in new[] { false, true })
        {
            var options = new Dictionary<string, object?> { ["dialect"] = dialect,
                ["pretty"] = pretty, ["comments"] = comments, ["identify"] = identify };
            var column = N("Column", N("Identifier", "Value", ["leaf"]), ["column"],
                ("catalog", N("Identifier", "cat")), ("db", N("Identifier", "db")),
                ("table", N("Identifier", "table")), ("join_mark", true));
            var nested = N("Alias", N("Paren", N("Neg", N("Neg", N("Literal", "5", ["number"],
                ("is_string", false)), ["inner negative"]), ["negative"]), ["paren"]), ["alias"],
                ("alias", N("Identifier", "result", ["alias identifier"])));
            var parameter = N("Alias", N("Parameter", N("Var", "p", ["variable"]), ["parameter"]), null,
                ("alias", N("Identifier", "p alias", null, ("quoted", true))));
            var text = N("Paren", N("Literal", "O'Reilly\n*/ /*", ["\n multiline\n", "", "*/ /*", "\u001c"], ("is_string", true)), ["outside"]);
            foreach (var node in new[] { column, nested, parameter, text, N("Null"), N("Boolean", false),
                N("Placeholder", "name"), N("Placeholder"), N("Alias", column) })
            {
                cases.Add(new(node, options));
                cases.Add(new(node, options, comment: false));
                cases.Add(new(node, options, key: "this", comment: false));
            }
        }
        var actual = cases.Select(c => new generator.Generator(dialect: dialect,
            pretty: c.options["pretty"], comments: (bool)c.options["comments"]!, identify: c.options["identify"])
            .sql(Build(c.node), c.key, c.comment)).ToArray();
        var expected = JsonSerializer.Deserialize<string[]>(await Python(Oracle, JsonSerializer.Serialize(cases)))!;
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Comments_match_exclusions_separation_and_whitespace()
    {
        const string script = """
            import sys,json
            sys.path.insert(0,'python')
            from sqlglot import exp
            from sqlglot.generator import Generator
            out=[]
            for pretty in [False,True]:
              for enabled in [False,True]:
                for typ in ['Identifier','Add','Union','Select','Command']:
                  for sql in ['', 'x', ' x', '\nX', '\u001cX']:
                    for separated in [False,True]:
                      n=getattr(exp,typ)()
                      n.comments=['attached']
                      out.append(Generator(pretty=pretty,comments=enabled).maybe_comment(sql,n,['','a',' */ /* ','a\nb'],separated))
            print(json.dumps(out))
            """;
        var actual = new List<string>();
        foreach (var pretty in new[] { false, true })
        foreach (var enabled in new[] { false, true })
        foreach (var type in new[] { "Identifier", "Add", "Union", "Select", "Command" })
        foreach (var sql in new[] { "", "x", " x", "\nX", "\u001cX" })
        foreach (var separated in new[] { false, true })
        {
            var node = Build(N(type, comments: ["attached"]));
            actual.Add(new generator.Generator(pretty: pretty, comments: enabled)
                .maybe_comment(sql, node, ["", "a", " */ /* ", "a\nb"], separated));
        }
        Assert.Equal(JsonSerializer.Deserialize<string[]>(await Python(script)), actual);
        var g = new generator.Generator();
        Assert.Equal("x", g.maybe_comment("x", comments: []));
        Assert.Throws<IndexOutOfRangeException>(() => g.sanitize_comment(""));
    }

    private sealed class Unknown : exp.Expression { }
    private sealed class SpecializedIdentifier : exp.Identifier { }
    private sealed class OverrideGenerator : generator.Generator
    {
        public override string identifier_sql(exp.Identifier expression) => "OVERRIDE";
    }

    [Fact]
    public void Dispatch_is_exact_cached_and_preserves_explicit_boundaries()
    {
        var g = new generator.Generator();
        Assert.Same(g._dispatch, new generator.Generator()._dispatch);
        Assert.Equal("x", g.sql(new exp.Identifier(@this: "x")));
        Assert.Equal("OVERRIDE", new OverrideGenerator().sql(new exp.Identifier(@this: "x")));
        Assert.Throws<ArgumentException>(() => g.sql(new Unknown()));
        Assert.Throws<ArgumentException>(() => g.sql(new SpecializedIdentifier()));
        foreach (var node in new exp.Expr[] { new exp.Get() })
            Assert.Throws<NotPortedException>(() => g.sql(node));
        Assert.Equal("x", g.generate(new exp.Identifier(@this: "x")));
        var identifier = new exp.Identifier(@this: "x");
        Assert.Same(identifier, g.preprocess(identifier));
        var pg = new generators.postgres.PostgresGenerator();
        Assert.Equal("$1", pg.sql(new exp.Parameter(@this: new exp.Literal(@this: "1", is_string: false))));
        Assert.Equal("%(name)s", pg.sql(new exp.Placeholder(@this: "name")));
        Assert.Equal("", g.sql(null));
        Assert.Equal("raw", g.sql("raw"));
        Assert.Equal("", g.sql(new exp.Identifier(@this: "x"), "missing"));
    }

    [Fact]
    public async Task Anchors_and_inventory()
    {
        const string script = """
            import sys,json,ast
            from pathlib import Path
            sys.path.insert(0,'src/tools')
            import port_status as p
            tree=ast.parse(Path('python/sqlglot/generator.py').read_text())
            cls=next(n for n in tree.body if isinstance(n,ast.ClassDef) and n.name=='Generator')
            names={'sql','sanitize_comment','maybe_comment'}
            hashes={n.name:p.normalized_hash([n]) for n in cls.body if isinstance(n,ast.FunctionDef) and n.name in names}
            anchors=p.scan_anchors(Path('src/SqlGlotSharp'))
            for name,h in hashes.items():
                assert anchors['sqlglot/generator.py::Generator.'+name][0]['hash']==h, (name,h)
            for name in ['_build_dispatch','_DISPATCH_CACHE','Generator.__init__','Generator.property_sql','Generator.preprocess']:
                assert anchors['sqlglot/generator.py::'+name][0]['hash']=='0000000000', name
            sys.path.insert(0,'python')
            from sqlglot.generator import Generator, _build_dispatch
            print(json.dumps(sorted(t.__name__ for t in _build_dispatch(Generator))))
            """;
        var expected = JsonSerializer.Deserialize<string[]>(await Python(script))!;
        var actual = new generator.Generator()._dispatch.Keys.Where(type => type != typeof(exp.TimeBucket)).Select(type => type.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, actual);
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
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            if (input is not null) await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, await stderr);
        return await stdout;
    }
}
