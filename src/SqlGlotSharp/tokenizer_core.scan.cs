namespace SqlGlotSharp;

public static partial class tokenizer_core
{
    public partial class TokenizerCore
    {
        // py: sqlglot/tokenizer_core.py::TokenizerCore.tokenize #0f15d3a862
        public List<Token> tokenize(string sql)
        {
            reset();
            this.sql = sql;
            size = py.len(sql);
            try { _scan(); }
            catch (Exception error)
            {
                var start = Math.Max(_current - 50, 0);
                var end = Math.Min(_current + 50, size - 1);
                var context = SliceSql(start, end);
                throw new errors.TokenError($"Error tokenizing '{context}'", start, end, error);
            }
            return tokens;
        }

        // py: sqlglot/tokenizer_core.py::TokenizerCore._scan #152956d9b2
        public virtual void _scan(bool check_semicolon = false)
        {
            while (size != 0 && !_end)
            {
                var current = _current;
                while (current < size && CodePoints[current] is " " or "\t") current++;
                var offset = current > _current ? current - _current : 1;
                _start = current;
                _advance(offset);
                if (!IsSpace(_char))
                {
                    if (IsDigit(_char)) _scan_number();
                    else if (identifiers.TryGetValue(_char, out var end)) _scan_identifier(end);
                    else _scan_keywords();
                }
                if (check_semicolon && _peek == ";") break;
            }
            if (tokens.Count != 0 && _comments.Count != 0) tokens[^1].comments.AddRange(_comments);
        }

        // py: sqlglot/tokenizer_core.py::TokenizerCore._scan_keywords #1f48fe99d6
        public void _scan_keywords()
        {
            var size = 0;
            string? word = null;
            var chars = _char;
            var character = chars;
            var prev_space = false;
            var skip = false;
            var trie = keyword_trie;
            var single_token = single_tokens.ContainsKey(character);
            while (chars.Length != 0)
            {
                if (!skip)
                {
                    if (!trie.TryGetValue(AsciiUpper(character), out var sub) || sub is null) break;
                    trie = (PyDict<object, object?>)sub;
                    if (trie.ContainsKey(0) || trie.ContainsKey(0L)) word = chars;
                }
                var end = _current + size;
                size++;
                if (end < this.size)
                {
                    character = CodePoints[end];
                    single_token = single_token || single_tokens.ContainsKey(character);
                    var is_space = IsSpace(character);
                    if (!is_space || !prev_space)
                    {
                        if (is_space) character = " ";
                        chars += character;
                        prev_space = is_space;
                        skip = false;
                    }
                    else skip = true;
                }
                else { character = ""; break; }
            }
            if (!string.IsNullOrEmpty(word))
            {
                if (_scan_string(word) || _scan_comment(word)) return;
                if (prev_space || single_token || character.Length == 0)
                {
                    word = py.upper(word);
                    if (keywords.TryGetValue(word, out var token_type))
                    { _advance(size - 1); _add(token_type, word); return; }
                }
            }
            if (single_tokens.TryGetValue(_char, out var single)) { _add(single, _char); return; }
            _scan_var();
        }
    }
}
