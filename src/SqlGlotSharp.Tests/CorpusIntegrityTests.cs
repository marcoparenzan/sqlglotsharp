using System.Text.Json;

namespace SqlGlotSharp.Tests;

public class CorpusIntegrityTests
{
    [Fact]
    [Trait("Level", "Tooling")]
    public void Recorded_corpus_matches_manifest_and_has_unique_sorted_cases()
    {
        var root = Path.GetFullPath(Path.Combine(global::SqlGlotSharp.Corpus.CorpusReader.RootDirectory, "v30.27.0"));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "MANIFEST.json")));
        var metadata = manifest.RootElement;
        Assert.True(metadata.GetProperty("full_suite").GetBoolean());
        Assert.True(metadata.GetProperty("suite_passed").GetBoolean());
        Assert.Equal(0, metadata.GetProperty("failures").GetInt32());
        Assert.Equal(0, metadata.GetProperty("errors").GetInt32());
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var files = metadata.GetProperty("counts").EnumerateObject().ToArray();
        var sharedCount = metadata.TryGetProperty("shared_files", out var shared) ? shared.EnumerateObject().Count() : 0;
        Assert.Equal(files.Length, Directory.GetFiles(root, "*.jsonl").Length + sharedCount);
        foreach (var file in files)
        {
            var count = 0;
            string? previous = null;
            foreach (var line in File.ReadLines(Corpus.CorpusReader.ResolveFile(root, file.Name)))
            {
                using var document = JsonDocument.Parse(line);
                var item = document.RootElement;
                var id = item.GetProperty("id").GetString()!;
                Assert.True(ids.Add(id), $"Duplicate case: {id}");
                Assert.True(previous is null || StringComparer.Ordinal.Compare(previous, id) < 0);
                Assert.StartsWith(item.GetProperty("level").GetString()! + ".", file.Name);
                var expected = item.GetProperty("expected");
                Assert.True(expected.TryGetProperty("result", out _) ^ expected.TryGetProperty("error", out _));
                Assert.NotEmpty(item.GetProperty("sources").EnumerateArray());
                previous = id;
                count++;
            }
            Assert.Equal(file.Value.GetInt32(), count);
        }
        Assert.NotEmpty(ids);
    }

    private static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.GetDirectoryName(path)!;
}
