using System.Collections;

namespace SqlGlotSharp;

public static partial class helper
{
    // py: sqlglot/helper.py::SingleValuedMapping #124c967a39
    public sealed class SingleValuedMapping<K, V> : IReadOnlyDictionary<K, V>, IPyMapping where K : notnull
    {
        private readonly IReadOnlySet<K> _keys;
        private readonly V _value;

        // py: sqlglot/helper.py::SingleValuedMapping.__init__ #b9947b6645
        public SingleValuedMapping(IEnumerable<K> keys, V value)
        {
            _keys = keys as IReadOnlySet<K> ?? keys.ToHashSet();
            _value = value;
        }

        // py: sqlglot/helper.py::SingleValuedMapping.__getitem__ #820884d3fe
        public V this[K key] => _keys.Contains(key) ? _value : throw new KeyNotFoundException(py.repr(key));
        // py: sqlglot/helper.py::SingleValuedMapping.__len__ #1e1fa3180b
        public int Count => _keys.Count;
        public IEnumerable<K> Keys => _keys;
        public IEnumerable<V> Values => _keys.Select(_ => _value);
        public bool ContainsKey(K key) => _keys.Contains(key);
        public bool TryGetValue(K key, out V value)
        {
            if (_keys.Contains(key)) { value = _value; return true; }
            value = default!;
            return false;
        }

        // py: sqlglot/helper.py::SingleValuedMapping.__iter__ #462632cb08
        public IEnumerable<K> keys() => _keys;
        public IEnumerator<KeyValuePair<K, V>> GetEnumerator()
        {
            foreach (var key in _keys) yield return new(key, _value);
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        IEnumerable<(object? Key, object? Value)> IPyMapping.repr_items() => _keys.Select(key => ((object?)key, (object?)_value));
    }
}
