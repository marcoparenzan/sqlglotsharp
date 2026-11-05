using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using static SqlGlotSharp.errors;
using Pg = SqlGlotSharp.generators.postgres.PostgresGenerator;

namespace SqlGlotSharp.Tests;

public class GeneratorSimpleTests
{
    // Test-only rendering of children: this is NOT the library's unported sql dispatch.
    // Python likewise calls Generator's base handlers directly, even on a PostgreSQL
    // token-configured instance. PostgreSQL's placeholder_sql override is out of scope.
    private sealed class ControlledGenerator(string dialect = "", object? identify = null,
        bool normalize = false, bool pretty = false, long pad = 2, long indent = 2,
        bool postgresTokens = false) : generator.Generator(dialect: dialect, identify: identify,
            normalize: normalize, pretty: pretty, pad: pad, indent: indent)
    {
        public readonly List<string> Calls = [];
        public override string PARAMETER_TOKEN => postgresTokens ? Pg.PARAMETER_TOKEN_ : base.PARAMETER_TOKEN;
        public override string NAMED_PLACEHOLDER_TOKEN => postgresTokens ? Pg.NAMED_PLACEHOLDER_TOKEN_ : base.NAMED_PLACEHOLDER_TOKEN;
        public override string sql(object? expression, string? key = null, bool comment = true)
        {
            Calls.Add(key ?? (expression is exp.Expr logged ? logged.GetType().Name : expression as string ?? "<null>"));
            if (!py.truthy(expression)) return "";
            if (expression is string text) return text;
            var node = (exp.Expr)expression!;
            if (!string.IsNullOrEmpty(key))
            {
                var value = node.args.get(key);
                return py.truthy(value) ? sql(value) : "";
            }
            return node switch
            {
                exp.Identifier e => identifier_sql(e),
                exp.Literal e => literal_sql(e),
                exp.Var e => var_sql(e),
                exp.Neg e => neg_sql(e),
                exp.Paren e => paren_sql(e),
                _ => throw new InvalidOperationException("Uncontrolled test child: " + node.GetType().Name)
            };
        }
    }

    private sealed record Node(string type, object? value = null, Dictionary<string, object?>? args = null);
    private sealed record Case(string handler, Node node, Dictionary<string, object?> options, bool postgres_tokens = false);
    private sealed record Outcome(string? sql, string? error, List<string> messages, List<string> calls);

    private static exp.Expr Build(Node node)
    {
        var args = new PyDict<string, object?> { ["this"] = node.value is Node child ? Build(child) : node.value };
        if (node.args is not null)
            foreach (var (key, value) in node.args) args[key] = value is Node n ? Build(n) : value;
        return node.type switch
        {
            "Identifier" => new exp.Identifier(args), "Literal" => new exp.Literal(args),
            "Var" => new exp.Var(args), "Placeholder" => new exp.Placeholder(args),
            "Parameter" => new exp.Parameter(args), "Neg" => new exp.Neg(args),
            "Paren" => new exp.Paren(args), "Alias" => new exp.Alias(args),
            "Column" => new exp.Column(args), _ => throw new InvalidOperationException(node.type)
        };
    }

    private static string Invoke(generator.Generator g, string handler, exp.Expr node) => handler switch
    {
        "var_sql" => g.var_sql((exp.Var)node),
        "placeholder_sql" => g.placeholder_sql((exp.Placeholder)node),
        "parameter_sql" => g.parameter_sql((exp.Parameter)node),
        "neg_sql" => g.neg_sql((exp.Neg)node),
        "paren_sql" => g.paren_sql((exp.Paren)node),
        "alias_sql" => g.alias_sql((exp.Alias)node),
        "column_sql" => g.column_sql((exp.Column)node),
        "column_parts" => g.column_parts((exp.Column)node),
        _ => throw new InvalidOperationException(handler)
    };

