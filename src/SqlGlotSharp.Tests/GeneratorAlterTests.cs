using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pg = SqlGlotSharp.generators.postgres.PostgresGenerator;

namespace SqlGlotSharp.Tests;

public class GeneratorAlterTests
{
    private sealed record Node(string type, Dictionary<string, object?> args, List<string>? comments = null);
    private sealed record Case(Node? node = null, string? input = null, bool postgres = true,
        bool pretty = false, bool comments = true, bool copy = true, bool identify = false,
        bool leading = false, string level = "WARN");
    private sealed record Result(Node initial, string? sql, string? error, List<string> messages, JsonElement state);
    private static Node N(string type, object? value = null, params (string key, object? value)[] extras)
    {
        var args = new Dictionary<string, object?>();
        if (value is not null) args["this"] = value;
        foreach (var (key, item) in extras) args[key] = item;
        return new(type, args);
    }
    private static Node I(string name, bool quoted = false) => N("Identifier", name, ("quoted", quoted));
    private static Node L(string value, bool text = false) => N("Literal", value, ("is_string", text));
    private static Node T(string name = "Target") => N("Table", I(name), ("db", I("My Schema", true)), ("catalog", I("Catalog"))) with { comments = ["table comment"] };
    private static Node D(string name) => N("DataType", name);
    private static Node Column(string name = "new column") => N("ColumnDef", I(name, true), ("kind", D("INT")),
        ("constraints", new object?[] {
            N("ColumnConstraint", extras: [("kind", N("DefaultColumnConstraint", L("a 'quote'\ntext", true)))]),
            N("ColumnConstraint", extras: [("kind", N("NotNullColumnConstraint"))]) })) with { comments = ["column\ncomment */ /*"] };
    private static Node Alter(params Node[] actions) => N("Alter", T(), ("kind", "TABLE"),
        ("actions", actions.Cast<object?>().ToArray())) with { comments = ["alter\ncomment */ /*"] };
    private static object? BuildValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => Build(value.Deserialize<Node>()!),
        JsonValueKind.Array => value.EnumerateArray().Select(BuildValue).ToList(),
        JsonValueKind.String => value.GetString(), JsonValueKind.True => true, JsonValueKind.False => false,
        JsonValueKind.Number => value.GetInt64(), _ => null
    };
    private static exp.Expr Build(Node node)
    {
        var args = new PyDict<string, object?>();
        foreach (var (key, value) in node.args)
        {
            var json = JsonSerializer.SerializeToElement(value);
            args[key] = node.type == "DataType" && key == "this" ? Enum.Parse<exp.DType>(json.GetString()!) : BuildValue(json);
        }
        var expression = (exp.Expr)Activator.CreateInstance(exp.EXPR_CLASSES.Values.Single(t => t.Name == node.type), [args])!;
        expression.comments = node.comments?.ToList();
        return expression;
    }
    private static generator.Generator G(Case c)
    {
        generator.Generator g = c.postgres ? new Pg() : new generator.Generator();
        g.dialect = dialects.dialect.Dialect.get_or_raise(c.postgres ? "postgres" : "");
        g.pretty = c.pretty; g.comments = c.comments; g.leading_comma = c.leading;
        g.max_text_width = 18; g.identify = c.identify; g.normalize = true;
        g.unsupported_level = Enum.Parse<errors.ErrorLevel>(c.level);
        return g;
    }
    private static List<exp.Expr> Nodes(exp.Expr root)
    {
        var result = new List<exp.Expr>();
        void Visit(exp.Expr node)
        {
            result.Add(node);
            foreach (var value in node.args.Values)
                if (value is exp.Expr child) Visit(child);
                else if (value is List<object?> list) foreach (var item in list) if (item is exp.Expr entry) Visit(entry);
        }
        Visit(root); return result;
    }
    private static object? Dump(object? value) => value switch
    {
        exp.Expr node => new { type = node.GetType().Name, args = node.args.ToDictionary(p => p.Key, p => Dump(p.Value)), comments = node.comments },
        exp.DType type => type.ToString(), List<object?> list => list.Select(Dump).ToArray(), _ => value
    };
    private static object State(List<exp.Expr> nodes) => nodes.Select(n => new
    {
        node = Dump(n), parent = n.parent?.GetType().Name, key = n.arg_key, index = n.index
    }).ToArray();

    private const string Oracle = """
        import sys,json
        sys.path.insert(0,'python')
        import sqlglot
        from sqlglot import exp
        from sqlglot.generator import Generator
        from sqlglot.generators.postgres import PostgresGenerator
        from sqlglot.errors import ErrorLevel
        def build(v):
            if isinstance(v,list): return [build(x) for x in v]
            if not isinstance(v,dict) or 'type' not in v: return v
            args={k:build(x) for k,x in v['args'].items()}
            if v['type']=='DataType': args['this']=getattr(exp.DType,args['this'])
            n=getattr(exp,v['type'])(**args); n.comments=v['comments']; return n
        def nodes(n):
            out=[n]
            for v in n.args.values():
                if isinstance(v,exp.Expr): out.extend(nodes(v))
                elif isinstance(v,list):
                    for x in v:
                        if isinstance(x,exp.Expr): out.extend(nodes(x))
            return out
        def dump(n):
            if isinstance(n,list): return [dump(x) for x in n]
            if isinstance(n,exp.DType): return n.name
            if not isinstance(n,exp.Expr): return n
            return dict(type=type(n).__name__,args={k:dump(v) for k,v in n.args.items()},comments=n.comments)
        out=[]
        for c in json.load(sys.stdin):
            g=(PostgresGenerator if c['postgres'] else Generator)(dialect='postgres' if c['postgres'] else '',
                pretty=c['pretty'],comments=c['comments'],leading_comma=c['leading'],max_text_width=18,
                identify=c['identify'],normalize=True,unsupported_level=ErrorLevel[c['level']])
            n=sqlglot.parse_one(c['input'],read='postgres') if c['input'] else build(c['node'])
            initial=dump(n); original=nodes(n); sql=None; error=None
            try: sql=g.generate(n,copy=c['copy'])
            except Exception as e: error=type(e).__name__+': '+str(e)
            state=[dict(node=dump(x),parent=type(x.parent).__name__ if x.parent is not None else None,key=x.arg_key,index=x.index) for x in original]
            out.append(dict(initial=initial,sql=sql,error=error,messages=g.unsupported_messages,state=state))
        print(json.dumps(out))
        """;
    private static async Task Compare(List<Case> cases)
    {
        var expected = JsonSerializer.Deserialize<Result[]>(await Python(JsonSerializer.Serialize(cases)))!;
        for (var i = 0; i < cases.Count; i++)
        {
            var c = cases[i]; var g = G(c); var node = Build(expected[i].initial); var original = Nodes(node);
            string? sql = null, error = null;
            g.unsupported_messages.Add("stale");
            try { sql = g.generate(node, copy: c.copy); }
            catch (errors.UnsupportedError e) { error = "UnsupportedError: " + e.Message; }
            var state = JsonSerializer.SerializeToElement(State(original));
            var context = $"Case {i}: {JsonSerializer.Serialize(c)}\nPython: {JsonSerializer.Serialize(expected[i])}\nC#: {sql}\nError: {error}\nState: {state}";
            Assert.True(expected[i].sql == sql && expected[i].error == error, context);
            Assert.True(expected[i].messages.SequenceEqual(g.unsupported_messages), context);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected[i].state.GetRawText()), JsonNode.Parse(state.GetRawText())), context);
        }
    }

    [Fact]
    public async Task Direct_actions_match_live_Python_SQL_comments_pretty_copy_and_mutation()
    {
        var actions = new[] {
            Column(),
            N("Drop", extras: [("kind", "COLUMN"), ("tables", new object?[] { N("Column", I("Old")) }), ("exists", true), ("cascade", true)]),
            N("AlterColumn", I("A"), ("dtype", D("VARCHAR")), ("collate", I("Collation", true)), ("using", N("Cast", N("Column", I("A")), ("to", D("VARCHAR"))))),
            N("AlterColumn", I("A"), ("default", N("Null"))),
            N("AlterColumn", I("A"), ("drop", true)),
            N("AlterColumn", I("A"), ("allow_null", false)),
            N("AlterColumn", I("A"), ("drop", true), ("allow_null", true)),
            N("RenameColumn", N("Column", I("Old")), ("to", N("Column", I("New", true))), ("exists", true)),
            N("AlterRename", T("Renamed")),
            N("AlterSet", extras: [("expressions", new object?[] {
                N("EQ", N("Column", I("compress"), ("table", I("timescaledb"))), ("expression", N("Boolean", true))),
                N("EQ", N("Column", I("fillfactor")), ("expression", L("70"))) })]),
            N("AlterSet", extras: [("expressions", new object?[] { N("Property", I("fillfactor"), ("value", L("70"))) })]),
            N("Schema", extras: [("expressions", new object?[] { Column(), Column("other") })])
        };
        var fixtures = actions.Select(a => Alter(a)).Concat(new[] { Alter(Column(), Column("other")), Alter(actions[2], actions[3], actions[7]),
            Alter(Column()) with { args = new(Alter(Column()).args) { ["exists"] = true, ["only"] = true, ["check"] = true, ["not_valid"] = true } } }).ToArray();
        var cases = new List<Case>();
        foreach (var postgres in new[] { false, true })
        foreach (var pretty in new[] { false, true })
        foreach (var comments in new[] { false, true })
        foreach (var copy in new[] { false, true })
        foreach (var identify in new[] { false, true })
            cases.AddRange(fixtures.Select(n => new Case(n, postgres: postgres, pretty: pretty, comments: comments, copy: copy, identify: identify, leading: pretty)));
        await Compare(cases);
    }

    [Fact]
    public async Task Python_parsed_ALTER_fixtures_use_production_generation()
    {
        // C# ALTER parsing is out of scope; consume the live Python parser's AST, not expected SQL strings.
        var inputs = new[] {
            "ALTER TABLE t ADD COLUMN a INT DEFAULT 1 NOT NULL",
            "ALTER TABLE IF EXISTS ONLY public.t ADD COLUMN IF NOT EXISTS a VARCHAR(20), ADD COLUMN b NUMERIC(10, 2)",
            "ALTER TABLE t DROP COLUMN IF EXISTS a CASCADE",
            "ALTER TABLE t DROP COLUMN a, DROP COLUMN b RESTRICT",
            "ALTER TABLE t ALTER COLUMN a TYPE VARCHAR(20) USING a::VARCHAR(20)",
            "ALTER TABLE t ALTER COLUMN a TYPE TEXT COLLATE \"C\"",
            "ALTER TABLE t ALTER COLUMN a SET DEFAULT 'a ''quote'''",
            "ALTER TABLE t ALTER COLUMN a DROP DEFAULT",
            "ALTER TABLE t ALTER COLUMN a SET NOT NULL",
            "ALTER TABLE t ALTER COLUMN a DROP NOT NULL",
            "ALTER TABLE t RENAME COLUMN a TO \"New Name\"",
            "ALTER TABLE public.t RENAME TO public.new_t",
            "ALTER TABLE t SET (timescaledb.compress = true, timescaledb.compress_segmentby = 'device_id')",
            "ALTER TABLE t RESET (timescaledb.compress, timescaledb.compress_segmentby)",
            "/* before */ ALTER TABLE \"My Table\" ADD COLUMN \"X\" INT /* column */",
            "ALTER TABLE t ALTER COLUMN IF EXISTS a TYPE INT"
        };
        var cases = new List<Case>();
        foreach (var input in inputs)
        foreach (var postgres in new[] { false, true })
        foreach (var pretty in new[] { false, true })
        foreach (var comments in new[] { false, true })
        foreach (var copy in new[] { false, true })
            cases.Add(new(input: input, postgres: postgres, pretty: pretty, comments: comments, copy: copy, identify: true));
        await Compare(cases);
    }

    [Fact]
    public async Task Upstream_unsupported_errors_and_messages_match_all_levels()
    {
        var cases = new List<Case>();
        foreach (var level in Enum.GetNames<errors.ErrorLevel>())
        foreach (var postgres in new[] { false, true })
        foreach (var copy in new[] { false, true })
        {
            cases.Add(new(Alter(N("AlterColumn", I("A"), ("exists", true), ("dtype", D("INT")), ("allow_null", false))), postgres: postgres, copy: copy, level: level));
            cases.Add(new(Alter(N("AlterColumn", I("A"), ("allow_null", false))), postgres: postgres, copy: copy, level: level));
        }
        await Compare(cases);
    }

    [Fact]
    public void Every_unported_or_malformed_action_is_explicit()
    {
        // AlterColumn, AlterSet and Drop are now generated exactly like upstream (see altercolumn_sql/alterset_sql);
        // the remaining bounded handlers keep their explicit boundaries.
        var unsupported = new List<Node>
        {
            Alter(N("ModifyColumn", Column())),
            Alter(N("RenameColumn", I("A"))),
            Alter(N("AlterRename", I("new")))
        };
        foreach (var postgres in new[] { false, true })
        foreach (var level in Enum.GetNames<errors.ErrorLevel>())
        {
            var offenders = new List<string>();
            for (var i = 0; i < unsupported.Count; i++)
            {
                try { offenders.Add($"#{i}: " + G(new(unsupported[i], postgres: postgres, level: level)).generate(Build(unsupported[i]))); }
                catch (NotPortedException) { }
            }
            Assert.Empty(offenders);
        }
    }

    private static string SourcePath([CallerFilePath] string source = "") => source;
    private static async Task<string> Python(string input)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SQLGLOT_PYTHON") ?? "python")
        {
            WorkingDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath())!, "..", "..")),
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-B"); start.ArgumentList.Add("-c"); start.ArgumentList.Add(Oracle);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token); process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, await stderr); return await stdout;
    }
}
