using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

public static partial class parser
{
    public partial class Parser
    {
        // Bind the existing partial primary table after all static field initializers.
        // This keeps the primary group untouched and preserves dialect table copies.
        static Parser() => PRIMARY_PARSERS_[TokenType.STAR] = (self, _) => self._parse_star_ops();

        // py: sqlglot/parser.py::Parser._parse_star #0000000000
        public exp.Expr? _parse_star()
        {
            if (_match(TokenType.STAR)) return PRIMARY_PARSERS[TokenType.STAR](this, _prev);
            return _parse_placeholder();
        }

        // py: sqlglot/parser.py::Parser._parse_star_op #0000000000
        public List<exp.Expr>? _parse_star_op(params string[] keywords)
        {
            if (!_match_texts(keywords)) return null;
            if (_match(TokenType.L_PAREN, advance: false))
                return _parse_wrapped_csv(() => _parse_expression());

            var expression_ = _parse_alias(_parse_disjunction(), @explicit: true);
            return py.truthy(expression_) ? [expression_!] : null;
        }

        // py: sqlglot/parser.py::Parser._parse_star_ops #0000000000
        public exp.Expr? _parse_star_ops()
        {
            var star_token = _prev;
            if (_match_text_seq(["COLUMNS", "("], advance: false))
            {
                var columns = _parse_function();
                if (columns is exp.Columns) columns.set("unpack", true);
                return columns;
            }

            var index = _index;
            var ilike = _match(TokenType.ILIKE) ? _parse_string() : null;
            if (!py.truthy(ilike)) _retreat(index);

            return expression(Mk<exp.Star>(
                ("ilike", ilike),
                ("except_", _parse_star_op("EXCEPT", "EXCLUDE")?.Cast<object?>().ToList()),
                ("replace", _parse_star_op("REPLACE")?.Cast<object?>().ToList()),
                ("rename", _parse_star_op("RENAME")?.Cast<object?>().ToList()))).update_positions(star_token);
        }
    }
}
