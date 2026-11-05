using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace SqlGlotSharp;

public static partial class py
{
    /// <summary>Shallow copy; an explicit public copy method has priority, as in object_to_dict.</summary>
    public static object? copy(object? value)
    {
        if (value is null or IPyImmutable) return value;
        var type = value.GetType();
        var method = type.GetMethod("copy", BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes);
        if (method is not null)
        {
            try { return method.Invoke(value, null); }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }
        if (value is string || type.IsValueType) return value;
        if (value is Array array) return array.Clone();
        if (value is ICloneable cloneable) return cloneable.Clone();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>))
        {
            var element = type.GetGenericArguments()[0];
            var constructor = type.GetConstructor([typeof(IEnumerable<>).MakeGenericType(element), typeof(IEqualityComparer<>).MakeGenericType(element)])!;
            return constructor.Invoke([value, type.GetProperty("Comparer")!.GetValue(value)]);
        }
        if (value is IList list && type.GetConstructor(Type.EmptyTypes) is not null)
        {
            var result = (IList)Activator.CreateInstance(type)!;
            foreach (var item in list) result.Add(item);
            return result;
        }
        if (value is IDictionary dictionary && type.GetConstructor(Type.EmptyTypes) is not null)
        {
            var result = (IDictionary)Activator.CreateInstance(type)!;
            var iterator = dictionary.GetEnumerator();
            try { while (iterator.MoveNext()) result.Add(iterator.Key, iterator.Value); }
            finally { (iterator as IDisposable)?.Dispose(); }
            return result;
        }
        // MemberwiseClone preserves the object's class and shared child references.
        return typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(value, null);
    }
}
