using Dialect = SqlGlotSharp.dialects.dialect.Dialect;
using Token = SqlGlotSharp.tokenizer_core.Token;
using Core = SqlGlotSharp.tokenizer_core.TokenizerCore;

namespace SqlGlotSharp;

public static partial class tokens
{
    public partial class Tokenizer
    {
        // py: sqlglot/tokens.py::Tokenizer.__slots__ #0801faea58
        public Dialect dialect { get; }
        public Core _core { get; }

        // py: sqlglot/tokens.py::Tokenizer.__init__ #562aab5b83
        public Tokenizer(object? dialect = null)
        {
            this.dialect = ResolveDialect(dialect);
            _core = _init_core();
        }

        // py: sqlglot/tokens.py::Tokenizer._init_core #da41d76f95
        public Core _init_core() => new(
            single_tokens: SINGLE_TOKENS, keywords: KEYWORDS, quotes: _QUOTES, format_strings: _FORMAT_STRINGS,
            identifiers: _IDENTIFIERS, comments: _COMMENTS, string_escapes: _STRING_ESCAPES,
            byte_string_escapes: _BYTE_STRING_ESCAPES, identifier_escapes: _IDENTIFIER_ESCAPES.Cast<string>().ToHashSet(),
            commands: COMMANDS, command_prefix_tokens: COMMAND_PREFIX_TOKENS, nested_comments: NESTED_COMMENTS,
            dash_comment_requires_boundary: DASH_COMMENT_REQUIRES_BOUNDARY, comments_terminate_at_newline_only: COMMENTS_TERMINATE_AT_NEWLINE_ONLY,
            hint_start: HINT_START, tokens_preceding_hint: TOKENS_PRECEDING_HINT, has_bit_strings: BIT_STRINGS.Count != 0,
            has_hex_strings: HEX_STRINGS.Count != 0, numeric_literals: StringValues(NUMERIC_LITERALS),
            var_single_tokens: VAR_SINGLE_TOKENS, string_escapes_allowed_in_raw_strings: STRING_ESCAPES_ALLOWED_IN_RAW_STRINGS,
            heredoc_tag_is_identifier: HEREDOC_TAG_IS_IDENTIFIER, heredoc_string_alternative: HEREDOC_STRING_ALTERNATIVE,
            keyword_trie: _KEYWORD_TRIE, numbers_can_be_underscore_separated: dialect.NUMBERS_CAN_BE_UNDERSCORE_SEPARATED,
            numbers_can_have_decimals: NUMBERS_CAN_HAVE_DECIMALS, identifiers_can_start_with_digit: dialect.IDENTIFIERS_CAN_START_WITH_DIGIT,
            unescaped_sequences: dialect.UNESCAPED_SEQUENCES, numeric_escapes: EscapeValues(NUMERIC_ESCAPES),
            drop_unknown_escapes: DROP_UNKNOWN_ESCAPES, numeric_escapes_are_bytes: NUMERIC_ESCAPES_ARE_BYTES,
            byte_strings_are_bytes: dialect.BYTE_STRING_IS_BYTES_TYPE, lone_surrogate_replacement: LONE_SURROGATE_REPLACEMENT);

        // py: sqlglot/tokens.py::Tokenizer.tokenize #705d0a75c6
        public List<Token> tokenize(string sql) => _core.tokenize(sql);
        // py: sqlglot/tokens.py::Tokenizer.sql #5c88b0eae6
        public string sql => _core.sql;
        // py: sqlglot/tokens.py::Tokenizer.size #e718251924
        public int size => _core.size;
        // py: sqlglot/tokens.py::Tokenizer.tokens #17914a89b3
        public List<Token> tokens => _core.tokens;

        public static Tokenizer ForDialect(string name = "")
        {
            return ForDialectInstance(ResolveDialect(name));
        }
        public static Tokenizer ForDialectInstance(Dialect dialect) => dialect.CreateTokenizer();

        internal static Tokenizer BuiltInTokenizer(Dialect dialect)
        {
            return dialect switch
            {
                dialects.fabric.Fabric => new dialects.fabric.Fabric.Tokenizer(dialect),
                dialects.tsql.TSQL => new dialects.tsql.TSQL.Tokenizer(dialect),
                dialects.timescale.Timescale when dialect.GetType() == typeof(dialects.timescale.Timescale)
                    => new dialects.postgres.Postgres.Tokenizer(dialect),
                dialects.postgres.Postgres => new dialects.postgres.Postgres.Tokenizer(dialect),
                _ => new Tokenizer(dialect)
            };
        }
        private static Dialect ResolveDialect(object? value) => Dialect.get_or_raise(value);
        private static PyDict<string, string> StringValues(IEnumerable<KeyValuePair<string, object?>> values)
        {
            var result = new PyDict<string, string>();
            foreach (var (key, value) in values) result[key] = (string)value!;
            return result;
        }
        private static PyDict<string, (int, int, int, int)> EscapeValues(IEnumerable<KeyValuePair<string, (long, long, long, long)>> values)
        {
            var result = new PyDict<string, (int, int, int, int)>();
            foreach (var (key, value) in values) result[key] = (checked((int)value.Item1), checked((int)value.Item2), checked((int)value.Item3), checked((int)value.Item4));
            return result;
        }
    }
}
