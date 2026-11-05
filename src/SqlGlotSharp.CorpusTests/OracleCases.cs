using SqlGlotSharp.Corpus;

namespace SqlGlotSharp.CorpusTests;

public partial class OracleCases
{
    private static readonly Lazy<Dictionary<string, CorpusCase>> Cases = new(() =>
        CorpusReader.Read(Path.Combine(global::SqlGlotSharp.Corpus.CorpusReader.RootDirectory, "v30.27.0"))
            .ToDictionary(c => c.Id, StringComparer.Ordinal));

    public static IEnumerable<object[]> Data(string level, string dialect) => Cases.Value.Values
        .Where(c => c.Level == level && c.Dialect == dialect)
        .OrderBy(c => c.Id, StringComparer.Ordinal)
        .Select(c => new object[] { c.Id, string.Join(";", c.Sources) });

    private static void Verify(string id, string sources)
    {
        var result = Assert.Single(CorpusRunner.Run([Cases.Value[id]], CorpusVerifier.Verify));
        Assert.True(result.Status == CaseStatus.Passed, $"{result.Detail ?? result.Status.ToString()}\nSources: {sources}");
    }

    private static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.GetDirectoryName(path)!;
}
