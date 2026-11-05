using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

public static partial class parser
{
    public partial class Parser
    {
        // Partial expression closure: unsupported expression branches remain explicit.
        // The upstream `values` parameter is retained even though it is unused.
        // py: sqlglot/parser.py::Parser._parse_value #0000000000
        public exp.Tuple? _parse_value(bool values = true)
        {
            exp.Expr? ParseValueExpression()
            {
                if (dialect.SUPPORTS_VALUES_DEFAULT && _match(TokenType.DEFAULT))
                    return new exp.Var(@this: py.upper(_prev.text));
                return _parse_expression();
            }

            if (_match(TokenType.L_PAREN))
            {
                var expressions = _parse_csv(ParseValueExpression);
                _match_r_paren();
                return expression(new exp.Tuple(expressions: expressions.Cast<object?>().ToList()));
            }

            var value = _parse_expression();
            return value is not null
                ? expression(new exp.Tuple(expressions: new List<object?> { value }))
                : null;
        }

        // Local builder closure preserves copy=True for SELECT/from_ and alias_.
        // py: sqlglot/parser.py::Parser._values_to_select #0000000000
        public exp.Select _values_to_select(exp.Values values)
        {
            // exp.select("*") parses the synthetic input and retains its source positions.
            var star = new exp.Star();
            star.update_positions(line: 1, col: 1, start: 0, end: 0);
            var select = new exp.Select(expressions: new List<object?> { star });
            foreach (var key in "match laterals joins connect pivots prewhere where group having qualify windows distribute sort cluster order limit offset locks sample settings format options for_".Split(' '))
            {
                var modifier = values.args.get(key);
                if (!py.truthy(modifier)) continue;
                values.set(key, null);
                select.set(key, modifier);
            }
            exp.Values source = values;
            if (!py.truthy(values.args.get("alias")))
            {
                source = (exp.Values)values.copy();
                source.set("alias", new exp.TableAlias(@this: new exp.Identifier(@this: "_values", quoted: false)));
            }
            var result = (exp.Select)select.copy();
            result.set("from_", new exp.From(@this: source));
            return result;
        }

        // No separate _parse_values exists in this upstream revision: this is the
        // production VALUES parser. _values_to_select remains an explicit boundary
        // in the CTE and set-operation callers; it needs expression builder closure.
        // py: sqlglot/parser.py::Parser._parse_derived_table_values #0000000000
        public exp.Values? _parse_derived_table_values(bool allow_value_synonym = false)
        {
            var is_derived = _match_pair(TokenType.L_PAREN, TokenType.VALUES);
            if (!is_derived && !(_match_text_seq(["VALUES"]) || _match_text_seq(["FORMAT", "VALUES"])
                || (allow_value_synonym && _match_text_seq(["VALUE"])))) return null;

            var expressions = _parse_csv(() => _parse_value());
            var alias = _parse_table_alias();
            if (is_derived) _match_r_paren();
            return expression(new exp.Values(expressions: expressions.Cast<object?>().ToList(),
                alias: alias ?? _parse_table_alias()));
        }
    }
}