    private const string OracleScript = """
        import sys, json
        sys.path.insert(0, 'python')
        from sqlglot import exp
        from sqlglot.generator import Generator
        from sqlglot.generators.postgres import PostgresGenerator
        class Controlled(Generator):
            def sql(self, expression, key=None, comment=True):
                self.calls.append(key if key is not None else type(expression).__name__ if isinstance(expression, exp.Expr) else expression if isinstance(expression, str) else '<null>')
                if not expression: return ''
                if isinstance(expression, str): return expression
                if key:
                    value = expression.args.get(key)
                    return self.sql(value) if value else ''
                handlers = {'Identifier': 'identifier_sql', 'Literal': 'literal_sql', 'Var': 'var_sql', 'Neg': 'neg_sql', 'Paren': 'paren_sql'}
                return getattr(Generator, handlers[type(expression).__name__])(self, expression)
        class ControlledPostgres(Controlled):
            PARAMETER_TOKEN = PostgresGenerator.PARAMETER_TOKEN
            NAMED_PLACEHOLDER_TOKEN = PostgresGenerator.NAMED_PLACEHOLDER_TOKEN
        def build(spec):
            if not isinstance(spec, dict) or 'type' not in spec: return spec
            args = {k: build(v) for k, v in (spec.get('args') or {}).items()}
            args['this'] = build(spec.get('value'))
            return getattr(exp, spec['type'])(**args)
        results = []
        for case in json.load(sys.stdin):
            g = (ControlledPostgres if case['postgres_tokens'] else Controlled)(**case['options'])
            g.calls = []
            result, error = None, None
            try: result = getattr(Generator, case['handler'])(g, build(case['node']))
            except IndexError: error = 'index'
            results.append({'sql': result, 'error': error, 'messages': g.unsupported_messages, 'calls': g.calls})
        print(json.dumps(results))
        """;

    [Theory]
    [InlineData("")]
    [InlineData("postgres")]
    public async Task Base_handlers_match_python_with_controlled_children_and_dialect_options(string dialect)
    {
        var cases = new List<Case>();
        foreach (var identify in new object[] { false, true, "safe", "unsafe" })
        foreach (var normalize in new[] { false, true })
        foreach (var pretty in new[] { false, true })
        foreach (var (pad, indent) in new (long, long)[] { (2, 2), (0, 4), (-1, 0) })
        {
            var options = new Dictionary<string, object?> { ["dialect"] = dialect, ["identify"] = identify,
                ["normalize"] = normalize, ["pretty"] = pretty, ["pad"] = pad, ["indent"] = indent };
            void Add(string handler, Node node, bool tokens = false) => cases.Add(new(handler, node, options, tokens));
            foreach (var value in new object?[] { null, "", "raw\"Name", "a\nb", new Node("Identifier", "MiXeD"), new Node("Literal", "1e-09", new() { ["is_string"] = false }) })
            {
                Add("var_sql", new("Var", value));
                Add("parameter_sql", new("Parameter", value));
                Add("parameter_sql", new("Parameter", value), tokens: true);
                Add("paren_sql", new("Paren", value));
            }
            foreach (var value in new object?[] { null, false, 0, "", "Name", "a b", "a\"b", new Node("Identifier", "MiXeD") })
            {
                Add("placeholder_sql", new("Placeholder", value));
                Add("placeholder_sql", new("Placeholder", value), tokens: true);
            }
            foreach (var value in new object?[] { null, "", "5", "-5", " -5", "--5", "\n-5", new Node("Neg", "5"), new Node("Paren", "-5") })
                Add("neg_sql", new("Neg", value));
            foreach (var alias in new object?[] { null, "", new Node("Identifier", "MiXeD"), new Node("Identifier", "a\"b", new() { ["quoted"] = true }) })
                Add("alias_sql", new("Alias", new Node("Literal", "O'Reilly", new() { ["is_string"] = true }), new() { ["alias"] = alias }));
            for (var mask = 0; mask < 16; mask++)
            foreach (var joinMark in new[] { false, true })
            foreach (var shadow in new[] { false, true })
            {
                var args = new Dictionary<string, object?> { ["join_mark"] = joinMark, ["shadow"] = shadow };
                string[] keys = ["catalog", "db", "table"];
                string[] names = ["Cat", "a\"b", "a b"];
                for (var i = 0; i < keys.Length; i++)
                    args[keys[i]] = (mask & (1 << i)) != 0 ? new Node("Identifier", names[i], new() { ["quoted"] = i == 1 }) : null;
                var node = new Node("Column", (mask & 8) != 0 ? new Node("Identifier", "MiXeD") : null, args);
                Add("column_parts", node);
                Add("column_sql", node);
            }
            // Truthy parts whose controlled rendering is empty must not be filtered after rendering.
            Add("column_parts", new("Column", new Node("Identifier", "x"), new() { ["table"] = new Node("Var", "") }));
        }
        var expected = JsonSerializer.Deserialize<Outcome[]>(await Python(OracleScript, JsonSerializer.Serialize(cases)))!;
        Assert.Equal(cases.Count, expected.Length);
        for (var i = 0; i < cases.Count; i++)
        {
            var c = cases[i];
            var o = c.options;
            var g = new ControlledGenerator(dialect, o["identify"], (bool)o["normalize"]!, (bool)o["pretty"]!,
                (long)o["pad"]!, (long)o["indent"]!, c.postgres_tokens);
            string? result = null, error = null;
            try { result = Invoke(g, c.handler, Build(c.node)); }
            catch (IndexOutOfRangeException) { error = "index"; }
            var actual = new Outcome(result, error, g.unsupported_messages, g.Calls);
            Assert.True(JsonSerializer.Serialize(expected[i]) == JsonSerializer.Serialize(actual),
                $"Case {i}: {JsonSerializer.Serialize(c)}\nExpected: {JsonSerializer.Serialize(expected[i])}\nActual: {JsonSerializer.Serialize(actual)}");
        }
    }

