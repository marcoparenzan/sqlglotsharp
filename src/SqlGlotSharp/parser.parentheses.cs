using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

// Ordinary parentheses and their expression dependency path, in upstream order.
// Zero hashes retain incomplete dependency closures. Assignment and function
// argument helpers live in parser.arguments.cs; missing grammar remains explicit.
public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser._can_parse_named_window #0000000000
        public bool _can_parse_named_window()
        {
            // `WINDOW` is in ID_VAR_TOKENS so it could be mistakenly consumed as an implicit alias.
            // Refuse only when the following tokens look like a named-window clause: `WINDOW <id> AS (`.
            if (!_match(TokenType.WINDOW, advance: false)) return false;
            var name = _index + 1 < _tokens.Count ? _tokens[checked((int)(_index + 1))] : null;
            if (name is null || !ID_VAR_TOKENS.Contains(name.token_type)) return false;
            var alias_tok = _index + 2 < _tokens.Count ? _tokens[checked((int)(_index + 2))] : null;
            if (alias_tok is null || alias_tok.token_type != TokenType.ALIAS) return false;
            var body = _index + 3 < _tokens.Count ? _tokens[checked((int)(_index + 3))] : null;
            return body is not null && body.token_type == TokenType.L_PAREN;
        }

        // py: sqlglot/parser.py::Parser._parse_expression #0000000000
        public exp.Expr? _parse_expression() => _parse_alias(_parse_assignment());

        // py: sqlglot/parser.py::Parser._parse_paren #0000000000
        public exp.Expr? _parse_paren()
        {
            if (!_match(TokenType.L_PAREN)) return null;
            var comments = _prev_comments;
            var query = _parse_select();
            var expressions = query is not null ? new List<exp.Expr> { query } : _parse_expressions();
            var @this = expressions.Count > 0 ? expressions[0] : null;
            if (@this is null && _match(TokenType.R_PAREN, advance: false))
                @this = expression(new exp.Tuple());
            else if (expressions.Count > 1 || _prev.token_type == TokenType.COMMA)
                @this = expression(new exp.Tuple(expressions: expressions.Cast<object?>().ToList()));
            else if (@this is exp.Select or exp.SetOperation)
                @this = _parse_subquery(@this, parse_alias: false);
            else if (@this is exp.Subquery or exp.Values)
                @this = _parse_subquery(_parse_query_modifiers(_parse_set_operations(@this)), parse_alias: false);
            else
                @this = expression(new exp.Paren(@this: @this));
            if (@this is not null) @this.add_comments(comments);
            _match_r_paren(expression: @this);
            if (@this is exp.Paren && @this.@this is exp.AggFunc)
                return _parse_window(@this);
            return @this;
        }

        // py: sqlglot/parser.py::Parser._parse_alias #0000000000
        public exp.Expr? _parse_alias(exp.Expr? @this, bool @explicit = false)
        {
            // In some dialects, LIMIT and OFFSET can act as both identifiers and keywords (clauses)
            // so this section tries to parse the clause version and if it fails, it treats the token
            // as an identifier (alias)
            if (_can_parse_limit_or_offset()) return @this;
            // WINDOW is in ID_VAR_TOKENS, so it can be consumed as an implicit alias. Detect the
            // named-window clause shape (`WINDOW <ident> AS (...)`) and avoid swallowing it.
            if (_can_parse_named_window()) return @this;
            var any_token = _match(TokenType.ALIAS);
            var comments = _prev_comments;
            if (@explicit && !any_token) return @this;
            if (_match(TokenType.L_PAREN))
            {
                var aliases = expression(new exp.Aliases(@this: @this,
                    expressions: _parse_csv(() => _parse_id_var(any_token)).Cast<object?>().ToList()), comments: comments);
                _match_r_paren(aliases);
                return aliases;
            }
            var alias = _parse_id_var(any_token, tokens: ALIAS_TOKENS)
                ?? (STRING_ALIASES ? _parse_string_as_identifier() : null);
            if (alias is not null)
            {
                comments.AddRange(alias.pop_comments() ?? []);
                @this = expression(new exp.Alias(@this: @this, alias: alias), comments: comments);
                var column = @this.@this as exp.Expr;
                // Moves the comment next to the alias in `expr /* comment */ AS alias`
                if (!py.truthy(@this.comments) && column is not null && py.truthy(column.comments))
                    @this.comments = column.pop_comments();
            }
            return @this;
        }

        // py: sqlglot/parser.py::Parser._parse_expressions #0000000000
        public List<exp.Expr> _parse_expressions() => _parse_csv(_parse_expression);

        // py: sqlglot/parser.py::Parser._match_r_paren #26d808375d
        public void _match_r_paren(exp.Expr? expression = null)
        {
            if (!_match(TokenType.R_PAREN, expression: expression)) raise_error("Expecting )");
        }
    }
}
