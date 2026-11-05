using Token = SqlGlotSharp.tokenizer_core.Token;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

// Callable tables and primary/literal/parameter group, in upstream order.
// Zero anchors denote partial members, not completed ports. Deep expression
// parsing is deliberately bounded at call sites; no reduced SQL entrypoint.
// Partial dependency-closure members retain zero anchors even where their local
// algorithm is exact. Upstream normalized hashes (port_status inventory):
// PRIMARY_PARSERS=310c3c021b, PLACEHOLDER_PARSERS=862cd55fb8,
// _parse_primary=d7f8a6e740, _parse_string=eb3fe42329, _parse_number=23f120d1b0,
// _parse_identifier=163a142fe2, _parse_var=71e2f8e46d, _parse_var_or_string=668be34324,
// _parse_primary_or_var=d66b686b4d, _parse_parameter=c2f5ee081b,
// _parse_placeholder=a3a83130fb. Promote only after deep dependencies are ported.
public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser.STRING_PARSERS #4de3fbe678
        public static readonly PyDict<TokenType, Func<Parser, Token, exp.Expr?>> STRING_PARSERS_ = new()
        {
            [TokenType.HEREDOC_STRING] = (self, token) => self.expression(new exp.RawString(@this: token.text), token),
            [TokenType.NATIONAL_STRING] = (self, token) => self.expression(new exp.National(@this: token.text), token),
            [TokenType.NATIONAL_RAW_STRING] = (self, token) => self.expression(new exp.National(@this: token.text), token),
            [TokenType.RAW_STRING] = (self, token) => self.expression(new exp.RawString(@this: token.text), token),
            [TokenType.STRING] = (self, token) => self.expression(new exp.Literal(@this: token.text, is_string: true), token),
            [TokenType.UNICODE_STRING] = (self, token) => self.expression(new exp.UnicodeString(
                @this: token.text, escape: self._match_text_seq(["UESCAPE"]) ? self._parse_string() : false), token),
        };
        public virtual PyDict<TokenType, Func<Parser, Token, exp.Expr?>> STRING_PARSERS => STRING_PARSERS_;

        // py: sqlglot/parser.py::Parser.NUMERIC_PARSERS #5955032154
        public static readonly PyDict<TokenType, Func<Parser, Token, exp.Expr?>> NUMERIC_PARSERS_ = new()
        {
            [TokenType.BIT_STRING] = (self, token) => self.expression(new exp.BitString(@this: token.text), token),
            [TokenType.BYTE_STRING] = (self, token) => self.expression(new exp.ByteString(@this: token.text,
                is_bytes: self.dialect.BYTE_STRING_IS_BYTES_TYPE ? true : null), token),
            [TokenType.HEX_STRING] = (self, token) => self.expression(new exp.HexString(@this: token.text,
                is_integer: self.dialect.HEX_STRING_IS_INTEGER_TYPE ? true : null), token),
            [TokenType.NUMBER] = (self, token) => self.expression(new exp.Literal(@this: token.text, is_string: false), token),
        };
        public virtual PyDict<TokenType, Func<Parser, Token, exp.Expr?>> NUMERIC_PARSERS => NUMERIC_PARSERS_;

        // py: sqlglot/parser.py::Parser.PRIMARY_PARSERS #0000000000
        public static readonly PyDict<TokenType, Func<Parser, Token, exp.Expr?>> PRIMARY_PARSERS_ = PrimaryTable();
        public virtual PyDict<TokenType, Func<Parser, Token, exp.Expr?>> PRIMARY_PARSERS => PRIMARY_PARSERS_;

        // Unanchored table construction adapter (Python dictionary unpacking).
        private static PyDict<TokenType, Func<Parser, Token, exp.Expr?>> PrimaryTable()
        {
            var table = new PyDict<TokenType, Func<Parser, Token, exp.Expr?>>();
            foreach (var (key, value) in STRING_PARSERS_) table[key] = value;
            foreach (var (key, value) in NUMERIC_PARSERS_) table[key] = value;
            table[TokenType.INTRODUCER] = (self, token) => self._parse_introducer__boundary(token);
            table[TokenType.NULL] = (self, _) => self.expression(new exp.Null());
            table[TokenType.TRUE] = (self, _) => self.expression(new exp.Boolean(@this: true));
            table[TokenType.FALSE] = (self, _) => self.expression(new exp.Boolean(@this: false));
            table[TokenType.SESSION_PARAMETER] = (self, _) => throw new NotPortedException("sqlglot/parser.py::Parser._parse_session_parameter");
            table[TokenType.STAR] = (self, _) => throw new NotPortedException("sqlglot/parser.py::Parser._parse_star_ops");
            return table;
        }

        // py: sqlglot/parser.py::Parser.PLACEHOLDER_PARSERS #0000000000
        public static readonly PyDict<TokenType, Func<Parser, exp.Expr?>> PLACEHOLDER_PARSERS_ = new()
        {
            [TokenType.PLACEHOLDER] = self => self.expression(new exp.Placeholder()),
            [TokenType.PARAMETER] = self => self._parse_parameter(),
            [TokenType.COLON] = self => self._match_set(self.COLON_PLACEHOLDER_TOKENS)
                ? self.expression(new exp.Placeholder(@this: self._prev.text)) : null,
        };
        public virtual PyDict<TokenType, Func<Parser, exp.Expr?>> PLACEHOLDER_PARSERS => PLACEHOLDER_PARSERS_;

        // py: sqlglot/parser.py::Parser._parse_primary #0000000000
        public exp.Expr? _parse_primary()
        {
            if (_match_set(PRIMARY_PARSERS.Keys.ToArray()))
            {
                var token_type = _prev.token_type;
                var primary = PRIMARY_PARSERS[token_type](this, _prev);
                if (token_type == TokenType.STRING)
                {
                    List<object?> expressions = [primary];
                    while (_match(TokenType.STRING, advance: false))
                    {
                        if (_is_connected() && ADJACENT_STRINGS_CANNOT_BE_CONNECTED)
                            raise_error("Adjacent string literals need to be separated by whitespace or comments");
                        _advance();
                        expressions.Add(new exp.Literal(@this: _prev.text, is_string: true));
                    }
                    if (expressions.Count > 1)
                        return expression(new exp.Concat(expressions: expressions, coalesce: dialect.CONCAT_COALESCE));
                }
                return primary;
            }
            if (_match_pair(TokenType.DOT, TokenType.NUMBER))
                return new exp.Literal(@this: $"0.{_prev.text}", is_string: false);
            return _parse_paren();
        }

        // Unanchored introducer adapter, NOT a full _parse_introducer port.
        private exp.Expr _parse_introducer__boundary(Token token)
        {
            var literal = _parse_primary();
            return literal is not null
                ? expression(new exp.Introducer(@this: token.text, expression: literal), token)
                : _identifier_expression(token);
        }

        // py: sqlglot/parser.py::Parser._parse_string #0000000000
        public exp.Expr? _parse_string()
        {
            if (_match_set(STRING_PARSERS.Keys.ToArray())) return STRING_PARSERS[_prev.token_type](this, _prev);
            return _parse_placeholder();
        }
        // py: sqlglot/parser.py::Parser._parse_number #0000000000
        public exp.Expr? _parse_number()
        {
            if (_match_set(NUMERIC_PARSERS.Keys.ToArray())) return NUMERIC_PARSERS[_prev.token_type](this, _prev);
            return _parse_placeholder();
        }
        // py: sqlglot/parser.py::Parser._parse_identifier #0000000000
        public exp.Expr? _parse_identifier()
        {
            if (_match(TokenType.IDENTIFIER)) return _identifier_expression(quoted: true);
            return _parse_placeholder();
        }
        // py: sqlglot/parser.py::Parser._parse_var #0000000000
        public exp.Expr? _parse_var(bool any_token = false, IReadOnlyCollection<TokenType>? tokens = null, bool upper = false)
        {
            if ((any_token && py.truthy(_advance_any())) || _match(TokenType.VAR)
                || (tokens is { Count: > 0 } && _match_set(tokens)))
                return expression(new exp.Var(@this: upper ? py.upper(_prev.text) : _prev.text));
            return _parse_placeholder();
        }
        // py: sqlglot/parser.py::Parser._parse_var_or_string #0000000000
        public exp.Expr? _parse_var_or_string(bool upper = false) => _parse_string() ?? _parse_var(any_token: true, upper: upper);
        // py: sqlglot/parser.py::Parser._parse_primary_or_var #0000000000
        public exp.Expr? _parse_primary_or_var() => _parse_primary() ?? _parse_var(any_token: true);

        // py: sqlglot/parser.py::Parser._parse_parameter #0000000000
        public exp.Parameter _parse_parameter()
        {
            var @this = _parse_identifier() ?? _parse_primary_or_var();
            return expression(new exp.Parameter(@this: @this));
        }
        // py: sqlglot/parser.py::Parser._parse_placeholder #0000000000
        public exp.Expr? _parse_placeholder()
        {
            if (_match_set(PLACEHOLDER_PARSERS.Keys.ToArray()))
            {
                var placeholder = PLACEHOLDER_PARSERS[_prev.token_type](this);
                if (placeholder is not null) return placeholder;
                _advance(-1);
            }
            return null;
        }
    }
}
