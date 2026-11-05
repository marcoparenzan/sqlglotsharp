using System.Reflection;

namespace SqlGlotSharp;

public static partial class helper
{
    // py: sqlglot/helper.py::subclasses #8288ebc145
    public static List<Type> subclasses(Type module, IReadOnlyList<Type> classes, IReadOnlySet<Type>? exclude = null)
    {
        var exports = module == typeof(exp) ? ExpressionExports : module.GetNestedTypes(BindingFlags.Public).Select(type => (Name: type.Name, Type: type)).ToArray();
        return exports.Where(entry => classes.Any(@class => @class.IsAssignableFrom(entry.Type) ||
                module == typeof(exp) && @class == typeof(exp.Expr) && entry.Type.IsInterface) && !(exclude?.Contains(entry.Type) ?? false))
            .OrderBy(entry => entry.Name, StringComparer.Ordinal).Select(entry => entry.Type).ToList();
    }

    public static List<Type> subclasses(string module_name, Type classes, IReadOnlySet<Type>? exclude = null) =>
        subclasses(module_name, new[] { classes }, exclude);

    public static List<Type> subclasses(string module_name, IReadOnlyList<Type> classes, IReadOnlySet<Type>? exclude = null)
    {
        var module = module_name switch
        {
            "sqlglot.expressions" => typeof(exp),
            "sqlglot.errors" => typeof(errors),
            "sqlglot.helper" => typeof(helper),
            _ => throw new NotPortedException($"helper.subclasses:module:{module_name}")
        };
        return subclasses(module, classes, exclude);
    }

    // py: sqlglot/helper.py::object_to_dict #22e2361c3c
    public static PyDict<string, object?> object_to_dict(object obj, PyKwargs? kwargs = null)
    {
        ArgumentNullException.ThrowIfNull(obj);
        var result = new PyDict<string, object?>();
        var type = obj.GetType();
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            result[field.Name] = py.copy(field.GetValue(obj));
        // Writable properties represent Python instance state in the C# adaptation;
        // computed/read-only and indexed properties are not vars(obj) entries.
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            if (property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
                result[property.Name] = py.copy(py.getattr(obj, property.Name));
        if (kwargs is not null)
            foreach (var (key, value) in kwargs) result[key] = value;
        return result;
    }
}
