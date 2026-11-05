using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

// Pipe syntax (FROM x |> WHERE ... |> SELECT ...) of parser.py, with the minimal AST builder
// helpers it needs (query.select/group_by/order_by/limit/offset/where/join/with_/union ...).
public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser.PIPE_SYNTAX_TRANSFORM_PARSERS #0000000000
        public static readonly PyDict<string, Func<Parser, exp.Expr, exp.Expr?>> PIPE_SYNTAX_TRANSFORM_PARSERS_ = new()
        {
            ["AGGREGATE"] = (self, query) => self._parse_pipe_syntax_aggregate((exp.Select)query),
            ["AS"] = (self, query) => self._build_pipe_cte(query, [new exp.Star()], self._parse_table_alias()),
            ["DISTINCT"] = (self, query) => { self._advance(); query.set("distinct", new exp.Distinct()); return query; },
            ["EXTEND"] = (self, query) => self._parse_pipe_syntax_extend((exp.Select)query),
            ["LIMIT"] = (self, query) => self._parse_pipe_syntax_limit((exp.Select)query),
            ["ORDER BY"] = (self, query) => { PipeOrderBy(query, [self._parse_order()!]); return query; },
            ["PIVOT"] = (self, query) => self._parse_pipe_syntax_pivot((exp.Select)query),
            ["SELECT"] = (self, query) => self._parse_pipe_syntax_select((exp.Select)query),
            ["TABLESAMPLE"] = (self, query) => self._parse_pipe_syntax_tablesample((exp.Select)query),
            ["UNPIVOT"] = (self, query) => self._parse_pipe_syntax_pivot((exp.Select)query),
            ["WHERE"] = (self, query) => { PipeWhere(query, self._parse_where()); return query; },
        };
        public virtual PyDict<string, Func<Parser, exp.Expr, exp.Expr?>> PIPE_SYNTAX_TRANSFORM_PARSERS => PIPE_SYNTAX_TRANSFORM_PARSERS_;

        // ---- minimal builders (py: sqlglot/expressions/query.py)

        // Select.select(*expressions, append=False)
        private static void PipeSetSelect(exp.Expr query, IEnumerable<exp.Expr> expressions, bool append = false)
        {
            var parsed = expressions.Cast<object?>().ToList();
            if (append && query.args.get("expressions") is List<object?> existing && existing.Count > 0) parsed = existing.Concat(parsed).ToList();
            query.set("expressions", parsed);
        }

        // Select.group_by(*expressions) (append defaults to True)
        private static void PipeGroupBy(exp.Expr query, IEnumerable<exp.Expr> expressions)
        {
            var parsed = expressions.Cast<object?>().ToList();
            if (query.args.get("group") is exp.Expr existing && existing.args.get("expressions") is List<object?> items && items.Count > 0)
                parsed = items.Concat(parsed).ToList();
            query.set("group", new exp.Group(expressions: parsed));
        }

        // Query.order_by(*expressions, append=False)
        private static void PipeOrderBy(exp.Expr query, IEnumerable<exp.Expr> expressions)
        {
            var items = expressions.ToList();
            // an Order instance is used as-is (_apply_child_list_builder merges its expressions and properties)
            var parsed = new List<object?>();
            var properties = new Dictionary<string, object?>();
            foreach (var e in items)
            {
                if (e is exp.Order order)
                {
                    foreach (var (k, v) in order.args)
                    {
                        if (k == "expressions") { foreach (var item in (System.Collections.IEnumerable)v!) parsed.Add(item); }
                        else properties[k] = v;
                    }
                }
                else parsed.Add(e);
            }
            var child = new exp.Order(expressions: parsed);
            foreach (var (k, v) in properties) child.set(k, v);
            query.set("order", child);
        }

        // Select.where(*expressions): Where nodes are unwrapped, then AND-ed with any existing condition
        private static void PipeWhere(exp.Expr query, exp.Expr? where)
        {
            if (where is null) return;
            var condition = where is exp.Where ? (exp.Expr)where.args["this"]! : where;
            var existing = query.args.get("where") as exp.Expr;
            exp.Expr node;
            if (existing is not null)
            {
                node = new exp.And(@this: wrap_connector((exp.Expr)existing.args["this"]!), expression: wrap_connector(condition));
            }
            else node = condition;
            query.set("where", new exp.Where(@this: node));
        }

        private static exp.Expr wrap_connector(exp.Expr e) => e is exp.Connector ? new exp.Paren(@this: e) : e;

        // exp.select(*expressions).from_(source)
        private static exp.Select PipeSelectFrom(IEnumerable<exp.Expr> expressions, object source)
        {
            var select = new exp.Select(expressions: expressions.Cast<object?>().ToList());
            exp.Expr from_this = source switch
            {
                // maybe_parse("FROM <name>") positions the identifier inside that synthetic SQL string
                string name => new exp.Table(@this: new exp.Identifier(@this: name, quoted: false)
                    .update_positions(line: 1, col: 5 + name.Length, start: 5, end: 4 + name.Length)),
                exp.Expr node => node,
                _ => throw new ArgumentException("Unsupported pipe source")
            };
            select.set("from_", new exp.From(@this: from_this));
            return select;
        }

        // py: sqlglot/parser.py::Parser._build_pipe_cte #0000000000
        public exp.Select _build_pipe_cte(exp.Expr query, List<exp.Expr> expressions, exp.Expr? alias_cte = null)
        {
            object new_cte;
            if (alias_cte is not null) new_cte = alias_cte;
            else
            {
                _pipe_cte_counter++;
                new_cte = $"__tmp{_pipe_cte_counter}";
            }

            var with_ = query.args.get("with_") as exp.Expr;
            var ctes = with_?.pop();

            var new_select = PipeSelectFrom(expressions, new_cte);
            if (ctes is not null) new_select.set("with_", ctes);

            // new_select.with_(new_cte, as_=query)
            exp.Expr alias_expression = new_cte is string cte_name
                ? new exp.TableAlias(@this: new exp.Identifier(@this: cte_name, quoted: false)
                    .update_positions(line: 1, col: cte_name.Length, start: 0, end: cte_name.Length - 1))
                : (exp.Expr)new_cte;
            var cte = new exp.CTE(@this: query, alias: alias_expression);
            var existing_with = new_select.args.get("with_") as exp.Expr;
            var cte_expressions = new List<object?>();
            if (existing_with is not null && existing_with.args.get("expressions") is List<object?> existing_items) cte_expressions.AddRange(existing_items);
            cte_expressions.Add(cte);
            var new_with = new exp.With(expressions: cte_expressions);
            if (existing_with is not null)
                foreach (var (k, v) in existing_with.args) if (k != "expressions") new_with.set(k, v);
            new_select.set("with_", new_with);
            return new_select;
        }

        // py: sqlglot/parser.py::Parser._parse_pipe_syntax_select #0000000000
        public exp.Expr _parse_pipe_syntax_select(exp.Select query)
        {
            var select = _parse_select(consume_pipe: false);
            if (select is null) return query;

            PipeSetSelect(query, ((System.Collections.IEnumerable)select.args["expressions"]!).Cast<exp.Expr>(), append: false);
            return _build_pipe_cte(query, [new exp.Star()]);
        }

        // py: sqlglot/parser.py::Parser._parse_pipe_syntax_limit #0000000000
        public exp.Expr _parse_pipe_syntax_limit(exp.Select query)
        {
            var limit = _parse_limit();
            var offset = _parse_offset();
            if (limit is exp.Limit)
            {
                var curr_limit = query.args.get("limit") as exp.Expr ?? limit;
                var curr_is_limit_all = curr_limit is exp.Limit && IsLimitAll(curr_limit);

                // LIMIT ALL means "no cap", so it is never tighter than a numeric limit.
                bool should_replace;
                if (IsLimitAll(limit)) should_replace = curr_is_limit_all;
                else
                    should_replace = curr_is_limit_all
                        || Convert.ToDecimal(((exp.Expr)curr_limit.args["expression"]!).to_py()) >= Convert.ToDecimal(((exp.Expr)limit.args["expression"]!).to_py());

                if (should_replace) query.set("limit", limit);
            }
            if (offset is not null)
            {
                var curr_offset = query.args.get("offset") as exp.Expr;
                var curr = curr_offset is not null ? Convert.ToInt64(((exp.Expr)curr_offset.args["expression"]!).to_py()) : 0L;
                query.set("offset", new exp.Offset(expression: exp.Literal.number(curr + Convert.ToInt64(((exp.Expr)offset.args["expression"]!).to_py()))));
            }

            return query;
        }

        private static bool IsLimitAll(exp.Expr limit) =>
            limit.args.get("expression") is exp.Var value && value.name.ToUpperInvariant() == "ALL";

        // py: sqlglot/parser.py::Parser._parse_pipe_syntax_aggregate_fields #0000000000
        public exp.Expr? _parse_pipe_syntax_aggregate_fields()
        {
            var @this = _parse_disjunction();
            if (_match_text_seq(["GROUP", "AND"], advance: false)) return @this;

            @this = _parse_alias(@this);

            if (_match_set([TokenType.ASC, TokenType.DESC], advance: false)) return _parse_ordered(() => @this);

            return @this;
        }

        // py: sqlglot/parser.py::Parser._parse_pipe_syntax_aggregate_group_order_by #0000000000
        public exp.Expr _parse_pipe_syntax_aggregate_group_order_by(exp.Select query, bool group_by_exists = true)
        {
            var expr = _parse_csv(_parse_pipe_syntax_aggregate_fields);
            List<exp.Expr> aggregates_or_groups = [], orders = [];
            foreach (var element in expr)
            {
                exp.Expr @this;
                if (element is exp.Ordered)
                {
                    @this = (exp.Expr)element.args["this"]!;
                    if (@this is exp.Alias) element.set("this", @this.args["alias"]);
                    orders.Add(element);
                }
                else @this = element;
                aggregates_or_groups.Add(@this);
            }

            if (group_by_exists)
            {
                var existing = ((System.Collections.IEnumerable)(query.args.get("expressions") ?? new List<object?>())).Cast<exp.Expr>();
                PipeSetSelect(query, aggregates_or_groups.Concat(existing).ToList(), append: false);
                PipeGroupBy(query, aggregates_or_groups.Select(projection => projection.args.get("alias") as exp.Expr ?? projection));
            }
            else PipeSetSelect(query, aggregates_or_groups, append: false);

            if (orders.Count > 0)
            {
                PipeOrderBy(query, orders);
                return query;
            }

            return query;
        }

        // py: sqlglot/parser.py::Parser._parse_pipe_syntax_aggregate #0000000000
        public exp.Expr _parse_pipe_syntax_aggregate(exp.Select query)
        {
            _match_text_seq(["AGGREGATE"]);
            var result = (exp.Select)_parse_pipe_syntax_aggregate_group_order_by(query, group_by_exists: false);

            if (_match(TokenType.GROUP_BY) || (_match_text_seq(["GROUP", "AND"]) && _match(TokenType.ORDER_BY)))
                result = (exp.Select)_parse_pipe_syntax_aggregate_group_order_by(result);

            return _build_pipe_cte(result, [new exp.Star()]);
        }

        // py: sqlglot/parser.py::Parser._parse_pipe_syntax_set_operator #0000000000
        public exp.Expr? _parse_pipe_syntax_set_operator(exp.Expr query)
        {
            var first_setop = parse_set_operation(query);
            if (first_setop is null) return null;

            exp.Expr? ParseAndUnwrapQuery()
            {
                var expr = _parse_paren();
                return expr is not null ? (expr is exp.Subquery ? expr.unnest() : throw new InvalidOperationException("Expected a Subquery")) : null;
            }

            ((exp.Expr)first_setop.args["this"]!).pop();

            var setops = new List<exp.Expr> { ((exp.Expr)first_setop.args["expression"]!).pop().unnest() };
            setops.AddRange(_parse_csv(ParseAndUnwrapQuery));

            var built = _build_pipe_cte(query, [new exp.Star()]);
            var with_ = built.args.get("with_") as exp.Expr;
            var ctes = with_?.pop();

            // query.union(*setops, copy=False, **first_setop.args) and friends: reduce into left-deep set operations
            var klass = first_setop.GetType();
            exp.Expr result = built;
            foreach (var right in setops)
            {
                // set_operation(this=x, expression=y, distinct=distinct, **opts), in that keyword order
                var arguments = new PyDict<string, object?>
                {
                    ["this"] = result,
                    ["expression"] = right,
                    ["distinct"] = first_setop.args.TryGetValue("distinct", out var distinct) ? distinct : true
                };
                foreach (var (k, v) in first_setop.args)
                    if (k != "this" && k != "expression" && k != "distinct") arguments[k] = v;
                result = (exp.Expr)Activator.CreateInstance(klass, [arguments])!;
            }

            result.set("with_", ctes);

            return _build_pipe_cte(result, [new exp.Star()]);
        }

        // py: sqlglot/parser.py::Parser._parse_pipe_syntax_join #0000000000
        public exp.Expr? _parse_pipe_syntax_join(exp.Expr query)
        {
            var join = _parse_join();
            if (join is null) return null;

            if (query is exp.Select)
            {
                var joins = new List<object?>();
                if (query.args.get("joins") is List<object?> existing) joins.AddRange(existing);
                joins.Add(join);
                query.set("joins", joins);
            }

            return query;
        }

        // py: sqlglot/parser.py::Parser._parse_pipe_syntax_pivot #0000000000
        public exp.Expr _parse_pipe_syntax_pivot(exp.Select query)
        {
            var pivots = _parse_pivots();
            if (pivots is null || pivots.Count == 0) return query;

            if (query.args.get("from_") is exp.Expr from_) ((exp.Expr)from_.args["this"]!).set("pivots", pivots);

            return _build_pipe_cte(query, [new exp.Star()]);
        }

        // py: sqlglot/parser.py::Parser._parse_pipe_syntax_extend #0000000000
        public exp.Expr _parse_pipe_syntax_extend(exp.Select query)
        {
            _match_text_seq(["EXTEND"]);
            var items = new List<exp.Expr> { new exp.Star() };
            items.AddRange(_parse_expressions());
            PipeSetSelect(query, items, append: false);
            return _build_pipe_cte(query, [new exp.Star()]);
        }

        // py: sqlglot/parser.py::Parser._parse_pipe_syntax_tablesample #0000000000
        public exp.Expr _parse_pipe_syntax_tablesample(exp.Select query)
        {
            var sample = _parse_table_sample();

            var with_ = query.args.get("with_") as exp.Expr;
            if (with_ is not null)
            {
                var items = (List<object?>)with_.args["expressions"]!;
                ((exp.Expr)((exp.Expr)items[^1]!).args["this"]!).set("sample", sample);
            }
            else query.set("sample", sample);

            return query;
        }

        // py: sqlglot/parser.py::Parser._parse_pipe_syntax_query #0000000000
        public exp.Expr? _parse_pipe_syntax_query(exp.Expr query)
        {
            // exp.select("*") parses the string "*", which positions the Star node inside that string
            exp.Expr ParsedStar() => new exp.Star().update_positions(line: 1, col: 1, start: 0, end: 0);

            if (query is exp.Subquery)
                query = PipeSelectFrom([ParsedStar()], query);

            if (!py.truthy(query.args.get("from_")))
                query = PipeSelectFrom([ParsedStar()], query.subquery(copy: false));

            while (_match(TokenType.PIPE_GT))
            {
                var start_index = _index;
                var start_text = py.upper(_curr.text);
                if (!PIPE_SYNTAX_TRANSFORM_PARSERS.TryGetValue(start_text, out var parser))
                {
                    // The set operators (UNION, etc) and the JOIN operator have a few common starting
                    // keywords, making it tricky to disambiguate them without lookahead. The approach
                    // here is to try and parse a set operation and if that fails, then try to parse a
                    // join operator. If that fails as well, then the operator is not supported.
                    var parsed_query = _parse_pipe_syntax_set_operator(query);
                    parsed_query ??= _parse_pipe_syntax_join(query);
                    if (parsed_query is null)
                    {
                        _retreat(start_index);
                        raise_error($"Unsupported pipe syntax operator: '{start_text}'.");
                        break;
                    }
                    query = parsed_query;
                }
                else query = parser(this, query)!;
            }

            return query;
        }
    }
}
