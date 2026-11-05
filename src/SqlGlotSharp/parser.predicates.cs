using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

// Predicate group from parser.py. Dependency boundaries (UNNEST, set
// operations, types and column accessors) remain explicit at their owners.
public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser._parse_is #9d4bf21275
        public exp.Expr? _parse_is(exp.Expr? @this)
        {
            var index = _index - 1;
            var negate = _match(TokenType.NOT);

            if (_match_text_seq(["DISTINCT", "FROM"]))
            {
                var klass = negate ? typeof(exp.NullSafeEQ) : typeof(exp.NullSafeNEQ);
                return _build_predicate(klass, @this, _parse_is_rhs(distinct: true));
            }

            exp.Expr? expression;
            if (_match(TokenType.JSON))
            {
                object kind = _match_texts(IS_JSON_PREDICATE_KIND) ? _prev.text.ToUpperInvariant() : false;
                bool? _with;
                if (_match_text_seq(["WITH"])) _with = true;
                else if (_match_text_seq(["WITHOUT"])) _with = false;
                else _with = null;
                var unique = _match(TokenType.UNIQUE);
                _match_text_seq(["KEYS"]);
                expression = this.expression(new exp.JSON(@this: kind, with_: _with, unique: unique));
            }
            else
            {
                expression = _parse_is_rhs();
                if (!py.truthy(expression))
                {
                    _retreat(index);
                    return null;
                }
            }

            if (negate && expression is exp.Null && !dialect.NORMALIZE_NOT_NULL)
                @this = _build_predicate(typeof(exp.Is), @this, expression, negate: true);
            else
            {
                @this = _build_predicate(typeof(exp.Is), @this, expression);
                @this = negate ? this.expression(new exp.Not(@this: @this)) : @this;
            }
            return _parse_column_ops(@this);
        }

        // py: sqlglot/parser.py::Parser._parse_is_rhs #9064bbff15
        public exp.Expr? _parse_is_rhs(bool distinct = false)
        {
            if (distinct)
            {
                if (IS_PREDICATE_PRECEDENCE == IsPredicatePrecedence.LOW) return _parse_equality(parse_is: false);
                if (IS_PREDICATE_PRECEDENCE == IsPredicatePrecedence.EQUALITY) return _parse_comparison();
                if (IS_PREDICATE_PRECEDENCE == IsPredicatePrecedence.COMPARISON) return _parse_range();
                return _parse_bitwise();
            }
            return _parse_null() ?? _parse_bitwise();
        }

        // py: sqlglot/parser.py::Parser._parse_in #8794a88700
        public exp.In _parse_in(exp.Expr? @this, bool alias = false)
        {
            exp.In result;
            var unnest = _parse_unnest(with_alias: false);
            if (unnest is not null)
                result = expression(new exp.In(@this: @this, unnest: unnest));
            else if (_match_set([TokenType.L_PAREN, TokenType.L_BRACKET]))
            {
                var matched_l_paren = _prev.token_type == TokenType.L_PAREN;
                var expressions = _parse_csv(() => _parse_select_or_expression(alias: alias));
                if (expressions.Count == 1 && expressions[0] is exp.Query)
                {
                    result = expression(new exp.In(@this: @this,
                        query: _parse_query_modifiers(expressions[0]!)!.subquery(copy: false)));
                }
                else
                    result = expression(new exp.In(@this: @this, expressions: expressions.Cast<object?>().ToList()));
                if (matched_l_paren) _match_r_paren(result);
                else if (!_match(TokenType.R_BRACKET, expression: result)) raise_error("Expecting ]");
            }
            else
                result = expression(new exp.In(@this: @this, field: _parse_column()));
            return result;
        }

        // py: sqlglot/parser.py::Parser._parse_between #832b9afd01
        public exp.Between _parse_between(exp.Expr? @this)
        {
            // Preserve grouping when BETWEEN is rewritten into comparisons.
            if (@this is exp.Is or exp.NullSafeEQ or exp.NullSafeNEQ) @this = new exp.Paren(@this: @this);
            bool? symmetric = null;
            if (_match_text_seq(["SYMMETRIC"])) symmetric = true;
            else if (_match_text_seq(["ASYMMETRIC"])) symmetric = false;
            var low = _parse_bitwise();
            _match(TokenType.AND);
            var high = _parse_bitwise();
            return expression(new exp.Between(@this: @this, low: low, high: high, symmetric: symmetric));
        }

        // Necessary predicate dependencies, kept in upstream order.
        // py: sqlglot/parser.py::Parser._parse_null #ac351b3ea6
        public exp.Expr? _parse_null()
        {
            if (_match_set([TokenType.NULL, TokenType.UNKNOWN])) return PRIMARY_PARSERS[TokenType.NULL](this, _prev);
            return _parse_placeholder();
        }

        // py: sqlglot/parser.py::Parser._parse_select_or_expression #47df08807b
        public exp.Expr? _parse_select_or_expression(bool alias = false) =>
            _parse_set_operations(alias ? _parse_alias(_parse_assignment(), @explicit: true) : _parse_assignment())
            ?? _parse_select();
    }
}
