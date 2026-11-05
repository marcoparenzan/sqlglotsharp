using System.Collections;
using System.Text;

namespace SqlGlotSharp;

/// <summary>Python keyword arguments retain explicit null and insertion order.</summary>
public sealed class PyKwargs : PyDict<string, object?>
{
    public PyKwargs() { }
    public PyKwargs(IEnumerable<KeyValuePair<string, object?>> values)
    {
        foreach (var (key, value) in values) this[key] = value;
    }
}

public static partial class py
{
    public static PyDict<K, V> merge<K, V>(params IEnumerable<KeyValuePair<K, V>>[] mappings) where K : notnull
    {
        var result = new PyDict<K, V>();
        foreach (var mapping in mappings)
            foreach (var (key, value) in mapping) result[key] = value;
        return result;
    }

    public static int len(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value is string text) return text.EnumerateRunes().Count();
        if (value is ICollection collection) return collection.Count;
        // Generic-only mappings/sets expose Count through collection interfaces.
        var type = value.GetType();
        var collectionInterface = type.GetInterfaces().FirstOrDefault(i => i.IsGenericType &&
            (i.GetGenericTypeDefinition() == typeof(ICollection<>) ||
             i.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>)));
        if (collectionInterface is not null)
            return (int)collectionInterface.GetProperty("Count")!.GetValue(value)!;
        throw new ArgumentException($"Object of type {type.Name} has no len()", nameof(value));
    }
}
