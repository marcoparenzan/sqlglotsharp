using SqlGlotSharp.Corpus;
using System.Text.Json;

namespace SqlGlotSharp.Tests;

public class CorpusRunnerTests
{
    [Fact]
    public void Entire_corpus_is_loaded_and_baseline_is_not_ported()
    {
        var directory = Path.Combine(global::SqlGlotSharp.Corpus.CorpusReader.RootDirectory, "v30.27.0");
        var cases = CorpusReader.Read(directory).ToArray();
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "MANIFEST.json")));
        Assert.Equal(manifest.RootElement.GetProperty("counts").EnumerateObject().Sum(p => p.Value.GetInt32()), cases.Length);
        var results = CorpusRunner.Run(cases, CorpusRunner.VerifyBaseline);
        Assert.Equal(cases.Length, results.Count);
        Assert.All(results, result => Assert.Equal(CaseStatus.NotPorted, result.Status));
        Assert.Equal(16, results.GroupBy(r => (r.Case.Level, r.Case.Dialect)).Count());
    }

    [Fact]
    public void Runner_distinguishes_success_failure_and_not_ported()
    {
        using var json = JsonDocument.Parse("{}");
        CorpusCase Item(string id) => new(id, "L0", "base", [], json.RootElement.Clone());
        var callingThread = Environment.CurrentManagedThreadId;
        var results = CorpusRunner.Run([Item("pass"), Item("fail"), Item("missing")], item =>
        {
            Assert.NotEqual(callingThread, Environment.CurrentManagedThreadId);
            if (item.Id == "fail") throw new InvalidOperationException("mismatch");
            if (item.Id == "missing") throw new NotPortedException("test");
        });
        Assert.Equal(new[] { CaseStatus.Passed, CaseStatus.Failed, CaseStatus.NotPorted }, results.Select(r => r.Status));
    }

    private static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.GetDirectoryName(path)!;
}
