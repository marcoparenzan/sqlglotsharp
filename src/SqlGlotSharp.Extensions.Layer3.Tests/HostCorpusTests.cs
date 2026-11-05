using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace SqlGlotSharp.Extensions.Layer3.Tests;

/// <summary>
/// The host's own corpus (repository root corpus.json, recorded with sqlglot 30.23.0): every parse must give the same
/// tree (class, key and args by name), generated text, walk order and error class; every render the same text.
/// </summary>
public class HostCorpusTests
{
    private static string CorpusPath([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "corpus.json"));

    private static JsonNode? ToTree(object? value) => value switch
    {
        null => null,
        exp.Expr node => new JsonObject
        {
            ["class"] = node.GetType().Name,
            ["key"] = node.key,
            ["args"] = new JsonObject(node.args.Where(kv => kv.Value is not null && !(kv.Value is System.Collections.ICollection { Count: 0 })).Select(kv => KeyValuePair.Create(kv.Key, ToTree(kv.Value)))),
        },
        System.Collections.IEnumerable list when value is not string =>
            new JsonArray(list.Cast<object?>().Select(ToTree).ToArray()),
        exp.DType type => new JsonObject { ["type"] = exp.ToValue(type) },
        bool b => b,
        string s => s,
        _ => JsonValue.Create(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)),
    };

    private static JsonNode? Sorted(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, Sorted(p.Value?.DeepClone())))),
        JsonArray a => new JsonArray(a.Select(x => Sorted(x?.DeepClone())).ToArray()),
        _ => node?.DeepClone()
    };

    [Fact]
    public void Host_corpus_json_passes_for_the_layer3_dialect()
    {
        var path = CorpusPath();
        if (!File.Exists(path)) return; // the host corpus is optional in this checkout

        var corpus = JsonNode.Parse(File.ReadAllText(path))!;
        var layer3 = dialects.dialect.Dialect.get_or_raise("layer3");
        var failures = new List<string>();
        var parses = corpus["parses"]!.AsArray();
        foreach (var entry in parses)
        {
            var text = entry!["text"]!.GetValue<string>();
            if (entry["error"] is { } error)
            {
                var actual = Record.Exception(() => SqlGlot.parse_one(text, read: "layer3"));
                if (actual is null || actual.GetType().Name != error.GetValue<string>())
                    failures.Add($"[error] {text}: expected {error}, got {actual?.GetType().Name ?? "none"}");
                continue;
            }

            try
            {
                var node = SqlGlot.parse_one(text, read: "layer3");
                if (!JsonNode.DeepEquals(Sorted(ToTree(node)), Sorted(entry["tree"])))
                    failures.Add($"[tree] {text}\n  C#:  {ToTree(node)}\n  exp: {entry["tree"]}".Replace("\n", "\n"));
                var sql = layer3.generator().generate(node);
                if (sql != entry["sql"]!.GetValue<string>()) failures.Add($"[sql] {text}: '{sql}' != '{entry["sql"]}'");
                var nodes = node.walk().Select(n => n.sql("layer3")).ToArray();
                var expected = entry["nodes"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
                if (!nodes.SequenceEqual(expected)) failures.Add($"[nodes] {text}\n  C#:  {string.Join(" | ", nodes)}\n  exp: {string.Join(" | ", expected)}");
            }
            catch (Exception ex) { failures.Add($"[throw] {text}: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }
        }

        var renders = corpus["renders"]!.AsArray();
        foreach (var entry in renders)
        {
            try
            {
                var expectedTree = entry!["tree"]!;
                var node = SqlGlot.parse_one(entry["sql"]!.GetValue<string>(), read: "layer3");
                if (node.sql("layer3") != entry["sql"]!.GetValue<string>()) failures.Add($"[render] {entry["sql"]}");
                if (!JsonNode.DeepEquals(Sorted(ToTree(node)), Sorted(expectedTree))) failures.Add($"[render-tree] {entry["sql"]}");
            }
            catch (Exception ex) { failures.Add($"[render-throw] {entry!["sql"]}: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} of {parses.Count} parses + {renders.Count} renders failed:\n" + string.Join("\n", failures.Take(25)));
    }
}
