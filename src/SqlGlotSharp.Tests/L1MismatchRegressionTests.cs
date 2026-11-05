using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using SqlGlotSharp.Corpus;

namespace SqlGlotSharp.Tests;

/// <summary>
/// Investigates the initial sixteen failures without modifying recorded expectations.
/// AST repairs belong to DateTrunc construction and the base dialect parser factory,
/// outside this task's ownership. This test verifies the Python reference and the
/// three already-correct CLR warning paths; it does not assert parity for those 13 ASTs.
/// </summary>
public class L1MismatchRegressionTests
{
    private static readonly HashSet<string> InitialIds =
    [
        "0327ff7091dc3642b22f240382069eb393f5fc9cd20ab997651308a0f96426df",
        "144e8150240a5447e89c3f5a858a4a0b2c030952f3c6aa9487c954a4ee829a5e",
        "16461e5565eb8c2555109873adf712c903f029601e50f61f90bd922609ba34b1",
        "178ebb4d3f2e93c2107da22dea639e94f5bf10913deb61a09470ffc45db55402",
        "3e04139ddd457182b3d56f047d4c6997d9d75fa1b7cb1080687b2390e8fe5f3d",
        "4d10c57af166493a72c79ba9b8a98c4376d21b98247be89cb4fc7d16b7f980c6",
        "63b333d1667557c1139ef47a2dfdf95590da8e6e8f27466196e0e49dbd559672",
        "7069d1c31896963f062003d1bb9cba1c502d79bc7a567311ebd9dfd416ed0dd5",
        "7a49e1ae7899302a9f1aa52fc18e2658ce1e4349771c0daaea49d4bf726e0bc7",
        "917d4deaf1a3728d509205598152ec56eafd12dadc3d795d4d592bbeb8d525bf",
        "950b736f8261be5cb19dbc2d6c7ab08ad266b170144e99f65716e4cb84643ff0",
        "b1ffcf986ef73bca582a08dee7ddf71c7878ea0047d7eb4d24186cd0bb49140e",
        "b71e6dc08995ca163afd61363e64da31a2e84e429c5a90667288d31cd59c0f44",
        "b7a0318a92168597fd5b14df0f2a2359329d77a1bc17ebfe279c6c73de623a2b",
        "b7d7e3fe7f5dc03bb8a996db1987c86b897bbd0f7c4084683ded3a72a03a8c46",
        "fc6b513867d2a9a3415e223f08147f684e1b8847aa59f3a0d9e1ded19f092777"
    ];

    [Fact]
    public async Task All_sixteen_recorded_asts_match_python_and_warning_paths_match_clr()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Source())!, "..", ".."));
        var cases = CorpusReader.Read(Path.Combine(global::SqlGlotSharp.Corpus.CorpusReader.RootDirectory, "v30.27.0"))
            .Where(c => InitialIds.Contains(c.Id)).ToArray();
        Assert.Equal(16, cases.Length);
        Assert.Equal(11, cases.Count(c => c.Payload.GetProperty("sql").GetString()!.Contains("DATE_TRUNC")));
        const string script = """
            import sys, json, logging, importlib.util
            sys.path.insert(0, 'python')
            import sqlglot
            from sqlglot import serde
            from sqlglot.errors import ErrorLevel
            spec = importlib.util.spec_from_file_location('extract_corpus', 'src/tools/extract_corpus.py')
            extractor = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(extractor)
            output = []
            for c in json.load(sys.stdin):
                messages = []
                class Capture(logging.Handler):
                    def emit(self, record):
                        if record.levelno >= logging.WARNING:
                            messages.append(dict(level=record.levelname, message=record.getMessage()))
                logger = logging.getLogger('sqlglot')
                capture = Capture()
                logger.addHandler(capture)
                opts = dict(c['options'])
                if 'error_level' in opts:
                    opts['error_level'] = ErrorLevel[opts['error_level']['value']]
                try:
                    nodes = getattr(sqlglot, c['boundary'])(c['sql'], read=c['read'], **opts)
                    if not isinstance(nodes, list): nodes = [nodes]
                    expected = dict(result=[extractor.normalize_ast(serde.dump(n), c['sql']) if n is not None else None for n in nodes])
                finally:
                    logger.removeHandler(capture)
                output.append(dict(expected=expected, warnings=messages))
            print(json.dumps(output))
            """;
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SQLGLOT_PYTHON") ?? "python")
        {
            WorkingDirectory = root, UseShellExecute = false,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
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
        Assert.Equal(16, output.RootElement.GetArrayLength());
        var warningCases = 0;
        for (var i = 0; i < cases.Length; i++)
        {
            var item = cases[i];
            var reference = output.RootElement[i];
            Assert.True(JsonElement.DeepEquals(item.Payload.GetProperty("expected"), reference.GetProperty("expected")), item.Id);
            Assert.Equal(0, item.Payload.GetProperty("warnings").GetArrayLength());
            var warnings = reference.GetProperty("warnings");
            if (warnings.GetArrayLength() == 0) continue;
            warningCases++;
            var sql = item.Payload.GetProperty("sql").GetString()!;
            Assert.True(sql == "x + 1. (" || sql == "SUM\n(\n-- test\n)" || sql == "SUM(-- test\r)");
            Assert.Equal(sql == "x + 1. (" ? 2 : 1, warnings.GetArrayLength());
            // A NEW live-oracle input, not a mutation of the recorded corpus.
            // C# must preserve every warning and match its exact Python message.
            var live = JsonNode.Parse(item.Payload.GetRawText())!.AsObject();
            live["warnings"] = JsonNode.Parse(warnings.GetRawText());
            CorpusVerifier.Verify(new CorpusCase($"live-{item.Id}", "L1", "base", [], JsonSerializer.SerializeToElement(live)));
            // The original case must still fail: silently suppressing these logs
            // in L1Replay would hide missing extractor context rather than fix SQL.
            var mismatch = Assert.Throws<InvalidOperationException>(() => CorpusVerifier.Verify(item));
            Assert.Contains("Actual warnings", mismatch.Message);
        }
        Assert.Equal(3, warningCases);
    }

    private static string Source([CallerFilePath] string source = "") => source;
}
