namespace SqlGlotSharp;

public static partial class tokenizer_core
{
    public partial class TokenizerCore
    {
        // py: sqlglot/tokenizer_core.py::TokenizerCore._scan_comment #027dadb1b2
        public bool _scan_comment(string comment_start)
        {
            if (!comments.TryGetValue(comment_start, out var comment_end)) return false;
            if (comment_start == "--" && dash_comment_requires_boundary)
            {
                var boundary_index = _current + 1;
                if (boundary_index < size)
                {
                    var boundary_char = CodePoints[boundary_index];
                    var code = boundary_char.Length == 1 ? boundary_char[0] : char.ConvertToUtf32(boundary_char, 0);
                    if (!(IsSpace(boundary_char) || code < 32 || code == 127)) return false;
                }
            }
            var comment_start_line = _line;
            var comment_start_size = py.len(comment_start);
            if (!string.IsNullOrEmpty(comment_end))
            {
                _advance(comment_start_size);
                var comment_count = 1;
                var comment_end_size = py.len(comment_end);
                while (!_end)
                {
                    if (_chars(comment_end_size) == comment_end)
                    {
                        comment_count--;
                        if (comment_count == 0) break;
                    }
                    _advance(alnum: true);
                    if (nested_comments && !_end && _chars(comment_end_size) == comment_start)
                    { _advance(comment_start_size); comment_count++; }
                }
                _comments.Add(py.slice(_text, comment_start_size, -comment_end_size + 1));
                _advance(comment_end_size - 1);
            }
            else
            {
                var peek = _peek;
                var terminates_at_carriage_return = !comments_terminate_at_newline_only;
                while (!_end && peek != "\n" && (!terminates_at_carriage_return || peek != "\r"))
                { _advance(alnum: true); peek = _peek; }
                _comments.Add(py.slice(_text, comment_start_size));
            }
            if (comment_start == hint_start && tokens.Count != 0 && tokens_preceding_hint.Contains(tokens[^1].token_type))
                _add(TokenType.HINT);
            if (comment_start_line == _prev_token_line)
            {
                tokens[^1].comments.AddRange(_comments);
                _comments = [];
                _prev_token_line = _line;
            }
            return true;
        }
    }
}
