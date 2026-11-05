using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using SqlGlotSharp.Corpus;

namespace SqlGlotSharp.Tests;

public class AstComparisonTests
{
    private static readonly string Report = Path.Combine(RepoPaths.Artifacts, "reports", "l2-central", "failures.json");
    private const string Fixture = """
        [{"c":"Root","o":["first","second"],"m":{"line":1,"col":2,"start":0,"end":1,"custom":null},
          "t":[{"c":"Type"},{"i":0,"k":"this","c":"DataType.Type","v":"INT"}]},
         {"i":0,"k":"left","c":"Child"},{"i":1,"k":"this","v":"x"},
         {"i":0,"k":"flag","v":false},
         {"i":0,"k":"items","a":true,"c":"Item"},{"i":4,"k":"this","v":1},
         {"i":0,"k":"items","a":true,"v":null},
         {"i":0,"k":"nullable","v":null}]
        """;

    [Fact]
    public void Argument_order_and_indexes_are_not_semantic_but_the_complete_graph_is()
    {
        var reordered = Parse("""
            [{"t":[{"c":"Type"},{"v":"INT","c":"DataType.Type","k":"this","i":0}],
              "m":{"custom":null,"end":1,"start":0,"col":2,"line":1},"o":["first","second"],"c":"Root"},
             {"i":0,"k":"nullable","v":null},
             {"i":0,"k":"items","a":true,"c":"Item"},
             {"i":0,"k":"items","a":true,"v":null},
             {"i":0,"k":"flag","v":false},
             {"i":0,"k":"left","c":"Child"},
             {"i":5,"k":"this","v":"x"},
             {"i":2,"k":"this","v":1}]
            """);
        Assert.False(JsonElement.DeepEquals(Parse(Fixture), reordered));
        Assert.True(AstComparison.AreEqual(Parse(Fixture), reordered));
        Assert.True(AstComparison.AreEqual(reordered, Parse(Fixture)));
    }

    public static IEnumerable<object[]> SemanticChanges()
    {
        yield return ["class", (Action<JsonArray>)(a => a[1]!["c"] = "Other")];
        yield return ["argument name", (Action<JsonArray>)(a => a[1]!["k"] = "right")];
        yield return ["scalar", (Action<JsonArray>)(a => a[2]!["v"] = "y")];
        yield return ["scalar kind", (Action<JsonArray>)(a => a[3]!["v"] = "false")];
        yield return ["list order", (Action<JsonArray>)(a => { var second = a[6]!.DeepClone(); a.RemoveAt(6); a.Insert(4, second); a[6]!["i"] = 5; })];
        yield return ["list versus single", (Action<JsonArray>)(a => { a.RemoveAt(6); a[4]!.AsObject().Remove("a"); })];
        yield return ["comments order", (Action<JsonArray>)(a => a[0]!["o"] = new JsonArray("second", "first"))];
        yield return ["comments missing", (Action<JsonArray>)(a => a[0]!.AsObject().Remove("o"))];
        foreach (var key in new[] { "line", "col", "start", "end" })
            yield return [$"position {key}", (Action<JsonArray>)(a => a[0]!["m"]![key] = 99)];
        yield return ["metadata missing versus null", (Action<JsonArray>)(a => a[0]!["m"]!.AsObject().Remove("custom"))];
        yield return ["metadata added", (Action<JsonArray>)(a => a[0]!["m"]!["extra"] = true)];
        yield return ["metadata nested list order", (Action<JsonArray>)(a => a[0]!["m"]!["custom"] = new JsonArray(2, 1))];
        yield return ["type class", (Action<JsonArray>)(a => a[0]!["t"]![0]!["c"] = "OtherType")];
        yield return ["type value", (Action<JsonArray>)(a => a[0]!["t"]![1]!["v"] = "TEXT")];
        yield return ["type missing", (Action<JsonArray>)(a => a[0]!.AsObject().Remove("t"))];
        yield return ["type null", (Action<JsonArray>)(a => a[0]!["t"] = null)];
        yield return ["argument null versus missing", (Action<JsonArray>)(a => a.RemoveAt(7))];
        yield return ["parent edge", (Action<JsonArray>)(a => a[7]!["i"] = 1)];
        yield return ["unknown payload field", (Action<JsonArray>)(a => a[0]!["future"] = "kept")];
    }

    [Theory]
    [MemberData(nameof(SemanticChanges))]
    public void Structural_changes_fail(string description, Action<JsonArray> change)
    {
        var modified = JsonNode.Parse(Fixture)!.AsArray();
        change(modified);
        var dump = JsonSerializer.SerializeToElement(modified);
        Assert.True(AstComparison.AreEqual(dump, dump), $"Mutation must remain a valid graph: {description}");
        Assert.False(AstComparison.AreEqual(Parse(Fixture), dump), description);
        Assert.False(AstComparison.AreEqual(dump, Parse(Fixture)), description);
    }

