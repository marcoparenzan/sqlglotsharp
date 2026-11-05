using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SqlGlotSharp;

public static partial class tokenizer_core
{
    public partial class TokenizerCore
    {
        // py: sqlglot/tokenizer_core.py::TokenizerCore._scan_string #e88c2faec3
        public bool _scan_string(string start)
        {
            int? @base = null;
            var token_type = TokenType.STRING;
            string end;
            if (quotes.TryGetValue(start, out var quoteEnd)) end = quoteEnd;
            else if (format_strings.TryGetValue(start, out var format))
            {
                (end, token_type) = format;
                if (token_type == TokenType.HEX_STRING) @base = 16;
                else if (token_type == TokenType.BIT_STRING) @base = 2;
                else if (token_type == TokenType.HEREDOC_STRING)
                {
                    _advance();
                    var tag = _char == end ? "" : _extract_string(end, raw_string: true, raise_unmatched: !heredoc_tag_is_identifier);
                    if (tag.Length != 0 && heredoc_tag_is_identifier &&
                        (_end || IsDigitString(tag) || tag.EnumerateRunes().Any(r => IsSpace(r.ToString()))))
                    {
                        if (!_end) _advance(-1);
                        _advance(-py.len(tag));
                        _add(heredoc_string_alternative);
                        return true;
                    }
                    end = start + tag + end;
                }
            }
            else return false;
            _advance(py.len(start));
            var text = _extract_string(end, escapes: token_type == TokenType.BYTE_STRING ? byte_string_escapes : string_escapes,
                raw_string: token_type is TokenType.RAW_STRING or TokenType.NATIONAL_RAW_STRING or TokenType.HEREDOC_STRING,
                bytes_literal: token_type == TokenType.BYTE_STRING && byte_strings_are_bytes);
            if (@base.HasValue && text.Length != 0 && !ValidIntegerBase(text, @base.Value))
                throw new errors.TokenError($"Numeric string contains invalid characters from {_line}:{_start}");
            _add(token_type, text);
            return true;
        }

        // py: sqlglot/tokenizer_core.py::TokenizerCore._scan_identifier #75329792b9
        public void _scan_identifier(string identifier_end)
        {
            _advance();
            var escapes = new HashSet<string>(identifier_escapes) { identifier_end };
            var text = _extract_string(identifier_end, escapes: escapes);
            _add(TokenType.IDENTIFIER, text);
        }

        private static bool IsDigitString(string text) => text.Length != 0 && text.EnumerateRunes().All(r =>
        {
            var category = Rune.GetUnicodeCategory(r);
            var value = Rune.GetNumericValue(r);
            return category == UnicodeCategory.DecimalDigitNumber || category == UnicodeCategory.OtherNumber && value >= 0 && value <= 9 && value == Math.Truncate(value);
        });

        private static bool ValidIntegerBase(string text, int @base)
        {
            var normalized = new StringBuilder();
            foreach (var rune in text.Trim().EnumerateRunes())
                normalized.Append(Rune.GetUnicodeCategory(rune) == UnicodeCategory.DecimalDigitNumber ? ((char)('0' + (int)Rune.GetNumericValue(rune))).ToString() : rune.ToString());
            var digits = @base == 2 ? "[01]" : "[0-9a-fA-F]";
            var prefix = @base == 2 ? "0[bB]" : "0[xX]";
            return Regex.IsMatch(normalized.ToString(), @"\A[+-]?(?:" + prefix + "_?)?" + digits + "(?:_?" + digits + @")*\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
    }
}
