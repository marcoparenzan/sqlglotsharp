using System.Text;

namespace SqlGlotSharp;

// Standalone leaves from sqlglot/generator.py. No dispatch or preprocessing changes.
public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.identifier_sql #fca0008f80
        public virtual string identifier_sql(exp.Identifier expression)
        {
            var text = expression.name;
            var lower = py.lower(text);
            var quoted = expression.quoted;
            text = normalize && !quoted ? lower : text;
            foreach (var (character, escaped) in _identifier_escaped_sequences)
                text = text.Replace(character, (string)escaped!, StringComparison.Ordinal);
            text = text.Replace(_identifier_end, _escaped_identifier_end, StringComparison.Ordinal);
            if (quoted || dialect.can_quote(expression, identify) || RESERVED_KEYWORDS.Contains(lower) ||
                (!dialect.IDENTIFIERS_CAN_START_WITH_DIGIT && StartsWithPythonDigit(text)))
                text = $"{_identifier_start}{_replace_line_breaks(text)}{_identifier_end}";
            return text;
        }

        // py: sqlglot/generator.py::Generator.literal_sql #e8a335d30b
        public virtual string literal_sql(exp.Literal expression)
        {
            var text = py.truthy(expression.@this) ? (string)expression.@this! : "";
            if (expression.is_string)
                text = $"{dialect.QUOTE_START}{escape_str(text)}{dialect.QUOTE_END}";
            return text;
        }

        // py: sqlglot/generator.py::Generator.escape_str #f4970b6fb0
        public virtual string escape_str(string text, bool escape_backslash = true,
            string? delimiter = null, string? escaped_delimiter = null,
            bool is_byte_string = false, bool is_bytes = false)
        {
            var supports_escape_sequences = is_byte_string
                ? dialect.BYTE_STRINGS_SUPPORT_ESCAPED_SEQUENCES
                : dialect.STRINGS_SUPPORT_ESCAPED_SEQUENCES;
            if (supports_escape_sequences)
            {
                var escaped = new StringBuilder();
                foreach (var rune in text.EnumerateRunes())
                {
                    var ch = rune.ToString();
                    escaped.Append(escape_backslash || ch != "\\" ? dialect.ESCAPED_SEQUENCES.get(ch, ch) : ch);
                }
                text = escaped.ToString();
                if (is_bytes && dialect.tokenizer().NUMERIC_ESCAPES.ContainsKey("x"))
                {
                    // Upstream _HIGH_BYTE_RE matches U+0080..U+00FF only, not all non-ASCII.
                    var bytes = new StringBuilder();
                    foreach (var ch in text)
                        bytes.Append(ch is >= '\u0080' and <= '\u00ff' ? $"\\x{(int)ch:x2}" : ch.ToString());
                    text = bytes.ToString();
                }
            }
            delimiter = string.IsNullOrEmpty(delimiter) ? dialect.QUOTE_END : delimiter;
            escaped_delimiter = string.IsNullOrEmpty(escaped_delimiter) ? _escaped_quote_end : escaped_delimiter;
            return _replace_line_breaks(text).Replace(delimiter, escaped_delimiter, StringComparison.Ordinal);
        }

        // py: sqlglot/generator.py::Generator.null_sql #eb97057af8
        public virtual string null_sql(params object?[] _) => "NULL";

        // py: sqlglot/generator.py::Generator.boolean_sql #87ed3df89c
        public virtual string boolean_sql(exp.Boolean expression) => py.truthy(expression.@this) ? "TRUE" : "FALSE";

        // py: sqlglot/generator.py::Generator._replace_line_breaks #e4548de8ac
        public virtual string _replace_line_breaks(string @string) => py.truthy(pretty)
            ? @string.Replace("\n", SENTINEL_LINE_BREAK, StringComparison.Ordinal) : @string;

        // Compatibility for Python str[:1].isdigit(): .NET IsDigit covers only Nd.
        // Additional Unicode Digit (not Numeric) ranges verified against the local Python oracle.
        private static bool StartsWithPythonDigit(string text)
        {
            if (text.Length == 0) return false;
            var rune = Rune.GetRuneAt(text, 0);
            return Rune.IsDigit(rune) || rune.Value is
                >= 178 and <= 179 or 185 or >= 4969 and <= 4977 or 6618 or 8304 or
                >= 8308 and <= 8313 or >= 8320 and <= 8329 or >= 9312 and <= 9320 or
                >= 9332 and <= 9340 or >= 9352 and <= 9360 or 9450 or >= 9461 and <= 9469 or 9471 or
                >= 10102 and <= 10110 or >= 10112 and <= 10120 or >= 10122 and <= 10130 or
                >= 68160 and <= 68163 or >= 69216 and <= 69224 or >= 69714 and <= 69722 or
                >= 127232 and <= 127242;
        }
    }
}
