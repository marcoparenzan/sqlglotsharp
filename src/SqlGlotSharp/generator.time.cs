namespace SqlGlotSharp;

public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.format_time #0000000000
        public virtual string? format_time(exp.Expr expression,
            IReadOnlyDictionary<string, string>? inverse_time_mapping = null, PyDict<object, object?>? inverse_time_trie = null) =>
            global::SqlGlotSharp.time.format_time(sql(expression, "format"),
                inverse_time_mapping is { Count: > 0 } ? inverse_time_mapping : dialect.INVERSE_TIME_MAPPING,
                inverse_time_trie is { Count: > 0 } ? inverse_time_trie : dialect.INVERSE_TIME_TRIE);
    }
}
