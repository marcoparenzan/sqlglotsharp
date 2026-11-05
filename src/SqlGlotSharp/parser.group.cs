using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

// Upstream grouping algorithms, in upstream order. Zero anchors are intentional:
// disjunction/bitwise (and their column/function/parenthesis dependency closure)
// remain partial. Unsupported expressions keep those dependencies' NotPorted boundaries.
public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser._parse_group #0000000000
        public exp.Group? _parse_group(bool skip_group_by_token = false)
        {
            if (!skip_group_by_token && !_match(TokenType.GROUP_BY)) return null;
            var comments = _prev_comments;
            var elements = new PyDict<string, object?>();
            // Local defaultdict(list) adapter: do not insert unused Group arguments.
            List<object?> list(string key)
            {
                if (!elements.TryGetValue(key, out var value))
                    elements[key] = value = new List<object?>();
                return (List<object?>)value!;
            }

            if (_match(TokenType.ALL)) elements["all"] = true;
            else if (_match(TokenType.DISTINCT)) elements["all"] = false;

            while (true)
            {
                // LIMIT/OFFSET/WINDOW also belong to the identifier token sets.
                if (_match_set(QUERY_MODIFIER_TOKENS, advance: false)) break;
                list("expressions").AddRange(_parse_csv(() =>
                    _parse_grouping_sets() ?? _parse_cube_or_rollup() ?? _parse_disjunction()).Cast<object?>());

                var before_with_index = _index;
                if (_match(TokenType.WITH) && _parse_cube_or_rollup(with_prefix: true) is { } cube_or_rollup)
                    list(cube_or_rollup is exp.Rollup ? "rollup" : "cube").Add(cube_or_rollup);
                else if (_parse_grouping_sets() is { } grouping_sets)
                {
                    // Hive-style suffix syntax: GROUP BY a, b GROUPING SETS (...).
                    list("grouping_sets").Add(grouping_sets);
                    break;
                }
                else if (_match_text_seq(["TOTALS"])) elements["totals"] = true;

                if (before_with_index <= _index && _index <= before_with_index + 1)
                {
                    _retreat(before_with_index);
                    break;
                }
            }
            return expression(new exp.Group(elements), comments: comments);
        }

        // py: sqlglot/parser.py::Parser._parse_cube_or_rollup #0000000000
        public exp.Expr? _parse_cube_or_rollup(bool with_prefix = false)
        {
            bool cube;
            if (_match(TokenType.CUBE)) cube = true;
            else if (_match(TokenType.ROLLUP)) cube = false;
            else return null;
            List<object?> expressions = with_prefix ? [] : _parse_wrapped_csv(_parse_bitwise).Cast<object?>().ToList();
            return expression(cube ? (exp.Expr)new exp.Cube(expressions: expressions)
                : new exp.Rollup(expressions: expressions));
        }

        // py: sqlglot/parser.py::Parser._parse_grouping_sets #0000000000
        public exp.GroupingSets? _parse_grouping_sets()
        {
            if (_match(TokenType.GROUPING_SETS))
                return expression(new exp.GroupingSets(expressions:
                    _parse_wrapped_csv(_parse_grouping_set).Cast<object?>().ToList()));
            return null;
        }

        // py: sqlglot/parser.py::Parser._parse_grouping_set #0000000000
        public exp.Expr? _parse_grouping_set() =>
            _parse_grouping_sets() ?? _parse_cube_or_rollup() ?? _parse_bitwise();

        // py: sqlglot/parser.py::Parser._parse_having #0000000000
        public exp.Having? _parse_having(bool skip_having_token = false)
        {
            if (!skip_having_token && !_match(TokenType.HAVING)) return null;
            var comments = _prev_comments;
            return expression(new exp.Having(@this: _parse_disjunction()), comments: comments);
        }
    }
}
