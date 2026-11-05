using System.Text.Json;
using System.Text.Json.Nodes;
using Dialect = SqlGlotSharp.dialects.dialect.Dialect;

namespace SqlGlotSharp.Corpus;

/// <summary>Replay of the extractor's public parse/parse_one boundaries, not a replacement parser.</summary>
public static class L1Replay
{
    public static void Verify(CorpusCase item)
    {
        var payload = item.Payload;
        if (!payload.TryGetProperty("boundary", out var boundaryValue))
            throw new NotPortedException("corpus:L1:boundary");
        var boundary = boundaryValue.GetString();
        if (boundary is not ("parse" or "parse_one"))
            throw new NotPortedException($"corpus:L1:{boundary}");
        var sql = payload.GetProperty("sql").GetString()!;
        var read = payload.GetProperty("read").GetString()!;
        // Validate capabilities before tokenization: unsupported configuration is never an expected error.
        var dialect = read switch
        {
            "" => new Dialect(),
            "postgres" => (Dialect)new dialects.postgres.Postgres(),
            "tsql" => new dialects.tsql.TSQL(),
            "fabric" => new dialects.fabric.Fabric(),
            _ => throw new NotPortedException($"corpus:L1:dialect:{read}")
        };
        var level = (errors.ErrorLevel?)null;
        var context = 100;
        var maxErrors = 3;
        var maxNodes = -1;
        object? into = null;
        if (payload.TryGetProperty("options", out var options))
        {
            foreach (var option in options.EnumerateObject())
            {
                switch (option.Name)
                {
                    case "error_level": level = ErrorLevel(option.Value); break;
                    case "error_message_context": context = Integer(option); break;
                    case "max_errors": maxErrors = Integer(option); break;
                    case "max_nodes": maxNodes = Integer(option); break;
                    // parse_one(into=None) is the ordinary parse path; every truthy into needs parse_into.
                    case "into" when boundary == "parse_one" && option.Value.ValueKind == JsonValueKind.Null: break;
                    case "into" when boundary == "parse_one": into = IntoTypes(option.Value); break;
                    case "into": throw new NotPortedException("corpus:L1:parse_into");
                    default: throw new NotPortedException($"corpus:L1:option:{option.Name}");
                }
            }
        }
        var identity = payload.TryGetProperty("parser", out var parserIdentity) ? parserIdentity.GetString() : null;
        parser.Parser parser = identity switch
        {
            null => dialect.parser(level),
            "sqlglot.parser::Parser" => new parser.Parser(error_level: level, dialect: dialect),
            "sqlglot.parsers.postgres::PostgresParser" => new parsers.postgres.PostgresParser(error_level: level, dialect: dialect),
            "sqlglot.parsers.tsql::TSQLParser" => new parsers.tsql.TSQLParser(error_level: level, dialect: dialect),
            "sqlglot.parsers.fabric::FabricParser" => new parsers.fabric.FabricParser(error_level: level, dialect: dialect),
            _ => throw new NotPortedException($"corpus:L1:parser:{identity}")
        };
        parser.error_message_context = context;
        parser.max_errors = maxErrors;
        parser.max_nodes = maxNodes;
        var capture = new CaptureLogger(payload);
        JsonElement actual;
        using (LogCapture.Begin(capture.Accept))
        try
        {
            var result = into is null ? parser.parse(dialect.tokenize(sql), sql) : parser.parse_into(into, dialect.tokenize(sql), sql);
            if (boundary == "parse_one")
            {
                if (result.Count == 0 || result[0] is null)
                    throw new errors.ParseError($"No expression was parsed from '{sql}'");
                // Upstream parse_one returns a Block for multiple statements, not only the first AST.
                result = [result.Count > 1 ? new exp.Block(expressions: result.Cast<object?>().ToList()) : result[0]];
            }
            actual = JsonSerializer.SerializeToElement(new
            {
                result = result.Select(node => node is null ? (JsonElement?)null : NormalizeAst(serde.dump_json(node), sql)).ToArray()
            });
        }
        catch (NotPortedException) { throw; }
        catch (errors.SqlglotError error)
        {
            actual = JsonSerializer.SerializeToElement(new
            {
                error = new { @class = error.GetType().Name, module = "sqlglot.errors", message = error.Message }
            });
        }
        // Unexpected CLR exceptions propagate as failures, never fabricated Python builtins errors.
        var expected = payload.GetProperty("expected");
        var warnings = JsonSerializer.SerializeToElement(capture.Messages);
        var expectedWarnings = payload.TryGetProperty("warnings", out var recordedWarnings)
            ? recordedWarnings : JsonSerializer.SerializeToElement(Array.Empty<object>());
        if (!AstComparison.ResultsEqual(expected, actual) || !JsonElement.DeepEquals(expectedWarnings, warnings))
            throw new InvalidOperationException($"Corpus mismatch {item.Id}\nExpected: {expected.GetRawText()}\nActual: {actual.GetRawText()}\nExpected warnings: {expectedWarnings.GetRawText()}\nActual warnings: {warnings.GetRawText()}");
    }

