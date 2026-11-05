using System.Collections.Generic;

namespace SqlGlotSharp;

/// <summary>Insertion-ordered mapping with Python lookup, removal and shallow-copy operations.</summary>
public interface IPyMapping
{
    IEnumerable<(object? Key, object? Value)> repr_items();
}

public class PyDict<K, V> : OrderedDictionary<K, V>, IPyMapping where K : notnull
{
    public V? get(K key) => TryGetValue(key, out var value) ? value : default;
    public V get(K key, V fallback) => TryGetValue(key, out var value) ? value : fallback;
    public V pop(K key)
    {
        if (!TryGetValue(key, out var value)) throw new KeyNotFoundException();
        Remove(key);
        return value;
    }
    public V? pop(K key, V? fallback)
    {
        if (!TryGetValue(key, out var value)) return fallback;
        Remove(key);
        return value;
    }
    public V setdefault(K key, V fallback)
    {
        if (TryGetValue(key, out var value)) return value;
        Add(key, fallback);
        return fallback;
    }
    public IEnumerable<KeyValuePair<K, V>> items() => this;
    IEnumerable<(object? Key, object? Value)> IPyMapping.repr_items() => this.Select(pair => ((object?)pair.Key, (object?)pair.Value));
    public PyDict<K, V> copy()
    {
        var result = new PyDict<K, V>();
        foreach (var (key, value) in this) result.Add(key, value);
        return result;
    }
}
