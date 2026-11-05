using System.Text;

namespace SqlGlotSharp;

public static partial class tokenizer_core
{
    public partial class TokenizerCore
    {
        private static string ByteLiteralText(string text) => text.All(c => c <= 127) ? text : Encoding.Latin1.GetString(StrictUtf8.GetBytes(text));

        // py: sqlglot/tokenizer_core.py::TokenizerCore._extract_string #8f5a1d6556
        public string _extract_string(string delimiter, HashSet<string>? escapes = null, bool raw_string = false,
            bool raise_unmatched = true, bool bytes_literal = false)
        {
            var text = new StringBuilder();
            var delim_size = py.len(delimiter);
            escapes ??= string_escapes;
            var points = CodePoints;
            if (delim_size == 1)
            {
                var pos = _current - 1;
                var end = Array.IndexOf(points, delimiter, pos);
                if (end != -1 && (end + 1 >= size || points[end + 1] != delimiter || !escapes.Contains(delimiter)) &&
                    (!(unescaped_sequences.Count != 0 || escapes.Contains("\\")) || Array.IndexOf(points, "\\", pos, end - pos) == -1))
                {
                    var newlines = 0;
                    var last_newline = -1;
                    for (var i = pos; i < end; i++) if (points[i] == "\n") { newlines++; last_newline = i; }
                    if (newlines != 0) { _line += newlines; _col = end - last_newline; }
                    else _col += end - pos;
                    _current = end + 1;
                    _end = _current >= size;
                    _char = points[end];
                    _peek = _end ? "" : points[_current];
                    var result = SliceSql(pos, end);
                    return bytes_literal ? ByteLiteralText(result) : result;
                }
            }
            while (true)
            {
                var backslash_escape = !raw_string && _char == "\\" && escapes.Contains("\\");
                if (backslash_escape && numeric_escapes.Count != 0)
                {
                    var decoded = _scan_numeric_escape();
                    if (decoded.Length != 0) { text.Append(decoded); continue; }
                }
                if (!raw_string && unescaped_sequences.Count != 0 && escapes.Contains(_char) && _current + 1 < size &&
                    unescaped_sequences.TryGetValue(_char + _peek, out var sequence))
                {
                    if (_peek is "\n" or "\r") { _advance(); _advance(); }
                    else _advance(2);
                    text.Append(sequence);
                    continue;
                }
                if (backslash_escape && drop_unknown_escapes && _current + 1 < size)
                {
                    var peek = _peek;
                    _advance(); _advance();
                    text.Append(bytes_literal ? ByteLiteralText(peek) : peek);
                    continue;
                }
                var escaped_delimiter = _peek == delimiter || delim_size > 1 &&
                    _peek == py.slice(delimiter, 0, 1) && quotes.ContainsKey(_peek);
                if ((string_escapes_allowed_in_raw_strings || !raw_string) && escapes.Contains(_char) &&
                    (escaped_delimiter || escapes.Contains(_peek)) && (!quotes.ContainsKey(_char) || _char == _peek))
                {
                    text.Append(escaped_delimiter ? !raw_string ? _peek : _char + _peek : _char + _peek);
                    if (_current + 1 < size) _advance(2);
                    else throw new errors.TokenError($"Missing {delimiter} from {_line}:{_current}");
                }
                else
                {
                    if (_chars(delim_size) == delimiter)
                    {
                        if (delim_size > 1) _advance(delim_size - 1);
                        break;
                    }
                    if (_end)
                    {
                        if (!raise_unmatched) return text.ToString() + _char;
                        throw new errors.TokenError($"Missing {delimiter} from {_line}:{_start}");
                    }
                    var current = _current - 1;
                    _advance(alnum: true);
                    var chunk = SliceSql(current, _current - 1);
                    text.Append(bytes_literal ? ByteLiteralText(chunk) : chunk);
                }
            }
            return text.ToString();
        }
    }
}
