using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using SqlGlotSharp.Corpus;

namespace SqlGlotSharp.Tests;

public class SharedCorpusTests
{
    [Fact]
    public void Both_versions_share_the_same_unchanged_L0_file()
    {
        var root = Path.GetFullPath(Path.Combine(global::SqlGlotSharp.Corpus.CorpusReader.RootDirectory));
        var first = CorpusReader.ResolveFile(Path.Combine(root, "v30.27.0"), "L0.base.jsonl");
        var second = CorpusReader.ResolveFile(Path.Combine(root, "v30.27.0-logger-context-full"), "L0.base.jsonl");
        Assert.Equal(first, second);
        Assert.Equal(Path.Combine(root, "L0.base.jsonl"), first);
        Assert.False(File.Exists(Path.Combine(root, "v30.27.0", "L0.base.jsonl")));
        Assert.False(File.Exists(Path.Combine(root, "v30.27.0-logger-context-full", "L0.base.jsonl")));
        using var stream = File.OpenRead(first);
        Assert.Equal("a571dec0eecd4ada392a0fec4ec54fdd313cd76a09959c3f2b743170cff1b977", Convert.ToHexStringLower(SHA256.HashData(stream)));
    }

    [Fact]
    public void Shared_filenames_cannot_escape_the_corpus_root()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sqlglot-shared-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "MANIFEST.json"), JsonSerializer.Serialize(new {
                shared_files = new Dictionary<string, string> { ["L0.base.jsonl"] = "../outside.jsonl" }
            }));
            Assert.Throws<InvalidDataException>(() => CorpusReader.ResolveFile(directory, "L0.base.jsonl"));
            Assert.Throws<InvalidDataException>(() => CorpusReader.ResolveFile(directory, "../outside.jsonl"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string SourceDirectory([CallerFilePath] string source = "") => Path.GetDirectoryName(source)!;
}
