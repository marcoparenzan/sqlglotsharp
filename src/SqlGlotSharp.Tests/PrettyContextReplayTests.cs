using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using SqlGlotSharp.Corpus;

namespace SqlGlotSharp.Tests;

public class PrettyContextReplayTests
{
    private static readonly string Artifacts = Path.Combine(RepoPaths.Scratch, "pretty-agent");
    private const string LegacyId = "b61bc651496722a775eaf997d056691a761c0e4b1fb0e03db3fd91769f6bf281";

    [Fact]
    public async Task Recorder_and_native_replay_match_live_global_and_override_matrix()
    {
        const string script = """
            import sys,json
            sys.path[:0]=['python','src/tools']
            import sqlglot,extract_corpus as corpus
            from sqlglot.generator import Generator
            from unittest.mock import patch
            recorder=corpus.Recorder();recorder.install()
            try:
                for global_pretty in (False,True):
                    with patch.object(sqlglot,'pretty',global_pretty):
                        for opts in ({},{'pretty':None},{'pretty':False},{'pretty':True}):
                            expression=sqlglot.parse_one('SELECT col FROM x')
                            expression.sql(**opts)
                            Generator(**opts).generate(expression)
                            sqlglot.transpile('SELECT col FROM x',**opts)
                with patch.object(sqlglot,'pretty',True): generator=Generator()
                with patch.object(sqlglot,'pretty',False): generator.generate(sqlglot.parse_one('SELECT col FROM x'))
            finally: recorder.uninstall()
            print(json.dumps([c for c in recorder.cases.values() if c['boundary'] in ('sql','generate','transpile')]))
            """;
        using var doc = JsonDocument.Parse(await Python(["-c", script]));
        var cases = doc.RootElement.EnumerateArray().Select(p => Item(p)).ToArray();
        Assert.Contains(cases, c => c.Payload.GetProperty("runtime_context").GetProperty("sqlglot.pretty").GetBoolean());
        Assert.Contains(cases, c => !c.Payload.GetProperty("runtime_context").GetProperty("sqlglot.pretty").GetBoolean());
        foreach (var boundary in new[] { "sql", "generate", "transpile" })
            Assert.Contains(cases, c => c.Payload.GetProperty("boundary").GetString() == boundary);
        Assert.Equal(8, cases.Count(c => c.Payload.GetProperty("boundary").GetString() == "sql"));
        Assert.Equal(8, cases.Count(c => c.Payload.GetProperty("boundary").GetString() == "transpile"));
        foreach (var c in cases)
        {
            CorpusVerifier.Verify(c);
            var corrupt = JsonNode.Parse(c.Payload.GetRawText())!.AsObject();
            corrupt["expected"] = new JsonObject { ["result"] = "incorrect SQL" };
            Assert.Throws<InvalidOperationException>(() => CorpusVerifier.Verify(Item(corrupt)));
        }
        // Wrong provenance must cause a difference for omitted/None, not be inferred from expected SQL.
        foreach (var c in cases.Where(c => c.Payload.GetProperty("boundary").GetString() != "generate" &&
                     (!c.Payload.GetProperty("options").TryGetProperty("pretty", out var option) || option.ValueKind == JsonValueKind.Null)))
        {
            var corrupt = JsonNode.Parse(c.Payload.GetRawText())!.AsObject();
            corrupt["runtime_context"]!["sqlglot.pretty"] = !c.Payload.GetProperty("runtime_context").GetProperty("sqlglot.pretty").GetBoolean();
            Assert.Throws<InvalidOperationException>(() => CorpusVerifier.Verify(Item(corrupt)));
        }
    }

