using System.Diagnostics;
using System.Text.Json;

namespace SqlGlotSharp.Tests;

public class DateTruncNormalizationTests
{
    private static readonly string[] Kinds = ["literal", "number", "var", "derived", "column", "table", "db", "catalog", "empty-column", "week", "null", "missing", "raw"];
    private static readonly string[] Names = ["d", "q", "ms", "minute", "ß", "unknown", "", "7"];
    private static readonly object?[] Flags = [System.Reflection.Missing.Value, true, false, null, 0, "", 1];

    [Fact]
    public async Task Constructor_matches_live_python_AST_and_parent_metadata()
    {
        var output = await Python("""
            import sys, json
            sys.path.insert(0, sys.argv[1] + '/python')
            from sqlglot import exp
            class DerivedVar(exp.Var): pass
            results = []
            for kind in ['literal','number','var','derived','column','table','db','catalog','empty-column','week','null','missing','raw']:
                for name in ['d','q','ms','minute','ß','unknown','','7']:
                    for flag_index, flag in enumerate([None, True, False, None, 0, '', 1]):
                        if kind == 'literal': unit = exp.Literal.string(name)
                        elif kind == 'number': unit = exp.Literal.number(name)
                        elif kind == 'var': unit = exp.Var(this=name)
                        elif kind == 'derived': unit = DerivedVar(this=name)
                        elif kind in ['column','table','db','catalog']:
                            unit = exp.Column(this=exp.Identifier(this=name), **({kind: exp.Identifier(this='t')} if kind != 'column' else {}))
                        elif kind == 'empty-column': unit = exp.Column()
                        elif kind == 'week': unit = exp.Week(this=exp.Var(this=name))
                        elif kind == 'raw': unit = name
                        else: unit = None
                        owner = None
                        if isinstance(unit, exp.Expr):
                            unit.meta['source'] = 'original'
                            unit.comments = ['note']
                            owner = exp.Select(expressions=[unit])
                        args = {'this': exp.Var(this='date'), 'zone': 'UTC', 'input_type_preserved': True}
                        if kind != 'missing': args['unit'] = unit
                        if flag_index: args['unabbreviate'] = flag
                        node = exp.DateTrunc(**args)
                        results.append({'dump': node.dump(), 'original_parent_preserved': owner is None or unit.parent is owner,
                                        'unit_present': 'unit' in node.args})
            print(json.dumps(results))
            """);
        using var oracle = JsonDocument.Parse(output);
        var i = 0;
        foreach (var kind in Kinds)
        foreach (var name in Names)
        foreach (var flag in Flags)
        {
            object? unit = kind switch
            {
                "literal" => new exp.Literal(@this: name, is_string: true),
                "number" => new exp.Literal(@this: name, is_string: false),
                "var" => new exp.Var(@this: name),
                "derived" => new DerivedVar(name),
                "column" => new exp.Column(@this: new exp.Identifier(@this: name)),
                "table" => new exp.Column(@this: new exp.Identifier(@this: name), table: new exp.Identifier(@this: "t")),
                "db" => new exp.Column(@this: new exp.Identifier(@this: name), db: new exp.Identifier(@this: "t")),
                "catalog" => new exp.Column(@this: new exp.Identifier(@this: name), catalog: new exp.Identifier(@this: "t")),
                "empty-column" => new exp.Column(),
                "week" => new exp.Week(@this: new exp.Var(@this: name)),
                "raw" => name,
                _ => null
            };
            exp.Select? owner = null;
            if (unit is exp.Expr original)
            {
                original.meta["source"] = "original";
                original.comments = ["note"];
                owner = new exp.Select(expressions: new List<object?> { original });
            }
            var args = new PyDict<string, object?> { ["this"] = new exp.Var(@this: "date"), ["zone"] = "UTC", ["input_type_preserved"] = true };
            if (kind != "missing") args["unit"] = unit;
            if (flag is not System.Reflection.Missing) args["unabbreviate"] = flag;
            var node = new exp.DateTrunc(args);
            var expected = oracle.RootElement[i++];
            Assert.True(JsonElement.DeepEquals(expected.GetProperty("dump"), serde.dump_json(node)), $"{kind}/{name}/{flag}: AST differs");
            Assert.Equal(expected.GetProperty("unit_present").GetBoolean(), node.args.ContainsKey("unit"));
            Assert.False(node.args.ContainsKey("unabbreviate"));
            Assert.Equal(flag is not System.Reflection.Missing, args.ContainsKey("unabbreviate"));
            Assert.Equal(expected.GetProperty("original_parent_preserved").GetBoolean(), owner is null || ReferenceEquals(((exp.Expr)unit!).parent, owner));
            foreach (var key in new[] { "this", "unit" })
                if (node.args.get(key) is exp.Expr child)
                {
                    Assert.Same(node, child.parent);
                    Assert.Equal(key, child.arg_key);
                    Assert.Null(child.index);
                }
        }
        Assert.Equal(oracle.RootElement.GetArrayLength(), i);
    }

    [Fact]
    public void Named_constructor_uses_same_normalization_and_consumes_flag()
    {
        var node = new exp.DateTrunc(unit: new exp.Var(@this: "q"), unabbreviate: false);
        var literal = Assert.IsType<exp.Literal>(node.args["unit"]);
        Assert.Equal("Q", literal.name);
        Assert.True(literal.is_string);
        Assert.False(node.args.ContainsKey("unabbreviate"));
        Assert.Equal("QUARTER", ((exp.Expr)new exp.DateTrunc(unit: new exp.Var(@this: "q")).args["unit"]!).name);
        Assert.False(new exp.DateTrunc().args.ContainsKey("unit"));
        Assert.Null(new exp.DateTrunc(unit: null).args["unit"]);
    }

    [Fact]
    public async Task Regeneration_retains_special_constructor_and_single_anchor()
    {
        await Python("""
            import sys, json
            from pathlib import Path
            root = Path(sys.argv[1])
            sys.path[:0] = [str(root / 'src/tools'), str(root / 'python')]
            import gen_csharp as gen
            scope = json.loads((root / 'src/tools/scope.json').read_text(encoding='utf-8'))
            models = gen.expression_model(root / 'python', scope)
            text = gen.render_constructors(models)['sqlglot/expressions/temporal.py']
            assert text == (root / 'src/SqlGlotSharp/expressions/temporal.constructors.g.cs').read_text(encoding='utf-8-sig')
            assert ': base(DateTrunc__init__helper(_constructor_args))' in text
            anchors = sum(p.read_text(encoding='utf-8-sig').count('// py: sqlglot/expressions/temporal.py::DateTrunc.__init__ #03bf81f184') for p in (root / 'src/SqlGlotSharp').rglob('*.cs'))
            assert anchors == 1, anchors
            print('ok')
            """);
    }

    private sealed class DerivedVar(string name) : exp.Var(@this: name);

    private static async Task<string> Python(string script)
    {
        var root = Path.GetFullPath(Path.Combine(SourceDirectory(), "../.."));
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SQLGLOT_PYTHON") ?? "python")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-B");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(script);
        start.ArgumentList.Add(root);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, await error);
        return await output;
    }

    private static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
