namespace SqlGlotSharp;

public static partial class generator
{
    public partial class Generator
    {
        // Byte strings use the same upstream escape implementation as other literals.
        // py: sqlglot/generator.py::Generator.bytestring_sql #0000000000
        public virtual string bytestring_sql(exp.ByteString expression)
        {
            var value = sql(expression, "this");
            var isBytes = py.truthy(expression.args.get("is_bytes"));
            if (isBytes && !string.IsNullOrEmpty(BYTE_STRING_FUNCTION))
                return func(BYTE_STRING_FUNCTION, $"'{LatinHex(value)}'");
            if (!string.IsNullOrEmpty(dialect.BYTE_START))
            {
                var tokenizer = dialect.tokenizer();
                var escaped = escape_str(value,
                    escape_backslash: tokenizer.NUMERIC_ESCAPES.Count > 0 || tokenizer.DROP_UNKNOWN_ESCAPES,
                    delimiter: dialect.BYTE_END, escaped_delimiter: _escaped_byte_quote_end,
                    is_byte_string: true, is_bytes: isBytes);
                var text = $"{dialect.BYTE_START}{escaped}{dialect.BYTE_END}";
                if ((isBytes && !dialect.BYTE_STRING_IS_BYTES_TYPE) || (!isBytes && dialect.BYTE_STRING_IS_BYTES_TYPE))
                {
                    var parsed = dialect.parse(text).Single() ?? throw new NotPortedException("Generator.bytestring_sql / parsed byte literal");
                    return sql(new exp.Cast(@this: parsed, to: new exp.DataType(@this: isBytes ? exp.DType.BINARY : exp.DType.VARCHAR)));
                }
                return text;
            }
            if (dialect.tokenizer().STRING_ESCAPES.Contains("\\")) return sql(exp.Literal.@string(value));
            unsupported($"Byte strings are not supported for {dialect.GetType().Name}");
            return "";
        }

        // py: sqlglot/generator.py::Generator.rawstring_sql #0000000000
        public virtual string rawstring_sql(exp.RawString expression)
        {
            var value = expression.name;
            if (dialect.tokenizer().STRING_ESCAPES.Contains("\\")) value = value.Replace("\\", "\\\\", StringComparison.Ordinal);
            return $"{dialect.QUOTE_START}{escape_str(value, escape_backslash: false)}{dialect.QUOTE_END}";
        }

        protected static string LatinHex(string value)
        {
            if (value.Any(ch => ch > 255)) throw new NotPortedException("Generator.bytestring_sql / non-Latin-1 byte value");
            return Convert.ToHexStringLower(value.Select(ch => (byte)ch).ToArray());
        }
    }
}
