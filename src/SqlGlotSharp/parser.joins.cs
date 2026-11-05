using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser._parse_using_identifiers #0000000000
        public List<exp.Expr> _parse_using_identifiers()
        {
            exp.Expr? ParseColumnAsIdentifier()
            {
                var node = _parse_column();
                return node is exp.Column ? node.args.get("this") as exp.Expr : node;
            }
            return _parse_wrapped_csv(ParseColumnAsIdentifier, optional: true);
        }

        // Partial: ARRAY and MATCH_CONDITION depend on unported branches; table,
        // lateral and pivot dependencies keep their own explicit boundaries.
        // py: sqlglot/parser.py::Parser._parse_join #0000000000
        public exp.Join? _parse_join(bool skip_join_token = false, bool parse_bracket = false,
            IReadOnlyCollection<TokenType>? alias_tokens = null)
        {
            if (_match(TokenType.COMMA))
            {
                var table = _try_parse(() => _parse_table(alias_tokens: alias_tokens)!);
                var crossJoin = table is not null ? expression(new exp.Join(@this: table)) : null;
                if (crossJoin is not null && JOINS_HAVE_EQUAL_PRECEDENCE) crossJoin.set("kind", "CROSS");
                return crossJoin;
            }

            var index = _index;
            var (method, side, kind) = _parse_join_parts();
            var directed = SUPPORTS_DIRECTED_JOIN && _match_text_seq(["DIRECTED"]);
            var hint = _match_texts(((System.Collections.IEnumerable)JOIN_HINTS!).Cast<string>()) ? _prev.text : null;
            var join = _match(TokenType.JOIN) || kind?.token_type == TokenType.STRAIGHT_JOIN;
            var joinComments = _prev_comments;
            if (!skip_join_token && !join)
            {
                _retreat(index);
                kind = null; method = null; side = null;
            }
            var outerApply = _match_pair(TokenType.OUTER, TokenType.APPLY, advance: false);
            var crossApply = _match_pair(TokenType.CROSS, TokenType.APPLY, advance: false);
            if (!skip_join_token && !join && !outerApply && !crossApply) return null;

            var kwargs = new PyDict<string, object?>
            {
                ["this"] = _parse_table(parse_bracket: parse_bracket, alias_tokens: alias_tokens)
            };
            if (kind?.token_type == TokenType.ARRAY && _match(TokenType.COMMA))
                throw new NotPortedException("Parser._parse_join / ARRAY table expressions");
            if (method is not null) kwargs["method"] = py.upper(method.text);
            if (side is not null) kwargs["side"] = py.upper(side.text);
            if (kind is not null) kwargs["kind"] = py.upper(kind.text);
            if (hint is not null) kwargs["hint"] = hint;

            if (_match(TokenType.MATCH_CONDITION))
                throw new NotPortedException("Parser._parse_join / MATCH_CONDITION");
            if (_match(TokenType.ON)) kwargs["on"] = _parse_disjunction();
            else if (_match(TokenType.USING)) kwargs["using"] = _parse_using_identifiers().Cast<object?>().ToList();
            else if (method is null && !outerApply && !crossApply && kwargs["this"] is not exp.Unnest
                && kind?.token_type is not (TokenType.CROSS or TokenType.ARRAY))
            {
                index = _index;
                List<exp.Join>? joins = _parse_joins(alias_tokens: alias_tokens).ToList();
                if (joins.Count > 0 && _match(TokenType.ON)) kwargs["on"] = _parse_disjunction();
                else if (joins.Count > 0 && _match(TokenType.USING)) kwargs["using"] = _parse_using_identifiers().Cast<object?>().ToList();
                else { joins = null; _retreat(index); }
                ((exp.Expr)kwargs["this"]!).set("joins", joins is { Count: > 0 } ? joins.Cast<object?>().ToList() : null);
            }
            kwargs["pivots"] = _parse_pivots();
            List<string> comments = [.. joinComments];
            foreach (var token in new[] { method, side, kind })
                if (token is not null) comments.AddRange(token.comments);

            if (ADD_JOIN_ON_TRUE && !py.truthy(kwargs.get("on")) && !py.truthy(kwargs.get("using"))
                && !py.truthy(kwargs.get("method")) && kwargs.get("kind") is null or "INNER" or "OUTER")
                throw new NotPortedException("Parser._parse_join / ADD_JOIN_ON_TRUE / exp.true");
            if (directed) kwargs["directed"] = directed;
            return expression(new exp.Join(kwargs), comments: comments);
        }

        // py: sqlglot/parser.py::Parser._parse_joins #0000000000
        public IEnumerable<exp.Join> _parse_joins(IReadOnlyCollection<TokenType>? alias_tokens = null)
        {
            while (_parse_join(alias_tokens: alias_tokens) is { } join) yield return join;
        }
    }
}
