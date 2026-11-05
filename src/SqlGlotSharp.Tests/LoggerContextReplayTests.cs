using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using SqlGlotSharp.Corpus;

namespace SqlGlotSharp.Tests;

public class LoggerContextReplayTests
{
    [Fact]
    public async Task Three_real_mock_paths_and_normal_and_disabled_logging_replay_faithfully()
    {
        const string script = """
            import sys, json, unittest, io, logging
            sys.path[:0] = ['python', 'src/tools']
            import extract_corpus as corpus
            import sqlglot
            from sqlglot import parser
            from sqlglot.errors import ErrorLevel
            names = ['tests.test_parser.TestParser.test_comment_error_n',
                     'tests.test_parser.TestParser.test_comment_error_r',
                     'tests.test_transpile.TestTranspile.test_error_level']
            recorder = corpus.Recorder()
            recorder.install()
            try:
                suites = [unittest.defaultTestLoader.loadTestsFromName(n) for n in names]
                recorder.cases.clear()
                for name, suite in zip(names, suites):
                    recorder.test_id.set(name)
                    result = unittest.TextTestRunner(stream=io.StringIO()).run(suite)
                    if not result.wasSuccessful(): raise AssertionError(str(result.failures) + str(result.errors))
                mocks = [c for c in recorder.cases.values() if c['level'] == 'L1' and c['warnings']]
                assert len(mocks) == 3
                logger = parser.logger
                for disabled in [False, True]:
                    logger.disabled = disabled
                    logger.setLevel(logging.WARNING)
                    for case in mocks:
                        recorder.test_id.set('live-disabled' if disabled else 'live-normal')
                        getattr(sqlglot, case['boundary'])(case['sql'], error_level=ErrorLevel.WARN)
                cases = [c for c in recorder.cases.values() if c['level'] == 'L1'
                         and c['options'].get('error_level', {}).get('value') == 'WARN']
                print(json.dumps(cases))
            finally:
                parser.logger.disabled = False
                recorder.uninstall()
            """;
        var root = Root();
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SQLGLOT_PYTHON") ?? "python")
        {
            WorkingDirectory = root, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-B"); start.ArgumentList.Add("-c"); start.ArgumentList.Add(script);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await stderr);
        using var output = JsonDocument.Parse(await stdout);
        var cases = output.RootElement.EnumerateArray().Select(c => new CorpusCase(
            c.GetProperty("id").GetString()!, "L1", "base", [], c.Clone())).ToArray();
        Assert.Equal(9, cases.Length);
        foreach (var mode in new[] { "mock", "enabled", "disabled" })
        {
            var group = cases.Where(c => c.Payload.GetProperty("logger_context").GetProperty("parser")
                .GetProperty("error").GetString() == mode).ToArray();
            Assert.Equal(3, group.Length);
            Assert.All(group, c => Assert.Equal(mode == "disabled" ? 0 : c.Payload.GetProperty("sql").GetString() == "x + 1. (" ? 2 : 1,
                c.Payload.GetProperty("warnings").GetArrayLength()));
            Assert.All(group, CorpusVerifier.Verify);
        }
        // Normal logs and mock calls are both checked, not silently dropped by replay.
        foreach (var item in cases.Where(c => c.Payload.GetProperty("warnings").GetArrayLength() > 0))
        {
            var broken = JsonNode.Parse(item.Payload.GetRawText())!.AsObject();
            broken["warnings"] = new JsonArray();
            Assert.Throws<InvalidOperationException>(() => CorpusVerifier.Verify(Item(broken)));
        }
        // Context is explicit and capability checked; malformed/unknown context is not an expected error.
        var invalid = JsonNode.Parse(cases[0].Payload.GetRawText())!.AsObject();
        invalid["logger_context"] = JsonNode.Parse("""{"parser":{"warning":"invented","error":"mock"}}""");
        Assert.Throws<NotPortedException>(() => CorpusVerifier.Verify(Item(invalid)));

        // No migration by SQL/source ID: the three legacy omissions must remain visible failures.
        var sqls = cases.Select(c => c.Payload.GetProperty("sql").GetString()).ToHashSet();
        var old = CorpusReader.Read(Path.Combine(global::SqlGlotSharp.Corpus.CorpusReader.RootDirectory, "v30.27.0"))
            .Where(c => c.Level == "L1" && sqls.Contains(c.Payload.GetProperty("sql").GetString())
                && c.Payload.GetProperty("options").TryGetProperty("error_level", out var level)
                && level.GetProperty("value").GetString() == "WARN").ToArray();
        Assert.Equal(3, old.Length);
        Assert.All(old, c =>
        {
            Assert.False(c.Payload.TryGetProperty("logger_context", out _));
            Assert.Empty(c.Payload.GetProperty("warnings").EnumerateArray());
            Assert.Contains("Actual warnings", Assert.Throws<InvalidOperationException>(() => CorpusVerifier.Verify(c)).Message);
        });
    }

    private static CorpusCase Item(JsonObject payload) => new("context-fixture", "L1", "base", [], JsonSerializer.SerializeToElement(payload));
    private static string Root([CallerFilePath] string source = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", ".."));
}
