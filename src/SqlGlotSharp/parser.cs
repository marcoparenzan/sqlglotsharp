using Token = SqlGlotSharp.tokenizer_core.Token;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;
using ErrorLevel = SqlGlotSharp.errors.ErrorLevel;
using Dialect = SqlGlotSharp.dialects.dialect.Dialect;

namespace SqlGlotSharp;

public static partial class parser
{
    // py: sqlglot/parser.py::_resolve_dialect #1514281ffd
    public static Dialect _resolve_dialect(object? dialect) => Dialect.get_or_raise(dialect);
    // py: sqlglot/parser.py::SENTINEL_NONE #f9eab24927
    public static readonly Token SENTINEL_NONE = new(TokenType.SENTINEL, "SENTINEL");

    public partial class Parser
    {
        public ErrorLevel error_level;
        public int error_message_context;
        public int max_errors;
        public int max_nodes;
        public Dialect dialect;
        public string sql;
        public List<errors.ParseError> errors;
        public List<Token> _tokens;
        public long _tokens_size;
        public long _index;
        public Token _curr;
        public Token _next;
        public Token _prev;
        public List<string> _prev_comments;
        public int _pipe_cte_counter;
        public List<List<Token>> _chunks;
        public long _chunk_index;
        public int _node_count;

        public Parser() : this(null, 100, 3, -1, null) { }
        // py: sqlglot/parser.py::Parser.__init__ #13a2126d0c
        public Parser(ErrorLevel? error_level = null, int error_message_context = 100, int max_errors = 3, int max_nodes = -1, object? dialect = null)
        {
            this.error_level = error_level ?? ErrorLevel.IMMEDIATE;
            this.error_message_context = error_message_context;
            this.max_errors = max_errors;
            this.max_nodes = max_nodes;
            this.dialect = _resolve_dialect(dialect);
            sql = ""; errors = []; _tokens = []; _tokens_size = 0; _index = 0;
            _curr = SENTINEL_NONE; _next = SENTINEL_NONE; _prev = SENTINEL_NONE;
            _prev_comments = []; _pipe_cte_counter = 0; _chunks = []; _chunk_index = 0; _node_count = 0;
        }
        // py: sqlglot/parser.py::Parser.reset #9ba752ec04
        public void reset()
        {
            sql = ""; errors = []; _tokens = []; _tokens_size = 0; _index = 0;
            _curr = SENTINEL_NONE; _next = SENTINEL_NONE; _prev = SENTINEL_NONE;
            _prev_comments = []; _pipe_cte_counter = 0; _chunks = []; _chunk_index = 0; _node_count = 0;
        }
        private Token At(long index) => _tokens[checked((int)(index < 0 ? _tokens.Count + index : index))];
        // py: sqlglot/parser.py::Parser._advance #d580c088b5
        public void _advance(long times = 1)
        {
            var index = _index + times;
            _index = index;
            _curr = index < _tokens_size ? At(index) : SENTINEL_NONE;
            _next = index + 1 < _tokens_size ? At(index + 1) : SENTINEL_NONE;
            if (index > 0) { _prev = At(index - 1); _prev_comments = _prev.comments; }
            else { _prev = SENTINEL_NONE; _prev_comments = []; }
        }
        // py: sqlglot/parser.py::Parser._advance_chunk #cbf4bdaa9c
        public void _advance_chunk()
        {
            _index = -1;
            _tokens = _chunks[checked((int)_chunk_index)];
            _tokens_size = _tokens.Count;
            _chunk_index++;
            _advance();
        }
        // py: sqlglot/parser.py::Parser._retreat #dd2e6b4381
        public void _retreat(long index) { if (index != _index) _advance(index - _index); }
        // py: sqlglot/parser.py::Parser._add_comments #ea395495d7
        public void _add_comments(exp.Expr? expression)
        {
            if (expression is not null && _prev_comments.Count != 0) { expression.add_comments(_prev_comments); _prev_comments = []; }
        }
        // py: sqlglot/parser.py::Parser._match #f236c27947
        public bool _match(TokenType token_type, bool advance = true, exp.Expr? expression = null)
        {
            if (_curr.token_type != token_type) return false;
            if (advance) _advance();
            _add_comments(expression);
            return true;
        }
        // py: sqlglot/parser.py::Parser._match_set #da0b7137fc
        public bool _match_set(IReadOnlyCollection<TokenType> types, bool advance = true)
        {
            if (!types.Contains(_curr.token_type)) return false;
            if (advance) _advance();
            return true;
        }
        // py: sqlglot/parser.py::Parser._match_pair #d83eb5ab2d
        public bool _match_pair(TokenType token_type_a, TokenType token_type_b, bool advance = true)
        {
            if (_curr.token_type != token_type_a || _next.token_type != token_type_b) return false;
            if (advance) _advance(2);
            return true;
        }
        // py: sqlglot/parser.py::Parser._match_texts #51fecc2153
        public bool _match_texts(IEnumerable<string> texts, bool advance = true)
        {
            if (TEXT_MATCH_EXCLUDED_TOKENS.Contains(_curr.token_type) || !texts.Contains(py.upper(_curr.text))) return false;
            if (advance) _advance();
            return true;
        }
        // py: sqlglot/parser.py::Parser._match_text_seq #a2a1531fee
        public bool _match_text_seq(string[] texts, bool advance = true)
        {
            var index = _index;
            foreach (var text in texts)
            {
                if (!TEXT_MATCH_EXCLUDED_TOKENS.Contains(_curr.token_type) && py.upper(_curr.text) == text) _advance();
                else { _retreat(index); return false; }
            }
            if (!advance) _retreat(index);
            return true;
        }
        // py: sqlglot/parser.py::Parser._is_connected #e8ddc7411e
        public bool _is_connected() => py.truthy(_prev) && py.truthy(_curr) && _prev.end + 1 == _curr.start;
    }
}
