using Token = SqlGlotSharp.tokenizer_core.Token;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

// Dependency-complete leaf helpers from parser.py, in upstream method order.
// Literal dispatch tables, placeholders and full SQL parsing are not implemented here.
public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser._advance_any #a7ca02d29e
        public Token? _advance_any(bool ignore_reserved = false)
        {
            if (py.truthy(_curr) && (ignore_reserved || !RESERVED_TOKENS.Contains(_curr.token_type)))
            {
                _advance();
                return _prev;
            }
            return null;
        }

        // py: sqlglot/parser.py::Parser._parse_csv #4449efb3b2
        public List<T> _parse_csv<T>(Func<T?> parse_method, TokenType sep = TokenType.COMMA) where T : class
        {
            var parse_result = parse_method();
            List<T> items = parse_result is not null ? [parse_result] : [];
            while (_match(sep))
            {
                if (parse_result is exp.Expr expression) _add_comments(expression);
                parse_result = parse_method();
                if (parse_result is not null) items.Add(parse_result);
            }
            return items;
        }

        // py: sqlglot/parser.py::Parser._identifier_expression #16354317a8
        public exp.Identifier _identifier_expression(Token? token = null, bool? quoted = null)
        {
            token = py.truthy(token) ? token! : _prev;
            return expression(new exp.Identifier(@this: token.text, quoted: quoted), token);
        }
    }
}
