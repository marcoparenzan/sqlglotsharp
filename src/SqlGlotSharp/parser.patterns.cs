using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

public static partial class parser
{
    // py: sqlglot/parser.py::binary_range_parser #0000000000
    // Shared upstream callable. Only the pattern entries are enabled by this port;
    // other RANGE_PARSERS entries retain their explicit dependency boundaries.
    public static Func<Parser, exp.Expr?, exp.Expr?> binary_range_parser(Type expr_type, bool reverse_args = false)
    {
        return (self, @this) =>
        {
            var expression = self._parse_bitwise();
            if (reverse_args) (@this, expression) = (expression, @this);
            return self._parse_escape(self.expression(Parser._operators__construct(expr_type, @this, expression)));
        };
    }

    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser._parse_escape #0000000000
        public exp.Expr? _parse_escape(exp.Expr? @this)
        {
            if (!_match(TokenType.ESCAPE)) return @this;
            return expression(new exp.Escape(@this: @this, expression: _parse_string() ?? _parse_null()));
        }
    }
}
