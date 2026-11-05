using Dialect = SqlGlotSharp.dialects.dialect.Dialect;

namespace SqlGlotSharp;

/// <summary>Bounded, entirely native equivalents of sqlglot's public SQL entrypoints.</summary>
/// <remarks>
/// Supports the exact base, PostgreSQL and Timescale dialects (names, enum, types or instances
/// accepted by the dialect resolver). Only parser error level and generator pretty/copy options
/// are exposed. This is a preview, not full SQLGlot compatibility: unavailable grammar,
/// transforms and handlers throw NotPortedException, never return raw input or call Python.
/// Timescale extension functions are identity-only; no DateBin mapping is promised.
/// </remarks>
public static class SqlGlot
{
    // py: sqlglot/__init__.py::logger #12092c5f48
    public static py.logging.Logger logger = py.logging.getLogger("sqlglot");

    /// <summary>Parse one nullable AST per statement. A truthy read takes precedence over dialect.</summary>
    public static List<exp.Expr?> Parse(string sql, object? read = null, object? dialect = null,
        errors.ErrorLevel? errorLevel = null)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ValidateErrorLevel(errorLevel);
        return Resolve(py.truthy(read) ? read : dialect).parse(sql, errorLevel);
    }

    /// <summary>Return a single expression, or a Block containing all statements (including null slots).</summary>
    /// <remarks>An empty input or null first statement raises ParseError, as in Python. A truthy into uses <see cref="ParseInto"/>.</remarks>
    public static exp.Expr ParseOne(string sql, object? read = null, object? dialect = null,
        object? into = null, errors.ErrorLevel? errorLevel = null)
    {
        ArgumentNullException.ThrowIfNull(sql);
        var result = py.truthy(into) ? ParseInto(into!, sql, py.truthy(read) ? read : dialect, errorLevel) : Parse(sql, read, dialect, errorLevel);
        if (result.Count == 0 || result[0] is null)
            throw new errors.ParseError($"No expression was parsed from '{sql}'");
        return result.Count > 1 ? new exp.Block(expressions: result.Cast<object?>().ToList()) : result[0]!;
    }

    /// <summary>Generate one SQL string per parsed statement, with empty strings for null AST slots.</summary>
    /// <remarks>
    /// Identity defaults to true: an omitted write uses read. An explicitly supplied empty write
    /// selects the base dialect, even with identity enabled. Identity=false with omitted write also
    /// selects base. Copy defaults to false like upstream transpile; true is a bounded native
    /// extension for generation of the freshly parsed trees. Other generator options are not exposed.
    /// </remarks>
    public static List<string> Transpile(string sql, object? read = null, object? write = null,
        bool identity = true, errors.ErrorLevel? errorLevel = null, bool pretty = false, bool copy = false)
    {
        ArgumentNullException.ThrowIfNull(sql);
        var target = Resolve(identity && write is null ? read : write);
        return Parse(sql, read, errorLevel: errorLevel)
            .Select(node => node is null ? "" : target.generator(pretty).generate(node, copy)).ToList();
    }

    /// <summary>Parse into an expression type (e.g. <c>typeof(exp.Table)</c>) or the first succeeding type of a list.</summary>
    public static List<exp.Expr?> ParseInto(object into, string sql, object? read = null,
        errors.ErrorLevel? errorLevel = null)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ValidateErrorLevel(errorLevel);
        return Resolve(read).parse_into(into, sql, errorLevel);
    }

    public static List<exp.Expr?> parse(string sql, object? read = null, object? dialect = null,
        errors.ErrorLevel? error_level = null) => Parse(sql, read, dialect, error_level);
    public static exp.Expr parse_one(string sql, object? read = null, object? dialect = null,
        object? into = null, errors.ErrorLevel? error_level = null) => ParseOne(sql, read, dialect, into, error_level);
    public static List<string> transpile(string sql, object? read = null, object? write = null,
        bool identity = true, errors.ErrorLevel? error_level = null, bool pretty = false, bool copy = false)
        => Transpile(sql, read, write, identity, error_level, pretty, copy);
    public static List<exp.Expr?> parse_into(object into, string sql, object? read = null,
        errors.ErrorLevel? error_level = null) => ParseInto(into, sql, read, error_level);

    private static Dialect Resolve(object? value)
    {
        var result = Dialect.get_or_raise(value);
        var type = result.GetType();
        if (type != typeof(Dialect) && type != typeof(dialects.postgres.Postgres)
            && type != typeof(dialects.timescale.Timescale) && type != typeof(dialects.tsql.TSQL) && type != typeof(dialects.fabric.Fabric)
            && type.Assembly == typeof(Dialect).Assembly)
            throw new NotPortedException($"sqlglot public API dialect ({type.FullName})");
        return result;
    }

    private static void ValidateErrorLevel(errors.ErrorLevel? level)
    {
        if (level is not null && !Enum.IsDefined(level.Value))
            throw new ArgumentOutOfRangeException(nameof(level));
    }
}
