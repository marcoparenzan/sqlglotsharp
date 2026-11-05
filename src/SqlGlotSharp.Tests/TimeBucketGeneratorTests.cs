using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Pg = SqlGlotSharp.generators.postgres.PostgresGenerator;
using Ts = SqlGlotSharp.generators.timescale.TimescaleGenerator;

namespace SqlGlotSharp.Tests;

public class TimeBucketGeneratorTests
{
    private sealed record Node(string type, Dictionary<string, object?> args, List<string>? comments = null);
    private sealed record Case(Node node, bool timescale, bool pretty, object identify, object normalize_functions,
        string level, bool generate, bool copy, bool comment);
    private sealed record Result(string? sql, string? error, string[] messages, JsonElement ast = default);
    private static Node N(string type, params (string key, object? value)[] args) => new(type, args.ToDictionary(x => x.key, x => x.value));
    private static Node L(string value, bool str = true) => N("Literal", ("this", value), ("is_string", str));
    private static Node V(string value) => N("Var", ("this", value));
    private static Node Width(string value = "5", string unit = "MINUTES") => N("Interval", ("this", L(value)), ("unit", V(unit)));
    private static Node Column() => N("Column", ("this", N("Identifier", ("this", "EventTime"), ("quoted", false))));
    private static Node Cast(Node value, string type = "TIMESTAMP") => N("Cast", ("this", value), ("to", N("DataType", ("this", type))));
    private static Node Bucket(Node? width = null, Node? source = null, params Node[] extras) =>
        N("TimeBucket", ("width", width ?? Width()), ("this", source ?? Cast(Column())), ("expressions", extras))
            with { comments = ["bucket comment", "embedded */ marker"] };

    private static object? BuildValue(object? value) => value switch
    {
        Node node => Build(node), Node[] nodes => nodes.Select(n => (object?)Build(n)).ToList(), _ => value
    };
    private static exp.Expr Build(Node node)
    {
        var args = new PyDict<string, object?>();
        foreach (var (key, value) in node.args) args[key] = BuildValue(value);
        if (node.type == "DataType") args["this"] = Enum.Parse<exp.DType>((string)node.args["this"]!);
        var type = node.type == "TimeBucket" ? typeof(exp.TimeBucket) : exp.EXPR_CLASSES.Values.Single(t => t.Name == node.type);
        var result = (exp.Expr)Activator.CreateInstance(type, [args])!;
        result.comments = node.comments?.ToList();
        return result;
    }

    private const string Oracle = """
        import sys,json
        sys.path[:0]=['python','src/tools']
        from sqlglot import exp,serde
        from sqlglot.errors import ErrorLevel
        from oracle_plugins.timebucket_rendering import TimeBucket,PostgresTimeBucketGenerator,TimescaleTimeBucketGenerator
        def value(v):
            if isinstance(v,dict) and 'type' in v: return build(v)
            if isinstance(v,list): return [value(x) for x in v]
            return v
        def build(n):
            args={k:value(v) for k,v in n['args'].items()}
            if n['type']=='DataType': args['this']=exp.DataType.Type[args['this']]
            cls=TimeBucket if n['type']=='TimeBucket' else getattr(exp,n['type'])
            e=cls(**args); e.comments=n['comments']; return e
        results=[]
        for c in json.load(sys.stdin):
            cls=TimescaleTimeBucketGenerator if c['timescale'] else PostgresTimeBucketGenerator
            g=cls(dialect='timescale' if c['timescale'] else 'postgres', pretty=c['pretty'],identify=c['identify'],
                normalize=True,normalize_functions=c['normalize_functions'],max_text_width=32,pad=3,
                unsupported_level=ErrorLevel[c['level']])
            try:
                e=build(c['node'])
                sql=g.generate(e,copy=c['copy']) if c['generate'] else g.sql(e,comment=c['comment'])
                results.append(dict(sql=sql,error=None,messages=g.unsupported_messages,ast=serde.dump(e)))
            except Exception as error:
                if str(error)=='':
                    import traceback
                    raise RuntimeError(traceback.format_exc())
                results.append(dict(sql=None,error=str(error),messages=g.unsupported_messages,ast=serde.dump(e)))
        print(json.dumps(results))
        """;

