using System.Collections;
using System.Runtime.CompilerServices;

namespace SqlGlotSharp;

public static partial class helper
{
    // py: sqlglot/helper.py::ensure_list #958e605eaf
    public static IList ensure_list(object? value)
    {
        if (value is null) return new List<object?>();
        if (value is IList list && value is not Array) return list;
        if (value is Array array) return array.Cast<object?>().ToList();
        if (value is ITuple tuple) return Enumerable.Range(0, tuple.Length).Select(i => tuple[i]).ToList();
        return new List<object?> { value };
    }

    // py: sqlglot/helper.py::ensure_collection #e6dff5da68
    public static object ensure_collection(object? value)
    {
        if (value is null) return new List<object?>();
        if (value is not string && value is not byte[] && (value is ICollection ||
            value.GetType().GetInterfaces().Any(i => i.IsGenericType &&
                (i.GetGenericTypeDefinition() == typeof(ICollection<>) || i.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>)))))
            return value;
        if (value is ITuple) return value;
        return new List<object?> { value };
    }

    // py: sqlglot/helper.py::csv #6319efedd9
    public static string csv(IEnumerable<string?> args, string sep = ", ") => string.Join(sep, args.Where(arg => !string.IsNullOrEmpty(arg)));
    public static string csv(params string?[] args) => csv(args.AsEnumerable());

    // py: sqlglot/helper.py::camel_to_snake_case #61a03f8de8
    public static string camel_to_snake_case(string name) => py.re.sub("(?<!^)(?=[A-Z])", "_", name).ToUpperInvariant();

    // py: sqlglot/helper.py::find_new_name #803f3815b8
    public static string find_new_name(IReadOnlyCollection<string> taken, string @base)
    {
        if (!taken.Contains(@base)) return @base;
        var i = 2;
        var name = $"{@base}_{i}";
        while (taken.Contains(name)) name = $"{@base}_{++i}";
        return name;
    }

    // py: sqlglot/helper.py::name_sequence #4bd09cafec
    public static Func<string> name_sequence(string prefix)
    {
        long sequence = 0;
        return () => prefix + (sequence++).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // py: sqlglot/helper.py::split_num_words #951a9fa644
    public static List<string?> split_num_words(string value, string sep, int min_num_words, bool fill_from_start = true)
    {
        if (sep.Length == 0) throw new ArgumentException("empty separator", nameof(sep));
        var words = value.Split(sep, StringSplitOptions.None).Cast<string?>().ToList();
        var padding = Enumerable.Repeat<string?>(null, Math.Max(0, min_num_words - words.Count));
        return fill_from_start ? padding.Concat(words).ToList() : words.Concat(padding).ToList();
    }

    // py: sqlglot/helper.py::is_iterable #79ffa47180
    public static bool is_iterable(object? value) => value is not exp.Expr && (value is ITuple || value is IEnumerable and not string and not byte[]);
}
