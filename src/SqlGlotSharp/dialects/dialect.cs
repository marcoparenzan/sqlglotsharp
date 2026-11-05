namespace SqlGlotSharp.dialects;

public static partial class dialect
{
    // py: sqlglot/dialects/dialect.py::logger #12092c5f48
    public static py.logging.Logger logger = py.logging.getLogger("sqlglot");

    public partial class Dialect
    {
        public (long major, long minor, long patch) version;
        public NormalizationStrategy normalization_strategy;
        public PyDict<string, object?> settings = new();
        private static readonly object RegistryLock = new();

        public Dialect() : this(null) { }
        // py: sqlglot/dialects/dialect.py::Dialect.__init__ #64a78a0381
        public Dialect(PyKwargs? kwargs) => InitializeSettings(kwargs);
        private void InitializeSettings(PyKwargs? kwargs)
        {
            var options = kwargs?.copy() ?? new();
            var parts = py.str(options.pop("version", long.MaxValue)).Split('.').ToList();
            while (parts.Count < 3) parts.Add("0");
            version = (py.@int(parts[0]), py.@int(parts[1]), py.@int(parts[2]));
            var strategy = options.pop("normalization_strategy", null);
            normalization_strategy = strategy is null ? NORMALIZATION_STRATEGY : NormalizationStrategyFromValue(((string)strategy).ToUpperInvariant());
            settings = options;
            foreach (var key in options.Keys)
                if (!SUPPORTED_SETTINGS.Contains(key)) helper.suggest_closest_match_and_fail("setting", key, SUPPORTED_SETTINGS);
        }

        // py: sqlglot/dialects/dialect.py::Dialect.get_or_raise #eede84f7d4
        public static Dialect get_or_raise(object? value)
        {
            if (!py.truthy(value)) return new Dialect();
            if (value is Dialect instance) return instance;
            if (value is Type type && typeof(Dialect).IsAssignableFrom(type)) return Create(type, new());
            if (value is Dialects enumValue) value = ToValue(enumValue);
            if (value is string text)
            {
                var parts = text.Split(',');
                var kwargs = new PyKwargs();
                foreach (var setting in parts.Skip(1))
                {
                    var pair = setting.Split('=');
                    kwargs[pair[0].Trim()] = helper.to_bool(pair.Length == 1 ? true : pair.Length == 2 ? pair[1].Trim() : null);
                }
                var registered = RegisteredType(parts[0].Trim());
                if (registered is null)
                {
                    // Built-in dialects outside the agreed scope remain explicit, not unknown names.
                    if (Enum.GetValues<Dialects>().Any(d => ToValue(d) == parts[0].Trim()))
                        throw new NotPortedException($"dialect:{parts[0].Trim()}");
                    helper.suggest_closest_match_and_fail("dialect", parts[0], RegisteredNames());
                }
                return Create(registered!, kwargs);
            }
            throw new ArgumentException($"Invalid dialect type: {value!.GetType().Name}");
        }
        private static Dialect Create(Type type, PyKwargs kwargs)
        {
            try
            {
                var constructor = type.GetConstructor([typeof(PyKwargs)]);
                if (constructor is not null) return (Dialect)constructor.Invoke([kwargs]);
                var instance = (Dialect)Activator.CreateInstance(type)!;
                instance.InitializeSettings(kwargs);
                return instance;
            }
            catch (System.Reflection.TargetInvocationException error) when (error.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }
        public static void register(string name, Type type)
        {
            if (!typeof(Dialect).IsAssignableFrom(type) || type.IsAbstract) throw new ArgumentException("Plugin must be a concrete Dialect type");
            lock (RegistryLock) DIALECT_REGISTRY__helper[name] = type;
        }

        /// <summary>Removes a dialect registered with <see cref="register"/> (host helper; Python registers via metaclass only).</summary>
        public static bool unregister(string name)
        {
            lock (RegistryLock) return DIALECT_REGISTRY__helper.Remove(name);
        }

        // py: sqlglot/dialects/dialect.py::Dialect.__eq__ #18e0c6ecbd
        public override bool Equals(object? other) => other switch
        {
            Dialect instance => GetType() == instance.GetType(),
            Type type => GetType() == type,
            string name => GetType() == RegisteredType(name),
            _ => false
        };
        // py: sqlglot/dialects/dialect.py::Dialect.__hash__ #f386dd5ccd
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(GetType().Name.ToLowerInvariant());
        // py: sqlglot/dialects/dialect.py::Dialect.tokenize #26787687a2
        public List<tokenizer_core.Token> tokenize(string sql, object? dialect = null) => tokenizer(dialect).tokenize(sql);
        // py: sqlglot/dialects/dialect.py::Dialect.tokenizer #7d2224aaac
        public tokens.Tokenizer tokenizer(object? dialect = null) => tokens.Tokenizer.ForDialectInstance(get_or_raise(py.truthy(dialect) ? dialect : this));
        // py: sqlglot/dialects/dialect.py::Dialect.jsonpath_tokenizer #2f8597874f
        public jsonpath.JSONPathTokenizer jsonpath_tokenizer(object? dialect = null) => new(get_or_raise(py.truthy(dialect) ? dialect : this));
    }
}
