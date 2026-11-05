namespace SqlGlotSharp.generators;

public static partial class postgres
{
    public partial class PostgresGenerator
    {
        // Host constructor for the bounded factory: initialize escaping with the actual dialect.
        public PostgresGenerator(object? dialect = null, bool pretty = false)
            : base(dialect: dialect, pretty: pretty) { }
    }
}
