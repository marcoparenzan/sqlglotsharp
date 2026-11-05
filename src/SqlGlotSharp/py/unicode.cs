using System.Text;

namespace SqlGlotSharp;

public static partial class py
{
    public static string upper(string text) => ChangeCase(text, false);
    public static string lower(string text) => ChangeCase(text, true);
    public static bool isupper(string text) => CasePredicate(text, UnicodeCaseData.UpperChars);
    public static bool islower(string text) => CasePredicate(text, UnicodeCaseData.LowerChars);

    private static List<(int Code, string Text)> Scalars(string text)
    {
        var points = new List<(int, string)>();
        for (var i = 0; i < text.Length; i++)
        {
            var pair = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
            points.Add((pair ? char.ConvertToUtf32(text, i) : text[i], text.Substring(i, pair ? 2 : 1)));
            if (pair) i++;
        }
        return points;
    }
    private static string ChangeCase(string text, bool lowercase)
    {
        var points = Scalars(text);
        var output = new StringBuilder();
        var mapping = lowercase ? UnicodeCaseData.Lower : UnicodeCaseData.Upper;
        for (var i = 0; i < points.Count; i++)
        {
            var (code, original) = points[i];
            if (lowercase && code == 0x03A3 && FinalSigma(points, i)) output.Append('ς');
            else output.Append(mapping.TryGetValue(code, out var changed) ? changed : original);
        }
        return output.ToString();
    }
    private static bool FinalSigma(List<(int Code, string Text)> points, int index)
    {
        var before = index - 1;
        while (before >= 0 && InRanges(points[before].Code, UnicodeCaseData.Ignored)) before--;
        if (before < 0 || !InRanges(points[before].Code, UnicodeCaseData.Cased)) return false;
        var after = index + 1;
        while (after < points.Count && InRanges(points[after].Code, UnicodeCaseData.Ignored)) after++;
        return after == points.Count || !InRanges(points[after].Code, UnicodeCaseData.Cased);
    }
    private static bool CasePredicate(string text, (int Start, int End)[] expected)
    {
        var found = false;
        foreach (var (code, _) in Scalars(text))
        {
            if (!InRanges(code, UnicodeCaseData.Cased)) continue;
            if (!InRanges(code, expected)) return false;
            found = true;
        }
        return found;
    }
    private static bool InRanges(int code, (int Start, int End)[] ranges)
    {
        var lo = 0;
        var hi = ranges.Length - 1;
        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (code < ranges[mid].Start) hi = mid - 1;
            else if (code > ranges[mid].End) lo = mid + 1;
            else return true;
        }
        return false;
    }
    public static bool safe_identifier(string text)
    {
        var points = Scalars(text);
        if (points.Count == 0) return false;
        var first = points[0].Code;
        if (!(first == '_' || first is >= 'a' and <= 'z' or >= 'A' and <= 'Z')) return false;
        // Python '$' accepts one final newline before the end of the input.
        var count = points.Count;
        if (points[^1].Code == '\n') count--;
        for (var i = 1; i < count; i++)
            if (!InRanges(points[i].Code, UnicodeCaseData.WordChars)) return false;
        return true;
    }
}
