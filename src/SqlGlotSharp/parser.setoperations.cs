using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser.parse_set_operation #0000000000
        public exp.SetOperation? parse_set_operation(exp.Expr? @this, bool consume_pipe = false, bool intersect_only = false)
        {
            var start = _index;
            var (_, side_token, kind_token) = _parse_join_parts();
            var side = side_token?.text;
            var kind = kind_token?.text;
            if (!_match_set(SET_OPERATIONS) || (intersect_only && _prev.token_type != TokenType.INTERSECT))
            {
                _retreat(start);
                return null;
            }

            var token_type = _prev.token_type;
            var operation = token_type == TokenType.UNION ? typeof(exp.Union)
                : token_type == TokenType.EXCEPT ? typeof(exp.Except) : typeof(exp.Intersect);
            var comments = _prev.comments;
            bool? distinct;
            if (_match(TokenType.DISTINCT)) distinct = true;
            else if (_match(TokenType.ALL)) distinct = false;
            else
            {
                // The generated base/PostgreSQL map is bool-valued. Preserve the
                // missing-default branch explicitly rather than silently assuming DISTINCT.
                if (!dialect.SET_OP_DISTINCT_BY_DEFAULT.TryGetValue(operation, out var default_distinct))
                    throw new NotPortedException("Parser.parse_set_operation / nullable SET_OP_DISTINCT_BY_DEFAULT");
                distinct = default_distinct;
                if (distinct is null) raise_error($"Expected DISTINCT or ALL for {operation.Name}");
            }

            bool? by_name = _match_text_seq(["BY", "NAME"]) || _match_text_seq(["STRICT", "CORRESPONDING"]) ? true : null;
            if (_match_text_seq(["CORRESPONDING"]))
            {
                by_name = true;
                if (!py.truthy(side) && !py.truthy(kind)) kind = "INNER";
            }
            List<object?>? on_column_list = null;
            if (by_name == true && _match_texts(["ON", "BY"]))
                on_column_list = _parse_wrapped_csv(() => _parse_column()).Cast<object?>().ToList();

            var right = _parse_select(nested: true, parse_set_operation: false, consume_pipe: consume_pipe);
            if (@this is exp.Values leftValues) @this = _values_to_select(leftValues);
            if (right is exp.Values rightValues) right = _values_to_select(rightValues);
            foreach (var operand in new[] { @this, right })
            {
                if (operand is exp.Subquery)
                {
                    // Subquery.unnest is not overridden by the current AST runtime.
                    // Keep its upstream loop local to this parser dependency check.
                    var values = operand;
                    while (values is exp.Subquery) values = (exp.Expr?)values.@this;
                    if (values is exp.Values nestedValues)
                    {
                        var wrapper = (exp.Subquery)nestedValues.parent!;
                        wrapper.set("this", _values_to_select(nestedValues));
                    }
                }
            }
            if (@this is exp.Alias && @this.@this is exp.Subquery subquery)
            {
                subquery.set("alias", new exp.TableAlias(@this: @this.args["alias"]));
                subquery.add_comments(@this.pop_comments());
                @this = subquery;
            }

            var args = new PyDict<string, object?>
            {
                ["this"] = @this, ["distinct"] = distinct, ["by_name"] = by_name,
                ["expression"] = right, ["side"] = side, ["kind"] = kind, ["on"] = on_column_list
            };
            exp.SetOperation node = token_type == TokenType.UNION ? new exp.Union(args)
                : token_type == TokenType.EXCEPT ? new exp.Except(args) : new exp.Intersect(args);
            return expression(node, comments: comments);
        }

        // py: sqlglot/parser.py::Parser._parse_set_operations #8c10823cd1
        public exp.Expr? _parse_set_operations(exp.Expr? @this)
        {
            var intersect_binds_tighter = dialect.INTERSECT_BINDS_TIGHTER_THAN_UNION_AND_EXCEPT;
            exp.SetOperation? last_setop = null;
            while (py.truthy(@this))
            {
                var setop = parse_set_operation(@this);
                if (setop is null) break;
                last_setop = setop;
                if (setop is exp.Intersect && setop.@this is exp.Union or exp.Except && !intersect_binds_tighter)
                    setop.set("this", ((exp.Expr)setop.@this!).subquery(copy: false));
                else if (setop is not exp.Intersect && intersect_binds_tighter)
                {
                    var operand = (exp.Expr?)setop.expression;
                    while (parse_set_operation(operand, intersect_only: true) is { } intersect)
                        last_setop = (exp.SetOperation)(operand = intersect);
                    if (!ReferenceEquals(operand, setop.expression))
                        setop.set("expression", operand!.subquery(copy: false));
                }
                @this = setop;
            }

            if (@this is exp.SetOperation && MODIFIERS_ATTACHED_TO_SET_OP)
            {
                var right = (exp.Expr?)last_setop?.expression;
                if (py.truthy(right))
                {
                    foreach (var arg in ((IEnumerable<string>)SET_OP_MODIFIERS!))
                    {
                        var expr = right!.args.get(arg);
                        if (py.truthy(expr) && !(arg == "limit" && expr is exp.Expr limit && py.truthy(limit.meta.get("top"))))
                        {
                            right.set(arg, null);
                            @this.set(arg, expr);
                        }
                    }
                }
                if (_match_set(QUERY_MODIFIER_PARSERS.Keys.ToArray(), advance: false))
                    @this = _parse_query_modifiers(@this);
            }
            return @this;
        }

        // py: sqlglot/parser.py::Parser._parse_wrapped_csv #9e0694ff28
        public List<T> _parse_wrapped_csv<T>(Func<T?> parse_method, TokenType sep = TokenType.COMMA, bool optional = false) where T : class =>
            _parse_wrapped(() => _parse_csv(parse_method, sep: sep), optional: optional);

        // py: sqlglot/parser.py::Parser._parse_wrapped #fd7462e0c1
        public T _parse_wrapped<T>(Func<T> parse_method, bool optional = false)
        {
            var wrapped = _match(TokenType.L_PAREN);
            if (!wrapped && !optional) raise_error("Expecting (");
            var parse_result = parse_method();
            if (wrapped) _match_r_paren();
            return parse_result;
        }
    }
}
