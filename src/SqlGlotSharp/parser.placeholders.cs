using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

// Closely related, dependency-complete identifier leaf in upstream method order.
// _parse_placeholder/_parse_identifier/_parse_id_var are deliberately not declared:
// PLACEHOLDER_PARSERS requires _parse_parameter -> _parse_primary_or_var ->
// _parse_primary and PRIMARY_PARSERS (including introducer/session/star parsing).
// A reduced placeholder table would silently misrepresent upstream behavior.
public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser._parse_string_as_identifier #c7419fb3c8
        public exp.Identifier? _parse_string_as_identifier()
        {
            if (!_match(TokenType.STRING))
                return null;
            var output = _parse_string_as_identifier__to_identifier(_prev.text, quoted: true);
            output.update_positions(_prev);
            return output;
        }

        // Unanchored call-site adapter, NOT a port of general exp.to_identifier.
        // Python's name is statically a string and quoted is explicitly True here:
        // to_identifier takes its string branch, bypassing SAFE_IDENTIFIER_RE and
        // maybe_copy. Keep direct construction (not Parser.expression): upstream
        // deliberately does not transfer comments or validate/count this node.
        private static exp.Identifier _parse_string_as_identifier__to_identifier(string name, bool quoted)
            => new(@this: name, quoted: quoted);
    }
}
