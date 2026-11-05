using System.Text.Json;
using SqlGlotSharp.Corpus;

namespace SqlGlotSharp.Tests;

public class L1SerdeGateTests
{
    [Fact]
    public void Every_recorded_L1_ast_roundtrips_without_skips()
    {
        var root = Path.Combine(global::SqlGlotSharp.Corpus.CorpusReader.RootDirectory, "v30.27.0");
        var failures = new List<string>();
        var astCount = 0;
        var cases = CorpusReader.Read(root).Where(c => c.Level == "L1").ToArray();
        var results = CorpusRunner.Run(cases, item =>
        {
            // Error-only records have no AST to serialize.
            if (!item.Payload.GetProperty("expected").TryGetProperty("result", out var outputs)) return;
            foreach (var ast in outputs.EnumerateArray())
            {
                if (ast.ValueKind == JsonValueKind.Null)
                {
                    if (serde.load(ast) is not null) throw new InvalidOperationException("Null AST changed");
                }
                else
                {
                    var loaded = serde.load(ast);
                    var actual = serde.dump_json(loaded);
                    if (!JsonElement.DeepEquals(ast, actual)) throw new InvalidOperationException("AST roundtrip differs");
                }
                astCount++;
            }
        });
        foreach (var result in results)
            if (result.Status != CaseStatus.Passed) failures.Add($"{result.Case.Id}: {result.Detail}");
        Assert.Equal(7855, cases.Length);
        Assert.True(astCount > 0);
        Assert.True(failures.Count == 0, $"{failures.Count} failed L1 records: " + string.Join("\n", failures.Take(5)));
    }

    private static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
