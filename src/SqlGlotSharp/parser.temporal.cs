using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

public static partial class parser
{
    public partial class Parser
    {
        // Partial dependency closure: arbitrary unit functions still depend on FUNCTION_PARSERS.
        // py: sqlglot/parser.py::Parser._parse_extract #0000000000
        public exp.Expr _parse_extract()
        {
            var unit = _parse_function() ?? _parse_var_or_string(upper: true);
            if (_match(TokenType.FROM))
                return expression(new exp.Extract(@this: unit, expression: _parse_bitwise()));
            if (!_match(TokenType.COMMA)) raise_error("Expected FROM or comma after EXTRACT", _prev);
            return expression(new exp.Extract(@this: unit, expression: _parse_bitwise()));
        }
    }
}
