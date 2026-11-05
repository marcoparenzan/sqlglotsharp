namespace SqlGlotSharp;

public static partial class helper
{
    // py: sqlglot/helper.py::while_changing #11ba7a5d04
    public static E while_changing<E>(E expression, Func<E, E> func) where E : notnull
    {
        while (true)
        {
            var start_hash = expression.GetHashCode();
            expression = func(expression);
            var end_hash = expression.GetHashCode();
            if (start_hash == end_hash) break;
        }
        return expression;
    }

    // py: sqlglot/helper.py::tsort #081a4c0dd2
    public static List<T> tsort<T>(PyDict<T, HashSet<T>> dag, IComparer<T>? comparer = null) where T : notnull
    {
        List<T> result = [];
        foreach (var (_, deps) in dag.ToArray())
            foreach (var dep in deps)
                if (!dag.ContainsKey(dep)) dag[dep] = [];
        comparer ??= typeof(T) == typeof(string) ? (IComparer<T>)(object)StringComparer.Ordinal : Comparer<T>.Default;
        while (dag.Count != 0)
        {
            var current = dag.Where(pair => pair.Value.Count == 0).Select(pair => pair.Key).ToHashSet();
            if (current.Count == 0) throw new ArgumentException("Cycle error");
            foreach (var node in current) dag.Remove(node);
            foreach (var deps in dag.Values) deps.ExceptWith(current);
            result.AddRange(current.OrderBy(node => node, comparer));
        }
        return result;
    }

    // py: sqlglot/helper.py::is_int #a44ce650ce
    public static bool is_int(string text) => is_type(text, py.@int);
    // py: sqlglot/helper.py::is_float #ace622536d
    public static bool is_float(string text) => is_type(text, py.@float);
    // py: sqlglot/helper.py::is_type #1c01540001
    public static bool is_type<T>(string text, Func<object, T> target_type)
    {
        try { target_type(text); return true; }
        catch (FormatException) { return false; }
    }
}