    private sealed class SupportedDialect : dialects.dialect.Dialect
    {
        public override bool SUPPORTS_COLUMN_JOIN_MARKS => true;
        public override bool PROJECTION_ALIASES_SHADOW_SOURCE_NAMES => true;
    }

    [Fact]
    public void Column_shadow_and_supported_join_mark_follow_capabilities()
    {
        var g = new ControlledGenerator { dialect = new SupportedDialect() };
        var node = new exp.Column(@this: "x", table: "t", db: "d", catalog: "c", join_mark: true);
        Assert.Equal("c.d.t.x (+)", g.column_sql(node));
        node.set("shadow", true);
        g.Calls.Clear();
        Assert.Equal("x (+)", g.column_sql(node));
        Assert.Equal(new[] { "this", "x" }, g.Calls);
        Assert.Empty(g.unsupported_messages);
        Assert.Equal("t", node.args["table"]); // Qualifiers are not mutated.
    }

    [Fact]
    public void Unsupported_join_mark_reports_before_rendering_and_honors_immediate()
    {
        var node = new exp.Column(@this: "x", join_mark: true);
        var g = new ControlledGenerator { unsupported_level = ErrorLevel.IMMEDIATE };
        Assert.Throws<UnsupportedError>(() => g.column_sql(node));
        Assert.Empty(g.Calls);
        g.unsupported_level = ErrorLevel.RAISE;
        Assert.Equal("x", g.column_sql(node)); // RAISE is deferred to generate, not the handler.
        Assert.Equal("Outer join syntax using the (+) operator is not supported.", Assert.Single(g.unsupported_messages));
        Assert.True(py.truthy(node.args["join_mark"]));
    }

    [Fact]
    public void Simple_handlers_preserve_real_dispatch_boundary()
    {
        var g = new generator.Generator();
        Assert.Equal("raw", g.var_sql(new exp.Var(@this: "raw")));
        Assert.Equal(":Name", g.placeholder_sql(new exp.Placeholder(@this: "Name")));
        Assert.Equal("@1", g.parameter_sql(new exp.Parameter(@this: "1")));
        var pg = new Pg();
        Assert.Equal("$1", pg.parameter_sql(new exp.Parameter(@this: "1")));
        Assert.Equal("%(Name)s", pg.placeholder_sql(new exp.Placeholder(@this: "Name"))); // Inherited base handler, not PostgreSQL override.
        Assert.Throws<IndexOutOfRangeException>(() => g.neg_sql(new exp.Neg(@this: ""))); // Python indexes [0], no silent fallback.
        Assert.Equal("x", g.alias_sql(new exp.Alias(@this: new exp.Identifier(@this: "x"))));
        Assert.Equal("x", g.column_sql(new exp.Column(@this: new exp.Identifier(@this: "x"))));
        Assert.Equal("x", g.sql(new exp.Var(@this: "x")));
        var variable = new exp.Var(@this: "x");
        Assert.Same(variable, g.preprocess(variable));
        Assert.Equal("x", g.generate(variable));
    }

    [Fact]
    public async Task Anchors_are_current_according_to_port_status()
    {
        const string script = """
            import sys, json
            from pathlib import Path
            sys.path.insert(0, 'src/tools')
            import port_status as p
            members = p.inventory(Path('python'), json.loads(Path('src/tools/scope.json').read_text()))
            anchors = p.scan_anchors(Path('src/SqlGlotSharp'))
            names = {'var_sql','placeholder_sql','parameter_sql','neg_sql','paren_sql','alias_sql','column_sql','column_parts'}
            entries = [x for x in p.report(members, anchors)['members'] if x['anchor'].startswith('sqlglot/generator.py::Generator.') and x['anchor'].split('.')[-1] in names]
            assert len(entries) == 8, entries
            assert all(x['status'] == 'ported' for x in entries), json.dumps(entries)
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
        start.ArgumentList.Add("-B");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(script);
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
