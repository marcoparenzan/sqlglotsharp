using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pg = SqlGlotSharp.generators.postgres.PostgresGenerator;

namespace SqlGlotSharp.Tests;

public class GeneratorPatternDistinctTests
{
    private sealed record Node(string type, Dictionary<string, object?> args, List<string>? comments = null);
    private sealed record Case(Node node, bool postgres, bool pretty, bool comments, bool copy);
    private sealed record Result(string? sql, string? error, JsonElement before, JsonElement after, JsonElement parents);
    private static Node N(string type, params (string key, object? value)[] args) =>
        new(type, args.ToDictionary(x => x.key, x => x.value), ["node */ /* comment\nline"]);
    private static Node C(string name = "x") => N("Column", ("this", N("Identifier", ("this", name))));
    private static Node L(string value = "a_%") => N("Literal", ("this", value), ("is_string", true));
    private static Node D(params Node[] args) => N("Distinct", ("expressions", args.Cast<object?>().ToArray()));
    private static Node S(Node projection, Node? distinct = null) => N("Select",
        ("expressions", new object?[] { projection }), ("distinct", distinct));
    private static object? BuildValue(object? value) => value switch
    {
        Node n => Build(n), object?[] a => a.Select(BuildValue).ToList(), _ => value
    };
    private static exp.Expr Build(Node n)
    {
        var args = new PyDict<string, object?>();
        foreach (var (k, v) in n.args) args[k] = BuildValue(v);
        var type = exp.EXPR_CLASSES.Values.Single(t => t.Name == n.type);
        var node = (exp.Expr)Activator.CreateInstance(type, [args])!;
        node.comments = n.comments?.ToList();
        return node;
    }
    private static object[] Parents(exp.Expr root) => root.walk().Select(n =>
        (object)new object?[] { n.GetType().Name, n.parent?.GetType().Name, n.arg_key, n.index }).ToArray();

