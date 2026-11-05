using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

// Window group in upstream order. Dependency closures (expressions, lambda and
// scope-aware null-wrapper relocation) are partial, so all anchors remain zero.
public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser._parse_window_clause #0000000000
        public List<exp.Expr>? _parse_window_clause() =>
            _match(TokenType.WINDOW) ? _parse_csv(_parse_named_window) : null;

        // py: sqlglot/parser.py::Parser._parse_named_window #0000000000
        public exp.Expr? _parse_named_window() => _parse_window(_parse_id_var(), alias: true);

        // py: sqlglot/parser.py::Parser._parse_respect_or_ignore_nulls #0000000000
        public exp.Expr? _parse_respect_or_ignore_nulls(exp.Expr? @this)
        {
            if (_curr.token_type == TokenType.VAR)
            {
                if (_match_text_seq(["IGNORE", "NULLS"]))
                    return expression(new exp.IgnoreNulls(@this: @this));
                if (_match_text_seq(["RESPECT", "NULLS"]))
                    return expression(new exp.RespectNulls(@this: @this));
            }
            return @this;
        }

        // py: sqlglot/parser.py::Parser._parse_per #0000000000
        public exp.Expr? _parse_per(exp.Expr? @this)
        {
            if (_next.token_type == TokenType.L_PAREN && _match_text_seq(["PER"]))
                return expression(new exp.Per(@this: @this,
                    expressions: _parse_wrapped_csv(_parse_disjunction).Cast<object?>().ToList()));
            return @this;
        }

        // py: sqlglot/parser.py::Parser._parse_window #0000000000
        public exp.Expr? _parse_window(exp.Expr? @this, bool alias = false)
        {
            var func = @this;
            var comments = func?.comments;
            if (SUPPORTS_NTH_VALUE_FROM_MODIFIER && @this is exp.NthValue)
            {
                if (_match_text_seq(["FROM", "FIRST"])) @this.set("from_first", true);
                else if (_match_text_seq(["FROM", "LAST"])) @this.set("from_first", false);
            }
            if (_match_text_seq(["WITHIN", "GROUP"]))
                @this = expression(new exp.WithinGroup(@this: @this,
                    expression: _parse_wrapped(() => _parse_order())));
            if (_match_pair(TokenType.FILTER, TokenType.L_PAREN))
            {
                _match(TokenType.WHERE);
                @this = expression(new exp.Filter(@this: @this,
                    expression: _parse_where(skip_where_token: true)));
                _match_r_paren();
            }
            if (@this is exp.AggFunc && @this.walk().Any(n =>
                    !ReferenceEquals(n, @this) && n is exp.IgnoreNulls or exp.RespectNulls))
                throw new NotPortedException("sqlglot/parser.py::Parser._parse_window / find_in_scope null-wrapper relocation");
            @this = _parse_respect_or_ignore_nulls(@this);

            string? over;
            if (alias)
            {
                over = null;
                _match(TokenType.ALIAS);
            }
            else if (!_match_set(WINDOW_BEFORE_PAREN_TOKENS)) return _parse_per(@this);
            else over = py.upper(_prev.text);

            if (py.truthy(comments) && func is not null) func.pop_comments();
            if (!_match(TokenType.L_PAREN))
                return _parse_per(expression(new exp.Window(@this: @this,
                    alias: _parse_id_var(false), over: over), comments: comments));

            var window_alias = _parse_id_var(any_token: false, tokens: WINDOW_ALIAS_TOKENS);
            bool? first = _match(TokenType.FIRST) ? true : null;
            if (_match_text_seq(["LAST"])) first = false;
            var (partition, order) = _parse_partition_and_order();
            var kind = _match_set([TokenType.ROWS, TokenType.RANGE]) || _match_text_seq(["GROUPS"])
                ? _prev.text : null;
            exp.Expr? spec = null;
            if (py.truthy(kind))
            {
                _match(TokenType.BETWEEN);
                var start = _parse_window_spec();
                var end = _match(TokenType.AND) ? _parse_window_spec() : new PyDict<string, object?>();
                var exclude = _match_text_seq(["EXCLUDE"])
                    ? _parse_window__exclude_option() : null;
                spec = expression(new exp.WindowSpec(kind: kind, start: start["value"],
                    start_side: start["side"], end: end.GetValueOrDefault("value"),
                    end_side: end.GetValueOrDefault("side"), exclude: exclude));
            }
            _match_r_paren();
            var window = expression(new exp.Window(@this: @this,
                partition_by: partition.Cast<object?>().ToList(), order: order, spec: spec,
                alias: window_alias, over: over, first: first), comments: comments);
            if (_match_set(WINDOW_BEFORE_PAREN_TOKENS, advance: false))
                return _parse_window(window, alias: alias);
            return _parse_per(window);
        }

        // py: sqlglot/parser.py::Parser._parse_partition_and_order #0000000000
        public (List<exp.Expr>, exp.Expr?) _parse_partition_and_order() =>
            (_parse_partition_by(), _parse_order());

        // py: sqlglot/parser.py::Parser._parse_partition_by #0000000000
        public List<exp.Expr> _parse_partition_by()
        {
            if (!_match(TokenType.PARTITION_BY)) return [];
            if (ID_VAR_TOKENS.Contains(_next.token_type) && _match_text_seq(["EXCLUDING"]))
                return [expression(new exp.PartitionByExcluding(
                    expressions: _parse_csv(_parse_disjunction).Cast<object?>().ToList()))];
            return _parse_csv(_parse_disjunction);
        }

        // py: sqlglot/parser.py::Parser._parse_window_spec #0000000000
        public PyDict<string, object?> _parse_window_spec()
        {
            _match(TokenType.BETWEEN);
            object? value = _match_text_seq(["UNBOUNDED"]) ? "UNBOUNDED"
                : _match_text_seq(["CURRENT", "ROW"]) ? "CURRENT ROW" : _parse_bitwise();
            return new() { ["value"] = value,
                ["side"] = _match_texts(WINDOW_SIDES) ? _prev.text : null };
        }

        // Bounded adapter for _parse_var_from_options: WINDOW_EXCLUDE_OPTIONS has
        // string continuations only; the generic tuple-continuation closure is not ported.
        private exp.Var? _parse_window__exclude_option()
        {
            var start = _curr;
            if (!py.truthy(start)) return null;
            var option = py.upper(start.text);
            var continuations = TEXT_MATCH_EXCLUDED_TOKENS.Contains(start.token_type)
                ? null : WINDOW_EXCLUDE_OPTIONS.GetValueOrDefault(option);
            var index = _index;
            _advance();
            var matched = false;
            foreach (var keyword in continuations ?? [])
            {
                if (!_match_text_seq([keyword])) continue;
                option = $"{option} {keyword}";
                matched = true;
                break;
            }
            if (!matched && (continuations is null || continuations.Count > 0))
            {
                raise_error($"Unknown option {option}");
                _retreat(index);
                return null;
            }
            return new exp.Var(@this: option);
        }
    }
}
