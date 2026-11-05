namespace SqlGlotSharp;

public static partial class helper
{
    // py: sqlglot/helper.py::suggest_closest_match_and_fail #082f5efd7b
    public static void suggest_closest_match_and_fail(string kind, string word, IEnumerable<string> possibilities)
    {
        var similar = py.difflib.closest(word, possibilities) ?? "";
        if (similar.Length != 0) similar = $" Did you mean {similar}?";
        throw new ArgumentException($"Unknown {kind} '{word}'.{similar}");
    }
}
