using System.Text;

namespace SqlGlotSharp;

public static partial class tokenizer_core
{
    public partial class TokenizerCore
    {
        // py: sqlglot/tokenizer_core.py::TokenizerCore._add #b59e722eb4
        public void _add(TokenType token_type, string? text = null)
        {
            _prev_token_line = _line;
            if (_comments.Count != 0 && token_type == TokenType.SEMICOLON && tokens.Count != 0)
            {
                tokens[^1].comments.AddRange(_comments);
                _comments = [];
            }
            text ??= SliceSql(_start, _current);
            tokens.Add(new Token(token_type, text, _line, _col, _start, _current - 1, _comments));
            _comments = [];
            if (commands.Contains(token_type) && _peek != ";" &&
                (tokens.Count == 1 || command_prefix_tokens.Contains(tokens[^2].token_type)))
            {
                var start = _current;
                var count = tokens.Count;
                _scan(check_semicolon: true);
                tokens = tokens.GetRange(0, count);
                text = SliceSql(start, _current).Trim();
                if (text.Length != 0) _add(TokenType.STRING, text);
            }
        }

        // py: sqlglot/tokenizer_core.py::TokenizerCore._scan_var #4f666c3118
        public void _scan_var()
        {
            var var_single_tokens = this.var_single_tokens;
            var single_tokens = this.single_tokens;
            while (true)
            {
                var peek = _peek;
                if (peek.Length == 0 || IsSpace(peek)) break;
                if (!var_single_tokens.Contains(peek) && single_tokens.ContainsKey(peek)) break;
                _advance(alnum: true);
            }
            _add(tokens.Count != 0 && tokens[^1].token_type == TokenType.PARAMETER ? TokenType.VAR :
                keywords.get(py.upper(SliceSql(_start, _current)), TokenType.VAR));
        }

        private static bool IsSpace(string value) => value.Length != 0 &&
            (value.Length == 1 && value[0] is >= '\u001c' and <= '\u001f' ||
             Rune.TryGetRuneAt(value, 0, out var rune) && Rune.IsWhiteSpace(rune));


    }
}
