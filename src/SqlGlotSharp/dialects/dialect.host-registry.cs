namespace SqlGlotSharp.dialects;

public static partial class dialect
{
    public partial class Dialect
    {
        // Bounded host overlay: gen_csharp.py owns the upstream registry. The plugin is
        // deliberately outside upstream Python and survives regeneration unchanged.
        private static readonly IReadOnlyDictionary<string, Type> HostDialects =
            new Dictionary<string, Type>(StringComparer.Ordinal)
            {
                ["timescale"] = typeof(timescale.Timescale)
            };

        private static Type? RegisteredType(string name)
        {
            lock (RegistryLock)
                return DIALECT_REGISTRY__helper.TryGetValue(name, out var type) ? type
                    : HostDialects.GetValueOrDefault(name);
        }

        private static IEnumerable<string> RegisteredNames()
        {
            lock (RegistryLock)
                return DIALECT_REGISTRY__helper.Keys.Concat(HostDialects.Keys).Distinct().ToArray();
        }
    }
}
