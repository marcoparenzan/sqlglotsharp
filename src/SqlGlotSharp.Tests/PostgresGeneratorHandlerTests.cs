using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Pg = SqlGlotSharp.generators.postgres.PostgresGenerator;

namespace SqlGlotSharp.Tests;

public class PostgresGeneratorHandlerTests
{
    private sealed record Node(string type, Dictionary<string, object?> args, List<string>? comments = null);
    private sealed record Case(Node node, Dictionary<string, object?> options, bool comment);
    private sealed record Result(string? sql, string[] messages, string? error);
    private static Node N(string type, object? value = null, params (string key, object? value)[] extras)
    {
        var args = new Dictionary<string, object?> { ["this"] = value };
        foreach (var (key, item) in extras) args[key] = item;
        return new(type, args, ["handler */ /*", "line\ncomment"]);
    }
    private static Node L(string value, bool isString = true) => N("Literal", value, ("is_string", isString));
    private static Node C(string value = "Mixed\"Name") => N("Column", N("Identifier", value, ("quoted", true)));
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
        import sys,json
        sys.path.insert(0,'python')
        from sqlglot import exp
        from sqlglot.errors import ErrorLevel
        from sqlglot.generators.postgres import PostgresGenerator
        def build(n):
            node=getattr(exp,n['type'])(**{k:build(v) if isinstance(v,dict) and 'type' in v else v for k,v in n['args'].items()})
            node.comments=n['comments']
            return node
        results=[]
        for c in json.load(sys.stdin):
            options=dict(c['options'])
            options['unsupported_level']=ErrorLevel[options['unsupported_level']]
            g=PostgresGenerator(dialect='postgres',**options)
            try:
                sql=g.sql(build(c['node']),comment=c['comment'])
                results.append(dict(sql=sql,messages=g.unsupported_messages,error=None))
            except Exception as e:
                results.append(dict(sql=None,messages=g.unsupported_messages,error=str(e)))
        print(json.dumps(results))
        """;

    [Fact]
    public async Task Complete_handlers_and_inline_transforms_match_Python_through_sql()
    {
        var nodes = new List<Node>();
        foreach (var value in new object?[] { null, "", "name", "MiXed", "a'b", "a\"b", "line\nname", C(), 0, 1, false, true })
        foreach (var jdbc in new object?[] { null, false, true, 0, 1, "", "yes" })
            nodes.Add(N("Placeholder", value, ("jdbc", jdbc)));
        foreach (var type in new[] { "ComputedColumnConstraint", "IsAscii", "IgnoreNulls", "RespectNulls", "CurrentSchema" })
        foreach (var value in new object?[] { null, C(), L("a'\\b\nend"), N("Placeholder", "arg"),
            N("Paren", N("Add", L("1", false), ("expression", N("Placeholder", "rhs")))) })
            nodes.Add(N(type, value));
        foreach (var flag in new object?[] { null, L(""), L("i"), L("c"), L("ic"), L("ci"), L("ici"),
            L("cic"), L("g"), L("ig"), L("Gi"), L("ii\nc"), L("9", false), C("flag") })
            nodes.Add(N("RegexpLike", C(), ("expression", L("^a'\\b$")), ("flag", flag)));
        foreach (var type in new[] { "BitwiseXor", "RegexpILike" })
        {
            nodes.Add(N(type, C(), ("expression", N("Placeholder", "other"))));
            nodes.Add(N(type, N(type, L("1", false), ("expression", L("2", false))),
                ("expression", N(type, C(), ("expression", L("3", false))))));
            // A custom binary operator and missing operands follow the shared full binary algorithm.
            nodes.Add(N(type, C(), ("expression", C("b")), ("operator", N("Var", "custom"))));
            nodes.Add(N(type));
        }
        foreach (var type in new[] { "CurrentTimestamp", "CurrentUser", "JSONPathRoot" })
        {
            nodes.Add(N(type));
            // Inline constant transforms deliberately ignore arguments, even unported Select.
            nodes.Add(N(type, N("Select")));
        }
        nodes.Add(N("PartitionedByProperty", C()));
        nodes.Add(N("PartitionedByProperty", N("Placeholder", "partition")));
        nodes.Add(N("PartitionedByProperty"));
        nodes.Add(N("Alias", N("IsAscii", N("Placeholder", "string")), ("alias", N("Identifier", "Alias\"Name", ("quoted", true)))));
        nodes.Add(N("RespectNulls", N("IgnoreNulls", N("RegexpLike", N("Placeholder", "text"),
            ("expression", L("a'\\b")), ("flag", L("ci"))))));
        nodes.Add(N("And", N("RegexpLike", C(), ("expression", L("a")), ("flag", L("i"))),
            ("expression", N("IsAscii", N("Placeholder", "text")))));

        var cases = new List<Case>();
        foreach (var pretty in new[] { false, true })
        foreach (var identify in new object[] { false, true, "safe" })
        foreach (var level in new[] { "IGNORE", "WARN", "RAISE", "IMMEDIATE" })
        {
            var options = new Dictionary<string, object?> { ["pretty"] = pretty, ["identify"] = identify,
                ["normalize"] = true, ["normalize_functions"] = "lower", ["pad"] = 3,
                ["max_text_width"] = 14, ["comments"] = true, ["unsupported_level"] = level };
            foreach (var node in nodes)
            {
                cases.Add(new(node, options, true));
                cases.Add(new(node, options, false));
            }
        }
        var expected = JsonSerializer.Deserialize<Result[]>(await Python(Oracle, JsonSerializer.Serialize(cases)))!;
        Assert.Equal(cases.Count, expected.Length);
        for (var i = 0; i < cases.Count; i++)
        {
            var c = cases[i];
            var g = new Pg { dialect = dialects.dialect.Dialect.get_or_raise("postgres"),
                pretty = c.options["pretty"]!, identify = c.options["identify"]!, normalize = true,
                normalize_functions = "lower", pad = 3, max_text_width = 14, comments = true,
                unsupported_level = Enum.Parse<errors.ErrorLevel>((string)c.options["unsupported_level"]!) };
            string? sql = null, error = null;
            try { sql = g.sql(Build(c.node), comment: c.comment); }
            catch (errors.UnsupportedError e) { error = e.Message; }
            Assert.True(expected[i].sql == sql && expected[i].error == error && expected[i].messages.SequenceEqual(g.unsupported_messages),
                $"Case {i}: {JsonSerializer.Serialize(c)}\nPython: {JsonSerializer.Serialize(expected[i])}\nC#: {JsonSerializer.Serialize(new Result(sql, g.unsupported_messages.ToArray(), error))}");
        }
    }

    private sealed class OverridePg : Pg
    {
        public override string bitwisexor_sql(exp.BitwiseXor e) => "WRONG BASE HANDLER";
        public override string placeholder_sql(exp.Placeholder e) => "OVERRIDE";
    }

    [Fact]
    public void Precedence_and_unported_dependencies_remain_explicit()
    {
        var pg = new Pg();
        Assert.Equal("1 # 2", pg.sql(new exp.BitwiseXor(@this: exp.Literal.number(1), expression: exp.Literal.number(2))));
        Assert.Equal("1 # 2", new OverridePg().sql(new exp.BitwiseXor(@this: exp.Literal.number(1), expression: exp.Literal.number(2))));
        Assert.Equal("OVERRIDE", new OverridePg().sql(new exp.Placeholder(@this: "name")));
        Assert.Equal(":name", new generator.Generator().sql(new exp.Placeholder(@this: "name")));
        Assert.Equal("%(name)s", pg.sql(new exp.Placeholder(@this: "name")));
        foreach (var node in new exp.Expr[] { new exp.Pivot() })
            Assert.Contains("sqlglot/generators/postgres.py", Assert.Throws<NotPortedException>(() => pg.sql(node)).Message);
        foreach (var type in new[] { "IsAscii", "IgnoreNulls", "RespectNulls", "PartitionedByProperty" })
            Assert.Throws<NotPortedException>(() => pg.sql(Build(N(type, N("Get")))));
        Assert.Equal("%s", pg.generate(new exp.Placeholder()));
        var placeholder = new exp.Placeholder();
        Assert.Same(placeholder, pg.preprocess(placeholder));
    }

    [Fact]
    public async Task Exact_handler_hashes_and_partial_transform_hash_are_checked_by_port_status()
    {
        const string script = """
            import sys,json
            from pathlib import Path
            sys.path.insert(0,'src/tools')
            import port_status as p
            members=p.inventory(Path('python'),json.loads(Path('src/tools/scope.json').read_text()))
            anchors=p.scan_anchors(Path('src/SqlGlotSharp'))
            entries=[x for x in p.report(members,anchors)['members'] if any(a['file']=='generators/postgres.handlers.cs' for a in x['csharp'])]
            errors=[x for x in entries if x['status']!=('stub' if x['anchor'].endswith('.TRANSFORMS') else 'ported')]
            assert len(entries)==8, len(entries)
            assert not errors, json.dumps([{'anchor':x['anchor'],'hash':x['hash'],'status':x['status']} for x in errors])
            assert anchors['sqlglot/generator.py::_build_dispatch'][0]['hash']=='0000000000'
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
        Assert.True(process.ExitCode == 0, await stderr);
        return await stdout;
    }
}
