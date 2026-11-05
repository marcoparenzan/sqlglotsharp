using System.Text;

namespace SqlGlotSharp;

public static partial class py
{
    public static T and<T>(T a, T b) => truthy(a) ? b : a;
    public static T or<T>(T a, Func<T> b) => truthy(a) ? a : b();
    public static T and<T>(T a, Func<T> b) => truthy(a) ? b() : a;

    public static IEnumerable<(TLeft, TRight)> zip<TLeft, TRight>(IEnumerable<TLeft> left, IEnumerable<TRight> right)
    {
        using var a = left.GetEnumerator();
        using var b = right.GetEnumerator();
        while (a.MoveNext() && b.MoveNext()) yield return (a.Current, b.Current);
    }

    public static List<T> sorted<T, TKey>(IEnumerable<T> values, Func<T, TKey> key, bool reverse = false,
        IComparer<TKey>? comparer = null) => (reverse ? values.OrderByDescending(key, comparer) : values.OrderBy(key, comparer)).ToList();

    public static List<T> sorted<T>(IEnumerable<T> values, bool reverse = false, IComparer<T>? comparer = null) =>
        sorted(values, v => v, reverse, comparer);

    public static List<T> slice<T>(IReadOnlyList<T> values, int? start = null, int? stop = null, int step = 1)
    {
        List<T> result = [];
        foreach (var index in SliceIndexes(values.Count, start, stop, step)) result.Add(values[index]);
        return result;
    }

    // Python strings slice by Unicode code points, not UTF-16 code units.
    public static string slice(string value, int? start = null, int? stop = null, int step = 1)
    {
        var runes = value.EnumerateRunes().ToArray();
        var output = new StringBuilder();
        foreach (var index in SliceIndexes(runes.Length, start, stop, step)) output.Append(runes[index].ToString());
        return output.ToString();
    }

    private static IEnumerable<int> SliceIndexes(int count, int? start, int? stop, int step)
    {
        if (step == 0) throw new ArgumentException("slice step cannot be zero", nameof(step));
        long Normalize(int value)
        {
            var index = (long)value;
            if (index < 0) index += count;
            return step > 0 ? Math.Clamp(index, 0, count) : Math.Clamp(index, -1, count - 1L);
        }
        var first = start.HasValue ? Normalize(start.Value) : step > 0 ? 0 : count - 1L;
        var last = stop.HasValue ? Normalize(stop.Value) : step > 0 ? count : -1L;
        for (var index = first; step > 0 ? index < last : index > last; index += step) yield return (int)index;
    }
}
