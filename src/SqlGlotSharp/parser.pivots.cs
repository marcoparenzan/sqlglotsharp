using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

// QUALIFY/PREWHERE, TABLESAMPLE and PIVOT/UNPIVOT parsing (parser.py).
public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser._parse_qualify #0000000000
        public exp.Expr? _parse_qualify()
        {
            if (!_match(TokenType.QUALIFY)) return null;
            return expression(Mk<exp.Qualify>(("this", _parse_disjunction())));
        }

        // py: sqlglot/parser.py::Parser._parse_prewhere #0000000000
        public exp.Expr? _parse_prewhere(bool skip_where_token = false)
        {
            if (!skip_where_token && !_match(TokenType.PREWHERE)) return null;

            var comments = _prev_comments;
            return expression(Mk<exp.PreWhere>(("this", _parse_disjunction())), comments: comments);
        }

        // py: sqlglot/parser.py::Parser._parse_table_sample #0000000000
        public exp.TableSample? _parse_table_sample(bool as_modifier = false)
        {
            if (!_match(TokenType.TABLE_SAMPLE) && !(as_modifier && _match_text_seq(["USING", "SAMPLE"]))) return null;

            exp.Expr? bucket_numerator = null, bucket_denominator = null, bucket_field = null, percent = null, size = null;
            object? seed = null;

            exp.Expr? method = _parse_var(tokens: [TokenType.ROW], upper: true);
            var matched_l_paren = _match(TokenType.L_PAREN);

            List<object?>? expressions;
            exp.Expr? num;
            if (TABLESAMPLE_CSV)
            {
                num = null;
                expressions = _parse_csv<exp.Expr>(() => _parse_primary()).Cast<object?>().ToList();
            }
            else
            {
                expressions = null;
                num = _match(TokenType.NUMBER, advance: false)
                    ? _parse_factor(parse_mod: false)
                    : _parse_primary() ?? _parse_placeholder();
            }

            if (_match_text_seq(["BUCKET"]))
            {
                bucket_numerator = _parse_number();
                _match_text_seq(["OUT", "OF"]);
                bucket_denominator = _parse_number();
                _match(TokenType.ON);
                bucket_field = _parse_field();
            }
            else if (_match_set([TokenType.PERCENT, TokenType.MOD])) percent = num;
            else if (_match(TokenType.ROWS) || !dialect.TABLESAMPLE_SIZE_IS_PERCENT) size = num;
            else percent = num;

            if (matched_l_paren) _match_r_paren();

            if (_match(TokenType.L_PAREN))
            {
                method = _parse_var(upper: true);
                seed = _match(TokenType.COMMA) ? _parse_number() : false;
                _match_r_paren();
            }
            else if (_match_texts(["SEED", "REPEATABLE"]))
                seed = _parse_wrapped(() => _parse_number());

            if (!py.truthy(method) && py.truthy(DEFAULT_SAMPLING_METHOD))
                method = new exp.Var(@this: py.str(DEFAULT_SAMPLING_METHOD));

            return (exp.TableSample)expression(Mk<exp.TableSample>(("expressions", expressions), ("method", method),
                ("bucket_numerator", bucket_numerator), ("bucket_denominator", bucket_denominator),
                ("bucket_field", bucket_field), ("percent", percent), ("size", size), ("seed", seed)));
        }

        // py: sqlglot/parser.py::Parser._parse_pivots #0000000000
        public List<object?>? _parse_pivots()
        {
            if (_curr.token_type is not (TokenType.PIVOT or TokenType.UNPIVOT)) return null;

            List<object?> pivots = [];
            while (_parse_pivot() is { } pivot) pivots.Add(pivot);
            return pivots.Count > 0 ? pivots : null;
        }

        // py: sqlglot/parser.py::_unpivot_target #0000000000
        private static exp.Expr _unpivot_target(exp.Expr expr)
        {
            // UNPIVOT's pre-FOR values and FOR field are new output names, not column references.
            if (expr is exp.Column column && !py.truthy(column.table)) return (exp.Expr)column.@this!;
            if (expr is exp.Tuple tuple)
                tuple.set("expressions", tuple.expressions.Select(e => (object?)_unpivot_target((exp.Expr)e!)).ToList());
            return expr;
        }

        // py: sqlglot/parser.py::Parser._parse_pivot #0000000000
        public exp.Pivot? _parse_pivot()
        {
            var index = _index;
            bool? include_nulls = null;

            bool unpivot;
            if (_match(TokenType.PIVOT)) unpivot = false;
            else if (_match(TokenType.UNPIVOT))
            {
                unpivot = true;

                // https://docs.databricks.com/en/sql/language-manual/sql-ref-syntax-qry-select-unpivot.html#syntax
                if (_match_text_seq(["INCLUDE", "NULLS"])) include_nulls = true;
                else if (_match_text_seq(["EXCLUDE", "NULLS"])) include_nulls = false;
            }
            else return null;

            if (!_match(TokenType.L_PAREN))
            {
                _retreat(index);
                return null;
            }

            List<exp.Expr> expressions = unpivot
                ? _parse_csv<exp.Expr>(() => _parse_column())
                : _parse_csv<exp.Expr>(() => _parse_pivot_aggregation());

            if (expressions.Count == 0) raise_error("Failed to parse PIVOT's aggregation list");

            if (!_match(TokenType.FOR)) raise_error("Expecting FOR");

            List<object?> fields = [];
            while (true)
            {
                var field = _try_parse(() => _parse_pivot_in());
                if (field is null) break;
                fields.Add(field);
            }

            object? default_on_null = _match_text_seq(["DEFAULT", "ON", "NULL"]) ? _parse_wrapped(() => _parse_bitwise()) : false;

            var group = _parse_group();

            _match_r_paren();

            var pivot = (exp.Pivot)expression(Mk<exp.Pivot>(("expressions", expressions.Cast<object?>().ToList()),
                ("fields", fields), ("unpivot", unpivot), ("include_nulls", include_nulls),
                ("default_on_null", default_on_null), ("group", group)));

            if (unpivot)
            {
                pivot.set("expressions", pivot.expressions.Select(e => (object?)_unpivot_target((exp.Expr)e!)).ToList());
                foreach (var pivot_field in (pivot.args.get("fields") as List<object?>) ?? [])
                    if (pivot_field is exp.In in_)
                        in_.set("this", _unpivot_target((exp.Expr)in_.@this!));

                pivot.set("value_columns_first", UNPIVOT_VALUE_COLUMNS_FIRST);
            }

            if (!_match_set([TokenType.PIVOT, TokenType.UNPIVOT], advance: false))
                pivot.set("alias", _parse_table_alias());

            if (!unpivot)
            {
                var names = _pivot_column_names(expressions);

                List<object?> columns = [];
                List<List<string>> all_fields = [];
                foreach (var pivot_field in (pivot.args.get("fields") as List<object?>) ?? [])
                {
                    var pivot_field_expressions = ((exp.Expr)pivot_field!).expressions;

                    // The `PivotAny` expression corresponds to `ANY ORDER BY <column>`; we can't infer in this case.
                    if (pivot_field_expressions.Count > 0 && pivot_field_expressions[0] is exp.PivotAny) continue;

                    all_fields.Add(pivot_field_expressions.Select(f =>
                    {
                        var fld = (exp.Expr)f!;
                        // An explicit `<field> AS <alias>` names the output column directly,
                        // so it wins over the dialect's string-identifying convention
                        return IDENTIFY_PIVOT_STRINGS && fld is not exp.PivotAlias
                            ? new dialects.dialect.Dialect().generator(false).generate(fld, true)
                            : fld.alias_or_name;
                    }).ToList());
                }

                if (all_fields.Count > 0)
                {
                    if (names.Count > 0) all_fields.Add(names);

                    // Generate all possible combinations of the pivot columns
                    // e.g PIVOT(sum(...) as total FOR year IN (2000, 2010) FOR country IN ('NL', 'US'))
                    // generates the product between [[2000, 2010], ['NL', 'US'], ['total']]
                    foreach (var fld_parts_tuple in CartesianProduct(all_fields))
                    {
                        var fld_parts = fld_parts_tuple.ToList();

                        if (names.Count > 0 && PREFIXED_PIVOT_COLUMNS)
                        {
                            // Move the "name" to the front of the list
                            var last = fld_parts[^1];
                            fld_parts.RemoveAt(fld_parts.Count - 1);
                            fld_parts.Insert(0, last);
                        }

                        columns.Add(exp.to_identifier(string.Join("_", fld_parts)));
                    }
                }

                pivot.set("columns", columns);
                pivot.set("identify_pivot_strings", IDENTIFY_PIVOT_STRINGS);
                pivot.set("prefixed_pivot_columns", PREFIXED_PIVOT_COLUMNS);
                pivot.set("pivot_column_naming", PIVOT_COLUMN_NAMING);
            }

            return pivot;
        }

        // itertools.product(*lists)
        private static IEnumerable<string[]> CartesianProduct(List<List<string>> lists)
        {
            IEnumerable<string[]> result = [[]];
            foreach (var list in lists)
                result = result.SelectMany(prefix => list.Select(item => prefix.Append(item).ToArray())).ToList();
            return result;
        }

        // py: sqlglot/parser.py::Parser._pivot_column_names #0000000000
        public virtual List<string> _pivot_column_names(List<exp.Expr> aggregations) =>
            aggregations.Where(agg => agg.alias.Length != 0).Select(agg => agg.alias).ToList();

        // py: sqlglot/parser.py::Parser._parse_pivot_in #0000000000
        public exp.Expr _parse_pivot_in()
        {
            exp.Expr? parse_aliased_expression()
            {
                var @this = _parse_select_or_expression();

                _match(TokenType.ALIAS);
                var alias = _parse_bitwise();
                if (py.truthy(alias))
                {
                    if (alias is exp.Column column && !py.truthy(column.args.get("db"))) alias = (exp.Expr)column.@this!;
                    return expression(Mk<exp.PivotAlias>(("this", @this), ("alias", alias)));
                }

                return @this;
            }

            var value = _parse_column();

            if (!_match(TokenType.IN)) raise_error("Expecting IN");

            if (_match(TokenType.L_PAREN))
            {
                List<object?> exprs;
                if (_match(TokenType.ANY)) exprs = [Mk<exp.PivotAny>(("this", _parse_order()))];
                else exprs = _parse_csv<exp.Expr>(parse_aliased_expression).Cast<object?>().ToList();
                _match_r_paren();
                return expression(Mk<exp.In>(("this", value), ("expressions", exprs)));
            }

            return expression(Mk<exp.In>(("this", value), ("field", _parse_id_var())));
        }

        // py: sqlglot/parser.py::Parser._parse_pivot_aggregation #0000000000
        public exp.Expr? _parse_pivot_aggregation()
        {
            var func = _parse_function();
            if (!py.truthy(func))
            {
                if (_prev.token_type == TokenType.COMMA) return null;
                raise_error("Expecting an aggregation function in PIVOT");
            }

            return _parse_alias(func);
        }

        // py: sqlglot/parser.py::Parser._parse_unpivot_columns #0000000000
        public exp.Expr? _parse_unpivot_columns()
        {
            if (!_match(TokenType.INTO)) return null;

            return expression(Mk<exp.UnpivotColumns>(
                ("this", _match_text_seq(["NAME"]) ? _parse_column() : false),
                ("expressions", _match_text_seq(["VALUE"]) ? _parse_csv<exp.Expr>(() => _parse_column()).Cast<object?>().ToList() : false)));
        }

        // py: sqlglot/parser.py::Parser._parse_simplified_pivot #0000000000
        public exp.Expr _parse_simplified_pivot(bool? is_unpivot = null)
        {
            exp.Expr? parse_on()
            {
                var @this = _parse_bitwise();

                if (_match(TokenType.IN))
                    // PIVOT ... ON col IN (row_val1, row_val2)
                    return _parse_in(@this);
                if (_match(TokenType.ALIAS, advance: false))
                    // UNPIVOT ... ON (col1, col2, col3) AS row_val
                    return _parse_alias(@this);

                return @this;
            }

            var @thisTable = _parse_table();
            object? expressions = _match(TokenType.ON) ? _parse_csv<exp.Expr>(parse_on).Cast<object?>().ToList() : false;
            var into = _parse_unpivot_columns();
            object? @using = _match(TokenType.USING)
                ? _parse_csv<exp.Expr>(() => _parse_alias(_parse_column())).Cast<object?>().ToList() : false;
            var group = _parse_group();

            return expression(Mk<exp.Pivot>(("this", @thisTable), ("expressions", expressions), ("using", @using),
                ("group", group), ("unpivot", is_unpivot), ("into", into)));
        }
    }
}
