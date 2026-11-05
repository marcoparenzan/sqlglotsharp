using Dialect = SqlGlotSharp.dialects.dialect.Dialect;
using ErrorLevel = SqlGlotSharp.errors.ErrorLevel;

namespace SqlGlotSharp;

// Bounded foundation of sqlglot/generator.py. Dispatch/transforms and AST SQL handlers
// are deliberately NOT supplied by this file. Zero hashes identify partial/stub members.
public static partial class generator
{
    // py: sqlglot/generator.py::logger #12092c5f48
    public static py.logging.Logger logger = py.logging.getLogger("sqlglot");

    // py: sqlglot/generator.py::unsupported_args #29c2f59b71
    public static Func<Func<Generator, exp.Expr, string>, Func<Generator, exp.Expr, string>> unsupported_args(params object[] args)
    {
        var diagnostic_by_arg = new PyDict<string, string?>();
        foreach (var arg in args)
        {
            if (arg is string name) diagnostic_by_arg[name] = null;
            else if (arg is ValueTuple<string, string> pair) diagnostic_by_arg[pair.Item1] = pair.Item2;
            else throw new ArgumentException("Expected an argument name or (name, diagnostic) pair", nameof(args));
        }
        return func => (generator, expression) =>
        {
            var expression_name = expression.GetType().Name;
            var dialect_name = generator.dialect.GetType().Name;
            foreach (var (arg_name, diagnostic) in diagnostic_by_arg)
            {
                if (py.truthy(expression.args.get(arg_name)))
                {
                    var message = !string.IsNullOrEmpty(diagnostic) ? diagnostic :
                        $"Argument '{arg_name}' is not supported for expression '{expression_name}' when targeting {dialect_name}.";
                    generator.unsupported(message);
                }
            }
            return func(generator, expression);
        };
    }

    public partial class Generator
    {
        public object pretty;
        public object identify;
        public bool normalize;
        public long pad;
        public long _indent;
        public object normalize_functions;
        public ErrorLevel unsupported_level;
        public int max_unsupported;
        public bool leading_comma;
        public long max_text_width;
        public bool comments;
        public Dialect dialect;
        public List<string> unsupported_messages;
        public string _escaped_quote_end;
        public string _escaped_byte_quote_end;
        public string _escaped_identifier_end;
        public PyDict<string, object?> _identifier_escaped_sequences;
        public Func<string> _next_name;
        public string _identifier_start;
        public string _identifier_end;
        public bool _quote_json_path_key_using_brackets;

        public IReadOnlyDictionary<Type, Func<Generator, exp.Expr, string>> _dispatch { get; }

        public Generator() : this(pretty: null) { }
        // Partial initializer: upstream hash 718165f77b. Option/escape/name state is ported;
        // global sqlglot.pretty is not available (use upstream default False), and dispatch
        // construction is bounded to existing handlers (see generator.dispatch.cs).
        // py: sqlglot/generator.py::Generator.__init__ #0000000000
        public Generator(object? pretty = null, object? identify = null, bool normalize = false,
            long pad = 2, long indent = 2, object? normalize_functions = null,
            ErrorLevel unsupported_level = ErrorLevel.WARN, int max_unsupported = 3,
            bool leading_comma = false, long max_text_width = 80, bool comments = true,
            object? dialect = null)
        {
            this.pretty = pretty ?? false;
            this.identify = identify ?? false;
            this.normalize = normalize;
            this.pad = pad;
            _indent = indent;
            this.unsupported_level = unsupported_level;
            this.max_unsupported = max_unsupported;
            this.leading_comma = leading_comma;
            this.max_text_width = max_text_width;
            this.comments = comments;
            this.dialect = Dialect.get_or_raise(dialect);
            // This is both a Dialect property and a Generator argument, so we prioritize the latter
            this.normalize_functions = normalize_functions ?? this.dialect.NORMALIZE_FUNCTIONS;
            unsupported_messages = [];
            var escape = this.dialect.tokenizer().STRING_ESCAPES[0];
            _escaped_quote_end = escape + this.dialect.QUOTE_END;
            _escaped_byte_quote_end = !string.IsNullOrEmpty(this.dialect.BYTE_END) ? escape + this.dialect.BYTE_END : "";
            _escaped_identifier_end = this.dialect.IDENTIFIER_END + this.dialect.IDENTIFIER_END;
            _identifier_escaped_sequences = this.dialect.IDENTIFIER_ESCAPED_SEQUENCES;
            _next_name = helper.name_sequence("_t");
            _identifier_start = this.dialect.IDENTIFIER_START;
            _identifier_end = this.dialect.IDENTIFIER_END;
            _quote_json_path_key_using_brackets = true;
            var cls = GetType();
            _dispatch = _DISPATCH_CACHE.GetOrAdd(cls, _build_dispatch);
        }

