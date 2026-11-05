using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Pg = SqlGlotSharp.generators.postgres.PostgresGenerator;
using Dialect = SqlGlotSharp.dialects.dialect.Dialect;

namespace SqlGlotSharp.Tests;

public class GeneratorFunctionTests
{
    private sealed record Node(string type, Dictionary<string, object?> args, List<string>? comments = null);
    private sealed record Case(Node node, bool postgres, Dictionary<string, object?> options, bool comment = true);
    private static Node N(string type, object? value = null, List<string>? comments = null,
        params (string key, object? value)[] extras)
    {
        var args = new Dictionary<string, object?> { ["this"] = value };
        foreach (var (key, item) in extras) args[key] = item;
        return new(type, args, comments);
    }
    private static Node L(string value = "5", List<string>? comments = null) => N("Literal", value, comments, ("is_string", false));
    private static Node C(string value = "A") => N("Column", N("Identifier", value));
    private static Node F(string name, params object?[] args) => N("Anonymous", name, ["function */ /*"], ("expressions", args));
    private static exp.Expr Build(Node node)
    {
        object? Value(object? value) => value switch
        {
            Node child => Build(child),
            object?[] list => list.Select(Value).ToList(),
            _ => value
        };
        var args = new PyDict<string, object?>();
        foreach (var (key, value) in node.args) args[key] = Value(value);
        var type = exp.EXPR_CLASSES.Values.Single(t => t.Name == node.type);
        var expression = (exp.Expr)Activator.CreateInstance(type, [args])!;
        expression.comments = node.comments?.ToList();
        return expression;
    }
    private static generator.Generator Generator(bool postgres, Dictionary<string, object?> options)
    {
        generator.Generator g = postgres ? new Pg() : new generator.Generator();
        g.pretty = options.GetValueOrDefault("pretty") ?? false;
        g.comments = (bool)(options.GetValueOrDefault("comments") ?? true);
        g.normalize_functions = options.GetValueOrDefault("normalize_functions") ?? g.dialect.NORMALIZE_FUNCTIONS;
        g.max_text_width = (long)(options.GetValueOrDefault("max_text_width") ?? 80L);
        g.pad = (long)(options.GetValueOrDefault("pad") ?? 2L);
        g.identify = options.GetValueOrDefault("identify") ?? false;
        return g;
    }
    private const string Oracle = """
        import sys,json
        sys.path.insert(0,'python')
        from sqlglot import exp
        from sqlglot.generator import Generator
        from sqlglot.generators.postgres import PostgresGenerator
        def value(v):
            if isinstance(v,dict) and 'type' in v:
                n=getattr(exp,v['type'])(**{k:value(a) for k,a in v['args'].items()})
                n.comments=v['comments']
                return n
            if isinstance(v,list): return [value(a) for a in v]
            return v
        print(json.dumps([(PostgresGenerator if c['postgres'] else Generator)(**c['options']).sql(value(c['node']),comment=c['comment']) for c in json.load(sys.stdin)]))
        """;
    private static async Task Compare(List<Case> cases)
    {
        var actual = cases.Select(c => Generator(c.postgres, c.options).sql(Build(c.node), comment: c.comment)).ToArray();
        var expected = JsonSerializer.Deserialize<string[]>(await Python(Oracle, JsonSerializer.Serialize(cases)))!;
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
            Assert.True(expected[i] == actual[i], $"Case {i}: {JsonSerializer.Serialize(cases[i])}\nExpected: {expected[i]}\nActual: {actual[i]}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Anonymous_and_generic_functions_use_actual_sql_with_nested_comments_and_formatting(bool postgres)
    {
        var cases = new List<Case>();
        foreach (var pretty in new[] { false, true })
        foreach (var comments in new[] { false, true })
        foreach (var normalize in new object[] { false, true, "upper", "lower", "unchanged" })
        foreach (var width in new long[] { -1, 0, 12, 80 })
        foreach (var pad in new long[] { 0, 3 })
        {
            var options = new Dictionary<string, object?> { ["dialect"] = postgres ? "postgres" : "",
                ["pretty"] = pretty, ["comments"] = comments, ["normalize_functions"] = normalize,
                ["max_text_width"] = width, ["pad"] = pad, ["identify"] = true };
            var nested = F("MiXeD", N("Abs", N("Neg", L("2", ["number"]))),
                F("CuStOm", N("Sum", C())), N("Add", C("b"), ["operator"], ("expression", L("3"))),
                N("Literal", "O'Reilly\n🙂", ["literal\ncomment"], ("is_string", true)));
            var qualified = N("Dot", N("Identifier", "Schema"), ["qualified"], ("expression", nested));
            foreach (var node in new[] { F("empty"), F("one", L()), F("flags", null, false, true, N("Boolean", false), N("Null")),
                F("idname", N("Tuple", extras: [("expressions", new object?[] { L(), C() })])), nested, qualified,
                N("Anonymous", N("Identifier", "QuotedName", null, ("quoted", true)), null, ("expressions", new object?[] { C() })),
                N("Coalesce", C(), ["coalesce"], ("expressions", new object?[] { N("Abs", L()), F("f", L()) })),
                N("ApproximateSimilarity", C(), ["alias"], ("expression", C("B"))),
                N("RegexpExtract", C(), null, ("expression", N("Literal", "abc", null, ("is_string", true))), ("group", L("1"))) })
            {
                cases.Add(new(node, postgres, options));
                cases.Add(new(node, postgres, options, false));
            }
        }
        await Compare(cases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Every_non_dispatch_Func_uses_registry_order_names_and_arity(bool postgres)
    {
        var g = Generator(postgres, []);
        var cases = new List<Case>();
        foreach (var type in exp.EXPR_CLASSES.Values.Distinct().Where(t => !t.IsInterface && !t.IsAbstract && typeof(exp.Func).IsAssignableFrom(t) && !g._dispatch.ContainsKey(t)))
        {
            var empty = (exp.Expr)Activator.CreateInstance(type, [new PyDict<string, object?>()])!;
            var args = new Dictionary<string, object?>();
            // Insert kwargs in reverse order to ensure fallback respects arg_types, not args.
            foreach (var key in empty.arg_types.Keys.Reverse())
                // DateTrunc's AST constructor normalization is outside this task; supply
                // its canonical unit to compare identical trees rather than constructor ports.
                args[key] = type == typeof(exp.DateTrunc) && key == "unit"
                    ? N("Literal", "DAY", null, ("is_string", true))
                    : key == empty.var_len_arg_key && empty.is_var_len_args
                        ? new object?[] { C("x"), null, false, N("Abs", L("7")) } : C(key);
            foreach (var pretty in new[] { false, true })
            {
                var options = new Dictionary<string, object?> { ["dialect"] = postgres ? "postgres" : "", ["pretty"] = pretty, ["max_text_width"] = 10L };
                cases.Add(new(new(type.Name, args, ["generic"]), postgres, options));
                cases.Add(new(new(type.Name, [], ["zero arity"]), postgres, options));
            }
        }
        Assert.True(cases.Count > 500);
        await Compare(cases);
    }

    [Fact]
    public async Task Func_and_format_args_helpers_match_Python_keyword_options()
    {
        const string script = """
            import sys,json
            sys.path.insert(0,'python')
            from sqlglot import exp
            from sqlglot.generator import Generator
            out=[]
            for pretty in [False,True]:
              for width in [-1,0,3,4,80]:
                for sep in [', ', ' | ', '\u001c']:
                  g=Generator(pretty=pretty,max_text_width=width,normalize_functions='lower',pad=3)
                  args=[None,True,False,'ab','cd','',exp.Boolean(this=False),exp.Abs(this=exp.Literal.number(2))]
                  out.append(g.format_args(*args,sep=sep))
                  out.append(g.func('MiXeD',*args,prefix='[',suffix=']',normalize=False))
                  out.append(g.func('MiXeD',*args))
            print(json.dumps(out))
            """;
        var actual = new List<string>();
        foreach (var pretty in new[] { false, true })
        foreach (var width in new long[] { -1, 0, 3, 4, 80 })
        foreach (var sep in new[] { ", ", " | ", "\u001c" })
        {
            var g = new generator.Generator(pretty: pretty, max_text_width: width, normalize_functions: "lower", pad: 3);
            object?[] args = [null, true, false, "ab", "cd", "", new exp.Boolean(@this: false), new exp.Abs(@this: new exp.Literal(@this: "2", is_string: false))];
            actual.Add(g.format_args(args, sep));
            actual.Add(g.func("MiXeD", args, prefix: "[", suffix: "]", normalize: false));
            actual.Add(g.func("MiXeD", args));
        }
        Assert.Equal(JsonSerializer.Deserialize<string[]>(await Python(script)), actual);
    }

    private sealed class OriginalNameDialect : Dialect
    {
        public override object? ORIGINAL_NAME_META_KEY => "original";
    }
    [Fact]
    public void Original_name_metadata_and_alias_registry_are_preserved()
    {
        var g = new generator.Generator(dialect: new OriginalNameDialect(), normalize_functions: false);
        var node = new exp.Abs(@this: new exp.Var(@this: "x"));
        node.meta["original"] = "MyAbs";
        Assert.Equal("MyAbs(x)", g.sql(node));
        node.meta["original"] = "";
        Assert.Equal("ABS(x)", g.sql(node));
        Assert.Equal(typeof(exp.ApproximateSimilarity), exp.FUNCTION_BY_NAME["APPROXIMATE_JACCARD_INDEX"]);
    }

    [Fact]
    public async Task Dispatch_inventory_and_missing_dialect_overrides_remain_explicit()
    {
        const string script = """
            import sys,json
            sys.path.insert(0,'python')
            from sqlglot.generator import Generator,_build_dispatch
            from sqlglot.generators.postgres import PostgresGenerator
            # Creating the dialect class pops the transforms of unsupported JSONPath parts (dialect.py).
            import sqlglot.dialects.postgres
            print(json.dumps([sorted(t.__name__ for t in _build_dispatch(c)) for c in [Generator,PostgresGenerator]]))
            """;
        var expected = JsonSerializer.Deserialize<string[][]>(await Python(script))!;
        var generators = new generator.Generator[] { new(), new Pg() };
        for (var i = 0; i < generators.Length; i++)
            Assert.Equal(expected[i], generators[i]._dispatch.Keys.Where(t => t != typeof(exp.TimeBucket)).Select(t => t.Name).Order(StringComparer.Ordinal));
        var g = generators[0];
        var pg = generators[1];
        foreach (var type in new[] { typeof(exp.Pivot) })
        {
            var node = (exp.Expr)Activator.CreateInstance(type, [new PyDict<string, object?>()])!;
            Assert.NotEmpty(g.sql(node));
            Assert.Contains($"PostgresGenerator.TRANSFORMS[{type.Name}]", Assert.Throws<NotPortedException>(() => pg.sql(node)).Message);
        }
        foreach (var type in new[] { typeof(exp.CurrentSchema), typeof(exp.IsAscii), typeof(exp.RegexpLike) })
        {
            var node = (exp.Expr)Activator.CreateInstance(type, [new PyDict<string, object?>()])!;
            Assert.NotEmpty(g.sql(node));
            Assert.NotEmpty(pg.sql(node));
        }
        foreach (var generator in generators)
        {
            Assert.Equal("=", generator.sql(new exp.Property()));
            Assert.Throws<NotPortedException>(() => generator.sql(new exp.Get()));
            var cast = new exp.Cast(@this: new exp.Var(@this: "x"), to: new exp.DataType(@this: exp.DType.INT));
            Assert.Equal("ABS(CAST(x AS INT))", generator.sql(new exp.Abs(@this: cast)));
            var abs = new exp.Abs();
            Assert.Same(abs, generator.preprocess(abs));
            Assert.Equal(generator.sql(abs), generator.generate(abs));
        }
    }

    [Fact]
    public async Task Local_full_anchors_and_dependency_incomplete_dispatch_are_distinct()
    {
        const string script = """
            import sys,json,ast
            from pathlib import Path
            sys.path.insert(0,'src/tools')
            import port_status as p
            cls=next(n for n in ast.parse(Path('python/sqlglot/generator.py').read_text()).body if isinstance(n,ast.ClassDef) and n.name=='Generator')
            names={'anonymous_sql','function_fallback_sql','function_name','func','format_args'}
            anchors=p.scan_anchors(Path('src/SqlGlotSharp'))
            hashes={n.name:p.normalized_hash([n]) for n in cls.body if isinstance(n,ast.FunctionDef) and n.name in names}
            for name,h in hashes.items():
                actual=anchors['sqlglot/generator.py::Generator.'+name]
                assert len(actual)==1 and actual[0]['hash']==h, json.dumps(hashes)
            for name in ['_build_dispatch','_DISPATCH_CACHE','Generator.preprocess','Generator.property_sql']:
                assert anchors['sqlglot/generator.py::'+name][0]['hash']=='0000000000', name
            print('ok')
            """;
        Assert.Equal("ok", (await Python(script)).Trim());
    }

    private static string SourcePath([CallerFilePath] string path = "") => path;
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
