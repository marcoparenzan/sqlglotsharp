using System.Text;

namespace SqlGlotSharp;

public static partial class trie
{
    // Python key = Sequence[Hashable] maps to IReadOnlyList<object> in the overloads below.
    // py: sqlglot/trie.py::key #26e84e2ce2

    // py: sqlglot/trie.py::new_trie #054aa0316e
    public static PyDict<object, object?> new_trie(IEnumerable<IEnumerable<object>> keywords, PyDict<object, object?>? trie = null)
    {
        trie ??= new PyDict<object, object?>();
        foreach (var key in keywords)
        {
            var current = trie;
            foreach (var character in key)
                current = (PyDict<object, object?>)current.setdefault(character, new PyDict<object, object?>())!;
            current[0] = true;
        }
        return trie;
    }
    public static PyDict<object, object?> new_trie(IEnumerable<string> keywords, PyDict<object, object?>? trie = null) =>
        new_trie(keywords.Select(word => word.EnumerateRunes().Select(r => (object)r.ToString())), trie);

    /// <summary>Deep copy of a trie (so a dialect can extend the base trie without mutating it).</summary>
    public static PyDict<object, object?> copy(PyDict<object, object?> trie)
    {
        var result = new PyDict<object, object?>();
        foreach (var (key, value) in trie)
            result[key] = value is PyDict<object, object?> child ? copy(child) : value;
        return result;
    }

    // py: sqlglot/trie.py::in_trie #fe12ffe1b2
    public static (TrieResult result, PyDict<object, object?> subtrie) in_trie(PyDict<object, object?> trie, IReadOnlyList<object> key)
    {
        if (key.Count == 0) return (TrieResult.FAILED, trie);
        var current = trie;
        foreach (var character in key)
        {
            if (!current.TryGetValue(character, out var child)) return (TrieResult.FAILED, current);
            current = (PyDict<object, object?>)child!;
        }
        return ((current.ContainsKey(0) || current.ContainsKey(0L)) ? TrieResult.EXISTS : TrieResult.PREFIX, current);
    }
    public static (TrieResult result, PyDict<object, object?> subtrie) in_trie(PyDict<object, object?> trie, string key) =>
        in_trie(trie, key.EnumerateRunes().Select(r => (object)r.ToString()).ToArray());
}
