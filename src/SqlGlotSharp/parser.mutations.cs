using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

public static partial class parser
{
    public partial class Parser
    {
        // Base/Postgres single-target closure. MySQL multiple-table DELETE and ON CLUSTER
        // remain explicit boundaries; table/expression helpers enforce their own closures.
        // py: sqlglot/parser.py::Parser._parse_delete #0000000000
        public exp.Delete _parse_delete()
        {
            var hint = _parse_hint();
            object? tables = null;
            if (!_match(TokenType.FROM, advance: false))
            {
                // This handles MySQL's "Multiple-Table Syntax"
                // https://dev.mysql.com/doc/refman/8.0/en/delete.html
                var missing = _parse_csv(() => _parse_table());
                tables = missing.Count > 0 ? missing.Cast<object?>().ToList() : null;
            }
            var returning = _parse_returning();
            object? target = _match(TokenType.FROM) ? _parse_table(joins: true) : false;
            object? usingTables = _match(TokenType.USING)
                ? _parse_csv(() => _parse_table(joins: true)).Cast<object?>().ToList() : false;
            object? cluster = _match(TokenType.ON) ? _parse_on_property() : false;
            var where = _parse_where();
            returning ??= _parse_returning();
            var order = _parse_order();
            var limit = _parse_limit();
            return expression(new exp.Delete(new PyDict<string, object?>
            {
                ["hint"] = hint, ["tables"] = tables, ["this"] = target,
                ["using"] = usingTables, ["cluster"] = cluster, ["where"] = where,
                ["returning"] = returning, ["order"] = order, ["limit"] = limit
            }));
        }

        // py: sqlglot/parser.py::Parser._parse_update_assignment #0000000000
        public exp.Expr? _parse_update_assignment()
        {
            var target = _parse_comparison();
            if (_match(TokenType.EQ))
            {
                var comments = _prev_comments;
                target = expression(new exp.EQ(new PyDict<string, object?>
                {
                    ["this"] = target, ["expression"] = _parse_disjunction()
                }), comments: comments);
            }
            return target;
        }

        // Keep the dynamically populated kwargs in upstream insertion order, including
        // clause permutations and repeated keys. Generated named constructors reorder them.
        // py: sqlglot/parser.py::Parser._parse_update #0000000000
        public virtual exp.Update _parse_update()
        {
            var hint = _parse_hint();
            var kwargs = new PyDict<string, object?>
            {
                ["hint"] = hint,
                ["this"] = _parse_table(joins: true, alias_tokens: UPDATE_ALIAS_TOKENS)
            };
            while (py.truthy(_curr))
            {
                if (_match(TokenType.SET))
                    kwargs["expressions"] = _parse_csv(_parse_update_assignment).Cast<object?>().ToList();
                else if (_match(TokenType.RETURNING, advance: false))
                    kwargs["returning"] = _parse_returning();
                else if (_match(TokenType.FROM, advance: false))
                {
                    var from = _parse_from(joins: true);
                    if (from?.@this is exp.Subquery table && _match(TokenType.JOIN, advance: false))
                    {
                        var joins = _parse_joins().Cast<object?>().ToList();
                        table.set("joins", joins.Count > 0 ? joins : null);
                    }
                    kwargs["from_"] = from;
                }
                else if (_match(TokenType.WHERE, advance: false)) kwargs["where"] = _parse_where();
                else if (_match(TokenType.ORDER_BY, advance: false)) kwargs["order"] = _parse_order();
                else if (_match(TokenType.LIMIT, advance: false)) kwargs["limit"] = _parse_limit();
                else break;
            }
            return expression(new exp.Update(kwargs));
        }
    }
}
