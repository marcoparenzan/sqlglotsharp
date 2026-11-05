using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Pg = SqlGlotSharp.generators.postgres.PostgresGenerator;

namespace SqlGlotSharp.Tests;

public class GeneratorClauseTests
{
    private sealed record Node(string type, Dictionary<string, object?> args, List<string>? comments = null);
    private sealed record Case(Node? node, bool postgres, Dictionary<string, object?> options,
        string method = "sql", Dictionary<string, object?>? kwargs = null);
    private static Node N(string type, object? value = null, List<string>? comments = null,
        params (string key, object? value)[] extras)
    {
        var args = new Dictionary<string, object?> { ["this"] = value };
        foreach (var (key, item) in extras) args[key] = item;
        return new(type, args, comments);
    }
    private static Node I(string name, bool quoted = false) => N("Identifier", name, extras: [("quoted", quoted)]);
    private static Node C(string name = "value") => N("Column", I(name), ["column */ /*"],
        ("table", I("alias")), ("db", I("Db", true)), ("catalog", I("Catalog")));
    private static Node L(string value = "2") => N("Literal", value, extras: [("is_string", false)]);
    private static object? BuildValue(object? value) => value switch
    {
        Node node => Build(node),
        object?[] list => list.Select(BuildValue).ToList(),
        _ => value
    };
    private static exp.Expr Build(Node node)
    {
        var args = new PyDict<string, object?>();
        foreach (var (key, value) in node.args) args[key] = BuildValue(value);
        var type = exp.EXPR_CLASSES.Values.Single(t => t.Name == node.type);
        var expression = (exp.Expr)Activator.CreateInstance(type, [args])!;
        expression.comments = node.comments?.ToList();
        return expression;
    }
    private const string Oracle = """
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
            g=(PostgresGenerator if c['postgres'] else Generator)(**c['options'])
            kw={k:build(v) for k,v in (c['kwargs'] or {}).items()}
            if c['method']=='op_expressions': out.append(g.op_expressions(expression=build(c['node']),**kw))
            else: out.append(getattr(g,c['method'])(build(c['node']),**kw))
        print(json.dumps(out))
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Clauses_match_Python_through_production_dispatch(bool postgres)
    {
        var cases = new List<Case>();
        foreach (var pretty in new[] { false, true })
        foreach (var comments in new[] { false, true })
        foreach (var leading in new[] { false, true })
        foreach (var identify in new object[] { false, true, "safe" })
        foreach (var pad in new long[] { 0, 2 })
        {
            var options = new Dictionary<string, object?> { ["dialect"] = postgres ? "postgres" : "",
                ["pretty"] = pretty, ["comments"] = comments, ["leading_comma"] = leading,
                ["identify"] = identify, ["pad"] = pad, ["max_text_width"] = 20L };
            void Add(Node n, string method = "sql", Dictionary<string, object?>? kwargs = null) => cases.Add(new(n, postgres, options, method, kwargs));
            var logical = N("And", N("EQ", C(), ["equal"], ("expression", L())), ["and"],
                ("expression", N("Or", N("GT", C("other"), extras: [("expression", L("7"))]), ["or\ncomment"],
                    ("expression", N("Not", N("Paren", N("Is", C("nullable"), extras: [("expression", N("Null"))])))))));
            foreach (var type in new[] { "Where", "Having", "Qualify" })
            {
                Add(N(type, logical, ["clause */ /*"]));
                Add(N(type));
                Add(N(type, logical, ["clause"]), kwargs: new() { ["comment"] = false });
            }
            var alias = N("TableAlias", I("alias", true), ["alias"],
                ("columns", new object?[] { I("first"), I("Second", true) }));
            var table = N("Table", I("Table", true), ["table\ncomment"],
                ("db", I("Db")), ("catalog", I("catalog", true)), ("alias", alias));
            Add(table); Add(N("From", table, ["from"]));
            Add(table, "table_sql", new() { ["sep"] = " " });
            Add(N("Table", I("table"), extras: [("db", ""), ("catalog", null)]));
            Add(N("Table"));
            Add(N("TableAlias", null, extras: [("columns", new object?[] { I("col") })]));
            Add(N("TableAlias", I("a")));
            foreach (var ordinality in new[] { false, true })
            foreach (var indexed in new object?[] { null, false, I("idx") })
                Add(N("Table", I("t"), extras: [("only", true), ("alias", alias), ("ordinality", ordinality),
                    ("indexed", indexed), ("format", N("Literal", "csv", extras: [("is_string", true)])),
                    ("pattern", N("Literal", ".*", extras: [("is_string", true)]))]));
            Add(N("Table", I("ignored"), extras: [("rows_from", new object?[] { C(), L() })]));
            var sort = new List<object?>();
            foreach (var desc in new object?[] { null, false, true })
            foreach (var first in new object?[] { null, false, true })
            {
                var ordered = N("Ordered", N("Add", C(), extras: [("expression", L())]), ["ordered"],
                    ("desc", desc), ("nulls_first", first));
                Add(ordered); sort.Add(ordered);
            }
            foreach (var siblings in new[] { false, true })
            {
                var order = N("Order", null, ["order"], ("expressions", sort.ToArray()), ("siblings", siblings));
                Add(order); Add(order, "order_sql", new() { ["flat"] = true });
                Add(N("Order", C(), extras: [("expressions", sort.ToArray()), ("siblings", siblings)]));
            }
            Add(N("Order"));
            var projection = N("Alias", N("Paren", logical, ["paren"]), ["projection */ /*"], ("alias", I("Result", true)));
            foreach (var flat in new[] { false, true })
            foreach (var dynamic in new[] { false, true })
            foreach (var newLine in new[] { false, true })
            foreach (var indent in new[] { false, true })
            {
                var kw = new Dictionary<string, object?> { ["flat"] = flat, ["dynamic"] = dynamic,
                    ["new_line"] = newLine, ["indent"] = indent, ["skip_first"] = true, ["skip_last"] = true,
                    ["prefix"] = ">", ["sep"] = ", \u001c" };
                Add(N("Select", extras: [("expressions", new object?[] { projection, "", C(), null, L() })]), "expressions", kw);
                Add(N("TableAlias", I("a"), extras: [("columns", new object?[] { I("one"), I("two", true) })]),
                    "expressions", new(kw) { ["key"] = "columns" });
            }
            cases.Add(new(null, postgres, options, "expressions", new() { ["sqls"] = new object?[] { projection, "", C(), L() } }));
            Add(N("Select"), "expressions");
            Add(N("Order", extras: [("expressions", new object?[] { projection, C() })]), "op_expressions", new() { ["op"] = "SORT" });
        }
        var expected = JsonSerializer.Deserialize<string[]>(await Python(Oracle, JsonSerializer.Serialize(cases)))!;
        Assert.Equal(cases.Count, expected.Length);
        for (var i = 0; i < cases.Count; i++)
        {
            var c = cases[i]; var o = c.options;
            generator.Generator g = postgres ? new Pg() : new generator.Generator(dialect: "");
            if (postgres) g.dialect = dialects.dialect.Dialect.get_or_raise("postgres");
            g.pretty = o["pretty"]!; g.comments = (bool)o["comments"]!; g.leading_comma = (bool)o["leading_comma"]!;
            g.identify = o["identify"]!; g.pad = (long)o["pad"]!; g.max_text_width = (long)o["max_text_width"]!;
            // Both oracle and C# call sql, not generate: preprocess intentionally remains unported.
            var node = c.node is null ? null : Build(c.node);
            object? K(string key, object? fallback = null) => c.kwargs?.GetValueOrDefault(key, fallback) ?? fallback;
            bool B(string key, bool fallback = false) => (bool)K(key, fallback)!;
            var actual = c.method switch
            {
                "sql" => g.sql(node, comment: B("comment", true)),
                "table_sql" => g.table_sql((exp.Table)node!, (string)K("sep")!),
                "order_sql" => g.order_sql((exp.Order)node!, B("flat")),
                "op_expressions" => g.op_expressions((string)K("op")!, node!),
                _ => g.expressions(node, key: (string?)K("key"), sqls: (System.Collections.ICollection?)BuildValue(K("sqls")),
                    flat: B("flat"), indent: B("indent", true), skip_first: B("skip_first"), skip_last: B("skip_last"),
                    sep: (string)K("sep", ", ")!, prefix: (string)K("prefix", "")!, dynamic: B("dynamic"), new_line: B("new_line"))
            };
            Assert.True(expected[i] == actual, $"Case {i}: {JsonSerializer.Serialize(c)}\nPython: {expected[i]}\nC#: {actual}");
        }
    }

    private sealed class NoNullOrdering : generator.Generator
    {
        public override bool? NULL_ORDERING_SUPPORTED => null;
    }
    private sealed class NoAliasColumns : generator.Generator
    {
        public override bool SUPPORTS_TABLE_ALIAS_COLUMNS => false;
        public override bool SUPPORTS_NAMED_CTE_COLUMNS => true;
    }
    [Fact]
    public void Absent_dependencies_and_rewrites_remain_explicit()
    {
        var g0 = new generator.Generator();
        foreach (var g in new generator.Generator[] { new(), new Pg() })
        {
            Assert.Contains("FROM t", g.generate(new exp.From(@this: new exp.Table(@this: new exp.Identifier(@this: "t")))));
            foreach (var arg in new[] { "sample", "changes" })
            {
                var table = new exp.Table(@this: new exp.Identifier(@this: "t"));
                var type = arg switch { "sample" => "Get", _ => "Changes" };
                table.set(arg, Build(N(type)));
                Assert.Throws<NotPortedException>(() => g.sql(new exp.From(@this: table)));
            }
            foreach (var (key, type) in new[] { ("pivots", "Get"), ("hints", "Get") })
            {
                var table = new exp.Table(@this: new exp.Identifier(@this: "t"));
                table.set(key, new List<object?> { Build(N(type)) });
                Assert.Throws<NotPortedException>(() => g.sql(table));
            }
            Assert.Contains(" IN ", g.sql(new exp.Where(@this: Build(N("In", C())))));
            Assert.Throws<NotPortedException>(() => g.sql(new exp.Ordered(@this: Build(C()), with_fill: Build(N("WithFill")))));
            var select = new exp.Select(expressions: new List<object?> { Build(C()) });
            Assert.Contains("SELECT", g.sql(select));
        }
        var simulated = new NoNullOrdering().sql(new exp.Ordered(@this: Build(C()), nulls_first: false));
        var target = g0.sql(Build(C())); // oracle: CASE WHEN <col> IS NULL THEN 1 ELSE 0 END, <col>
        Assert.Equal($"CASE WHEN {target} IS NULL THEN 1 ELSE 0 END, {target}", simulated);
        var noColumns = new NoAliasColumns();
        var alias = new exp.TableAlias(@this: new exp.Identifier(@this: "t"), columns: new List<object?> { new exp.Identifier(@this: "c") });
        Assert.Equal("t", noColumns.sql(alias));
        Assert.Equal(new[] { "Named columns are not supported in table alias." }, noColumns.unsupported_messages);
        _ = new exp.CTE(@this: new exp.Table(@this: new exp.Identifier(@this: "x")), alias: alias);
        Assert.Equal("t(c)", noColumns.sql(alias));
    }

    [Fact]
    public async Task Anchors_match_port_status_and_partial_members_stay_zero()
    {
        const string script = """
            import sys,json
            from pathlib import Path
            sys.path.insert(0,'src/tools')
            import port_status as p
            members=p.inventory(Path('python'),json.loads(Path('src/tools/scope.json').read_text()))
            anchors=p.scan_anchors(Path('src/SqlGlotSharp'))
            entries=[x for x in p.report(members,anchors)['members'] if any(a['file']=='generator.clauses.cs' for a in x['csharp'])]
            errors=[x for x in entries if x['status']!=('stub' if x['anchor'].endswith('.ordered_sql') else 'ported')]
            assert len(entries)==11,len(entries)
            assert not errors,json.dumps({x['anchor'].split('.')[-1]: x['hash'] for x in errors})
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
