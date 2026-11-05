using System.Collections;

namespace SqlGlotSharp;

public static partial class py
{
    public static bool truthy(object? value) => value switch
    {
        null => false,
        IPyTruthiness custom => custom.__bool__(),
        bool b => b,
        System.Numerics.BigInteger integer => !integer.IsZero,
        string s => s.Length != 0,
        ICollection collection => collection.Count != 0,
        byte n => n != 0,
        sbyte n => n != 0,
        short n => n != 0,
        ushort n => n != 0,
        int n => n != 0,
        uint n => n != 0,
        long n => n != 0,
        ulong n => n != 0,
        float n => n != 0,
        double n => n != 0,
        decimal n => n != 0,
        System.Runtime.CompilerServices.ITuple tuple => tuple.Length != 0,
        _ when value.GetType().GetInterfaces().Any(i => i.IsGenericType &&
            (i.GetGenericTypeDefinition() == typeof(ICollection<>) || i.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>))) => len(value) != 0,
        _ => true
    };

    public static T or<T>(T a, T b) => truthy(a) ? a : b;
    public static IEnumerable<(int, T)> enumerate<T>(IEnumerable<T> values, int start = 0)
    {
        var i = start;
        foreach (var value in values) yield return (i++, value);
    }
    public static List<T> slice<T>(List<T> values, int start)
    {
        start = start < 0 ? Math.Max(0, values.Count + start) : Math.Min(start, values.Count);
        return values.GetRange(start, values.Count - start);
    }
    public static void require_trait<T>(exp.Expr value)
    {
        if (value is not T) throw new InvalidOperationException($"Required trait: {typeof(T).Name}");
    }
}