    // Recorder encodes a Python class as {"$type": "module::Class"}, or a list of them.
    private static object IntoTypes(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array) return value.EnumerateArray().Select(v => (Type)IntoTypes(v)).ToList();
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("$type", out var name) && name.ValueKind == JsonValueKind.String)
            return serde.TypeFromPythonClassName(name.GetString()!.Replace("::", "."))
                ?? throw new NotPortedException($"corpus:L1:into:{name.GetString()}");
        throw new NotPortedException($"corpus:L1:into:{value.GetRawText()}");
    }

    private static int Integer(JsonProperty option) => option.Value.ValueKind == JsonValueKind.Number && option.Value.TryGetInt32(out var value) ? value
        : throw new NotPortedException($"corpus:L1:option:{option.Name}:{option.Value.GetRawText()}");

    private static errors.ErrorLevel? ErrorLevel(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        // extract_corpus.normalize encodes the exact Python enum identity and string value.
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("$enum", out var identity)
            && identity.ValueKind == JsonValueKind.String && identity.GetString() == "sqlglot.errors::ErrorLevel"
            && value.TryGetProperty("value", out var name) && name.ValueKind == JsonValueKind.String)
        {
            return name.GetString() switch
            {
                "IGNORE" => errors.ErrorLevel.IGNORE,
                "WARN" => errors.ErrorLevel.WARN,
                "RAISE" => errors.ErrorLevel.RAISE,
                "IMMEDIATE" => errors.ErrorLevel.IMMEDIATE,
                _ => throw new NotPortedException($"corpus:L1:option:error_level:{value.GetRawText()}")
            };
        }
        throw new NotPortedException($"corpus:L1:option:error_level:{value.GetRawText()}");
    }

    /// <summary>Matches extract_corpus.normalize_ast: only m positions and recursively t payloads change.</summary>
    public static JsonElement NormalizeAst(JsonElement ast, string sql)
    {
        if (sql.Length == 0) return ast.Clone();
        var root = JsonNode.Parse(ast.GetRawText())!.AsArray();
        var offsets = Offsets(sql);
        var lines = Lines(sql).Select(Offsets).ToArray();
        Normalize(root);
        return JsonSerializer.SerializeToElement(root);

        void Normalize(JsonArray items)
        {
            foreach (var item in items.OfType<JsonObject>())
            {
                if (item["m"] is JsonObject meta)
                {
                    if (Int(meta["col"], out var col) && Int(meta["line"], out var line) && line > 0 && line <= lines.Length)
                        meta["col"] = Slice(lines[line - 1], col);
                    if (Int(meta["start"], out var start)) meta["start"] = Slice(offsets, start);
                    if (Int(meta["end"], out var end)) meta["end"] = Slice(offsets, end + 1) - 1;
                }
                if (item["t"] is JsonArray type) Normalize(type);
            }
        }
    }

    private static bool Int(JsonNode? node, out int value)
    {
        value = 0;
        return node is JsonValue json && json.TryGetValue(out value);
    }
    private static int[] Offsets(string text)
    {
        List<int> offsets = [0];
        for (var i = 0; i < text.Length;)
        {
            i += char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
            offsets.Add(i);
        }
        return offsets.ToArray();
    }
    private static int Slice(int[] offsets, int stop) => offsets[Math.Clamp(stop < 0 ? offsets.Length - 1 + stop : stop, 0, offsets.Length - 1)];

    // Python str.splitlines(keepends=True), including CRLF as a single boundary.
    private static IEnumerable<string> Lines(string sql)
    {
        var start = 0;
        for (var i = 0; i < sql.Length; i++)
        {
            if (sql[i] is not ('\n' or '\r' or '\v' or '\f' or '\u001c' or '\u001d' or '\u001e' or '\u0085' or '\u2028' or '\u2029')) continue;
            if (sql[i] == '\r' && i + 1 < sql.Length && sql[i + 1] == '\n') i++;
            yield return sql[start..(i + 1)];
            start = i + 1;
        }
        if (start < sql.Length) yield return sql[start..];
    }

    private sealed class CaptureLogger
    {
        private readonly bool warning = true;
        private readonly bool error = true;
        public CaptureLogger(JsonElement payload)
        {
            // Legacy cases have no provenance: retain normal logging, never infer suppression.
            if (!payload.TryGetProperty("logger_context", out var context)) return;
            if (context.ValueKind != JsonValueKind.Object || context.EnumerateObject().Count() != 1
                || !context.TryGetProperty("parser", out var parserContext)
                || parserContext.ValueKind != JsonValueKind.Object || parserContext.EnumerateObject().Count() != 2)
                throw new NotPortedException("corpus:L1:logger_context");
            warning = Enabled("warning");
            error = Enabled("error");
            bool Enabled(string name)
            {
                if (!parserContext.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
                    throw new NotPortedException($"corpus:L1:logger_context:{name}");
                return value.GetString() switch
                {
                    "enabled" or "mock" => true, // Mock methods observe calls even if logging is disabled globally.
                    "disabled" => false,
                    _ => throw new NotPortedException($"corpus:L1:logger_context:{name}:{value.GetRawText()}")
                };
            }
        }
        public List<object> Messages { get; } = [];
        // Only the parser's logger context is recorded at L1; records from other modules use normal logging.
        public void Accept(py.logging.LogRecord record)
        {
            var owner = LogCapture.Owner(record);
            var enabled = record.levelno switch
            {
                py.logging.WARNING => owner != "parser" || warning,
                py.logging.ERROR => owner != "parser" || error,
                _ => record.levelno >= py.logging.CRITICAL
            };
            if (!enabled) return;
            var level = record.levelno switch { py.logging.WARNING => "WARNING", py.logging.ERROR => "ERROR", _ => "CRITICAL" };
            Messages.Add(new { level, message = record.getMessage() });
        }
    }
}