    [Fact]
    public async Task Live_adapters_match_identity_safe_subset_options_copy_and_hard_errors()
    {
        var nodes = new List<Node>();
        foreach (var type in new[] { "TIMESTAMP", "TIMESTAMPTZ" })
        {
            foreach (var unit in new[] { "microsecond", "MILLISECONDS", "seconds", "minute", "HOURS", "day", "weeks" })
                nodes.Add(Bucket(Width("7", unit), Cast(Column(), type)));
            nodes.Add(Bucket(source: Cast(Column(), type), extras: [Cast(L(type == "TIMESTAMP"
                ? "1999-12-31T23:59:59.123456" : "1999-12-31T23:59:59.123456Z"), type)]));
            nodes.Add(Bucket(Width("9223372036854775807", "microsecond"), Cast(Column(), type)));
        }
        foreach (var unit in new[] { "MONTH", "quarter", "YEAR", "fortnight", "", "DAY TO SECOND" }) nodes.Add(Bucket(Width("1", unit)));
        foreach (var value in new[] { "0", "-1", "1.5", "1e2", "1 day", "NaN", "9223372036854775808", "9999999999999999999" }) nodes.Add(Bucket(Width(value)));
        nodes.Add(Bucket(N("Interval", ("this", L("5 minutes")))));
        nodes.Add(Bucket(N("Interval", ("this", Column()), ("unit", V("MINUTE")))));
        nodes.Add(Bucket(source: Column()));
        nodes.Add(Bucket(source: L("2024-01-01")));
        nodes.Add(Bucket(source: Cast(Column(), "DATE")));
        nodes.Add(Bucket(source: N("TryCast", ("this", Column()), ("to", N("DataType", ("this", "TIMESTAMP"))))));
        nodes.Add(Bucket(source: N("Cast", ("this", Column()), ("to", N("DataType", ("this", "TIMESTAMP"))), ("format", L("YYYY")))));
        // Overloads: timezone text, interval offset, named origin, duplicate origin/zone, mismatched type.
        nodes.Add(Bucket(extras: [L("UTC")]));
        nodes.Add(Bucket(extras: [Width("1", "HOUR")]));
        nodes.Add(Bucket(extras: [N("Kwarg", ("this", V("origin")), ("expression", Cast(L("2000-01-03 00:00:00"))))]));
        nodes.Add(Bucket(extras: [Cast(L("2000-01-03 00:00:00")), L("Europe/Berlin")]));
        nodes.Add(Bucket(extras: [Cast(L("2000-01-03 00:00:00+00:00"), "TIMESTAMPTZ")]));
        nodes.Add(Bucket(extras: [Cast(Column())]));
        foreach (var text in new[] { "now", "infinity", "01/03/2000", "2000-02-30 00:00:00", "2000-01-03 00:00:00+00:00", "2000-01-03 00:00:00.1234567" })
            nodes.Add(Bucket(extras: [Cast(L(text))]));
        foreach (var text in new[] { "2000-01-03 00:00:00", "2000-01-03 00:00:00 Europe/Berlin", "2000-01-03 00:00:00-05:00" })
            nodes.Add(Bucket(source: Cast(Column(), "TIMESTAMPTZ"), extras: [Cast(L(text), "TIMESTAMPTZ")]));
        var cases = new List<Case>();
        foreach (var ts in new[] { false, true })
        foreach (var pretty in new[] { false, true })
        foreach (var identify in new object[] { false, true, "safe" })
        foreach (var normalize in new object[] { "lower", false })
        foreach (var level in new[] { "IGNORE", "WARN", "RAISE", "IMMEDIATE" })
        foreach (var node in nodes)
        {
            cases.Add(new(node, ts, pretty, identify, normalize, level, false, true, false));
            cases.Add(new(node, ts, pretty, identify, normalize, level, true, true, true));
            cases.Add(new(node, ts, pretty, identify, normalize, level, true, false, true));
        }
        var expected = JsonSerializer.Deserialize<Result[]>(await Python(Oracle, JsonSerializer.Serialize(cases)))!;
        Assert.Equal(cases.Count, expected.Length);
        for (var i = 0; i < cases.Count; i++)
        {
            var c = cases[i];
            generator.Generator g = c.timescale ? new Ts(dialect: "timescale") : new Pg(dialect: "postgres");
            g.pretty = c.pretty; g.identify = c.identify; g.normalize = true; g.normalize_functions = c.normalize_functions;
            g.max_text_width = 32; g.pad = 3; g.unsupported_level = Enum.Parse<errors.ErrorLevel>(c.level);
            var node = Build(c.node); var before = node.copy();
            string? sql = null, error = null;
            try { sql = c.generate ? g.generate(node, copy: c.copy) : g.sql(node, comment: c.comment); }
            catch (NotPortedException e) { error = e.Message; }
            Assert.True(expected[i].sql == sql && expected[i].error == error && expected[i].messages.SequenceEqual(g.unsupported_messages),
                $"Case {i}: {JsonSerializer.Serialize(c)}\nPython {JsonSerializer.Serialize(expected[i])}\nC# {JsonSerializer.Serialize(new { sql, error, messages = g.unsupported_messages.ToArray() })}");
            var actualAst = JsonSerializer.SerializeToElement(serde.dump(node));
            Assert.True(Corpus.AstComparison.AreEqual(expected[i].ast, actualAst), $"Mutation mismatch at case {i}: {JsonSerializer.Serialize(c)}");
            if (c.generate && c.copy)
            {
                Assert.True(node.Equals(before), $"copy=true mutated original AST at case {i}");
                Assert.Equal(before.comments, node.comments);
            }
        }
    }

