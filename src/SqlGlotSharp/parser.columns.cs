using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

// Column dependency group, in upstream method order. Zero anchors include the
// unavailable dependency closure. No SQL/statement entrypoint is introduced.
public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser.NO_PAREN_FUNCTION_PARSERS #0000000000
        public static readonly PyDict<string, Func<Parser, exp.Expr?>> NO_PAREN_FUNCTION_PARSERS_ = new()
        {
            ["ANY"] = self => self.expression(new exp.Any(@this: self._parse_bitwise())),
            ["CASE"] = self => self._parse_case(),
            ["CONNECT_BY_ROOT"] = self => self.expression(new exp.ConnectByRoot(@this: self._parse_column())),
            ["IF"] = self => self._parse_if(),
        };
        public virtual PyDict<string, Func<Parser, exp.Expr?>> NO_PAREN_FUNCTION_PARSERS => NO_PAREN_FUNCTION_PARSERS_;

        // py: sqlglot/parser.py::Parser._parse_column #0000000000
        public virtual exp.Expr? _parse_column()
        {
            var column = _parse_column_parts_fast();
            if (column is null)
            {
                var @this = _parse_column_reference();
                if (@this is null) @this = _parse_bracket(@this);
                column = @this is not null ? _parse_column_ops(@this) : @this;
            }
            if (column is not null)
            {
                if (dialect.SUPPORTS_COLUMN_JOIN_MARKS && _match(TokenType.JOIN_MARKER))
                    column.set("join_mark", true);
                if (COLON_IS_VARIANT_EXTRACT) column = _parse_colon_as_variant_extract(column);
            }
            return column;
        }

        // py: sqlglot/parser.py::Parser._parse_column_parts_fast #0000000000
        /// <summary>Fast path for simple column and dot references (a, a.b, ...).
        /// Greedily consumes VAR/IDENTIFIER tokens separated by DOTs, then checks
        /// that nothing complex follows. If it does, retreats and returns None so
        /// the slow path can handle it. For &gt;4 parts, wraps in exp.Dot nodes.</summary>
        public exp.Expr? _parse_column_parts_fast()
        {
            var index = _index;
            List<exp.Identifier>? parts = null;
            List<string>? all_comments = null;
            while (_match_set(IDENTIFIER_TOKENS))
            {
                var token = _prev;
                var comments = _prev_comments;
                if (parts is null && (NO_PAREN_FUNCTION_PARSERS.ContainsKey(py.upper(token.text)) || (_connect_prior && py.upper(token.text) == "PRIOR")))
                {
                    _retreat(index);
                    return null;
                }
                var has_dot = _match(TokenType.DOT);
                var curr_tt = _curr.token_type;
                if (!has_dot)
                {
                    if (COLUMN_OPERATORS.ContainsKey(curr_tt) || COLUMN_POSTFIX_TOKENS.Contains(curr_tt))
                    {
                        _retreat(index);
                        return null;
                    }
                }
                else if (!IDENTIFIER_TOKENS.Contains(curr_tt))
                {
                    _retreat(index);
                    return null;
                }
                parts ??= [];
                if (comments.Count != 0)
                {
                    all_comments ??= [];
                    all_comments.AddRange(comments);
                    _prev_comments = [];
                }
                parts.Add(expression(new exp.Identifier(@this: token.text,
                    quoted: token.token_type == TokenType.IDENTIFIER), token));
                if (!has_dot) break;
            }
            if (parts is null) return null;
            var n = parts.Count;
            exp.Expr column;
            if (n == 1) column = new exp.Column(@this: parts[0]);
            else if (n == 2) column = new exp.Column(@this: parts[1], table: parts[0]);
            else if (n == 3) column = new exp.Column(@this: parts[2], table: parts[1], db: parts[0]);
            else
            {
                column = new exp.Column(@this: parts[3], table: parts[2], db: parts[1], catalog: parts[0]);
                for (var i = 4; i < n; i++) column = new exp.Dot(@this: column, expression: parts[i]);
            }
            if (all_comments is { Count: > 0 }) column.add_comments(all_comments);
            return column;
        }

        // py: sqlglot/parser.py::Parser._parse_column_reference #0000000000
        public exp.Expr? _parse_column_reference()
        {
            var @this = _parse_field();
            if (@this is null && _match(TokenType.VALUES, advance: false) && VALUES_FOLLOWED_BY_PAREN
                && (!py.truthy(_next) || _next.token_type != TokenType.L_PAREN))
                @this = _parse_id_var();
            // We bubble up comments from the Identifier to the Column
            if (@this is exp.Identifier) @this = expression(new exp.Column(@this: @this), comments: @this.pop_comments());
            return @this;
        }

        // py: sqlglot/parser.py::Parser._parse_colon_as_variant_extract #0000000000
        public exp.Expr? _parse_colon_as_variant_extract(exp.Expr? @this)
        {
            if (_curr.token_type != TokenType.COLON) return @this;
            throw new NotPortedException("sqlglot/parser.py::Parser._parse_colon_as_variant_extract (JSON path/bracket/cast)");
        }

        // py: sqlglot/parser.py::Parser._parse_dcolon #0000000000
        public virtual exp.Expr? _parse_dcolon() => _parse_types();

        // py: sqlglot/parser.py::Parser._parse_column_ops #0000000000
        public exp.Expr? _parse_column_ops(exp.Expr? @this)
        {
            while (BRACKETS.Contains(_curr.token_type)) @this = _parse_bracket(@this);
            var column_operators = COLUMN_OPERATORS;
            var cast_column_operators = CAST_COLUMN_OPERATORS;
            while (py.truthy(_curr))
            {
                var op_token = _curr.token_type;
                if (!column_operators.TryGetValue(op_token, out var op)) break;
                _advance();
                exp.Expr? field;
                if (cast_column_operators.Contains(op_token))
                {
                    field = _parse_dcolon();
                    if (field is null) raise_error("Expected type");
                }
                else if (op is not null && py.truthy(_curr))
                {
                    field = _parse_column_reference() ?? _parse_bitwise();
                    if (field is exp.Column && _match(TokenType.DOT, advance: false))
                        field = _parse_column_ops(field);
                }
                else
                {
                    var dot = _is_connected() && _prev.token_type == TokenType.DOT;
                    field = _parse_field(any_token: true, anonymous_func: true);
                    // In t.true, t.null we should produce an Identifier node
                    if (dot && field is exp.Null or exp.Boolean)
                        field = expression(new exp.Identifier(@this: _prev.text), comments: field.comments);
                }
                // Function calls can be qualified, e.g., x.y.FOO(). Function
                // parsing stops explicitly before the transform/to_dot branch.
                // This converts the final AST to a series of Dots leading to the function call
                // https://cloud.google.com/bigquery/docs/reference/standard-sql/functions-reference#function_call_rules
                if (field is exp.Func or exp.Window && py.truthy(@this))
                    @this = (exp.Expr)@this!.transform(n => n is exp.Column column ? column.to_dot(include_dots: false) : n);

                if (op is not null) @this = op(this, @this, field);
                else if (@this is exp.Column && !py.truthy(@this.args.get("catalog")))
                    @this = expression(new exp.Column(@this: field, table: @this.@this,
                        db: @this.args.get("table"), catalog: @this.args.get("db")), comments: @this.comments);
                else if (field is exp.Window)
                {
                    // Move the exp.Dot's to the window's function
                    var window_func = expression(new exp.Dot(@this: @this, expression: field.@this));
                    field.set("this", window_func);
                    @this = field;
                }
                else @this = expression(new exp.Dot(@this: @this, expression: field));
                if (field?.comments is { Count: > 0 }) @this!.add_comments(field.pop_comments());
                @this = _parse_bracket(@this);
            }
            return @this;
        }

        // py: sqlglot/parser.py::Parser._parse_field #0000000000
        public exp.Expr? _parse_field(bool any_token = false, IReadOnlyCollection<TokenType>? tokens = null,
            bool anonymous_func = false)
        {
            var after_dot = SUPPORTS_DIGIT_PREFIXED_FIELD_NAMES && _prev.token_type == TokenType.DOT;
            var field = anonymous_func
                ? _parse_function(anonymous: anonymous_func, any_token: any_token) ?? _parse_primary()
                : _parse_primary() ?? _parse_function(anonymous: anonymous_func, any_token: any_token);
            field ??= _parse_id_var(any_token: any_token, tokens: tokens);
            if (after_dot && field is exp.Literal && field.is_number)
            {
                var name = field.name;
                if (_is_connected() && _parse_var(any_token: true) is not null) name += _prev.text;
                field = new exp.Identifier(@this: name, quoted: true).update_positions(field);
            }
            return field;
        }

        // Ordinary function-call group lives in parser.functions.cs. Special
        // grammar and window dependencies remain explicit NotPorted boundaries.

        // py: sqlglot/parser.py::Parser._parse_id_var #0000000000
        public virtual exp.Expr? _parse_id_var(bool any_token = true, IReadOnlyCollection<TokenType>? tokens = null)
        {
            var expression = _parse_identifier();
            if (expression is null && ((any_token && py.truthy(_advance_any()))
                || _match_set(tokens is { Count: > 0 } ? tokens : ID_VAR_TOKENS)))
            {
                var quoted = _prev.token_type == TokenType.STRING;
                expression = _identifier_expression(quoted: quoted);
            }
            return expression;
        }
    }
}
