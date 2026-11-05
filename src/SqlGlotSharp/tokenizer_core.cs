namespace SqlGlotSharp;

public static partial class tokenizer_core
{
    // py: sqlglot/tokenizer_core.py::Token #bacf3be091
    public class Token : IPyTruthiness, IPyRepresentation
    {
        // py: sqlglot/tokenizer_core.py::Token._attrs #c3b64f714b
        public static readonly IReadOnlyList<string> _attrs = Array.AsReadOnly(new[] { "token_type", "text", "line", "col", "start", "end", "comments" });
        // py: sqlglot/tokenizer_core.py::Token.__slots__ #ec08c3cd90
        public static readonly IReadOnlyList<string> __slots__ = _attrs;

        public TokenType token_type;
        public string text;
        public int line;
        public int col;
        public int start;
        public int end;
        public List<string> comments;

        // py: sqlglot/tokenizer_core.py::Token.number #592e1ee17e
        public static Token number(long number) => new(TokenType.NUMBER, py.str(number));
        // py: sqlglot/tokenizer_core.py::Token.string #a476b916c0
        public static Token @string(string @string) => new(TokenType.STRING, @string);
        // py: sqlglot/tokenizer_core.py::Token.identifier #5a80a0639f
        public static Token identifier(string identifier) => new(TokenType.IDENTIFIER, identifier);
        // py: sqlglot/tokenizer_core.py::Token.var #bbc00d50af
        public static Token @var(string @var) => new(TokenType.VAR, @var);

        // py: sqlglot/tokenizer_core.py::Token.__init__ #d49e5dfcdc
        public Token(TokenType token_type, string text, int line = 1, int col = 1, int start = 0, int end = 0, List<string>? comments = null)
        {
            this.token_type = token_type; this.text = text; this.line = line; this.col = col;
            this.start = start; this.end = end; this.comments = comments ?? [];
        }
        // py: sqlglot/tokenizer_core.py::Token.__bool__ #60f4febb6d
        public bool __bool__() => token_type != TokenType.SENTINEL;
        // py: sqlglot/tokenizer_core.py::Token.__repr__ #ce17bff583
        public string __repr__() => $"<Token token_type: TokenType.{token_type}, text: {text}, line: {line}, col: {col}, start: {start}, end: {end}, comments: {py.str(comments)}>";
        public override string ToString() => __repr__();
    }
}