    [Fact]
    public void Nested_metadata_arrays_and_type_argument_order_obey_the_same_rules()
    {
        var left = Parse("""[{"c":"Root","m":{"array":[1,2]},"t":[{"c":"Type"},{"i":0,"k":"x","v":1},{"i":0,"k":"y","v":2}]}]""");
        var right = Parse("""[{"c":"Root","m":{"array":[1,2]},"t":[{"c":"Type"},{"i":0,"k":"y","v":2},{"i":0,"k":"x","v":1}]}]""");
        Assert.True(AstComparison.AreEqual(left, right));
        Assert.False(AstComparison.AreEqual(left, Parse(right.GetRawText().Replace("[1,2]", "[2,1]"))));
    }

    [Fact]
    public void Explicit_null_metadata_is_not_missing_and_scalar_array_order_is_preserved()
    {
        foreach (var key in new[] { "t", "m", "o", "future" })
        {
            var missing = Parse("""[{"c":"Root"}]""");
            var present = Parse($"[{{\"c\":\"Root\",\"{key}\":null}}]");
            Assert.True(AstComparison.AreEqual(present, present));
            Assert.False(AstComparison.AreEqual(missing, present));
        }
        var left = Parse("""[{"c":"Root"},{"i":0,"k":"value","v":[1,null,"x"]}]""");
        var right = Parse("""[{"c":"Root"},{"i":0,"k":"value","v":["x",null,1]}]""");
        Assert.True(AstComparison.AreEqual(left, left));
        Assert.True(AstComparison.AreEqual(right, right));
        Assert.False(AstComparison.AreEqual(left, right));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[{}]")]
    [InlineData("[{\"c\":\"Root\",\"i\":0}]")]
    [InlineData("[{\"c\":\"Root\",\"k\":\"this\"}]")]
    [InlineData("[{\"c\":\"Root\",\"a\":false}]")]
    [InlineData("[{\"c\":\"Root\"},{\"i\":-1,\"k\":\"x\",\"v\":1}]")]
    [InlineData("[{\"c\":\"Root\"},{\"i\":1,\"k\":\"x\",\"v\":1}]")]
    [InlineData("[{\"c\":\"Root\"},{\"i\":2,\"k\":\"x\",\"v\":1},{\"i\":1,\"k\":\"y\",\"v\":1}]")]
    [InlineData("[{\"c\":\"Root\"},{\"i\":999,\"k\":\"x\",\"v\":1}]")]
    [InlineData("[{\"c\":\"Root\"},{\"i\":0.5,\"k\":\"x\",\"v\":1}]")]
    [InlineData("[{\"c\":\"Root\"},{\"i\":\"0\",\"k\":\"x\",\"v\":1}]")]
    [InlineData("[{\"c\":\"Root\"},{\"i\":null,\"k\":\"x\",\"v\":1}]")]
    [InlineData("[{\"c\":\"Root\"},{\"k\":\"x\",\"v\":1}]")]
    [InlineData("[{\"c\":\"Root\"},{\"i\":0,\"v\":1}]")]
    [InlineData("[{\"c\":\"Root\"},{\"i\":0,\"k\":null,\"v\":1}]")]
    [InlineData("[{\"c\":\"Root\"},{\"c\":\"OtherRoot\"}]")]
    [InlineData("[{\"c\":\"Root\"},{\"i\":0,\"k\":\"x\",\"v\":1},{\"i\":1,\"k\":\"y\",\"v\":2}]")]
    [InlineData("[{\"c\":\"DataType.Type\",\"v\":\"INT\"},{\"i\":0,\"k\":\"x\",\"v\":1}]")]
    [InlineData("[{\"c\":\"Root\"},{\"i\":0,\"k\":\"x\",\"v\":1},{\"i\":0,\"k\":\"x\",\"v\":2}]")]
    [InlineData("[{\"c\":\"Root\"},{\"i\":0,\"k\":\"x\",\"a\":true,\"v\":1},{\"i\":0,\"k\":\"x\",\"v\":2}]")]
    [InlineData("[{\"c\":\"Root\"},{\"i\":0,\"k\":\"x\",\"a\":1,\"v\":1}]")]
    [InlineData("[{\"c\":\"Root\"},{\"i\":0,\"k\":\"x\",\"i\":0,\"v\":1}]")]
    [InlineData("[{\"c\":\"Root\",\"t\":[{\"c\":\"Type\"},{\"i\":1,\"k\":\"x\",\"v\":1}]}]")]
    public void Invalid_graphs_never_compare_equal_even_to_themselves(string json)
    {
        var dump = Parse(json);
        Assert.False(AstComparison.AreEqual(dump, dump));
        Assert.False(AstComparison.AreEqual(Parse(Fixture), dump));
        Assert.False(AstComparison.AreEqual(dump, Parse(Fixture)));
    }

    [Fact]
    public void Result_statement_order_nulls_and_error_envelopes_remain_exact()
    {
        var left = Parse("""{"result":[[{"c":"A"}],null,[{"c":"B"}]]}""");
        Assert.True(AstComparison.ResultsEqual(left, left));
        Assert.False(AstComparison.ResultsEqual(left, Parse("""{"result":[[{"c":"B"}],null,[{"c":"A"}]]}""")));
        Assert.False(AstComparison.ResultsEqual(left, Parse("""{"result":[[{"c":"A"}],[],[{"c":"B"}]]}""")));
        Assert.False(AstComparison.ResultsEqual(left, Parse("""{"result":[[{"c":"A"}],[{"c":"B"}]]}""")));
        var error = Parse("""{"error":{"class":"ParseError","module":"sqlglot.errors","message":"original"}}""");
        Assert.True(AstComparison.ResultsEqual(error, error));
        foreach (var field in new[] { "class", "module", "message" })
        {
            var changed = JsonNode.Parse(error.GetRawText())!;
            changed["error"]![field] = "changed";
            Assert.False(AstComparison.ResultsEqual(error, JsonSerializer.SerializeToElement(changed)));
        }
    }

    [Fact]
    public void All_14_reported_dumps_are_complete_equivalent_graphs_and_replay_without_changing_corpus()
    {
        var records = Failures();
        Assert.Equal(14, records.Length);
        Assert.Equal(10, records.Count(r => r.GetProperty("Dialect").GetString() == "base"));
        Assert.Equal(4, records.Count(r => r.GetProperty("Dialect").GetString() == "postgres"));
        var files = CorpusFiles().Append(Report).ToArray();
        var hashes = files.ToDictionary(f => f, Hash);
        var cases = RealCases(records);
        foreach (var record in records)
        {
            var id = record.GetProperty("Id").GetString()!;
            var detail = record.GetProperty("Detail").GetString()!;
            var expected = DetailJson(detail, "Expected: ");
            var actual = DetailJson(detail, "Actual: ");
            Assert.False(JsonElement.DeepEquals(expected, actual));
            // Compare the two independent historical full dumps, not just the first difference.
            Assert.True(AstComparison.ResultsEqual(expected, actual), detail);
            Assert.True(JsonElement.DeepEquals(DetailJson(detail, "Expected warnings: "), DetailJson(detail, "Actual warnings: ")));
            Assert.True(JsonElement.DeepEquals(expected, cases[id].Payload.GetProperty("expected")));
            L1Replay.Verify(cases[id]); // Original exception/report survives any genuine discrepancy.
        }
        foreach (var file in files) Assert.Equal(hashes[file], Hash(file));
    }

    [Fact]
    public async Task Live_python_randomized_argument_dictionaries_preserve_full_graphs()
    {
        var cases = RealCases(Failures()).Values.ToArray();
        const string script = """
            import sys, json, random, importlib.util
            sys.path.insert(0, 'python')
            import sqlglot
            from sqlglot import exp, serde
            from sqlglot.errors import ErrorLevel
            from sqlglot.dialects import Dialect
            from sqlglot.parser import Parser
            from sqlglot.parsers.postgres import PostgresParser
            spec = importlib.util.spec_from_file_location('extract_corpus', 'src/tools/extract_corpus.py')
            extractor = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(extractor)
            def shuffle(node, rng, seen):
                if not isinstance(node, exp.Expr) or id(node) in seen: return
                seen.add(id(node))
                pairs = list(node.args.items())
                rng.shuffle(pairs)
                node.args.clear()
                node.args.update(pairs)
                for value in node.args.values():
                    for child in value if isinstance(value, list) else [value]: shuffle(child, rng, seen)
                shuffle(node.type, rng, seen)
            output = []
            for case in json.load(sys.stdin):
                sql = case['sql']
                opts = dict(case.get('options', {}))
                if isinstance(opts.get('error_level'), dict): opts['error_level'] = ErrorLevel[opts['error_level']['value']]
                if 'parser' in case:
                    dialect = Dialect.get_or_raise(case['read'])
                    cls = Parser if case['parser'] == 'sqlglot.parser::Parser' else PostgresParser
                    nodes = cls(dialect=dialect, **opts).parse(dialect.tokenize(sql), sql)
                    if case['boundary'] == 'parse_one': nodes = [exp.Block(expressions=nodes) if len(nodes) > 1 else nodes[0]]
                else:
                    nodes = getattr(sqlglot, case['boundary'])(sql, read=case['read'], **opts)
                    if not isinstance(nodes, list): nodes = [nodes]
                def dumps():
                    return {'result': [extractor.normalize_ast(serde.dump(n), sql) if n is not None else None for n in nodes]}
                baseline = dumps()
                variants = []
                for seed in range(25):
                    rng = random.Random(seed)
                    for node in nodes: shuffle(node, rng, set())
                    variants.append(dumps())
                # Exercise type dumps and ordered comments/metadata in actual Python serde as well.
                nodes[0].type = exp.DataType.build('ARRAY<INT>')
                nodes[0].comments = ['first', 'second']
                nodes[0].meta['custom'] = {'ordered': [1, 2], 'nullable': None}
                decorated = dumps()
                decorated_variants = []
                for seed in range(10):
                    for node in nodes: shuffle(node, random.Random(seed), set())
                    decorated_variants.append(dumps())
                output.append(dict(baseline=baseline, variants=variants, decorated=decorated, decorated_variants=decorated_variants))
            print(json.dumps(output))
            """;
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SQLGLOT_PYTHON") ?? "python")
        {
            WorkingDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Source())!, "..", "..")),
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-B"); start.ArgumentList.Add("-c"); start.ArgumentList.Add(script);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(cases.Select(c => c.Payload)).AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) { process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await stderr);
        using var output = JsonDocument.Parse(await stdout);
        Assert.Equal(14, output.RootElement.GetArrayLength());
        var reordered = 0;
        for (var i = 0; i < cases.Length; i++)
        {
            var entry = output.RootElement[i];
            var baseline = entry.GetProperty("baseline");
            Assert.True(AstComparison.ResultsEqual(cases[i].Payload.GetProperty("expected"), baseline), cases[i].Id);
            foreach (var variant in entry.GetProperty("variants").EnumerateArray())
            {
                Assert.True(AstComparison.ResultsEqual(baseline, variant), cases[i].Id);
                if (!JsonElement.DeepEquals(baseline, variant)) reordered++;
                var live = JsonNode.Parse(cases[i].Payload.GetRawText())!.AsObject();
                live["expected"] = JsonNode.Parse(variant.GetRawText());
                L1Replay.Verify(cases[i] with { Payload = JsonSerializer.SerializeToElement(live) });
            }
            foreach (var variant in entry.GetProperty("decorated_variants").EnumerateArray())
                Assert.True(AstComparison.ResultsEqual(entry.GetProperty("decorated"), variant), cases[i].Id);
            Assert.False(AstComparison.ResultsEqual(baseline, entry.GetProperty("decorated")));
        }
        Assert.True(reordered > 300, $"Only {reordered} of 350 dumps had different traversal order");
    }

    private static JsonElement[] Failures()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Report));
        return doc.RootElement.EnumerateArray().Where(r => r.GetProperty("Level").GetString() == "L1").Select(r => r.Clone()).ToArray();
    }

    private static string[] CorpusFiles() => new[] { "L1.base.jsonl", "L1.postgres.jsonl" }
        .Select(f => CorpusReader.ResolveFile(Path.Combine(CorpusReader.RootDirectory, "v30.27.0-logger-context-full"), f)).ToArray();

    private static Dictionary<string, CorpusCase> RealCases(JsonElement[] records)
    {
        var ids = records.Select(r => r.GetProperty("Id").GetString()!).ToHashSet(StringComparer.Ordinal);
        var cases = new Dictionary<string, CorpusCase>(StringComparer.Ordinal);
        foreach (var file in CorpusFiles())
        foreach (var line in File.ReadLines(file))
        {
            using var doc = JsonDocument.Parse(line);
            var payload = doc.RootElement;
            var id = payload.GetProperty("id").GetString()!;
            if (!ids.Contains(id)) continue;
            var dialect = records.Single(r => r.GetProperty("Id").GetString() == id).GetProperty("Dialect").GetString()!;
            cases.Add(id, new CorpusCase(id, "L1", dialect, [], payload.Clone()));
        }
        Assert.Equal(14, cases.Count);
        return cases;
    }

    private static JsonElement DetailJson(string detail, string prefix)
    {
        var start = detail.IndexOf("\n" + prefix, StringComparison.Ordinal) + 1;
        Assert.True(start > 0, prefix);
        start += prefix.Length;
        var end = detail.IndexOf('\n', start);
        return Parse(end < 0 ? detail[start..] : detail[start..end]);
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    private static JsonElement Parse(string json) => JsonSerializer.Deserialize<JsonElement>(json);
    private static string Source([CallerFilePath] string source = "") => source;
}
