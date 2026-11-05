using System.Text.Json;
using Dialect = SqlGlotSharp.dialects.dialect.Dialect;
using Generator = SqlGlotSharp.generator.Generator;
using ErrorLevel = SqlGlotSharp.errors.ErrorLevel;

namespace SqlGlotSharp.Corpus;

/// <summary>Bounded recorder replay. SQL is never reconstructed from the recorded expectation.</summary>
public static class L2Replay
{
    public static void Verify(CorpusCase item)
    {
        var p = item.Payload;
        var boundary = p.GetProperty("boundary").GetString();
        var helper = p.TryGetProperty("pipeline", out var pipeline) && pipeline.ValueKind == JsonValueKind.String
            && pipeline.GetString() == "parse_one_then_sql"
            && boundary is "validate_identity" or "validate_all" or "validate_transpile";
        if (boundary is not ("sql" or "generate" or "transpile") && !helper) throw Missing($"boundary:{boundary}");
        if (p.TryGetProperty("pipeline", out _) && !helper) throw Missing("pipeline");
        var write = CreateDialect(p.GetProperty("write").GetString());
        var read = boundary == "transpile" || helper ? CreateDialect(p.GetProperty("read").GetString()) : null;
        // Python transpile always invokes Dialect.generate(copy=False).
        var settings = new Settings { Copy = boundary != "transpile" };
        if (p.TryGetProperty("options", out var options))
        {
            if (options.ValueKind != JsonValueKind.Object) throw Missing("options");
            if (helper)
            {
                var groups = options.EnumerateObject().ToArray();
                if (groups.Length != 2 || groups.Select(g => g.Name).Distinct().Count() != 2
                    || !options.TryGetProperty("parse", out var parseOptions)
                    || !options.TryGetProperty("generate", out var generateOptions)
                    || parseOptions.ValueKind != JsonValueKind.Object || generateOptions.ValueKind != JsonValueKind.Object)
                    throw Missing("helper:options");
                HashSet<string> parseNames = new(StringComparer.Ordinal);
                foreach (var o in parseOptions.EnumerateObject())
                {
                    if (!parseNames.Add(o.Name) || o.Name != "error_level") throw Missing($"helper:parse:{o.Name}");
                    settings.Set(o, "transpile");
                }
                options = generateOptions;
            }
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (var o in options.EnumerateObject())
            {
                if (!names.Add(o.Name)) throw Missing($"option:duplicate:{o.Name}");
                settings.Set(o, boundary!);
            }
        }
        var globalPretty = PrettyContext(p);
        // An explicit boolean option wins; omitted/None uses the recorded Python global.
        // Legacy records have no provenance: retain the old false default, never infer from SQL.
        settings.Pretty ??= globalPretty;
        // Optional explicit identities are capabilities, not hints. No dialect fallback for unknown classes.
        var generatorIdentity = Identity(p, "generator");
        if (generatorIdentity is not (null or "sqlglot.generator::Generator" or "sqlglot.generators.postgres::PostgresGenerator" or "oracle_plugins.timescale::TimescaleGenerator"))
            throw Missing($"generator:{generatorIdentity}");
        var parserIdentity = Identity(p, "parser");
        if (parserIdentity is not null && (boundary != "transpile" || parserIdentity is not ("sqlglot.parser::Parser" or "sqlglot.parsers.postgres::PostgresParser" or "oracle_plugins.timescale::TimescaleParser")))
            throw Missing($"parser:{parserIdentity}");
        // Helpers have no recorded class/logger provenance. Their known Validator contract
        // uses the dialect factory and records only the generation stage's diagnostics.
        if (helper && (generatorIdentity is not null || p.TryGetProperty("logger_context", out _)))
            throw Missing("helper:context");
        var capture = new Capture(p);
        exp.Expr? ast = null;
        if (boundary != "transpile" && !helper)
        {
            var payload = p.GetProperty("ast");
            ValidateAst(payload);
            ast = serde.load(payload) as exp.Expr ?? throw Missing("ast:non-expression");
            // Recorded L2 metadata is already UTF-16 normalized. Do not apply L1's source mapping again.
        }
        JsonElement actual;
        using (LogCapture.Begin(capture.Accept))
        try
        {
            var g = CreateGenerator(write, generatorIdentity, settings);
            if (boundary == "transpile")
            {
                parser.Parser parser = parserIdentity switch
                {
                    "sqlglot.parser::Parser" => new parser.Parser(error_level: settings.ParseLevel, dialect: read),
                    "sqlglot.parsers.postgres::PostgresParser" => new parsers.postgres.PostgresParser(error_level: settings.ParseLevel, dialect: read),
                    "sqlglot.parsers.fabric::FabricParser" => new parsers.fabric.FabricParser(error_level: settings.ParseLevel, dialect: read),
                    "sqlglot.parsers.tsql::TSQLParser" => new parsers.tsql.TSQLParser(error_level: settings.ParseLevel, dialect: read),
                    "oracle_plugins.timescale::TimescaleParser" => new parsers.timescale.TimescaleParser(error_level: settings.ParseLevel, dialect: read),
                    _ => read!.parser(settings.ParseLevel)
                };
                var sql = p.GetProperty("sql").GetString()!;
                var result = parser.parse(read!.tokenize(sql), sql).Select(e => e is null ? "" : g.generate(e, settings.Copy)).ToArray();
                actual = JsonSerializer.SerializeToElement(new { result });
            }
            else
            {
                if (helper)
                {
                    // Helpers record only the generation stage's diagnostics.
                    capture.Active = false;
                    ast = SqlGlot.parse_one(p.GetProperty("sql").GetString()!, read: read, error_level: settings.ParseLevel);
                    capture.Active = true;
                }
                actual = JsonSerializer.SerializeToElement(new { result = g.generate(ast!, settings.Copy) });
            }
        }
        catch (NotPortedException) { throw; }
        catch (errors.SqlglotError e)
        {
            actual = JsonSerializer.SerializeToElement(new { error = new { @class = e.GetType().Name, module = "sqlglot.errors", message = e.Message } });
        }
        // Unexpected CLR exceptions and all comparison differences are failures, never NotPorted.
        var expected = p.GetProperty("expected");
        var warnings = JsonSerializer.SerializeToElement(capture.Messages);
        var expectedWarnings = p.TryGetProperty("warnings", out var w) ? w : JsonSerializer.SerializeToElement(Array.Empty<object>());
        // Recorder normalize preserves SQL verbatim (including whitespace, comments and Unicode).
        if (!JsonElement.DeepEquals(expected, actual) || !JsonElement.DeepEquals(expectedWarnings, warnings))
            throw new InvalidOperationException($"Corpus mismatch {item.Id}\nExpected: {expected.GetRawText()}\nActual: {actual.GetRawText()}\nExpected warnings: {expectedWarnings.GetRawText()}\nActual warnings: {warnings.GetRawText()}");
    }

