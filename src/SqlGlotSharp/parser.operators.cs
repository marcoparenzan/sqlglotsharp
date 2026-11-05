using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

// Operator group in upstream order. This revision of parser.py has no _parse_tokens
// member: each precedence level has its own loop. Do not invent a generic loop.
// All zero anchors are intentional: the operator dependency closure is incomplete.
// Type/column dependencies live in parser.columns.cs. Complex types, functions,
// brackets and subqueries remain explicit boundaries, not a reduced entrypoint.
// Existing generated Type tables remain the source of mappings.
public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser.COLUMN_OPERATORS #0000000000
        public static readonly PyDict<TokenType, Func<Parser, exp.Expr?, exp.Expr?, exp.Expr?>?> COLUMN_OPERATORS_ = ColumnBoundaries();
        public virtual PyDict<TokenType, Func<Parser, exp.Expr?, exp.Expr?, exp.Expr?>?> COLUMN_OPERATORS => COLUMN_OPERATORS_;
        private static PyDict<TokenType, Func<Parser, exp.Expr?, exp.Expr?, exp.Expr?>?> ColumnBoundaries()
        {
            var table = new PyDict<TokenType, Func<Parser, exp.Expr?, exp.Expr?, exp.Expr?>?> { [TokenType.DOT] = null };
            table[TokenType.DCOLON] = (self, @this, to) =>
                self.build_cast(self.STRICT_CAST, new PyDict<string, object?> { ["this"] = @this, ["to"] = to });
            table[TokenType.DOTCOLON] = (self, @this, to) => self.expression(new exp.JSONCast(@this: @this, to: to));
            table[TokenType.PLACEHOLDER] = (self, @this, key) => self.expression(new exp.JSONBContainsTopKey(@this: @this, expression: key));
            table[TokenType.ARROW] = build_json_extract;
            table[TokenType.DARROW] = build_json_extract_scalar;
            table[TokenType.HASH_ARROW] = build_jsonb_extract;
            table[TokenType.DHASH_ARROW] = build_jsonb_extract_scalar;
            return table;
        }

        // py: sqlglot/parser.py::Parser.JSON_OPERATORS #0000000000
        public static readonly PyDict<TokenType, Func<Parser, exp.Expr?, exp.Expr?, exp.Expr?>> JSON_OPERATORS_ = new();
        public virtual PyDict<TokenType, Func<Parser, exp.Expr?, exp.Expr?, exp.Expr?>> JSON_OPERATORS => JSON_OPERATORS_;

        // py: sqlglot/parser.py::Parser.UNARY_PARSERS #0000000000
        public static readonly PyDict<TokenType, Func<Parser, exp.Expr?>> UNARY_PARSERS_ = new()
        {
            [TokenType.PLUS] = self => self._parse_unary(), // Unary + is handled as a no-op
            [TokenType.NOT] = self => self.expression(new exp.Not(@this: self._parse_equality())),
            [TokenType.TILDE] = self => self.expression(new exp.BitwiseNot(@this: self._parse_unary())),
            [TokenType.DASH] = self => self.expression(new exp.Neg(@this: self._parse_unary())),
            [TokenType.PIPE_SLASH] = self => self.expression(new exp.Sqrt(@this: self._parse_unary())),
            [TokenType.DPIPE_SLASH] = self => self.expression(new exp.Cbrt(@this: self._parse_unary())),
        };
        public virtual PyDict<TokenType, Func<Parser, exp.Expr?>> UNARY_PARSERS => UNARY_PARSERS_;

        // py: sqlglot/parser.py::Parser.RANGE_PARSERS #0000000000
        public static readonly PyDict<TokenType, Func<Parser, exp.Expr?, exp.Expr?>> RANGE_PARSERS_ = RangeBoundaries();
        public virtual PyDict<TokenType, Func<Parser, exp.Expr?, exp.Expr?>> RANGE_PARSERS => RANGE_PARSERS_;

        // Table body of Parser.RANGE_PARSERS (anchored above).
        private static PyDict<TokenType, Func<Parser, exp.Expr?, exp.Expr?>> RangeBoundaries()
        {
            var table = new PyDict<TokenType, Func<Parser, exp.Expr?, exp.Expr?>>
            {
                [TokenType.AT_GT] = binary_range_parser(typeof(exp.ArrayContainsAll)),
                [TokenType.BETWEEN] = (self, @this) => self._parse_between(@this),
                [TokenType.GLOB] = binary_range_parser(typeof(exp.Glob)),
                [TokenType.ILIKE] = binary_range_parser(typeof(exp.ILike)),
                [TokenType.IN] = (self, @this) => self._parse_in(@this),
                [TokenType.IRLIKE] = binary_range_parser(typeof(exp.RegexpILike)),
                [TokenType.IS] = (self, @this) => self._parse_is(@this),
                [TokenType.LIKE] = binary_range_parser(typeof(exp.Like)),
                [TokenType.LT_AT] = binary_range_parser(typeof(exp.ArrayContainedBy)),
                [TokenType.OVERLAPS] = binary_range_parser(typeof(exp.Overlaps)),
                [TokenType.RLIKE] = binary_range_parser(typeof(exp.RegexpLike)),
                [TokenType.SIMILAR_TO] = binary_range_parser(typeof(exp.SimilarTo)),
                [TokenType.FOR] = (self, @this) => self._parse_comprehension(@this),
                [TokenType.QMARK_AMP] = binary_range_parser(typeof(exp.JSONBContainsAllTopKeys)),
                [TokenType.QMARK_PIPE] = binary_range_parser(typeof(exp.JSONBContainsAnyTopKeys)),
                [TokenType.HASH_DASH] = binary_range_parser(typeof(exp.JSONBDeleteAtPath)),
                [TokenType.AT_QMARK] = binary_range_parser(typeof(exp.JSONBPathExists)),
                [TokenType.ADJACENT] = binary_range_parser(typeof(exp.Adjacent)),
                [TokenType.OPERATOR] = (self, @this) => self._parse_operator(@this),
                [TokenType.AMP_LT] = binary_range_parser(typeof(exp.ExtendsLeft)),
                [TokenType.AMP_GT] = binary_range_parser(typeof(exp.ExtendsRight)),
                [TokenType.CARET_AT] = binary_range_parser(typeof(exp.StartsWith)),
            };
            return table;
        }

        // py: sqlglot/parser.py::Parser._parse_operator #0000000000
        public exp.Expr? _parse_operator(exp.Expr? @this)
        {
            if (!_match(TokenType.L_PAREN))
            {
                _retreat(_index - 1);
                return null;
            }

            var op = "";
            while (py.truthy(_curr) && !_match(TokenType.R_PAREN))
            {
                op += _curr.text;
                _advance();
            }

            var comments = _prev_comments;
            return expression(new exp.Operator(@this: @this, @operator: op, expression: _parse_bitwise()), comments: comments);
        }

        // py: sqlglot/parser.py::Parser._parse_comprehension #0000000000
        public exp.Expr? _parse_comprehension(exp.Expr? @this)
        {
            var index = _index;
            var expression_ = _parse_id_var();
            var position = _match(TokenType.COMMA) ? _parse_id_var() : null;

            if (!_match(TokenType.IN))
            {
                _retreat(index - 1);
                return null;
            }
            var iterator = _parse_column();
            var condition = _match_text_seq(["IF"]) ? _parse_disjunction() : null;
            return expression(new exp.Comprehension(@this: @this, expression: expression_, position: position,
                iterator: iterator, condition: condition));
        }

        // Type-valued generated dictionaries represent Python class callables.
        // Pass explicit null arguments via the generated dictionary constructor.
        internal static exp.Expr _operators__construct(Type klass, exp.Expr? @this, exp.Expr? expression) =>
            (exp.Expr)Activator.CreateInstance(klass, [new PyDict<string, object?>
                { ["this"] = @this, ["expression"] = expression }])!;

        // py: sqlglot/parser.py::Parser._parse_disjunction #0000000000
        public exp.Expr? _parse_disjunction()
        {
            var @this = _parse_conjunction();
            while (_match_set(DISJUNCTION.Keys.ToArray()))
            {
                var comments = _prev_comments;
                @this = expression(_operators__construct(DISJUNCTION[_prev.token_type], @this, _parse_conjunction()), comments: comments);
            }
            return @this;
        }

        // py: sqlglot/parser.py::Parser._parse_conjunction #0000000000
        public exp.Expr? _parse_conjunction()
        {
            var @this = _parse_equality();
            while (_match_set(CONJUNCTION.Keys.ToArray()))
            {
                var comments = _prev_comments;
                @this = expression(_operators__construct(CONJUNCTION[_prev.token_type], @this, _parse_equality()), comments: comments);
            }
            return @this;
        }

        // py: sqlglot/parser.py::Parser._build_predicate #0000000000
        public exp.Expr _build_predicate(Type klass, exp.Expr? @this, exp.Expr? expression,
            List<string>? comments = null, bool negate = false)
        {
            Type[] is_predicates = [typeof(exp.Is), typeof(exp.NullSafeEQ), typeof(exp.NullSafeNEQ)];
            if (is_predicates.Contains(klass))
            {
                if (@this is exp.Connector or exp.Not or exp.NullSafeNEQ
                    || (@this is not null && EQUALITY.Values.Contains(@this.GetType()))
                    || (@this is not null && COMPARISON.Values.Contains(@this.GetType()))
                    || (klass != typeof(exp.Is) && @this is exp.Is))
                    @this = new exp.Paren(@this: @this);
                if (expression is exp.Predicate or exp.Connector or exp.Not or exp.Escape)
                    expression = new exp.Paren(@this: expression);
            }
            else
            {
                var left = @this is exp.Not ? @this.@this : @this;
                if (left is exp.Is or exp.NullSafeEQ or exp.NullSafeNEQ) @this = new exp.Paren(@this: @this);
                var right = expression is exp.Not ? expression.@this : expression;
                if (right is exp.Is or exp.NullSafeEQ or exp.NullSafeNEQ) expression = new exp.Paren(@this: expression);
            }
            var predicate = _operators__construct(klass, @this, expression);
            if (negate) predicate.set("negate", true);
            return this.expression(predicate, comments: comments);
        }

        // py: sqlglot/parser.py::Parser._parse_equality #0000000000
        public exp.Expr? _parse_equality(bool parse_is = true)
        {
            var @this = _parse_comparison();
            while (true)
            {
                if (_match_set(EQUALITY.Keys.ToArray()))
                {
                    var comments = _prev_comments;
                    @this = _build_predicate(EQUALITY[_prev.token_type], @this, _parse_comparison(), comments: comments);
                }
                else if (parse_is && _is_predicate_ahead())
                {
                    var index = _index;
                    // A postfix predicate can itself be an operand, e.g. x IS NULL = FALSE.
                    @this = _parse_comparison(_parse_range(@this, parse_is: true));
                    if (_index == index) break;
                }
                else break;
            }
            return @this;
        }

        // py: sqlglot/parser.py::Parser._parse_comparison #0000000000
        public exp.Expr? _parse_comparison(exp.Expr? @this = null)
        {
            @this ??= _parse_range();
            while (_match_set(COMPARISON.Keys.ToArray()))
            {
                var comments = _prev_comments;
                @this = _build_predicate(COMPARISON[_prev.token_type], @this, _parse_range(), comments: comments);
            }
            return @this;
        }

        // py: sqlglot/parser.py::Parser._is_predicate_ahead #0000000000
        public bool _is_predicate_ahead()
        {
            if (IS_PREDICATE_PRECEDENCE == IsPredicatePrecedence.HIGH || !IS_PREDICATE_TOKENS.Contains(_curr.token_type))
                return false;
            return _curr.token_type != TokenType.NOT || _next.token_type == TokenType.NULL;
        }

        // py: sqlglot/parser.py::Parser._parse_range #0000000000
        public exp.Expr? _parse_range(exp.Expr? @this = null, bool parse_is = false)
        {
            @this ??= _parse_bitwise();
            while (true)
            {
                if (!parse_is && _is_predicate_ahead()) break;
                var negate = _match(TokenType.NOT);
                if (_match_set(RANGE_PARSERS.Keys.ToArray()))
                {
                    var expression = RANGE_PARSERS[_prev.token_type](this, @this);
                    if (expression is null) return @this;
                    @this = expression;
                }
                else if (_match(TokenType.ISNULL) || (negate && _match(TokenType.NULL)))
                    @this = _build_predicate(typeof(exp.Is), @this, new exp.Null());
                else if (_match(TokenType.NOTNULL))
                {
                    if (dialect.NORMALIZE_NOT_NULL)
                    {
                        @this = _build_predicate(typeof(exp.Is), @this, new exp.Null());
                        @this = expression(new exp.Not(@this: @this));
                    }
                    else @this = _build_predicate(typeof(exp.Is), @this, new exp.Null(), negate: true);
                }
                else
                {
                    if (negate) _retreat(_index - 1);
                    break;
                }
                if (negate) @this = _negate_range(@this);
                if ((negate || (@this is exp.Not && @this.@this is exp.Is))
                    && (_curr.token_type == TokenType.NOT || RANGE_PARSERS.ContainsKey(_curr.token_type)))
                    @this = expression(new exp.Paren(@this: @this));
            }
            return @this;
        }

        // py: sqlglot/parser.py::Parser._negate_range #0000000000
        public exp.Expr? _negate_range(exp.Expr? @this = null)
        {
            if (@this is null) return @this;
            var expression = @this is exp.Escape ? @this.@this : @this;
            if (expression is exp.Like or exp.ILike)
            {
                ((exp.Expr)expression).set("negate", true);
                return @this;
            }
            return this.expression(new exp.Not(@this: @this));
        }

        // py: sqlglot/parser.py::Parser._parse_bitwise #0000000000
        public exp.Expr? _parse_bitwise()
        {
            var @this = _parse_term();
            while (true)
            {
                if (_match_set(BITWISE.Keys.ToArray()))
                    @this = expression(_operators__construct(BITWISE[_prev.token_type], @this, _parse_term()));
                else if (dialect.DPIPE_IS_STRING_CONCAT && _match(TokenType.DPIPE))
                    @this = expression(new exp.DPipe(@this: @this, expression: _parse_term(), safe: !dialect.STRICT_STRING_CONCAT));
                else if (_match(TokenType.DQMARK))
                    @this = expression(new exp.Coalesce(@this: @this, expressions: helper.ensure_list(_parse_term())));
                else if (_match_pair(TokenType.LT, TokenType.LT))
                    @this = expression(new exp.BitwiseLeftShift(@this: @this, expression: _parse_term()));
                else if (_match_pair(TokenType.GT, TokenType.GT))
                    @this = expression(new exp.BitwiseRightShift(@this: @this, expression: _parse_term()));
                else if (JSON_OPERATORS.Count != 0 && _match_set(JSON_OPERATORS.Keys.ToArray()))
                    @this = JSON_OPERATORS[_prev.token_type](this, @this, _parse_term());
                else break;
            }
            return @this;
        }

        // py: sqlglot/parser.py::Parser._parse_term #0000000000
        public exp.Expr? _parse_term(bool parse_mod = true)
        {
            var @this = _parse_factor(parse_mod: parse_mod);
            while (_match_set(TERM.Keys.ToArray()))
            {
                var klass = TERM[_prev.token_type];
                var comments = _prev_comments;
                var expression = _parse_factor(parse_mod: parse_mod);
                @this = this.expression(_operators__construct(klass, @this, expression), comments: comments);
                if (@this is exp.Collate collate) _normalize_collate(collate);
            }
            return @this;
        }

        // py: sqlglot/parser.py::Parser._normalize_collate #0000000000
        public void _normalize_collate(exp.Collate collate)
        {
            var expr = collate.expression;
            // Preserve collations such as pg_catalog."default" (Postgres) as columns, otherwise
            // fallback to Identifier / Var
            if (expr is exp.Column column && column.parts.Count == 1)
            {
                var ident = column.@this;
                if (ident is exp.Identifier identifier)
                    collate.set("expression", identifier.quoted ? identifier : new exp.Var(@this: identifier.name));
            }
        }

        // py: sqlglot/parser.py::Parser._parse_factor #0000000000
        public exp.Expr? _parse_factor(bool parse_mod = true)
        {
            Func<exp.Expr?> parse_method = _parse_factor_operand;
            var @this = _parse_at_time_zone(parse_method());
            while (_match_set(FACTOR.Keys.ToArray(), advance: false))
            {
                if (!parse_mod && _curr.token_type == TokenType.MOD) break;
                _advance();
                var klass = FACTOR[_prev.token_type];
                var comments = _prev_comments;
                var expression = parse_method();
                if (expression is null && klass == typeof(exp.IntDiv) && _operators__isalpha(_prev.text))
                {
                    _retreat(_index - 1);
                    return @this;
                }
                @this = this.expression(_operators__construct(klass, @this, expression), comments: comments);
                if (@this is exp.Div)
                {
                    @this.set("typed", dialect.TYPED_DIVISION);
                    @this.set("safe", dialect.SAFE_DIVISION);
                }
            }
            return @this;
        }
        // Python str.isalpha uses Unicode Letter categories, including astral letters.
        private static bool _operators__isalpha(string text) => text.Length != 0 && text.EnumerateRunes().All(System.Text.Rune.IsLetter);

        // py: sqlglot/parser.py::Parser._parse_factor_operand #0000000000
        public exp.Expr? _parse_factor_operand() => EXPONENT.Count != 0 ? _parse_exponent() : _parse_unary();

        // py: sqlglot/parser.py::Parser._parse_exponent #0000000000
        public exp.Expr? _parse_exponent()
        {
            var @this = _parse_unary();
            while (_match_set(EXPONENT.Keys.ToArray()))
            {
                var comments = _prev_comments;
                @this = expression(_operators__construct(EXPONENT[_prev.token_type], @this, _parse_unary()), comments: comments);
            }
            return @this;
        }

        // py: sqlglot/parser.py::Parser._parse_unary #0000000000
        public exp.Expr? _parse_unary()
        {
            if (_match_set(UNARY_PARSERS.Keys.ToArray())) return UNARY_PARSERS[_prev.token_type](this);
            return _parse_type();
        }

        // Module constant TIME_ZONE_RE of sqlglot/parser.py
        private static readonly py.re.Pattern TIME_ZONE_RE = py.re.compile(@":.*?[a-zA-Z\+\-]");

        // Parser.TYPE_LITERAL_PARSERS of sqlglot/parser.py
        public static readonly Dictionary<exp.DType, Func<Parser, exp.Expr?, exp.DataType, exp.Expr>> TYPE_LITERAL_PARSERS_ = new()
        {
            [exp.DType.JSON] = (self, @this, _) => self.expression(Mk<exp.ParseJSON>(("this", @this), ("is_literal", true))),
        };
        public virtual Dictionary<exp.DType, Func<Parser, exp.Expr?, exp.DataType, exp.Expr>> TYPE_LITERAL_PARSERS => TYPE_LITERAL_PARSERS_;

        // py: sqlglot/parser.py::Parser._parse_type #0000000000
        public exp.Expr? _parse_type(bool parse_interval = true, bool fallback_to_identifier = false)
        {
            if (!fallback_to_identifier && _parse_atom() is { } atom) return atom;

            if (parse_interval && _parse_interval() is { } interval) return _parse_column_ops(interval);

            var index = _index;
            var data_type = _parse_types(check_func: true, allow_identifiers: false);
            if (data_type is exp.Cast) return _parse_column_ops(data_type);
            if (data_type is not null)
            {
                var index2 = _index;
                var primary = _parse_primary();

                if (primary is exp.Literal)
                {
                    var literal = primary.name;
                    var column_ops = _parse_column_ops(primary);

                    if (ZONE_AWARE_TIMESTAMP_CONSTRUCTOR && TIME_ZONE_RE.search(literal) is not null)
                    {
                        if (data_type.is_type(exp.DType.TIMESTAMP)) data_type = new exp.DataType(@this: exp.DType.TIMESTAMPTZ);
                        else if (data_type.is_type(exp.DType.TIME)) data_type = new exp.DataType(@this: exp.DType.TIMETZ);
                    }

                    if (data_type is exp.DataType typed && typed.@this is exp.DType dtype && TYPE_LITERAL_PARSERS.TryGetValue(dtype, out var literal_parser))
                        return literal_parser(this, column_ops, typed);

                    return expression(new exp.Cast(@this: column_ops, to: data_type));
                }

                if (data_type.expressions.Count > 0 && index2 - index > 1)
                {
                    _retreat(index2);
                    return _parse_column_ops(data_type);
                }
                // Bare type keywords may be column names (e.g. PostgreSQL NAME).
                _retreat(index);
            }

            if (fallback_to_identifier) return _parse_id_var();
            return _parse_column();
        }

        // py: sqlglot/parser.py::Parser._parse_at_time_zone #0000000000
        public exp.Expr? _parse_at_time_zone(exp.Expr? @this)
        {
            if (!_match_text_seq(["AT", "TIME", "ZONE"])) return @this;
            return _parse_at_time_zone(expression(new exp.AtTimeZone(@this: @this, zone: _parse_unary())));
        }

        // py: sqlglot/parser.py::Parser._parse_atom #0000000000
        public exp.Expr? _parse_atom()
        {
            if (IDENTIFIER_TOKENS.Contains(_curr.token_type)) return _parse_column();
            var token = _curr;
            var token_type = token.token_type;
            if (!PRIMARY_PARSERS.TryGetValue(token_type, out var primary_parser)) return null;
            var next_type = _next.token_type;
            if (COLUMN_OPERATORS.ContainsKey(next_type) || COLUMN_POSTFIX_TOKENS.Contains(next_type)
                || (token_type == TokenType.STRING && next_type == TokenType.STRING)) return null;
            _advance();
            return primary_parser(this, token);
        }
    }
}