    [Fact]
    public async Task Production_generate_matches_live_Python_SQL_errors_and_mutation()
    {
        var nodes = new List<Node>
        {
            D(), D(C()), D(C(), C("y")),
            S(C(), D()),
            S(C(), N("Distinct", ("on", N("Tuple", ("expressions", new object?[] { C(), C("y") }))))),
            S(N("Count", ("this", D(C())))),
            S(N("Count", ("this", D(C(), C("y"))))),
            S(N("Count", ("this", D(C(), L(), N("Null")))))
        };
        foreach (var kind in new[] { "Like", "ILike", "SimilarTo" })
        foreach (var negate in new[] { false, true })
        {
            var pattern = N(kind, ("this", C()), ("expression", L()), ("negate", negate));
            nodes.Add(pattern);
            nodes.Add(S(pattern, D()));
            nodes.Add(N("Escape", ("this", pattern), ("expression", L("!"))));
            nodes.Add(N("Not", ("this", N("Paren", ("this", pattern)))));
        }
        foreach (var kind in new[] { "Like", "ILike" })
        foreach (var quantifier in new[] { "Any", "All" })
        {
            var pattern = N(kind, ("this", C()), ("expression", N(quantifier,
                ("this", N("Tuple", ("expressions", new object?[] { L(), L("b%") }))))), ("negate", true));
            nodes.Add(S(pattern));
            nodes.Add(N("Escape", ("this", pattern), ("expression", L("!"))));
        }
        foreach (var rhs in new[]
        {
            N("Any", ("this", S(C()))),
            N("Any", ("this", N("Paren", ("this", L())))),
            N("All", ("this", S(C())))
        })
            nodes.Add(N("Like", ("this", C()), ("expression", rhs)));
        // Missing binary arguments must raise, never silently drop the pattern.
        nodes.Add(N("ILike", ("this", C())));
        nodes.Add(N("SimilarTo", ("this", C())));
        nodes.Add(N("Like", ("this", C())));
        nodes.Add(N("Escape", ("expression", L())));
        var cases = nodes.SelectMany(node => new[] { false, true }.SelectMany(postgres =>
            new[] { false, true }.SelectMany(pretty => new[] { false, true }.SelectMany(comments =>
                new[] { false, true }.Select(copy => new Case(node, postgres, pretty, comments, copy)))))).ToArray();
        var expected = JsonSerializer.Deserialize<Result[]>(await Python("""
            import sys,json
            sys.path.insert(0,'python')
            from sqlglot import exp
            from sqlglot.generator import Generator
            from sqlglot.generators.postgres import PostgresGenerator
            def build(v):
                if isinstance(v,list): return [build(x) for x in v]
                if not isinstance(v,dict) or 'type' not in v: return v
                n=getattr(exp,v['type'])(**{k:build(x) for k,x in v['args'].items()})
                n.comments=v['comments']
                return n
            out=[]
            for c in json.load(sys.stdin):
                n=build(c['node']); before=n.dump()
                g=(PostgresGenerator if c['postgres'] else Generator)(dialect='postgres' if c['postgres'] else '',pretty=c['pretty'],comments=c['comments'],max_text_width=24)
                sql=error=None
                try: sql=g.generate(n,copy=c['copy'])
                except Exception as e: error=type(e).__name__
                out.append(dict(sql=sql,error=error,before=before,after=n.dump(),parents=[[type(x).__name__,type(x.parent).__name__ if x.parent else None,x.arg_key,x.index] for x in n.walk()]))
            print(json.dumps(out))
            """, JsonSerializer.Serialize(cases)))!;
        for (var i = 0; i < cases.Length; i++)
        {
            var c = cases[i]; var n = Build(c.node); var e = expected[i];
            EqualJson(e.before, serde.dump(n), $"before {i}");
            generator.Generator g = c.postgres ? new Pg() : new generator.Generator();
            g.dialect = dialects.dialect.Dialect.get_or_raise(c.postgres ? "postgres" : "");
            g.pretty = c.pretty; g.comments = c.comments; g.max_text_width = 24;
            string? actual = null;
            var failure = Record.Exception(() => actual = g.generate(n, copy: c.copy));
            var error = failure is KeyNotFoundException ? "KeyError" : failure?.GetType().Name;
            Assert.True(e.error == error, $"case {i} {c}: expected {e.error}, got {failure}");
            Assert.True(e.sql == actual, $"SQL case {i} {c}\nExpected: {e.sql}\nActual: {actual}");
            EqualJson(e.after, serde.dump(n), $"after {i}");
            EqualJson(e.parents, Parents(n), $"parents {i}");
        }
    }
    private static void EqualJson(JsonElement expected, object actual, string context) =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected.GetRawText()), JsonSerializer.SerializeToNode(actual)), context);

    [Fact]
    public async Task Parsed_DISTINCT_and_patterns_use_production_generation()
    {
        string[] samples = [
            "SELECT DISTINCT x FROM t", "SELECT DISTINCT ON (x, y) x FROM t ORDER BY x",
            "SELECT COUNT(DISTINCT x), COUNT(DISTINCT x, y) FROM t",
            "SELECT x LIKE 'a%', x NOT LIKE 'b%', x ILIKE 'C%' FROM t",
            "SELECT x NOT ILIKE 'a%' ESCAPE '!' FROM t",
            "SELECT x SIMILAR TO '(a|b)%' ESCAPE '!' FROM t",
            "SELECT /* select */ DISTINCT /* distinct */ x FROM t WHERE x /* lhs */ LIKE /* rhs */ 'a%'"
        ];
        var cases = samples.SelectMany(sql => new[] { false, true }.SelectMany(postgres =>
            new[] { false, true }.SelectMany(pretty => new[] { false, true }.Select(copy =>
                new { sql, postgres, pretty, copy })))).ToArray();
        var expected = JsonSerializer.Deserialize<Result[]>(await Python("""
            import sys,json
            sys.path.insert(0,'python')
            import sqlglot
            from sqlglot.generator import Generator
            from sqlglot.generators.postgres import PostgresGenerator
            out=[]
            for c in json.load(sys.stdin):
                dialect='postgres' if c['postgres'] else ''
                n=sqlglot.parse_one(c['sql'],read=dialect); before=n.dump()
                g=(PostgresGenerator if c['postgres'] else Generator)(dialect=dialect,pretty=c['pretty'])
                sql=error=None
                try: sql=g.generate(n,copy=c['copy'])
                except Exception as e: error=type(e).__name__
                out.append(dict(sql=sql,error=error,before=before,after=n.dump(),parents=[[type(x).__name__,type(x.parent).__name__ if x.parent else None,x.arg_key,x.index] for x in n.walk()]))
            print(json.dumps(out))
            """, JsonSerializer.Serialize(cases)))!;
        for (var i = 0; i < cases.Length; i++)
        {
            var c = cases[i]; var e = expected[i];
            var n = (exp.Expr)serde.load(e.before)!;
            generator.Generator g = c.postgres ? new Pg() : new generator.Generator();
            g.dialect = dialects.dialect.Dialect.get_or_raise(c.postgres ? "postgres" : "");
            g.pretty = c.pretty;
            Assert.Null(e.error);
            Assert.Equal(e.sql, g.generate(n, copy: c.copy));
            EqualJson(e.after, serde.dump(n), $"parsed after {i}");
            EqualJson(e.parents, Parents(n), $"parsed parents {i}");
        }
    }

    private sealed class NoQuantifiers : generator.Generator
    {
        public override bool SUPPORTS_LIKE_QUANTIFIERS => false;
    }
    [Fact]
    public void Unsupported_quantifier_rewrites_are_explicit_without_mutation()
    {
        foreach (var kind in new[] { "Like", "ILike" })
        foreach (var quantifier in new[] { "Any", "All" })
        foreach (var escape in new[] { false, true })
        {
            var pattern = N(kind, ("this", C()), ("expression", N(quantifier,
                ("this", N("Tuple", ("expressions", new object?[] { L(), L("b%") }))))));
            var n = Build(escape ? N("Escape", ("this", pattern), ("expression", L("!"))) : pattern);
            var before = JsonSerializer.Serialize(serde.dump(n));
            var g = new NoQuantifiers();
            Assert.Contains("quantifier expansion", Assert.Throws<NotPortedException>(() => g.generate(n, copy: false)).Message);
            Assert.Equal(before, JsonSerializer.Serialize(serde.dump(n)));
        }
    }

    private static async Task<string> Python(string script, string input, [CallerFilePath] string source = "")
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SQLGLOT_PYTHON") ?? "python")
        {
            WorkingDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..")),
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-B"); start.ArgumentList.Add("-c"); start.ArgumentList.Add(script);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token); process.StandardInput.Close();
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, await stderr); return await stdout;
    }
}
