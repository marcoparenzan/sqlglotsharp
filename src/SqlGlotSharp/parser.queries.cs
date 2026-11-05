using Token = SqlGlotSharp.tokenizer_core.Token;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

// Bounded upstream query dependency group. No parse/statement entrypoint.
// Zero hashes identify incomplete dependency closures, NOT complete member ports.
public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser.QUERY_MODIFIER_PARSERS #0000000000
        public static readonly PyDict<TokenType, Func<Parser, (string, object?)>> QUERY_MODIFIER_PARSERS_ = new()
        {
            [TokenType.MATCH_RECOGNIZE] = _ => throw new NotPortedException("Parser._parse_match_recognize"),
            [TokenType.PREWHERE] = p => ("prewhere", p._parse_prewhere()),
            [TokenType.WHERE] = p => ("where", p._parse_where()),
            [TokenType.GROUP_BY] = p => ("group", p._parse_group()),
            [TokenType.HAVING] = p => ("having", p._parse_having()),
            [TokenType.QUALIFY] = p => ("qualify", p._parse_qualify()),
            [TokenType.WINDOW] = p => ("windows", p._parse_window_clause()?.Cast<object?>().ToList()),
            [TokenType.ORDER_BY] = p => ("order", p._parse_order()),
            [TokenType.LIMIT] = p => ("limit", p._parse_limit()),
            [TokenType.FETCH] = p => ("limit", p._parse_limit()),
            [TokenType.OFFSET] = p => ("offset", p._parse_offset()),
            [TokenType.FOR] = p => ("locks", p._parse_locks().Cast<object?>().ToList()),
            [TokenType.LOCK] = p => ("locks", p._parse_locks().Cast<object?>().ToList()),
            [TokenType.TABLE_SAMPLE] = p => ("sample", p._parse_table_sample(as_modifier: true)),
            [TokenType.USING] = p => ("sample", p._parse_table_sample(as_modifier: true)),
            [TokenType.CLUSTER_BY] = p => ("cluster", p._parse_cluster()),
            [TokenType.DISTRIBUTE_BY] = p => ("distribute", p._parse_sort<exp.Distribute>(TokenType.DISTRIBUTE_BY)),
            [TokenType.SORT_BY] = p => ("sort", p._parse_sort<exp.Sort>(TokenType.SORT_BY)),
            [TokenType.CONNECT_BY] = p => ("connect", p._parse_connect(skip_start_token: true)),
        };
        public virtual PyDict<TokenType, Func<Parser, (string, object?)>> QUERY_MODIFIER_PARSERS => QUERY_MODIFIER_PARSERS_;

        // py: sqlglot/parser.py::Parser._parse_projections #0000000000
        public virtual (List<exp.Expr>, List<exp.Expr>?) _parse_projections() => (_parse_expressions(), null);

        // py: sqlglot/parser.py::Parser._parse_wrapped_select #0000000000
        public virtual exp.Expr? _parse_wrapped_select(bool table = false)
        {
            exp.Expr? @this;
            if (_match_set([TokenType.PIVOT, TokenType.UNPIVOT]))
                @this = _parse_simplified_pivot(is_unpivot: _prev.token_type == TokenType.UNPIVOT);
            else
            {
                // Support parentheses for duckdb FROM-first syntax
                @this = table && !_match(TokenType.FROM, advance: false)
                    ? _parse_table(consume_pipe: true)
                    : _parse_select(nested: true, parse_set_operation: false);

                // Transform exp.Values into a exp.Table to pass through parse_query_modifiers
                // in case a modifier (e.g. join) is following
                if (table && @this is exp.Values && py.truthy(@this.alias))
                {
                    var alias = ((exp.Expr)@this.args["alias"]!).pop();
                    @this = new exp.Table(@this: @this, alias: alias);
                }

                @this = _parse_query_modifiers(_parse_set_operations(@this));
            }

            return @this;
        }

        // py: sqlglot/parser.py::Parser._parse_select #0000000000
        public exp.Expr? _parse_select(bool nested = false, bool table = false,
            bool parse_subquery_alias = true, bool parse_set_operation = true, bool consume_pipe = true)
        {
            var query = _parse_select_query(nested, table, parse_subquery_alias, parse_set_operation);
            if (consume_pipe && _match(TokenType.PIPE_GT, advance: false) && query is exp.Query)
            {
                query = _parse_pipe_syntax_query(query);
                query = query is not null && table ? query.subquery(copy: false) : query;
            }

            return query;
        }

        // py: sqlglot/parser.py::Parser._parse_select_query #0000000000
        public exp.Expr? _parse_select_query(bool nested = false, bool table = false,
            bool parse_subquery_alias = true, bool parse_set_operation = true)
        {
            var cte = _parse_with();
            if (cte is not null)
            {
                var statement = _parse_statement();
                if (statement is null)
                {
                    raise_error("Failed to parse any statement following CTE");
                    return cte;
                }
                while (statement is exp.Subquery && statement.args.All(pair => pair.Key == "this" || pair.Value is null))
                    statement = (exp.Expr)statement.@this!;
                if (statement.arg_types.ContainsKey("with_"))
                {
                    if (statement.args.get("with_") is exp.With inner_cte)
                    {
                        var merged = ((System.Collections.IEnumerable)cte.args["expressions"]!).Cast<object?>()
                            .Concat(((System.Collections.IEnumerable)inner_cte.args["expressions"]!).Cast<object?>()).ToList();
                        cte.set("expressions", merged);
                        if (py.truthy(inner_cte.args.get("recursive"))) cte.set("recursive", true);
                    }
                    statement.set("with_", cte);
                }
                else
                {
                    raise_error($"{statement.key} does not support CTE");
                    statement = cte;
                }
                return statement;
            }
            // duckdb supports leading with FROM x
            var from_ = _match(TokenType.FROM, advance: false) ? _parse_from(joins: true, consume_pipe: true) : null;
            List<object?>? laterals = null;
            if (from_ is not null)
            {
                laterals = [];
                while (_parse_lateral() is { } lateral) laterals.Add(lateral);
            }

            // Hive supports multi-table inserts: FROM x INSERT ... SELECT ... [INSERT ... SELECT ...]
            if (from_ is not null && _match(TokenType.INSERT, advance: false))
            {
                var source = (exp.Expr)from_.@this!;
                source.set("laterals", laterals!.Count > 0 ? laterals : null);

                List<object?> inserts = [];
                while (_match(TokenType.INSERT, advance: false))
                    inserts.Add(_parse_statement());

                return expression(new exp.MultitableInserts(new PyDict<string, object?>
                {
                    ["expressions"] = inserts, ["source"] = source
                }), comments: from_.comments);
            }

            exp.Expr? @this;
            if (_match(TokenType.SELECT))
            {
                var comments = _prev_comments;
                var hint = _parse_hint();
                bool? all;
                bool matched_distinct;
                if (py.truthy(_next) && _next.token_type != TokenType.DOT)
                {
                    all = _match(TokenType.ALL);
                    matched_distinct = _match_set(DISTINCT_TOKENS);
                }
                else { all = null; matched_distinct = false; }
                var kind = _match(TokenType.ALIAS) && _match_texts(["STRUCT", "VALUE"]) ? py.upper(_prev.text) : null;
                // DISTINCT ON uses the upstream VALUES tuple parser.
                exp.Expr? distinct = null;
                if (matched_distinct)
                {
                    distinct = expression(new exp.Distinct(on: _match(TokenType.ON) ? _parse_value() : null));
                }
                List<object?> operation_modifiers = [];
                while (py.truthy(_curr) && _match_texts(OPERATION_MODIFIERS.Cast<string>()))
                    operation_modifiers.Add(new exp.Var(@this: py.upper(_prev.text)));
                var limit = _parse_limit(top: true);
                if (limit is not null && !matched_distinct && all != true)
                {
                    matched_distinct = _match_set(DISTINCT_TOKENS);
                    if (matched_distinct)
                    {
                        distinct = expression(new exp.Distinct(on: _match(TokenType.ON) ? _parse_value() : null));
                    }
                    else all = _match(TokenType.ALL);
                }
                if (all == true && distinct is not null) raise_error("Cannot specify both ALL and DISTINCT after SELECT");
                var (projections, exclude) = _parse_projections();
                // Constructor parameter declaration order differs from Python kwargs order.
                // Preserve upstream insertion order for faithful serialized AST output.
                @this = expression(new exp.Select(new PyDict<string, object?>
                {
                    ["kind"] = kind, ["hint"] = hint, ["distinct"] = distinct,
                    ["expressions"] = projections.Cast<object?>().ToList(), ["limit"] = limit,
                    ["exclude"] = exclude?.Cast<object?>().ToList(),
                    ["operation_modifiers"] = operation_modifiers.Count > 0 ? operation_modifiers : null
                }));
                @this.comments = comments;
                var into = _parse_into();
                if (into is not null) @this.set("into", into);
                from_ ??= _parse_from();
                if (from_ is not null) @this.set("from_", from_);
                @this = _parse_query_modifiers(@this);
            }
            else if ((table || nested) && _match(TokenType.L_PAREN))
            {
                var comments = _prev_comments;
                @this = _parse_wrapped_select(table);
                @this?.add_comments(comments, prepend: true);
                _match_r_paren();
                return _parse_subquery(@this, parse_alias: parse_subquery_alias);
            }
            else if (_match(TokenType.VALUES, advance: false)) @this = _parse_derived_table_values();
            else if (from_ is not null)
            {
                // exp.select("*").from_(from_.this, copy=False): "*" is parsed, so the Star carries
                // the position of the single token of that string (line 1, col 1, offsets 0..0).
                var star_select = new exp.Select(expressions: new List<object?> { new exp.Star().update_positions(line: 1, col: 1, start: 0, end: 0) });
                star_select.set("from_", new exp.From(@this: from_.@this));
                @this = _parse_query_modifiers(star_select);
            }
            else if (_match(TokenType.SUMMARIZE)) throw new NotPortedException("Parser._parse_select / SUMMARIZE");
            else if (_match(TokenType.DESCRIBE)) throw new NotPortedException("Parser._parse_describe");
            else @this = null;

            if (laterals is { Count: > 0 } && @this is exp.Select select)
                select.set("laterals", new List<object?>(laterals.Concat(
                    select.args.get("laterals") as List<object?> ?? [])));

            return parse_set_operation ? _parse_set_operations(@this) : @this;
        }

        // py: sqlglot/parser.py::Parser._parse_table_alias #0000000000
        public exp.TableAlias? _parse_table_alias(IReadOnlyCollection<TokenType>? alias_tokens = null)
        {
            if (_can_parse_limit_or_offset()) return null;
            if (py.upper(_curr.text) == "START" && py.upper(_next.text) == "WITH") return null;
            var any_token = _match(TokenType.ALIAS);
            var alias = _parse_id_var(any_token: any_token, tokens: alias_tokens is { Count: > 0 } ? alias_tokens : TABLE_ALIAS_TOKENS)
                ?? _parse_string_as_identifier();
            var index = _index;
            List<object?>? columns = null;
            if (_match(TokenType.L_PAREN))
            {
                columns = _parse_csv(_parse_function_parameter).Cast<object?>().ToList();
                if (columns.Count > 0) _match_r_paren();
                else _retreat(index);
            }
            if (alias is null && !py.truthy(columns)) return null;
            var table_alias = expression(new exp.TableAlias(@this: alias, columns: columns));
            if (alias is exp.Identifier) table_alias.add_comments(alias.pop_comments());
            return table_alias;
        }

        // py: sqlglot/parser.py::Parser._parse_subquery #0000000000
        public exp.Subquery? _parse_subquery(exp.Expr? @this, bool parse_alias = true)
        {
            if (@this is null) return null;
            return expression(new exp.Subquery(@this: @this, pivots: _parse_pivots(),
                alias: parse_alias ? _parse_table_alias() : null, sample: _parse_table_sample()));
        }

        // py: sqlglot/parser.py::Parser._parse_query_modifiers #0000000000
        public exp.Expr? _parse_query_modifiers(exp.Expr? @this)
        {
            if (@this is exp.Query or exp.Table or exp.TableFromRows or exp.Values)
            {
                foreach (var join in _parse_joins()) @this.append("joins", join);
                while (_parse_lateral() is { } lateral) @this.append("laterals", lateral);
                while (true)
                {
                    if (_match_set(QUERY_MODIFIER_PARSERS.Keys.ToArray(), advance: false))
                    {
                        var modifier_token = _curr;
                        if (modifier_token.token_type is TokenType.LIMIT or TokenType.FETCH
                            && @this.args.get("limit") is exp.Expr old_limit && py.truthy(old_limit.meta.get("top"))) break;
                        var (key, node) = QUERY_MODIFIER_PARSERS[modifier_token.token_type](this);
                        if (py.truthy(node))
                        {
                            if (py.truthy(@this.args.get(key)))
                                raise_error($"Found multiple '{py.upper(modifier_token.text)}' clauses", token: modifier_token);
                            @this.set(key, node);
                            if (key == "limit")
                            {
                                var limit_node = (exp.Expr)node!;
                                var offset = limit_node.args.get("offset");
                                limit_node.set("offset", null);
                                if (py.truthy(offset))
                                {
                                    if (py.truthy(@this.args.get("offset"))) raise_error("Found multiple 'OFFSET' clauses", token: modifier_token);
                                    var offset_node = new exp.Offset(expression: offset);
                                    @this.set("offset", offset_node);
                                    var limit_by_expressions = limit_node.expressions;
                                    limit_node.set("expressions", null);
                                    offset_node.set("expressions", limit_by_expressions);
                                }
                            }
                            continue;
                        }
                    }
                    if (py.upper(_curr.text) == "START")
                    {
                        var modifier_token = _curr;
                        var connect = _parse_connect();
                        if (connect is not null)
                        {
                            if (py.truthy(@this.args.get("connect"))) raise_error("Found multiple 'START WITH' clauses", token: modifier_token);
                            @this.set("connect", connect);
                            continue;
                        }
                    }
                    break;
                }
            }
            if (SUPPORTS_IMPLICIT_UNNEST && @this is not null && py.truthy(@this.args.get("from_")))
                throw new NotPortedException("Parser._implicit_unnests_to_explicit");
            return @this;
        }

        // py: sqlglot/parser.py::Parser._parse_from #0000000000
        public exp.From? _parse_from(bool joins = false, bool skip_from_token = false, bool consume_pipe = false)
        {
            if (!skip_from_token && !_match(TokenType.FROM)) return null;
            var comments = _prev_comments;
            return expression(new exp.From(@this: _parse_table(joins: joins, consume_pipe: consume_pipe)), comments: comments);
        }

        // py: sqlglot/parser.py::Parser._parse_lateral #bb1b127f68
        public exp.Lateral? _parse_lateral()
        {
            bool? cross_apply = null;
            if (_match_pair(TokenType.CROSS, TokenType.APPLY)) cross_apply = true;
            else if (_match_pair(TokenType.OUTER, TokenType.APPLY)) cross_apply = false;

            exp.Expr? @this;
            bool? view, outer;
            if (cross_apply is not null)
            {
                @this = _parse_select(table: true);
                view = null;
                outer = null;
            }
            else if (_match(TokenType.LATERAL))
            {
                @this = _parse_select(table: true);
                view = _match(TokenType.VIEW);
                outer = _match(TokenType.OUTER);
            }
            else return null;

            if (!py.truthy(@this))
            {
                @this = _parse_unnest() ?? _parse_function() ?? _parse_id_var(any_token: false);

                while (_match(TokenType.DOT))
                    @this = new exp.Dot(@this: @this,
                        expression: _parse_function() ?? _parse_id_var(any_token: false));
            }

            bool? ordinality = null;
            exp.Expr? table_alias;

            if (view == true)
            {
                var table = _parse_id_var(any_token: false);
                var columns = _match(TokenType.ALIAS) ? _parse_csv(() => _parse_id_var()).Cast<object?>().ToList() : [];
                table_alias = expression(new exp.TableAlias(@this: table, columns: columns));
            }
            else if (@this is exp.Subquery or exp.Unnest && py.truthy(@this.alias))
            {
                // We move the alias from the lateral's child node to the lateral itself
                table_alias = ((exp.Expr)@this!.args["alias"]!).pop();
            }
            else
            {
                ordinality = _match_pair(TokenType.WITH, TokenType.ORDINALITY);
                table_alias = _parse_table_alias();
            }

            return expression(new exp.Lateral(new PyDict<string, object?>
            {
                ["this"] = @this, ["view"] = view, ["outer"] = outer, ["alias"] = table_alias,
                ["cross_apply"] = cross_apply, ["ordinality"] = ordinality
            }));
        }
        // py: sqlglot/parser.py::Parser._parse_stream #0000000000
        public exp.Stream? _parse_stream()
        {
            if (_match(TokenType.STREAM)) throw new NotPortedException("Parser._parse_stream");
            return null;
        }
        // py: sqlglot/parser.py::Parser._parse_join_parts #0000000000
        public (Token?, Token?, Token?) _parse_join_parts() =>
            (_match_set(JOIN_METHODS) ? _prev : null, _match_set(JOIN_SIDES) ? _prev : null, _match_set(JOIN_KINDS) ? _prev : null);

        // py: sqlglot/parser.py::Parser._parse_table_parts_fast #0000000000
        public exp.Table? _parse_table_parts_fast()
        {
            var index = _index;
            List<exp.Identifier>? parts = null;
            List<string>? all_comments = null;
            while (_match_set(IDENTIFIER_TOKENS))
            {
                var token = _prev;
                var comments = _prev_comments;
                var has_dot = _match(TokenType.DOT);
                var curr_tt = _curr.token_type;
                if ((!has_dot && TABLE_POSTFIX_TOKENS.Contains(curr_tt)) || (has_dot && !IDENTIFIER_TOKENS.Contains(curr_tt)))
                { _retreat(index); return null; }
                parts ??= [];
                if (comments.Count > 0) { all_comments ??= []; all_comments.AddRange(comments); _prev_comments = []; }
                parts.Add(expression(new exp.Identifier(@this: token.text, quoted: token.token_type == TokenType.IDENTIFIER), token));
                if (!has_dot) break;
            }
            if (parts is null) return null;
            var n = parts.Count;
            exp.Table table;
            if (n == 1) table = new exp.Table(@this: parts[0]);
            else if (n == 2) table = new exp.Table(@this: parts[1], db: parts[0]);
            else
            {
                exp.Expr @this = parts[2];
                for (var i = 3; i < n; i++) @this = new exp.Dot(@this: @this, expression: parts[i]);
                table = new exp.Table(@this: @this, db: parts[1], catalog: parts[0]);
            }
            if (all_comments is { Count: > 0 }) table.add_comments(all_comments);
            return table;
        }
        // py: sqlglot/parser.py::Parser._parse_table #0000000000
        public virtual exp.Expr? _parse_table(bool schema = false, bool joins = false,
            IReadOnlyCollection<TokenType>? alias_tokens = null, bool parse_bracket = false,
            bool is_db_reference = false, bool parse_partition = false, bool consume_pipe = false)
        {
            if (!schema && !is_db_reference && !consume_pipe && !joins)
            {
                var index = _index;
                var table = _parse_table_parts(fast: true);
                if (table is not null)
                {
                    var curr_tt = _curr.token_type;
                    var next_tt = _next.token_type;
                    if (TABLE_TERMINATORS.Contains(curr_tt) && next_tt != TokenType.MATCH_CONDITION) return table;
                    if (!TABLE_POSTFIX_TOKENS.Contains(curr_tt) && !TABLE_POSTFIX_TOKENS.Contains(next_tt))
                    {
                        var alias = _parse_table_alias(alias_tokens is { Count: > 0 } ? alias_tokens : TABLE_ALIAS_TOKENS);
                        if (alias is not null) table.set("alias", alias);
                        if (TABLE_TERMINATORS.Contains(_curr.token_type)) return table;
                    }
                    _retreat(index);
                }
            }
            var stream = _parse_stream(); if (stream is not null) return stream;
            var lateral = _parse_lateral(); if (lateral is not null) return lateral;
            var unnest = _parse_unnest(); if (unnest is not null) return unnest;
            var values = _parse_derived_table_values(); if (values is not null) return values;
            var subquery = _parse_select(table: true, consume_pipe: consume_pipe);
            if (subquery is not null)
            {
                if (!py.truthy(subquery.args.get("pivots"))) subquery.set("pivots", _parse_pivots());
                if (joins) foreach (var join in _parse_joins()) subquery.append("joins", join);
                return subquery;
            }
            if (schema) return _parse_insert_schema_table(parse_bracket, is_db_reference, parse_partition);
            return _parse_table_slow(schema, joins, alias_tokens, parse_bracket, is_db_reference, parse_partition);
        }
        // py: sqlglot/parser.py::Parser._parse_unnest #ccb9f1c8c9
        public exp.Unnest? _parse_unnest(bool with_alias = true)
        {
            if (!_match_pair(TokenType.UNNEST, TokenType.L_PAREN, advance: false)) return null;

            _advance();

            var expressions = _parse_wrapped_csv(() => _parse_equality());
            object? offset = _match_pair(TokenType.WITH, TokenType.ORDINALITY);

            var alias = with_alias ? _parse_table_alias() : null;

            if (alias is not null)
            {
                if (dialect.UNNEST_COLUMN_ONLY)
                {
                    if (py.truthy(alias.args.get("columns"))) raise_error("Unexpected extra column alias in unnest.");

                    alias.set("columns", new List<object?> { alias.@this });
                    alias.set("this", null);
                }

                var columns = alias.args.get("columns") as List<object?> ?? [];
                if (py.truthy(offset) && expressions.Count < columns.Count)
                {
                    offset = columns[^1];
                    columns.RemoveAt(columns.Count - 1);
                }
            }

            if (!py.truthy(offset) && _match_pair(TokenType.WITH, TokenType.OFFSET))
            {
                _match(TokenType.ALIAS);
                offset = _parse_id_var(any_token: false, tokens: UNNEST_OFFSET_ALIAS_TOKENS)
                    ?? exp.to_identifier("offset");
            }

            return expression(new exp.Unnest(new PyDict<string, object?>
            {
                ["expressions"] = expressions.Cast<object?>().ToList(), ["alias"] = alias, ["offset"] = offset
            }));
        }
        // py: sqlglot/parser.py::Parser._parse_where #0000000000
        public exp.Where? _parse_where(bool skip_where_token = false)
        {
            if (!skip_where_token && !_match(TokenType.WHERE)) return null;
            var comments = _prev_comments;
            return expression(new exp.Where(@this: _parse_disjunction()), comments: comments);
        }
        // py: sqlglot/parser.py::Parser._parse_order #0000000000
        public exp.Expr? _parse_order(exp.Expr? @this = null, bool skip_order_token = false)
        {
            bool? siblings = null;
            if (!skip_order_token && !_match(TokenType.ORDER_BY))
            {
                if (!_match(TokenType.ORDER_SIBLINGS_BY)) return @this;
                siblings = true;
            }
            var comments = _prev_comments;
            return expression(new exp.Order(@this: @this, expressions: _parse_csv(() => _parse_ordered()).Cast<object?>().ToList(), siblings: siblings), comments: comments);
        }
        // py: sqlglot/parser.py::Parser._parse_ordered #0000000000
        public exp.Ordered? _parse_ordered(Func<exp.Expr?>? parse_method = null)
        {
            var @this = parse_method is not null ? parse_method() : _parse_disjunction();
            if (@this is null) return null;
            if (py.upper(@this.name) == "ALL" && dialect.SUPPORTS_ORDER_BY_ALL) @this = new exp.Var(@this: "ALL");
            var asc = _match(TokenType.ASC);
            bool? desc = _match(TokenType.DESC) ? true : asc ? false : null;
            var first = _match_text_seq(["NULLS", "FIRST"]);
            var last = _match_text_seq(["NULLS", "LAST"]);
            var nulls_first = first;
            if (!(first || last) && ((desc != true && dialect.NULL_ORDERING == "nulls_are_small")
                || (desc == true && dialect.NULL_ORDERING != "nulls_are_small")) && dialect.NULL_ORDERING != "nulls_are_last") nulls_first = true;
            if (_match_text_seq(["WITH", "FILL"])) throw new NotPortedException("Parser._parse_ordered / WithFill/interpolate");
            return expression(new exp.Ordered(@this: @this, desc: desc, nulls_first: nulls_first, with_fill: null));
        }
        // py: sqlglot/parser.py::Parser._parse_limit_options #0000000000
        public exp.LimitOptions? _parse_limit_options()
        {
            var percent = _match_set([TokenType.PERCENT, TokenType.MOD]);
            var rows = _match_texts(["ROW", "ROWS"]);
            _match_text_seq(["ONLY"]);
            var ties = _match_text_seq(["WITH", "TIES"]);
            if (!(percent || rows || ties)) return null;
            return expression(new exp.LimitOptions(percent: percent, rows: rows, with_ties: ties));
        }
        // py: sqlglot/parser.py::Parser._parse_limit #0000000000
        public exp.Expr? _parse_limit(exp.Expr? @this = null, bool top = false, bool skip_limit_token = false)
        {
            if (skip_limit_token || _match(top ? TokenType.TOP : TokenType.LIMIT))
            {
                var comments = _prev_comments;
                exp.Expr? value;
                if (top)
                {
                    var paren = _match(TokenType.L_PAREN);
                    value = paren ? _parse_term() ?? _parse_select() : _parse_number();
                    if (paren) _match_r_paren();
                }
                else value = dialect.SUPPORTS_LIMIT_ALL && _match(TokenType.ALL) ? new exp.Var(@this: "ALL") : _parse_term(parse_mod: false);
                var options = _parse_limit_options();
                exp.Expr? offset = null;
                if (_match(TokenType.COMMA)) { offset = value; value = _parse_term(); }
                var limit = expression(new exp.Limit(@this: @this, expression: value, offset: offset,
                    limit_options: options, expressions: _parse_limit_by()), comments: comments);
                if (top) limit.meta["top"] = true;
                return limit;
            }
            if (_match(TokenType.FETCH))
            {
                var direction = _match_set([TokenType.FIRST, TokenType.NEXT]) ? py.upper(_prev.text) : "FIRST";
                var count = _match_texts(["ROW", "ROWS"], advance: false) ? null : _parse_field(tokens: FETCH_TOKENS);
                return expression(new exp.Fetch(direction: direction, count: count, limit_options: _parse_limit_options()));
            }
            return @this;
        }
        // py: sqlglot/parser.py::Parser._parse_offset #0000000000
        public exp.Expr? _parse_offset(exp.Expr? @this = null)
        {
            if (!_match(TokenType.OFFSET)) return @this;
            var count = _parse_term();
            _match_texts(["ROW", "ROWS"]);
            return expression(new exp.Offset(@this: @this, expression: count, expressions: _parse_limit_by()));
        }
        // py: sqlglot/parser.py::Parser._can_parse_limit_or_offset #0000000000
        public bool _can_parse_limit_or_offset()
        {
            if (!_match_set([AMBIGUOUS_ALIAS_TOKENS.Item1, AMBIGUOUS_ALIAS_TOKENS.Item2], advance: false)) return false;
            var index = _index;
            var result = _try_parse<exp.Expr>(() => _parse_limit()!, retreat: true) is not null
                || _try_parse<exp.Expr>(() => _parse_offset()!, retreat: true) is not null;
            _retreat(index);
            if (_next.token_type == TokenType.MATCH_CONDITION) result = false;
            return result;
        }
        // py: sqlglot/parser.py::Parser._parse_limit_by #0000000000
        public List<object?>? _parse_limit_by() => _match_text_seq(["BY"]) ? _parse_csv(() => _parse_bitwise()).Cast<object?>().ToList() : null;
    }
}
