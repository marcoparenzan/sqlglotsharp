using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Pg = SqlGlotSharp.generators.postgres.PostgresGenerator;

namespace SqlGlotSharp.Tests;

public class GeneratorOperatorTests
{
    private sealed record Node(string type, Dictionary<string, object?> args, List<string>? comments = null);
    private sealed record Case(Node node, bool postgres, Dictionary<string, object?> options, bool comment = true);
    private static Node N(string type, object? value = null, object? rhs = null,
        List<string>? comments = null, params (string key, object? value)[] extras)
    {
        var args = new Dictionary<string, object?> { ["this"] = value };
        if (rhs is not null) args["expression"] = rhs;
        foreach (var (key, item) in extras) args[key] = item;
        return new(type, args, comments);
    }
    private static Node L(string value = "5", List<string>? comments = null) =>
        N("Literal", value, comments: comments, extras: [("is_string", false)]);
    private static Node C(string value = "a", List<string>? comments = null) =>
        N("Column", N("Identifier", value), comments: comments);
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
        from sqlglot.generators.postgres import PostgresGenerator
        def build(n):
            args = {k: build(v) if isinstance(v, dict) and 'type' in v else v for k,v in n['args'].items()}
            node = getattr(exp, n['type'])(**args)
            node.comments = n['comments']
            return node
        print(json.dumps([(PostgresGenerator if c['postgres'] else Generator)(**c['options']).sql(build(c['node']), comment=c['comment']) for c in json.load(sys.stdin)]))
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Operators_use_production_dispatch_and_match_Python(bool postgres)
    {
        var cases = new List<Case>();
        foreach (var pretty in new[] { false, true })
        foreach (var comments in new[] { false, true })
        foreach (var width in new long[] { -1, 0, 14, 80, 1000 })
        foreach (var pad in new long[] { 0, 2 })
        {
            var options = new Dictionary<string, object?> { ["dialect"] = postgres ? "postgres" : "",
                ["pretty"] = pretty, ["comments"] = comments, ["max_text_width"] = width, ["pad"] = pad };
            void Add(Node n) { cases.Add(new(n, postgres, options)); cases.Add(new(n, postgres, options, false)); }
            foreach (var type in new[] { "Add", "Sub", "Mul", "EQ", "NEQ", "GT", "GTE", "LT", "LTE",
                "NullSafeEQ", "NullSafeNEQ", "BitwiseAnd", "BitwiseOr", "BitwiseLeftShift", "BitwiseRightShift",
                "DPipe", "Overlaps", "Distance", "DistanceNd", "PropertyEQ", "Glob", "SimilarTo", "Dot", "Match" })
            {
                Add(N(type, C("A", ["lhs */ /*"]), N(type, L("2"), L("3", ["rhs"]), ["inner op"]), ["outer op"]));
                Add(N(type, N(type, L(), L("2"), ["left op"]), C("B"), ["top op"]));
                // Upstream binary's operator variable persists during flattening.
                Add(N(type, N(type, L(), L("2"), extras: [("operator", N("Var", "custom"))]),
                    N(type, C(), L("3"))));
                Add(N(type)); // Missing operands follow Python sql(None), not invented validation.
            }
            if (!postgres)
            {
                Add(N("BitwiseXor", L(), L("2"), ["xor"]));
                Add(N("Xor", N("Xor", C(), C("b")), N("Not", C("c"))));
            }
            Add(N("Div", L(), L("2"), ["divide"], ("typed", postgres)));
            Add(N("Neg", N("Neg", L())));
            Add(N("BitwiseNot", N("Paren", N("Add", C(), L()))));
            Add(N("Not", N("Paren", N("EQ", C(), N("Null")))));
            Add(N("Mod", N("Add", C(), L()), L("3"), ["mod"]));
            foreach (var parent in new[] { "Mul", "Div", "Mod" })
            {
                Add(N(parent, L(), N("Mod", C(), L("3"), ["right modulus"]), extras: [("typed", postgres)]));
                Add(N(parent, N("Mod", C(), L("3")), L(), extras: [("typed", postgres)]));
            }
            foreach (var negate in new object?[] { null, false, true, 0, 1 })
            foreach (var nestedNegate in new object?[] { null, false, true, 0, 1 })
            foreach (var rhs in new[] { N("Boolean", true), N("Null"), C("c") })
                Add(N("Is", N("Is", C(), rhs, ["nested is"], ("negate", nestedNegate)), rhs,
                    ["is"], ("negate", negate)));
            var comparisons = N("And", N("EQ", C("a", ["column"]), N("Add", L(), L("2")), ["eq"]),
                N("Or", N("GT", C("b"), L("3")), N("Not", N("Paren", N("LTE", C("c"), L("9")))), ["or\ncomment"]),
                ["and", "*/ /*", ""]);
            Add(comparisons);
            Add(N("Alias", N("Paren", comparisons, comments: ["paren"]), extras: [("alias", N("Identifier", "result"))]));
            // getattr inside connector_sql intentionally bypasses nested PostgreSQL Xor transforms.
            Add(N("And", N("Xor", C(), C("b"), ["nested xor"]), C("c")));
            Add(N("Or", N("And", C(), C("b")), N("And", C("c"), C("d"))));
            Add(N("And", null, extras: [("expression", null)]));
        }
        var expected = JsonSerializer.Deserialize<string[]>(await Python(Oracle, JsonSerializer.Serialize(cases)))!;
        Assert.Equal(cases.Count, expected.Length);
        for (var i = 0; i < cases.Count; i++)
        {
            var c = cases[i];
            generator.Generator g = postgres ? new Pg() : new generator.Generator();
            g.dialect = dialects.dialect.Dialect.get_or_raise(postgres ? "postgres" : "");
            g.pretty = c.options["pretty"]!; g.comments = (bool)c.options["comments"]!;
            g.max_text_width = (long)c.options["max_text_width"]!; g.pad = (long)c.options["pad"]!;
            var actual = g.sql(Build(c.node), comment: c.comment);
            Assert.True(expected[i] == actual, $"Case {i}: {JsonSerializer.Serialize(c)}\nPython: {expected[i]}\nC#: {actual}");
        }
    }

