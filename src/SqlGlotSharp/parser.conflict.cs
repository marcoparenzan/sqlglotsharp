using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

public static partial class parser
{
    public partial class Parser
    {
        // PostgreSQL closure; MySQL duplicate-key and operator classes are explicit boundaries.
        // Keep Python keyword insertion order (including nulls) for dump/serde fidelity.
        // py: sqlglot/parser.py::Parser._parse_on_conflict #0000000000
        public exp.OnConflict? _parse_on_conflict()
        {
            var conflict = _match_text_seq(["ON", "CONFLICT"]);
            var duplicate = _match_text_seq(["ON", "DUPLICATE", "KEY"]);
            if (!conflict && !duplicate) return null;

            object? conflict_keys = null;
            exp.Expr? constraint = null;
            if (conflict)
            {
                if (_match_text_seq(["ON", "CONSTRAINT"]))
                {
                    constraint = _parse_id_var();
                    if (_match(TokenType.DOT, advance: false))
                        throw new NotPortedException("sqlglot/parser.py::Parser._parse_on_conflict / qualified ON CONSTRAINT");
                }
                else if (_match(TokenType.L_PAREN))
                {
                    conflict_keys = _parse_csv(_parse_indexed_column).Cast<object?>().ToList();
                    _match_r_paren();
                }
            }
            _reject_conflict_modifiers();
            var index_predicate = _parse_where();
            _reject_conflict_modifiers();
            var action = _parse_conflict_action();
            object? assignments = null;
            if (_prev.token_type == TokenType.UPDATE)
            {
                _match(TokenType.SET);
                assignments = _parse_csv(_parse_update_assignment).Cast<object?>().ToList();
            }
            return expression(new exp.OnConflict(new PyDict<string, object?>
            {
                ["duplicate"] = duplicate, ["expressions"] = assignments,
                ["action"] = action, ["conflict_keys"] = conflict_keys,
                ["index_predicate"] = index_predicate, ["constraint"] = constraint,
                ["where"] = _parse_where()
            }));
        }

        // These are outside the bounded conflict grammar. Do not let ErrorLevel.IGNORE
        // turn an unconsumed constraint/index modifier into an apparently valid INSERT.
        private void _reject_conflict_modifiers()
        {
            if (_match_texts(["INCLUDE", "DEFERRABLE", "INITIALLY", "USING", "WITH", "NULLS"], advance: false)
                || _match_text_seq(["NOT", "DEFERRABLE"], advance: false))
                throw new NotPortedException("sqlglot/parser.py::Parser._parse_on_conflict / constraint or index modifier");
        }

        // py: sqlglot/parser.py::Parser._parse_indexed_column #0000000000
        public exp.Expr? _parse_indexed_column() => _parse_ordered(_parse_opclass);

        // py: sqlglot/parser.py::Parser._parse_opclass #0000000000
        public exp.Expr? _parse_opclass()
        {
            var target = _parse_disjunction();

            if (_match_texts(OPCLASS_FOLLOW_KEYWORDS, advance: false)) return target;

            if (!_match_set(OPTYPE_FOLLOW_TOKENS, advance: false))
                return expression(Mk<exp.Opclass>(("this", target), ("expression", _parse_table_parts())));

            return target;
        }

        // py: sqlglot/parser.py::Parser._parse_with_operator #0000000000
        public exp.Expr? _parse_with_operator()
        {
            var @this = _parse_indexed_column();

            if (!_match(TokenType.WITH)) return @this;

            var op = _parse_var(any_token: true, tokens: RESERVED_TOKENS);

            return expression(Mk<exp.WithOperator>(("this", @this), ("op", op)));
        }

        // Bounded _parse_var_from_options adapter: CONFLICT_ACTIONS has only string
        // continuations. Like exp.var in Python, the Var is not counted via expression().
        private exp.Var? _parse_conflict_action()
        {
            var start = _curr;
            if (!py.truthy(start)) return null;
            var option = py.upper(start.text);
            var continuations = TEXT_MATCH_EXCLUDED_TOKENS.Contains(start.token_type)
                ? null : CONFLICT_ACTIONS.GetValueOrDefault(option);
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