    [Fact]
    public void Typed_defaults_and_identity_are_explicit_not_approximate()
    {
        var pg = new Pg(); var ts = new Ts();
        var naive = Build(Bucket()) as exp.TimeBucket;
        Assert.Contains("CAST('2000-01-03 00:00:00' AS TIMESTAMP)", pg.sql(naive, comment: false));
        var zoned = Build(Bucket(source: Cast(Column(), "TIMESTAMPTZ")));
        Assert.Contains("CAST('2000-01-03 00:00:00+00:00' AS TIMESTAMPTZ)", pg.sql(zoned, comment: false));
        Assert.StartsWith("TIME_BUCKET(INTERVAL '5 MINUTES',", ts.sql(naive, comment: false));
        Assert.StartsWith("DATE_BIN(INTERVAL '300000000 MICROSECONDS',", pg.sql(naive, comment: false));
        pg.unsupported_level = errors.ErrorLevel.IGNORE;
        Assert.Throws<NotPortedException>(() => pg.generate(Build(Bucket(extras: [Width("1", "HOUR")]))));
        Assert.Empty(pg.unsupported_messages);
        Assert.Throws<NotPortedException>(() => new generator.Generator().sql(naive));
        // Anonymous extension SQL is outside the canonical proof contract and remains identity.
        Assert.Equal("TIME_BUCKET(1)", pg.sql(new exp.Anonymous(@this: "time_bucket", expressions: new List<object?> { exp.Literal.number(1) })));
    }

    [Fact]
    public async Task Fixed_microsecond_floor_matches_boundary_math_before_and_after_documented_origins()
    {
        // Integer model only: no database execution. Timescale and DATE_BIN both floor distance
        // from 2000-01-03 (not PostgreSQL epoch 2000-01-01). Zoned default is the same instant in UTC.
        var origin = new DateTime(2000, 1, 3, 0, 0, 0, DateTimeKind.Utc);
        var epoch = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(172800000000L, (origin.Ticks - epoch.Ticks) / 10);
        var inputs = new List<long[]>();
        foreach (var o in new[] { 172800000000L, -1234567890123L, 0L })
        foreach (var width in new[] { 1L, 7L, 1000000L, 300000000L, 86400000000L, 604800000000L })
        foreach (var multiple in new[] { -100L, -2L, -1L, 0L, 1L, 2L, 100L })
        foreach (var delta in new[] { -1L, 0L, 1L }) inputs.Add([o + multiple * width + delta, o, width]);
        var python = await Python("import json,sys; print(json.dumps([o+(t-o)//w*w for t,o,w in json.load(sys.stdin)]))", JsonSerializer.Serialize(inputs));
        var expected = JsonSerializer.Deserialize<long[]>(python)!;
        for (var i = 0; i < inputs.Count; i++)
        {
            var (t, o, w) = (inputs[i][0], inputs[i][1], inputs[i][2]);
            var quotient = BigInteger.DivRem((BigInteger)t - o, w, out var remainder);
            if (remainder < 0) quotient--;
            var bucket = (long)(o + quotient * w);
            Assert.Equal(expected[i], bucket);
            Assert.True(bucket <= t && (BigInteger)t < (BigInteger)bucket + w);
            Assert.Equal(BigInteger.Zero, ((BigInteger)bucket - o) % w);
        }
        // A pre-origin Sunday belongs to the previous Monday; truncation toward zero is wrong.
        Assert.Equal(-432000000000L, expected[inputs.FindIndex(x => x.SequenceEqual(new[] { 172799999999L, 172800000000L, 604800000000L }))]);
        // ISO UTC and an offset representation denote the same instant, but only UTC origin syntax is admitted.
        Assert.Equal(new DateTimeOffset(origin), DateTimeOffset.Parse("2000-01-02T19:00:00-05:00", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string SourcePath([CallerFilePath] string source = "") => source;
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
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
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
