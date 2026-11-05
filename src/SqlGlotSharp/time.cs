namespace SqlGlotSharp;

public static partial class time
{
    // py: sqlglot/time.py::format_time #575d641885
    public static string? format_time(string @string, IReadOnlyDictionary<string, string> mapping, PyDict<object, object?>? trie = null)
    {
        if (@string.Length == 0) return null;
        var start = 0;
        var end = 1;
        var size = py.len(@string);
        trie = trie is { Count: > 0 } ? trie : global::SqlGlotSharp.trie.new_trie(mapping.Keys);
        var current = trie;
        var chunks = new List<string>();
        string? sym = null;
        while (end <= size)
        {
            var chars = py.slice(@string, start, end);
            var (result, subtrie) = global::SqlGlotSharp.trie.in_trie(current, py.slice(chars, -1));
            current = subtrie;
            if (result == global::SqlGlotSharp.trie.TrieResult.FAILED)
            {
                if (!string.IsNullOrEmpty(sym)) { end--; chars = sym; sym = null; }
                else { chars = py.slice(chars, 0, 1); end = start + 1; }
                start += py.len(chars);
                chunks.Add(chars);
                current = trie;
            }
            else if (result == global::SqlGlotSharp.trie.TrieResult.EXISTS) sym = chars;
            end++;
            if (result != global::SqlGlotSharp.trie.TrieResult.FAILED && end > size) chunks.Add(chars);
        }
        return string.Concat(chunks.Select(chars => mapping.TryGetValue(chars, out var value) ? value : chars));
    }

    // py: sqlglot/time.py::subsecond_precision #cf9a6f05aa
    public static int subsecond_precision(string timestamp_literal)
    {
        var component = helper.IsoTimeComponent(timestamp_literal);
        if (component is null) return 0;
        var dot = Math.Max(component.IndexOf('.'), component.IndexOf(','));
        if (dot < 0) return 0;
        var fraction = component[(dot + 1)..].PadRight(6, '0')[..6].TrimEnd('0');
        return fraction.Length > 3 ? 6 : fraction.Length > 0 ? 3 : 0;
    }
}
