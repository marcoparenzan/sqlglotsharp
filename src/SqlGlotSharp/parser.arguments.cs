using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser._parse_assignment #0000000000
        public exp.Expr? _parse_assignment()
        {
            var @this = _parse_disjunction();
            if (@this is null && ASSIGNMENT.ContainsKey(_next.token_type))
            {
                // Upstream exp.column is not validated by the parser at this boundary.
                var token = _advance_any(ignore_reserved: true);
                @this = new exp.Column(@this: token is not null
                    ? new exp.Identifier(@this: _prev.text, quoted: !py.safe_identifier(_prev.text)) : null);
            }
            while (_match_set(ASSIGNMENT.Keys.ToArray()))
            {
                if (@this is exp.Column column && column.parts.Count == 1)
                    @this = column.@this as exp.Expr;
                var klass = ASSIGNMENT[_prev.token_type];
                var comments = _prev_comments;
                var rhs = _parse_assignment();
                @this = expression((exp.Expr)Activator.CreateInstance(klass, [new PyDict<string, object?>
                {
                    ["this"] = @this, ["expression"] = rhs
                }])!, comments: comments);
            }
            return @this;
        }

        // py: sqlglot/parser.py::Parser._parse_lambda_arg #0000000000
        public exp.Expr? _parse_lambda_arg() => _parse_id_var();

        // py: sqlglot/parser.py::Parser.LAMBDAS #0000000000
        public static readonly PyDict<TokenType, Func<Parser, List<exp.Expr?>, exp.Expr>> LAMBDAS_ = new()
        {
            [TokenType.ARROW] = (self, expressions) => self.expression(Mk<exp.Lambda>(
                ("this", self._replace_lambda(self._parse_disjunction(), expressions)),
                ("expressions", expressions.Cast<object?>().ToList()))),
            [TokenType.FARROW] = (self, expressions) => self.expression(Mk<exp.Kwarg>(
                ("this", new exp.Var(@this: expressions[0]!.name)),
                ("expression", self._parse_disjunction() ?? self._parse_select()))),
        };
        public virtual PyDict<TokenType, Func<Parser, List<exp.Expr?>, exp.Expr>> LAMBDAS => LAMBDAS_;

        // py: sqlglot/parser.py::Parser._parse_lambda #0000000000
        public exp.Expr? _parse_lambda(bool alias = false)
        {
            var next_token_type = _next.token_type;

            // Fast path: simple atom (column, literal, null, bool) followed by , or )
            if (LAMBDA_ARG_TERMINATORS.Contains(next_token_type) && _parse_atom() is { } atom) return atom;

            var index = _index;

            if (_match(TokenType.L_PAREN))
            {
                var expressions = _parse_csv<exp.Expr>(_parse_lambda_arg).Cast<exp.Expr?>().ToList();

                if (!_match(TokenType.R_PAREN)) _retreat(index);
                else if (_match_set(LAMBDAS.Keys.ToArray())) return LAMBDAS[_prev.token_type](this, expressions);
                else _retreat(index);
            }
            else if (TYPED_LAMBDA_ARGS || LAMBDAS.ContainsKey(next_token_type))
            {
                List<exp.Expr?> expressions = [_parse_lambda_arg()];

                if (_match_set(LAMBDAS.Keys.ToArray())) return LAMBDAS[_prev.token_type](this, expressions);

                _retreat(index);
            }

            exp.Expr? @this;

            if (_match(TokenType.DISTINCT))
                @this = expression(new exp.Distinct(expressions: _parse_csv<exp.Expr>(() => _parse_disjunction()).Cast<object?>().ToList()));
            else
            {
                _match(TokenType.ALL); // ALL is the default/no-op aggregate modifier (SQL-92)
                @this = _parse_select_or_expression(alias: alias);
            }

            return _parse_limit(_parse_respect_or_ignore_nulls(
                _parse_order(_parse_having_max(_parse_respect_or_ignore_nulls(@this)))));
        }

        // py: sqlglot/parser.py::Parser._parse_having_max #0000000000
        public exp.Expr? _parse_having_max(exp.Expr? @this)
        {
            if (_match(TokenType.HAVING))
            {
                _match_texts(["MAX", "MIN"]);
                var max = py.upper(_prev.text) != "MIN";
                return expression(Mk<exp.HavingMax>(("this", @this), ("expression", _parse_column()), ("max", max)));
            }

            return @this;
        }

        // py: sqlglot/parser.py::Parser._replace_lambda #0000000000
        public exp.Expr? _replace_lambda(exp.Expr? node, List<exp.Expr?> expressions)
        {
            if (!py.truthy(node)) return node;

            var lambda_types = new Dictionary<string, object?>();
            foreach (var e in expressions) lambda_types[e!.name] = e.args.get("to") ?? (object)false;

            foreach (var column in node!.find_all<exp.Column>().ToList())
            {
                if (!lambda_types.TryGetValue(column.parts[0].name, out var typ)) continue;

                exp.Expr dot_or_id = py.truthy(column.table) ? column.to_dot() : (exp.Expr)column.@this!;

                if (typ is exp.DataType data_type)
                    dot_or_id = expression(Mk<exp.Cast>(("this", dot_or_id), ("to", data_type.copy())));

                var parent = column.parent;

                var replaced = false;
                while (parent is exp.Dot)
                {
                    if (parent.parent is not exp.Dot)
                    {
                        parent.replace(dot_or_id);
                        replaced = true;
                        break;
                    }
                    parent = parent.parent;
                }

                if (!replaced && parent is not exp.Dot)
                {
                    if (ReferenceEquals(column, node)) node = dot_or_id;
                    else column.replace(dot_or_id);
                }
            }
            return node;
        }

        private exp.Expr? _parse_function_args__ordinary_lambda(bool alias, bool allow_distinct = false) => _parse_lambda(alias);

        private exp.Expr _parse_named_argument(IReadOnlyList<exp.Expr> expressions)
        {
            // Python uses only the first speculative identifier, its name (not positions,
            // quotes or comments), and disjunction-or-select, not assignment, for the RHS.
            var name = new exp.Var(@this: expressions[0].name);
            var value = _parse_disjunction() ?? _parse_select();
            return expression(new exp.Kwarg(@this: name, expression: value));
        }
    }
}
