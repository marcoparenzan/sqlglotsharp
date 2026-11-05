using System.Globalization;

namespace SqlGlotSharp;

// JSONPath rendering of the base generator. Dialects override single parts through TRANSFORMS
// (the dispatch table), exactly as upstream resolves them with self.TRANSFORMS.get(cls).
public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/jsonpath.py::JSON_PATH_PART_TRANSFORMS #cd54b92f2c
        internal static readonly Dictionary<Type, Func<Generator, exp.Expr, string>> JSON_PATH_PART_TRANSFORMS = new()
        {
            [typeof(exp.JSONPathFilter)] = (_, e) => $"?{PyStr(e.@this)}",
            [typeof(exp.JSONPathKey)] = (self, e) => self._jsonpathkey_sql((exp.JSONPathKey)e),
            [typeof(exp.JSONPathRecursive)] = (self, e) => $"..{(py.truthy(e.@this) ? self.PartText(e.@this) : "")}",
            [typeof(exp.JSONPathRoot)] = (_, _) => "$",
            [typeof(exp.JSONPathScript)] = (_, e) => $"({PyStr(e.@this)}",
            [typeof(exp.JSONPathSelector)] = (self, e) => $"[{self.json_path_part(e.@this)}]",
            [typeof(exp.JSONPathSlice)] = (self, e) => string.Join(":",
                new[] { e.args.get("start"), e.args.get("end"), e.args.get("step") }
                    .Where(p => p is not null)
                    .Select(p => p is false ? "" : self.json_path_part(p))),
            [typeof(exp.JSONPathSubscript)] = (self, e) => self._jsonpathsubscript_sql((exp.JSONPathSubscript)e),
            [typeof(exp.JSONPathUnion)] = (self, e) => $"[{string.Join(",", e.expressions.Select(p => self.json_path_part(p)))}]",
            [typeof(exp.JSONPathWildcard)] = (_, _) => "*",
        };

        // py: sqlglot/generator.py::Generator.jsonpath_sql #d4a50539d1
        public virtual string jsonpath_sql(exp.JSONPath expression)
        {
            var path = expressions(expression, sep: "", flat: true).TrimStart('.');

            if (QUOTE_JSON_PATH)
            {
                path = escape_str(path);
                path = $"{dialect.QUOTE_START}{path}{dialect.QUOTE_END}";
            }

            return path;
        }

        // py: sqlglot/generator.py::Generator.json_path_part #812e9ef852
        public virtual string json_path_part(object? expression)
        {
            if (expression is exp.JSONPathPart part)
            {
                if (!_dispatch.TryGetValue(part.GetType(), out var transform))
                {
                    unsupported($"Unsupported JSONPathPart type {part.GetType().Name}");
                    return "";
                }

                return transform(this, part);
            }

            if (expression is int or long or System.Numerics.BigInteger)
                return Convert.ToString(expression, CultureInfo.InvariantCulture)!;

            // Upstream would fail with AttributeError/TypeError on any other payload (None, float, node);
            // stay explicit instead of guessing a rendering.
            if (expression is not string text)
                throw new NotPortedException("sqlglot/generator.py::Generator.json_path_part payload (expected integer, string or JSONPathPart)");
            string escaped;
            if (_quote_json_path_key_using_brackets && JSON_PATH_SINGLE_QUOTE_ESCAPE)
            {
                escaped = text.Replace("'", "\\'");
                escaped = $"'{escaped}'";
            }
            else
            {
                escaped = text.Replace("\"", "\\\"");
                escaped = $"\"{escaped}\"";
            }

            return escaped;
        }

        // py: sqlglot/generator.py::Generator._jsonpathkey_sql #409d2b7312
        internal string _jsonpathkey_sql(exp.JSONPathKey expression)
        {
            var @this = expression.@this;
            if (@this is exp.JSONPathWildcard wildcard)
            {
                var wild = json_path_part(wildcard);
                return wild.Length != 0 ? $".{wild}" : "";
            }

            var quoted = expression.args.get("quoted");
            var key = (string)@this!;
            if (!(py.truthy(quoted) && JSON_PATH_KEY_QUOTED_FORCES_BRACKETS)
                && SAFE_JSON_PATH_KEY_RE.match(key) is not null)
                return $".{key}";

            var part = json_path_part(key);

            return _quote_json_path_key_using_brackets && JSON_PATH_BRACKETED_KEY_SUPPORTED
                ? $"[{part}]"
                : $".{part}";
        }

        // py: sqlglot/generator.py::Generator._jsonpathsubscript_sql #8259fa1856
        internal string _jsonpathsubscript_sql(exp.JSONPathSubscript expression)
        {
            var @this = json_path_part(expression.@this);
            return @this.Length != 0 ? $"[{@this}]" : "";
        }

        // Python f-string formatting of a str-or-None payload.
        private static string PyStr(object? value) => value switch
        {
            null => "None",
            string text => text,
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        };

        // f"{e.this}" for the Recursive payload: a str, or a wildcard node rendered by sql().
        private string PartText(object? value) => value is exp.Expr node ? sql(node) : PyStr(value);
    }
}
