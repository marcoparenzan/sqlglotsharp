using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

public static partial class parser
{
    public partial class Parser
    {
        // Partial closure: recursive SEARCH/CYCLE, UDFs and alias column parsing remain explicit dependencies.
        // py: sqlglot/parser.py::Parser._parse_with #0000000000
        public exp.With? _parse_with(bool skip_with_token = false)
        {
            if (!skip_with_token && !_match(TokenType.WITH)) return null;
            var comments = _prev_comments;
            var recursive = _match(TokenType.RECURSIVE);
            List<string>? last_comments = null;
            List<object?> expressions = [];
            List<object?> udfs = [];
            while (true)
            {
                var cte = _parse_cte();
                if (cte is not null)
                {
                    if (cte is exp.FunctionSpecification) udfs.Add(cte);
                    else expressions.Add(cte);
                    if (last_comments is { Count: > 0 }) cte.add_comments(last_comments);
                }
                if (!_match(TokenType.COMMA) && !_match(TokenType.WITH)) break;
                _match(TokenType.WITH);
                recursive = _match(TokenType.RECURSIVE) || recursive;
                last_comments = _prev_comments;
            }
            var search = _parse_recursive_with_search();

            // A SEARCH clause can be followed by a CYCLE clause
            var cycle = search is not null && !Equals(search.args.get("kind"), "CYCLE") && _match_text_seq(["CYCLE"], advance: false)
                ? _parse_recursive_with_search() : null;

            return expression(new exp.With(expressions: expressions, recursive: recursive ? true : null,
                search: search, cycle: cycle, udfs: udfs.Count > 0 ? udfs : null), comments: comments);
        }

        // py: sqlglot/parser.py::Parser._parse_cte #0000000000
        public exp.Expr? _parse_cte()
        {
            var index = _index;
            var alias = _parse_table_alias(ID_VAR_TOKENS);
            if (alias is null || !py.truthy(alias.@this)) raise_error("Expected CTE to have alias");
            List<object?>? key_expressions = null;
            if (_match_text_seq(["USING", "KEY"]))
                throw new NotPortedException("Parser._parse_wrapped_id_vars / CTE USING KEY");
            if (!_match(TokenType.ALIAS) && !OPTIONAL_ALIAS_TOKEN_CTE)
            {
                _retreat(index);
                return null;
            }
            var comments = _prev_comments;
            bool? materialized = _match_text_seq(["NOT", "MATERIALIZED"]) ? false
                : _match_text_seq(["MATERIALIZED"]) ? true : null;
            var cte = expression(new exp.CTE(@this: _parse_wrapped(() => _parse_statement()),
                alias: alias, materialized: materialized, key_expressions: key_expressions), comments: comments);
            if (cte.@this is exp.Values values) cte.set("this", _values_to_select(values));
            return cte;
        }
    }
}