    private static NotPortedException Missing(string capability) => new($"corpus:L2:{capability}");
    private static bool PrettyContext(JsonElement p)
    {
        if (!p.TryGetProperty("runtime_context", out var context)) return false;
        if (context.ValueKind != JsonValueKind.Object) throw Missing("runtime_context");
        var properties = context.EnumerateObject().ToArray();
        if (properties.Length != 1 || properties[0].Name != "sqlglot.pretty")
            throw Missing("runtime_context:keys");
        return properties[0].Value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw Missing("runtime_context:sqlglot.pretty")
        };
    }
    private static string? Identity(JsonElement p, string key) => p.TryGetProperty(key, out var v)
        ? v.ValueKind == JsonValueKind.String ? v.GetString() : throw Missing(key) : null;
    private static Dialect CreateDialect(string? name) => name switch
    {
        "" => new Dialect(), "postgres" => new dialects.postgres.Postgres(),
        "timescale" => new dialects.timescale.Timescale(), "tsql" => new dialects.tsql.TSQL(), "fabric" => new dialects.fabric.Fabric(), _ => throw Missing($"dialect:{name}")
    };
    private static void ValidateAst(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Array) throw Missing("ast");
        foreach (var node in payload.EnumerateArray())
        {
            if (node.TryGetProperty("c", out var c))
            {
                var name = c.GetString()!;
                // serde's short-name compatibility must not admit local/custom Python expression identities.
                if (name != "DataType.Type" && name != "oracle_plugins.timebucket_contract.TimeBucket"
                    && !name.StartsWith("sqlglot.expressions.", StringComparison.Ordinal)) throw Missing($"ast:class:{name}");
                // Never let serde's short-name compatibility masquerade as the custom contract.
                if (name.EndsWith(".TimeBucket", StringComparison.Ordinal) && name != "oracle_plugins.timebucket_contract.TimeBucket")
                    throw Missing($"ast:class:{name}");
            }
            if (node.TryGetProperty("t", out var t)) ValidateAst(t);
        }
    }

    private static Generator CreateGenerator(Dialect dialect, string? identity, Settings s)
    {
        var configured = new Generator(pretty: s.Pretty, identify: s.Identify, normalize: s.Normalize,
            pad: s.Pad, indent: s.Indent, normalize_functions: s.Functions, unsupported_level: s.Unsupported,
            max_unsupported: s.MaxUnsupported, leading_comma: s.LeadingComma, max_text_width: s.Width,
            comments: s.Comments, dialect: dialect);
        if (identity == "sqlglot.generator::Generator" || (identity is null && dialect.GetType() == typeof(Dialect))) return configured;
        Generator native = identity == "oracle_plugins.timescale::TimescaleGenerator"
            || (identity is null && dialect is dialects.timescale.Timescale)
            ? new generators.timescale.TimescaleGenerator(dialect: dialect, pretty: (bool)s.Pretty!)
            : identity == "sqlglot.generators.fabric::FabricGenerator" || (identity is null && dialect is dialects.fabric.Fabric)
            ? new generators.fabric.FabricGenerator(dialect: dialect, pretty: (bool)s.Pretty!)
            : identity == "sqlglot.generators.tsql::TSQLGenerator" || (identity is null && dialect is dialects.tsql.TSQL)
            ? new generators.tsql.TSQLGenerator(dialect: dialect, pretty: (bool)s.Pretty!)
            : new generators.postgres.PostgresGenerator(dialect: dialect, pretty: (bool)s.Pretty!);
        // Constructors own dialect initialization and type-specific dispatch. Only generator
        // options are transferred; no escape/configuration/cache state.
        native.identify = configured.identify;
        native.normalize = configured.normalize;
        native.pad = configured.pad;
        native._indent = configured._indent;
        native.normalize_functions = configured.normalize_functions;
        native.unsupported_level = configured.unsupported_level;
        native.max_unsupported = configured.max_unsupported;
        native.leading_comma = configured.leading_comma;
        native.max_text_width = configured.max_text_width;
        native.comments = configured.comments;
        return native;
    }

    private sealed class Settings
    {
        public object? Pretty, Identify, Functions;
        public bool Normalize, LeadingComma, Comments = true, Copy = true;
        public long Pad = 2, Indent = 2, Width = 80;
        public int MaxUnsupported = 3;
        public ErrorLevel Unsupported = ErrorLevel.WARN;
        public ErrorLevel? ParseLevel;
        public void Set(JsonProperty o, string boundary)
        {
            switch (o.Name)
            {
                case "pretty": Pretty = OptionalBool(o); break;
                case "identify": Identify = o.Value.ValueKind == JsonValueKind.String && o.Value.GetString() is "safe" or "always" ? o.Value.GetString() : OptionalBool(o); break;
                case "normalize_functions": Functions = o.Value.ValueKind == JsonValueKind.String && o.Value.GetString() is "upper" or "lower" ? o.Value.GetString() : OptionalBool(o); break;
                case "normalize": Normalize = Bool(o); break;
                case "leading_comma": LeadingComma = Bool(o); break;
                case "comments": Comments = Bool(o); break;
                // Transpile does not accept an overriding copy kwarg (Python raises TypeError).
                case "copy" when boundary != "transpile": Copy = Bool(o); break;
                case "pad": Pad = Integer(o); break;
                case "indent": Indent = Integer(o); break;
                case "max_text_width": Width = Integer(o); break;
                case "max_unsupported": MaxUnsupported = Integer(o); break;
                case "unsupported_level": Unsupported = Level(o.Value, false)!.Value; break;
                case "error_level" when boundary == "transpile": ParseLevel = Level(o.Value, true); break;
                // The recorder has already resolved the effective write identity. It must not be recomputed.
                case "identity" when boundary == "transpile": _ = Bool(o); break;
                default: throw Missing($"option:{o.Name}");
            }
        }
        private static object? OptionalBool(JsonProperty o) => o.Value.ValueKind == JsonValueKind.Null ? null : Bool(o);
        private static bool Bool(JsonProperty o) => o.Value.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => throw Missing($"option:{o.Name}:{o.Value.GetRawText()}") };
        private static int Integer(JsonProperty o) => o.Value.ValueKind == JsonValueKind.Number && o.Value.TryGetInt32(out var v) ? v : throw Missing($"option:{o.Name}:{o.Value.GetRawText()}");
        private static ErrorLevel? Level(JsonElement v, bool nullable)
        {
            if (nullable && v.ValueKind == JsonValueKind.Null) return null;
            if (v.ValueKind == JsonValueKind.Object && v.EnumerateObject().Count() == 2
                && v.TryGetProperty("$enum", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() == "sqlglot.errors::ErrorLevel"
                && v.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString() switch { "IGNORE" => ErrorLevel.IGNORE, "WARN" => ErrorLevel.WARN, "RAISE" => ErrorLevel.RAISE, "IMMEDIATE" => ErrorLevel.IMMEDIATE, _ => throw Missing($"option:ErrorLevel:{v}") };
            throw Missing($"option:ErrorLevel:{v}");
        }
    }

    private sealed class Capture
    {
        public List<object> Messages { get; } = [];
        public bool Active { get; set; } = true;
        private readonly Dictionary<string, (bool Warning, bool Error)> owners = new(StringComparer.Ordinal);
        public Capture(JsonElement p)
        {
            JsonElement context = default;
            if (p.TryGetProperty("logger_context", out context))
            {
                if (context.ValueKind != JsonValueKind.Object) throw Missing("logger_context");
                foreach (var entry in context.EnumerateObject())
                    if (entry.Name is not ("parser" or "generator")) throw Missing($"logger_context:{entry.Name}");
            }
            foreach (var owner in new[] { "parser", "generator" })
            {
                if (context.ValueKind == JsonValueKind.Undefined || !context.TryGetProperty(owner, out var c)) continue;
                if (c.ValueKind != JsonValueKind.Object || c.EnumerateObject().Count() != 2) throw Missing($"logger_context:{owner}");
                owners[owner] = (Enabled(c, owner, "warning"), Enabled(c, owner, "error"));
            }
            static bool Enabled(JsonElement c, string owner, string name) => c.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() switch { "enabled" or "mock" => true, "disabled" => false, _ => throw Missing($"logger_context:{owner}:{name}") }
                : throw Missing($"logger_context:{owner}:{name}");
        }
        public void Accept(py.logging.LogRecord record)
        {
            if (!Active) return;
            var (warning, error) = owners.GetValueOrDefault(LogCapture.Owner(record), (true, true));
            var enabled = record.levelno switch { py.logging.WARNING => warning, py.logging.ERROR => error, _ => record.levelno >= py.logging.CRITICAL };
            if (enabled) Messages.Add(new { level = record.levelno == py.logging.WARNING ? "WARNING" : record.levelno == py.logging.ERROR ? "ERROR" : "CRITICAL", message = record.getMessage() });
        }
    }
}
