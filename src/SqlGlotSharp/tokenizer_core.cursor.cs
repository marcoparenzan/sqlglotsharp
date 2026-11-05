using System.Globalization;
using System.Text;

namespace SqlGlotSharp;

public static partial class tokenizer_core
{
    public partial class TokenizerCore
    {
        private string? _indexedSql;
        private string[] _codePoints = [];
        private string[] CodePoints
        {
            get
            {
                if (!ReferenceEquals(_indexedSql, sql))
                {
                    List<string> points = [];
                    for (var i = 0; i < sql.Length; i++)
                    {
                        var length = char.IsHighSurrogate(sql[i]) && i + 1 < sql.Length && char.IsLowSurrogate(sql[i + 1]) ? 2 : 1;
                        points.Add(sql.Substring(i, length));
                        i += length - 1;
                    }
                    _codePoints = points.ToArray();
                    _indexedSql = sql;
                }
                return _codePoints;
            }
        }
        private string SliceSql(int start, int end) => string.Concat(py.slice((IReadOnlyList<string>)CodePoints, start, end));
        private static bool IsAlnum(string value)
        {
            if (value.Length == 0 || !Rune.TryGetRuneAt(value, 0, out var rune)) return false;
            return Rune.GetUnicodeCategory(rune) is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or
                UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or
                UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber;
        }

        // py: sqlglot/tokenizer_core.py::TokenizerCore._chars #544ff9e9ed
        public string _chars(int size)
        {
            if (size == 1) return _char;
            var start = _current - 1;
            var end = start + size;
            return end <= this.size ? SliceSql(start, end) : "";
        }

        // py: sqlglot/tokenizer_core.py::TokenizerCore._advance #147c403627
        public void _advance(int i = 1, bool alnum = false)
        {
            var character = _char;
            if (character is "\n" or "\r")
            {
                if (!(character == "\r" && _peek == "\n")) { _col = i; _line++; }
            }
            else _col += i;
            _current += i;
            _end = _current >= size;
            var points = CodePoints;
            var index = _current - 1;
            _char = points[index < 0 ? points.Length + index : index];
            _peek = _end ? "" : points[_current];
            if (alnum && IsAlnum(_char))
            {
                var col = _col; var current = _current; var end = _end; var peek = _peek;
                while (IsAlnum(peek))
                {
                    col++; current++; end = current >= size;
                    peek = end ? "" : points[current];
                }
                _col = col; _current = current; _end = end; _peek = peek; _char = points[current - 1];
            }
        }

        // py: sqlglot/tokenizer_core.py::TokenizerCore._text #6a013ed015
        public string _text => SliceSql(_start, _current);
    }
}