    [Fact]
    public void Missing_handlers_transforms_and_rewrites_stay_explicit()
    {
        var g = new generator.Generator();
        var pg = new Pg { dialect = dialects.dialect.Dialect.get_or_raise("postgres") };
        foreach (var node in new exp.Expr[] { new exp.Pivot() })
        {
            Assert.Contains("TRANSFORMS", Assert.Throws<NotPortedException>(() => pg.sql(node)).Message);
            Assert.Throws<NotPortedException>(() => pg.sql(new exp.Paren(@this: node)));
        }
        Assert.Equal("CAST(x / y AS BIGINT)", g.sql(new exp.IntDiv(@this: new exp.Var(@this: "x"), expression: new exp.Var(@this: "y"))));
        Assert.Equal("a || b", pg.sql(new exp.DPipe(@this: "a", expression: "b", safe: true)));
        g.dialect = new StrictConcatDialect();
        Assert.Throws<NotPortedException>(() => g.sql(new exp.DPipe(@this: "a", expression: "b", safe: true)));
        g.dialect = dialects.dialect.Dialect.get_or_raise("");
        Assert.Throws<KeyNotFoundException>(() => g.sql(new exp.And()));
        Assert.Throws<NotPortedException>(() => g.sql(new exp.Add(@this: new exp.Get(), expression: "x")));
        var addition = new exp.Add(@this: "x", expression: "y");
        Assert.Same(addition, g.preprocess(addition));
        Assert.Equal("x + y", g.generate(addition));
        static exp.Expr CNode() => new exp.Column(@this: new exp.Identifier(@this: "x"));
    }

    private sealed class StrictConcatDialect : dialects.dialect.Dialect
    {
        public override bool STRICT_STRING_CONCAT => true;
    }

    [Fact]
    public void Connector_stack_and_width_boundary_follow_upstream()
    {
        var g = new generator.Generator(pretty: true, max_text_width: 5);
        Assert.False(g.too_wide(new[] { "ab", "cde" }));
        Assert.True(g.too_wide(new[] { "ab", "cdef" }));
        var stack = new List<object?> { "existing" };
        var node = new exp.And(@this: "a", expression: "b") { comments = ["comment"] };
        Assert.Equal("AND /* comment */", g.and_sql(node, stack));
        Assert.Equal(new object?[] { "existing", "b", "AND /* comment */", "a" }, stack);
        Assert.Equal("a\nAND b", g.sql(new exp.And(@this: "a", expression: "b")));
    }

    [Fact]
    public async Task Operator_anchors_are_exact_and_incomplete_members_have_zero_hashes()
    {
        const string script = """
            import sys,json,re
            from pathlib import Path
            sys.path.insert(0,'src/tools')
            import port_status as p
            members=p.inventory(Path('python'),json.loads(Path('src/tools/scope.json').read_text()))
            anchors=p.scan_anchors(Path('src/SqlGlotSharp'))
            entries=[x for x in p.report(members,anchors)['members'] if any(a['file']=='generator.operators.cs' for a in x['csharp'])]
            partial={'div_sql','dpipe_sql','is_sql'}
            errors=[x for x in entries if x['status']!=('stub' if x['anchor'].split('.')[-1] in partial else 'ported')]
            assert len(entries)==38, len(entries)
            assert not errors, json.dumps([{ 'name':x['anchor'].split('.')[-1], 'hash':x['hash'], 'status':x['status']} for x in errors])
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
