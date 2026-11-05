using System.Reflection;
using System.Runtime.ExceptionServices;

namespace SqlGlotSharp;

public static partial class py
{
    public static object? getattr(object value, string name) => GetAttribute(value, name, false, null);
    public static object? getattr(object value, string name, object? fallback) => GetAttribute(value, name, true, fallback);

    private static object? GetAttribute(object value, string name, bool hasFallback, object? fallback)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrEmpty(name);
        var type = value as Type ?? value.GetType();
        var flags = BindingFlags.Public | BindingFlags.FlattenHierarchy |
            (value is Type ? BindingFlags.Static : BindingFlags.Instance | BindingFlags.Static);
        var target = value is Type ? null : value;
        var property = type.GetProperty(name, flags);
        if (property is not null && property.GetIndexParameters().Length == 0)
        {
            try { return property.GetValue(target); }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }
        var field = type.GetField(name, flags);
        if (field is not null) return field.GetValue(target);
        if (type.GetMethods(flags).Any(m => m.Name == name))
            throw new NotPortedException("py.getattr:bound methods");
        return hasFallback ? fallback : throw new MissingMemberException(type.FullName, name);
    }
}