    [Fact]
    public async Task Selected_upstream_sources_produce_fresh_records_and_evidence_without_changing_legacy()
    {
        // Canonical corpus is read-only. This exact historical mismatch remains a mismatch without provenance.
        var matches = Directory.EnumerateFiles(CorpusReader.RootDirectory, "L2.base.jsonl", SearchOption.AllDirectories)
            .SelectMany(path => File.ReadLines(path).Where(line => line.Contains(LegacyId, StringComparison.Ordinal))
                .Select(line => (Path: path, Line: line))).ToArray();
        Assert.NotEmpty(matches);
        var originalHashes = matches.Select(m => m.Path).Distinct().ToDictionary(p => p, p => SHA256.HashData(File.ReadAllBytes(p)));
        foreach (var match in matches)
        {
            using var legacy = JsonDocument.Parse(match.Line);
            Assert.False(legacy.RootElement.TryGetProperty("runtime_context", out _));
            Assert.Contains("tests.test_parser.TestParser.test_pretty_config_override", legacy.RootElement.GetProperty("sources").EnumerateArray().Select(s => s.GetString()));
            Assert.Equal("SELECT\n  col\nFROM x", legacy.RootElement.GetProperty("expected").GetProperty("result").GetString());
            Assert.Contains("SELECT col FROM x", Assert.Throws<InvalidOperationException>(() => CorpusVerifier.Verify(Item(legacy.RootElement))).Message);
        }
        var directory = Path.Combine(Artifacts, "pretty-context-selected");
        await Python(["src/tools/extract_corpus.py", "--test", "tests.test_parser.TestParser.test_pretty_config_override",
            "--test", "tests.test_transpile.TestTranspile.test_pretty_line_breaks", "--output", directory,
            "--log", Path.Combine(directory, "source-tests.log")]);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "MANIFEST.json")));
        Assert.False(manifest.RootElement.GetProperty("full_suite").GetBoolean());
        Assert.True(manifest.RootElement.GetProperty("suite_passed").GetBoolean());
        Assert.Equal(2, manifest.RootElement.GetProperty("tests_run").GetInt32());
        var records = CorpusReader.Read(directory).ToArray();
        var sqlCases = records.Where(c => c.Level == "L2" && c.Payload.GetProperty("boundary").GetString() == "sql").ToArray();
        Assert.Equal(3, sqlCases.Length);
        Assert.All(sqlCases, CorpusVerifier.Verify);
        Assert.All(records.Where(c => c.Payload.GetProperty("boundary").GetString() is "sql" or "generate" or "transpile"),
            c => Assert.True(c.Payload.GetProperty("runtime_context").GetProperty("sqlglot.pretty").ValueKind is JsonValueKind.True or JsonValueKind.False));
        foreach (var (path, hash) in originalHashes) Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(path)));
        File.WriteAllText(Path.Combine(directory, "root-cause-evidence.json"), JsonSerializer.Serialize(new
        {
            legacy_id = LegacyId, canonical_files = originalHashes.Keys,
            source = "python/tests/test_parser.py:915-920: test_pretty_config_override patches sqlglot.pretty=True",
            generator = "python/sqlglot/generator.py:951: pretty if pretty is not None else sqlglot.pretty",
            fresh_sql_record_ids = sqlCases.Select(c => c.Id),
            legacy_status = "Unchanged mismatch: absent context cannot be reconstructed; full corpus regeneration required."
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    [Fact]
    public async Task Targeted_Python_recorder_tests_pass_in_isolated_scratch()
    {
        await Python(["src/tools/test_extract_corpus.py"]);
    }

    [Fact]
    public void Unknown_or_malformed_context_is_rejected_even_with_explicit_pretty()
    {
        var payload = JsonNode.Parse("""{"boundary":"sql","write":"","options":{"pretty":true},"ast":[{"c":"sqlglot.expressions.core.Literal"},{"i":0,"k":"this","v":"1"},{"i":0,"k":"is_string","v":false}],"expected":{"result":"1"},"warnings":[]}""")!.AsObject();
        foreach (var context in new[] { "null", "true", "[]", "{}", "{\"sqlglot.pretty\":null}", "{\"sqlglot.pretty\":1}", "{\"sqlglot.pretty\":\"true\"}", "{\"unknown\":true}", "{\"sqlglot.pretty\":true,\"unknown\":false}", "{\"sqlglot.pretty\":true,\"sqlglot.pretty\":false}" })
        {
            // Parse as JsonElement to preserve duplicate keys for strict validation.
            var text = payload.ToJsonString();
            using var doc = JsonDocument.Parse(text[..^1] + ",\"runtime_context\":" + context + "}");
            Assert.Throws<NotPortedException>(() => CorpusVerifier.Verify(Item(doc.RootElement)));
        }
        foreach (var globalPretty in new[] { false, true })
        {
            payload["runtime_context"] = new JsonObject { ["sqlglot.pretty"] = globalPretty };
            CorpusVerifier.Verify(Item(payload));
        }
    }

    private static CorpusCase Item(JsonObject p) => Item(JsonSerializer.SerializeToElement(p));
    private static CorpusCase Item(JsonElement p) => new(p.TryGetProperty("id", out var id) ? id.GetString()! : "fixture", "L2", "base", [], p.Clone());
    private static async Task<string> Python(string[] args)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SQLGLOT_PYTHON") ?? "python")
        {
            WorkingDirectory = Root(), UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        var scratch = Path.Combine(Artifacts, "tmp"); Directory.CreateDirectory(scratch);
        start.Environment["TEMP"] = scratch; start.Environment["TMP"] = scratch;
        start.ArgumentList.Add("-B"); foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); } catch (OperationCanceledException) { process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await output + await error);
        return await output;
    }
    private static string Root([CallerFilePath] string source = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", ".."));
}
