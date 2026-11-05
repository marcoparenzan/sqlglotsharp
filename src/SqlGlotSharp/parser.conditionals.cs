using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

public static partial class parser
{
    public partial class Parser
    {
        // Partial closure: expression dependencies and the interval-END SQL/builder
        // workaround are not fully ported. Do not claim a complete upstream anchor.
        // py: sqlglot/parser.py::Parser._parse_case #0000000000
        public exp.Expr? _parse_case()
        {
            if (_match(TokenType.DOT, advance: false))
            {
                _retreat(_index - 1);
                return null;
            }

            List<object?> ifs = [];
            exp.Expr? @default = null;
            var comments = _prev_comments;
            var operand = _parse_disjunction();
            while (_match(TokenType.WHEN))
            {
                var condition = _parse_disjunction();
                _match(TokenType.THEN);
                var result = _parse_disjunction();
                ifs.Add(expression(new exp.If(@this: condition, @true: result)));
            }
            if (_match(TokenType.ELSE)) @default = _parse_disjunction();
            if (!_match(TokenType.END))
            {
                if (@default is exp.Interval interval && interval.@this is exp.Expr interval_this
                    && py.upper(new generator.Generator().generate(interval_this, copy: false)) == "END")
                    @default = new exp.Column(@this: exp.to_identifier("interval"));
                else raise_error("Expected END after CASE", _prev);
            }
            return expression(new exp.Case(@this: operand, ifs: ifs, @default: @default), comments: comments);
        }

        // Partial closure: command parsing and assignment/alias dependencies retain
        // their explicit NotPorted boundaries.
        // py: sqlglot/parser.py::Parser._parse_if #0000000000
        public virtual exp.Expr? _parse_if()
        {
            exp.Expr result;
            if (_match(TokenType.L_PAREN))
            {
                var args = _parse_csv(() => _parse_alias(_parse_assignment(), @explicit: true))
                    .Cast<object?>().ToList();
                result = validate_expression(new exp.If().from_arg_list(args), args);
                _match_r_paren();
            }
            else
            {
                var index = _index - 1;
                if (NO_PAREN_IF_COMMANDS && index == 0)
                    throw new NotPortedException("sqlglot/parser.py::Parser._parse_as_command");
                var condition = _parse_disjunction();
                if (condition is null || !_match(TokenType.THEN))
                {
                    _retreat(index);
                    return null;
                }
                var @true = _parse_disjunction();
                var @false = _match(TokenType.ELSE) ? _parse_disjunction() : null;
                _match(TokenType.END);
                result = expression(new exp.If(@this: condition, @true: @true, @false: @false));
            }
            return result;
        }
    }
}
