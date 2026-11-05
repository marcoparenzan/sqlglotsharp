using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SqlGlotSharp;

public static partial class tokenizer_core
{
    public partial class TokenizerCore
    {
        private static bool IsDigit(string value) => value.Length == 1 && value[0] is >= '0' and <= '9';
        private static string AsciiUpper(string value) => value.Length == 1 && value[0] is >= 'a' and <= 'z' ? ((char)(value[0] - 32)).ToString() : value;
        private static bool IsIdentifierStart(string value) => value == "_" || value.Length != 0 && Rune.TryGetRuneAt(value, 0, out var rune) &&
            (Rune.IsLetter(rune) || Rune.GetUnicodeCategory(rune) == UnicodeCategory.LetterNumber || value is "℘" or "℮");

        // py: sqlglot/tokenizer_core.py::TokenizerCore._scan_number #0538e9c048
        public void _scan_number()
        {
            if (_char == "0")
            {
                var peek = AsciiUpper(_peek);
                if (peek == "B" && has_bit_strings) { _scan_bits(); return; }
                if (peek == "X") { if (has_hex_strings) _scan_hex(); else _add(TokenType.NUMBER); return; }
            }
            var @decimal = false;
            var scientific = 0;
            var is_underscore_separated = false;
            var number_text = "";
            var numeric_literal = "";
            TokenType? numeric_type = null;
            while (true)
            {
                if (IsDigit(_peek))
                {
                    var end = _current + 1;
                    while (end < size && IsDigit(CodePoints[end])) end++;
                    _advance(end - _current);
                }
                else if (_peek == "." && !@decimal)
                {
                    if (tokens.Count != 0 && tokens[^1].token_type == TokenType.PARAMETER || !numbers_can_have_decimals) break;
                    @decimal = true; _advance();
                }
                else if (_peek is "-" or "+" && scientific == 1)
                {
                    if (_current + 1 < size && IsDigit(CodePoints[_current + 1])) { scientific++; _advance(); }
                    else break;
                }
                else if (AsciiUpper(_peek) == "E" && scientific == 0) { scientific++; _advance(); }
                else if (_peek == "_" && numbers_can_be_underscore_separated) { is_underscore_separated = true; _advance(); }
                else if (IsIdentifierStart(_peek))
                {
                    number_text = _text;
                    while (_peek.Length != 0 && !IsSpace(_peek) && !single_tokens.ContainsKey(_peek))
                    { numeric_literal += _peek; _advance(); }
                    if (keywords.TryGetValue(numeric_literals.get(numeric_literal.ToUpperInvariant(), ""), out var type)) numeric_type = type;
                    if (numeric_type.HasValue) break;
                    if (identifiers_can_start_with_digit) { _add(TokenType.VAR); return; }
                    _advance(-py.len(numeric_literal));
                    break;
                }
                else break;
            }
            if (number_text.Length == 0) number_text = SliceSql(_start, _current);
            if (is_underscore_separated) number_text = number_text.Replace("_", "");
            _add(TokenType.NUMBER, number_text);
            if (numeric_type.HasValue) { _add(TokenType.DCOLON, "::"); _add(numeric_type.Value, numeric_literal); }
        }

        // py: sqlglot/tokenizer_core.py::TokenizerCore._scan_bits #89d6924131
        public void _scan_bits()
        {
            _advance();
            var value = _extract_value();
            if (ValidRadix(value, 2)) _add(TokenType.BIT_STRING, py.slice(value, 2));
            else _add(TokenType.IDENTIFIER);
        }
        // py: sqlglot/tokenizer_core.py::TokenizerCore._scan_hex #0db6541fe2
        public void _scan_hex()
        {
            _advance();
            var value = _extract_value();
            if (ValidRadix(value, 16)) _add(TokenType.HEX_STRING, py.slice(value, 2));
            else _add(TokenType.IDENTIFIER);
        }
        private static bool ValidRadix(string value, int radix)
        {
            var normalized = new StringBuilder();
            foreach (var rune in value.EnumerateRunes())
                normalized.Append(Rune.GetUnicodeCategory(rune) == UnicodeCategory.DecimalDigitNumber ? ((char)('0' + (int)Rune.GetNumericValue(rune))).ToString() : rune.ToString());
            var digits = radix == 2 ? "[01]" : "[0-9a-fA-F]";
            var prefix = radix == 2 ? "0[bB]" : "0[xX]";
            return Regex.IsMatch(normalized.ToString(), @"\A" + prefix + "_?" + digits + "(?:_?" + digits + @")*\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }

        // py: sqlglot/tokenizer_core.py::TokenizerCore._extract_value #1c66951518
        public string _extract_value()
        {
            while (true)
            {
                var character = _peek.Trim();
                if (character.Length != 0 && !single_tokens.ContainsKey(character)) _advance(alnum: true);
                else break;
            }
            return _text;
        }
    }
}