        /// <summary>
        /// Generates the SQL string corresponding to the given syntax tree.
        /// expression: The syntax tree. copy: Whether to copy the expression. The generator
        /// performs mutations so it is safer to copy.
        /// Returns the SQL string corresponding to expression.
        /// </summary>
        // py: sqlglot/generator.py::Generator.generate #b49b59ed5a
        public virtual string generate(exp.Expr expression, bool copy = true)
        {
            if (copy) expression = expression.copy();
            expression = preprocess(expression);
            unsupported_messages = [];
            var sql = Strip(this.sql(expression));
            if (py.truthy(pretty)) sql = sql.Replace(SENTINEL_LINE_BREAK, "\n", StringComparison.Ordinal);
            if (unsupported_level == ErrorLevel.IGNORE) return sql;
            if (unsupported_level == ErrorLevel.WARN)
            {
                foreach (var msg in unsupported_messages) logger.warning(msg);
            }
            else if (unsupported_level == ErrorLevel.RAISE && unsupported_messages.Count != 0)
                throw new errors.UnsupportedError(errors.concat_messages(unsupported_messages, max_unsupported));
            return sql;
        }

        // py: sqlglot/generator.py::Generator.unsupported #b06a23c91a
        public virtual void unsupported(string message)
        {
            if (unsupported_level == ErrorLevel.IMMEDIATE) throw new errors.UnsupportedError(message);
            unsupported_messages.Add(message);
        }

        // py: sqlglot/generator.py::Generator.sep #928c41878e
        public virtual string sep(string sep = " ") => py.truthy(pretty) ? Strip(sep) + "\n" : sep;

        // py: sqlglot/generator.py::Generator.seg #dd089fec3f
        public virtual string seg(string sql, string sep = " ") => this.sep(sep) + sql;

        // py: sqlglot/generator.py::Generator.wrap #9228bca4e0
        public virtual string wrap(object expression)
        {
            // UNWRAPPED_QUERIES = (Select, SetOperation); SetOperation is a C# AST trait.
            var this_sql = expression is exp.Select or exp.SetOperation ? sql(expression) : sql(expression, "this");
            if (this_sql.Length == 0) return "()";
            this_sql = indent(this_sql, level: 1, pad: 0);
            return $"({sep("")}{this_sql}{seg(")", sep: "")}";
        }

        // py: sqlglot/generator.py::Generator.no_identify #ac29d35630
        public virtual string no_identify(Func<string> func)
        {
            var original = identify;
            identify = false;
            var result = func();
            identify = original;
            return result;
        }

        // py: sqlglot/generator.py::Generator.normalize_func #e1dadd1e31
        public virtual string normalize_func(string name)
        {
            if (normalize_functions is true || normalize_functions is string upper && upper == "upper") return py.upper(name);
            if (normalize_functions is string lower && lower == "lower") return py.lower(name);
            return name;
        }

        // py: sqlglot/generator.py::Generator.indent #6161c648b3
        public virtual string indent(string sql, long level = 0, long? pad = null, bool skip_first = false, bool skip_last = false)
        {
            if (!py.truthy(pretty) || sql.Length == 0) return sql;
            pad ??= this.pad;
            var lines = sql.Split('\n');
            var spaces = new string(' ', checked((int)Math.Max(0, checked(level * _indent + pad.Value))));
            return string.Join("\n", lines.Select((line, i) =>
                (skip_first && i == 0) || (skip_last && i == lines.Length - 1) ? line : spaces + line));
        }

        // Routing is complete; the dispatch table and fallback implementations remain bounded.
        // py: sqlglot/generator.py::Generator.sql #8f16de6d59
        public virtual string sql(object? expression, string? key = null, bool comment = true)
        {
            if (!py.truthy(expression)) return "";
            if (expression is string text) return text;
            var node = (exp.Expr)expression!;
            if (!string.IsNullOrEmpty(key))
            {
                var value = node.args.get(key);
                if (py.truthy(value)) return sql(value);
                return "";
            }

            string result;
            if (_dispatch.TryGetValue(node.GetType(), out var handler))
                result = handler(this, node);
            else if (node is exp.Func)
                result = function_fallback_sql(node);
            else if (node is exp.Property)
                result = property_sql(node);
            else
                throw new ArgumentException($"Unsupported expression type {node.GetType().Name}");

            return comments && comment ? maybe_comment(result, node) : result;
        }

        // Python str.strip includes the four ASCII information separators, unlike .NET Trim.
        private static string Strip(string text)
        {
            static bool White(char c) => char.IsWhiteSpace(c) || c is '\u001c' or '\u001d' or '\u001e' or '\u001f';
            var start = 0;
            var end = text.Length;
            while (start < end && White(text[start])) start++;
            while (end > start && White(text[end - 1])) end--;
            return text[start..end];
        }
    }
}
