using System.Text.Json;

namespace SqlGlotSharp.Corpus;

public sealed record CorpusCase(string Id, string Level, string Dialect, string[] Sources, JsonElement Payload);

public static class CorpusReader
{
    // Development-only comparison data, intentionally outside all agent workspaces.
    public const string RootDirectory = @"D:\clones\corpus";

    public static string ResolveFile(string directory, string filename)
    {
        if (Path.GetFileName(filename) != filename || !filename.EndsWith(".jsonl", StringComparison.Ordinal))
            throw new InvalidDataException($"Invalid corpus filename: {filename}");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "MANIFEST.json")));
        if (manifest.RootElement.TryGetProperty("shared_files", out var shared) && shared.TryGetProperty(filename, out var entry))
        {
            var target = entry.GetString() ?? throw new InvalidDataException("Missing shared filename");
            if (Path.GetFileName(target) != target || !target.EndsWith(".jsonl", StringComparison.Ordinal))
                throw new InvalidDataException($"Invalid shared filename: {target}");
            return Path.Combine(Path.GetFullPath(Path.Combine(directory, "..")), target);
        }
        return Path.Combine(directory, filename);
    }

    public static IEnumerable<CorpusCase> Read(string directory)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "MANIFEST.json")));
        foreach (var entry in manifest.RootElement.GetProperty("counts").EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            if (Path.GetFileName(entry.Name) != entry.Name || !entry.Name.EndsWith(".jsonl", StringComparison.Ordinal))
                throw new InvalidDataException($"Invalid corpus filename: {entry.Name}");
            var parts = entry.Name.Split('.');
            var count = 0;
            foreach (var line in File.ReadLines(ResolveFile(directory, entry.Name)))
            {
                using var document = JsonDocument.Parse(line);
                var payload = document.RootElement;
                var level = payload.GetProperty("level").GetString()!;
                if (level != parts[0]) throw new InvalidDataException($"Level mismatch in {entry.Name}");
                count++;
                yield return new(payload.GetProperty("id").GetString()!, level, parts[1],
                    payload.GetProperty("sources").EnumerateArray().Select(s => s.GetString()!).ToArray(), payload.Clone());
            }
            if (count != entry.Value.GetInt32()) throw new InvalidDataException($"Count mismatch in {entry.Name}");
        }
    }
}
