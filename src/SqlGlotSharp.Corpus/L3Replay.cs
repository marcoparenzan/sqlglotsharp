using System.Text.Json;

namespace SqlGlotSharp.Corpus;

/// <summary>
/// Bounded L3 replay of transpile and the recorder's known Validator generation
/// observations. Helper pipelines return one SQL string, not a transpile list.
/// </summary>
public static class L3Replay
{
    public static void Verify(CorpusCase item)
    {
        var p = item.Payload;
        if (p.ValueKind != JsonValueKind.Object) throw Missing("payload");
        // Do not silently ignore newly recorded execution capabilities. Metadata is inert;
        // SQL, options, diagnostics and context go unchanged to the native replay path.
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (var property in p.EnumerateObject())
        {
            if (!seen.Add(property.Name)) throw Missing($"duplicate:{property.Name}");
            if (property.Name is not ("id" or "level" or "sources" or "boundary" or "sql" or "read" or "write"
                or "options" or "runtime_context" or "logger_context" or "parser" or "generator" or "expected" or "warnings" or "pipeline"))
                throw Missing(property.Name);
        }
        if (!p.TryGetProperty("boundary", out var boundary) || boundary.ValueKind != JsonValueKind.String)
            throw Missing("boundary");
        if (p.TryGetProperty("pipeline", out var pipeline))
        {
            if (pipeline.ValueKind != JsonValueKind.String || pipeline.GetString() != "parse_one_then_sql"
                || boundary.GetString() is not ("validate_identity" or "validate_all" or "validate_transpile"))
                throw Missing("pipeline");
            // Recorder v4 helper records carry no runtime_context; L2Replay then uses the
            // Python default sqlglot.pretty=False, as for other legacy records.
            if (!p.TryGetProperty("options", out _))
                throw Missing("helper:provenance");
        }
        else if (boundary.GetString() != "transpile") throw Missing($"boundary:{boundary}");
        foreach (var name in new[] { "sql", "read", "write" })
            if (!p.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) throw Missing(name);
        // In particular, write is already resolved by recorder.transpile_case (including
        // identity=False and omitted write). Never resolve it again from read or options.
        L2Replay.Verify(item);
    }

    private static NotPortedException Missing(string capability) => new($"corpus:L3:{capability}");
}
