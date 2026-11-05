using System.Collections;

namespace SqlGlotSharp;

// Local function algorithms from sqlglot/generator.py, in upstream source order.
// Full local ports, NOT a complete generation pipeline: children call real sql dispatch,
// whose missing handlers/transforms still fail. generate/preprocess and Property are unported.
// Existing normalize_func, indent, too_wide and expressions retain their own source anchors.
public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.anonymous_sql #a967ccd10a
        public virtual string anonymous_sql(exp.Anonymous expression)
        {
            // Qualified function names may be case-sensitive.
            var parent = expression.parent;
            var is_qualified = parent is exp.Dot && ReferenceEquals(expression, parent.expression);
            return func(sql(expression, "this"), expression.expressions.ToArray(), prefix: "(", normalize: !is_qualified);
        }

        // py: sqlglot/generator.py::Generator.function_fallback_sql #9694ed65aa
        public virtual string function_fallback_sql(exp.Expr expression)
        {
            var args = new List<object?>();
            foreach (var key in expression.arg_types.Keys)
            {
                var arg_value = expression.args.get(key);
                if (arg_value is IList list)
                {
                    foreach (var value in list) args.Add(value);
                }
                else if (arg_value is not null) args.Add(arg_value);
            }
            return func(function_name(expression), args.ToArray());
        }

        // py: sqlglot/generator.py::Generator.function_name #2fa3cb31c9
        public virtual string function_name(exp.Expr expression)
        {
            var name_key = dialect.ORIGINAL_NAME_META_KEY;
            var name = py.truthy(name_key) ? expression.meta_get((string)name_key!) : null;
            return py.truthy(name) ? (string)name! : expression.sql_name();
        }

        // C# positional-varargs adapters; keyword options live on the array overloads.
        public virtual string func(string name, params object?[] args) => func(name, args, prefix: "(");

        // py: sqlglot/generator.py::Generator.func #0d3ebff6ff
        public virtual string func(string name, object?[] args, string prefix, string suffix = ")", bool normalize = true)
        {
            name = normalize ? normalize_func(name) : name;
            return $"{name}{prefix}{format_args(args)}{suffix}";
        }

        public virtual string format_args(params object?[] args) => format_args(args, sep: ", ");

        // py: sqlglot/generator.py::Generator.format_args #2b877febb5
        public virtual string format_args(object?[] args, string sep)
        {
            var arg_sqls = args.Where(arg => arg is not null and not bool).Select(arg => sql(arg)).ToArray();
            if (py.truthy(pretty) && too_wide(arg_sqls))
                return indent("\n" + string.Join(Strip(sep) + "\n", arg_sqls) + "\n", skip_first: true, skip_last: true);
            return string.Join(sep, arg_sqls);
        }
    }
}
