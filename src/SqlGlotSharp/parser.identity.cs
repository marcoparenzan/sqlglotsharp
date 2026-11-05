using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser._parse_generated_as_identity #0000000000
        public virtual exp.Expr _parse_generated_as_identity()
        {
            exp.Expr node;
            if (_match_text_seq(["BY", "DEFAULT"]))
                node = expression(new exp.GeneratedAsIdentityColumnConstraint(@this: false,
                    on_null: _match_pair(TokenType.ON, TokenType.NULL)));
            else
            {
                _match_text_seq(["ALWAYS"]);
                node = expression(new exp.GeneratedAsIdentityColumnConstraint(@this: true));
            }

            _match(TokenType.ALIAS);

            if (_match_text_seq(["ROW"]))
            {
                var start = _match_text_seq(["START"]);
                if (!start) _match(TokenType.END);
                var hidden = _match_text_seq(["HIDDEN"]);
                return expression(new exp.GeneratedAsRowColumnConstraint(start: start, hidden: hidden));
            }

            var identity = _match_text_seq(["IDENTITY"]);

            if (_match(TokenType.L_PAREN))
            {
                if (_match_text_seq(["START", "WITH"])) node.set("start", _parse_bitwise());
                if (_match_text_seq(["INCREMENT", "BY"])) node.set("increment", _parse_bitwise());
                if (_match_text_seq(["MINVALUE"])) node.set("minvalue", _parse_bitwise());
                if (_match_text_seq(["MAXVALUE"])) node.set("maxvalue", _parse_bitwise());

                if (_match_text_seq(["CYCLE"])) node.set("cycle", true);
                else if (_match_text_seq(["NO", "CYCLE"])) node.set("cycle", false);

                if (!identity) node.set("expression", _parse_disjunction());
                else if (!py.truthy(node.args.get("start")) && _match(TokenType.NUMBER, advance: false))
                {
                    var args = _parse_csv(() => _parse_bitwise());
                    node.set("start", helper.seq_get(args, 0));
                    node.set("increment", helper.seq_get(args, 1));
                }

                _match_r_paren();
            }

            return node;
        }
    }
}
