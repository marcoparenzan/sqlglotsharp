using System.Collections;
using System.Runtime.CompilerServices;

namespace SqlGlotSharp;

public static partial class helper
{
    // py: sqlglot/helper.py::flatten #b35d17f4a8
    public static IEnumerable<object?> flatten(IEnumerable values)
    {
        var elements = values is IPyMapping mappingValues ? mappingValues.repr_items().Select(p => p.Key) :
            values is IDictionary dictionaryValues ? dictionaryValues.Keys : values;
        foreach (var value in elements)
        {
            if (is_iterable(value))
            {
                var iterable = value is ITuple tuple ? Enumerable.Range(0, tuple.Length).Select(i => tuple[i]) :
                    value is IPyMapping mapping ? mapping.repr_items().Select(p => p.Key) :
                    value is IDictionary dict ? dict.Keys.Cast<object?>() : ((IEnumerable)value!).Cast<object?>();
                foreach (var item in flatten(iterable)) yield return item;
            }
            else yield return value;
        }
    }

    // py: sqlglot/helper.py::dict_depth #1f105a286e
    public static int dict_depth(object? d)
    {
        IEnumerable<object?> values;
        if (d is IPyMapping mapping) values = mapping.repr_items().Select(p => p.Value);
        else if (d is IDictionary dictionary) values = dictionary.Values.Cast<object?>();
        else return 0;
        using var iterator = values.GetEnumerator();
        return iterator.MoveNext() ? 1 + dict_depth(iterator.Current) : 1;
    }

    // py: sqlglot/helper.py::first #caae4c9325
    public static T first<T>(IEnumerable<T> it)
    {
        using var iterator = it.GetEnumerator();
        if (!iterator.MoveNext()) throw new InvalidOperationException("StopIteration");
        return iterator.Current;
    }

    // py: sqlglot/helper.py::to_bool #6c4635e9fc
    public static object? to_bool(object? value)
    {
        if (value is null or bool) return value;
        if (value is not string text) throw new ArgumentException("to_bool requires string, bool or null", nameof(value));
        return text.ToLowerInvariant() switch { "true" or "1" => true, "false" or "0" => false, _ => value };
    }

    // py: sqlglot/helper.py::merge_ranges #e3234fa4cd
    public static List<(T low, T high)> merge_ranges<T>(IReadOnlyList<(T low, T high)> ranges, IComparer<T>? comparer = null)
    {
        if (ranges.Count == 0) return [];
        comparer ??= typeof(T) == typeof(string) ? (IComparer<T>)(object)StringComparer.Ordinal : Comparer<T>.Default;
        var sorted = ranges.OrderBy(r => r.low, comparer).ThenBy(r => r.high, comparer).ToArray();
        List<(T low, T high)> merged = [sorted[0]];
        foreach (var (start, end) in sorted.Skip(1))
        {
            var (last_start, last_end) = merged[^1];
            if (comparer.Compare(start, last_end) <= 0)
                merged[^1] = (last_start, comparer.Compare(last_end, end) >= 0 ? last_end : end);
            else merged.Add((start, end));
        }
        return merged;
    }

    // py: sqlglot/helper.py::DATE_UNITS #b85f5cea65
    public static readonly HashSet<string> DATE_UNITS = ["day", "week", "month", "quarter", "year", "year_month"];
    // py: sqlglot/helper.py::is_date_unit #03ee8cc3fb
    public static bool is_date_unit(exp.Expr? expression) => expression is not null && DATE_UNITS.Contains(expression.name.ToLowerInvariant());
}
