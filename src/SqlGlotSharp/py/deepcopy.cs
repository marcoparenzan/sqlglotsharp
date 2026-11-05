using System.Collections;

namespace SqlGlotSharp;

public static partial class py
{
    public static object? deepcopy(object? value, Dictionary<object, object>? memo = null)
    {
        if (value is null || value is string or Type or IPyImmutable || value.GetType().IsValueType) return value;
        memo ??= new(ReferenceEqualityComparer.Instance);
        if (memo.TryGetValue(value, out var existing)) return existing;
        if (value is exp.Expr expression) return expression.__deepcopy__(memo);
        if (value is PyDict<string, object?> mapping)
        {
            var result = (PyDict<string, object?>)Activator.CreateInstance(value.GetType())!;
            memo[value] = result;
            foreach (var (key, item) in mapping) result[key] = deepcopy(item, memo);
            return result;
        }
        if (value is IDictionary dictionary)
        {
            var result = (IDictionary)Activator.CreateInstance(value.GetType())!;
            memo[value] = result;
            var iterator = dictionary.GetEnumerator();
            try { while (iterator.MoveNext()) result.Add(deepcopy(iterator.Key, memo)!, deepcopy(iterator.Value, memo)); }
            finally { (iterator as IDisposable)?.Dispose(); }
            return result;
        }
        if (value is Array array && array.Rank == 1)
        {
            var result = Array.CreateInstance(value.GetType().GetElementType()!, array.Length);
            memo[value] = result;
            for (var i = 0; i < array.Length; i++) result.SetValue(deepcopy(array.GetValue(i), memo), i);
            return result;
        }
        if (value is IList list)
        {
            var result = (IList)Activator.CreateInstance(value.GetType())!;
            memo[value] = result;
            foreach (var item in list) result.Add(deepcopy(item, memo));
            return result;
        }
        throw new NotPortedException($"py.deepcopy:{value.GetType().Name}");
    }
}
