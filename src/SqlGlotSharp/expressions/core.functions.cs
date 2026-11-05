namespace SqlGlotSharp;

public static partial class exp
{
    public abstract partial class Expr
    {
        #region trait Func
        // py: sqlglot/expressions/core.py::Func.from_arg_list #b4aa1f8c70
        public virtual Expr from_arg_list(IReadOnlyList<object?> args)
        {
            py.require_trait<Func>(this);
            var keys = arg_types.Keys.ToArray();
            var arguments = new PyDict<string, object?>();
            if (is_var_len_args)
            {
                var index = System.Array.IndexOf(keys, var_len_arg_key);
                if (index < 0) throw new InvalidOperationException("Variable argument key is not in arg_types");
                for (var i = 0; i < Math.Min(index, args.Count); i++) arguments[keys[i]] = args[i];
                arguments[var_len_arg_key] = py.slice(args, index);
            }
            else for (var i = 0; i < Math.Min(keys.Length, args.Count); i++) arguments[keys[i]] = args[i];
            return (Expr)Activator.CreateInstance(GetType(), [arguments])!;
        }
        // py: sqlglot/expressions/core.py::Func.sql_names #49fa278d4e
        public virtual List<string> sql_names()
        {
            py.require_trait<Func>(this);
            return _sql_names.Count == 0 ? [helper.camel_to_snake_case(GetType().Name)] : _sql_names;
        }
        // py: sqlglot/expressions/core.py::Func.sql_name #0ca2711fcf
        public virtual string sql_name()
        {
            var names = sql_names();
            return names.Count != 0 ? names[0] : throw new InvalidOperationException($"Expected non-empty sql_names for {GetType().Name}");
        }
        // py: sqlglot/expressions/core.py::Func.default_parser_mappings #5424702d65
        public virtual PyDict<string, System.Func<IReadOnlyList<object?>, Expr>> default_parser_mappings()
        {
            var result = new PyDict<string, System.Func<IReadOnlyList<object?>, Expr>>();
            foreach (var name in sql_names()) result[name] = from_arg_list;
            return result;
        }
        #endregion
    }

    private static readonly Lazy<List<Type>> FunctionClasses = new(() => helper.subclasses("sqlglot.expressions", typeof(Func),
        new HashSet<Type> { typeof(Func), typeof(AggFunc), typeof(Anonymous) }));
    // py: sqlglot/expressions/__init__.py::ALL_FUNCTIONS #821befd26c
    public static List<Type> ALL_FUNCTIONS => FunctionClasses.Value;
    private static readonly Lazy<PyDict<string, Type>> FunctionsByName = new(() =>
    {
        var result = new PyDict<string, Type>();
        foreach (var type in ALL_FUNCTIONS)
        {
            var function = (Expr)Activator.CreateInstance(type, [new PyDict<string, object?>()])!;
            foreach (var name in function.sql_names()) result[name] = type;
        }
        return result;
    });
    // py: sqlglot/expressions/__init__.py::FUNCTION_BY_NAME #fd47b1ff9b
    public static PyDict<string, Type> FUNCTION_BY_NAME => FunctionsByName.Value;
}
