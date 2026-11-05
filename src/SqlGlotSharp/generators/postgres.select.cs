namespace SqlGlotSharp.generators;

public static partial class postgres
{
    public partial class PostgresGenerator
    {
        // Bounded transforms.preprocess closure for PostgreSQL's Select entry.
        // The two transforms run in upstream order, then call the virtual SQL handler
        // directly (not sql, which would recursively re-enter the transform).
        // Only Select -> Select is implemented, so this anchor is deliberately partial.
        // py: sqlglot/transforms.py::preprocess #0000000000
        internal static string select_preprocess_sql(generator.Generator self, exp.Expr expression)
        {
            try
            {
                expression = eliminate_semi_and_anti_joins(expression);
                expression = eliminate_qualify(expression);
            }
            catch (errors.UnsupportedError error)
            {
                self.unsupported(error.Message);
            }
            return self.select_sql((exp.Select)expression);
        }

        // Query builders aren't ported: inline their exact AST operations here.
        // from_ and where default to copy=True in the inner query; the outer where
        // uses copy=False. Connector operands are parenthesized by _combine.
        // py: sqlglot/transforms.py::eliminate_semi_and_anti_joins #0000000000
        internal static exp.Expr eliminate_semi_and_anti_joins(exp.Expr expression)
        {
            if (expression is exp.Select select)
            {
                var joins = (select.args.get("joins") as System.Collections.IEnumerable ?? Array.Empty<object>())
                    .Cast<exp.Join>().ToArray();
                foreach (var join in joins)
                {
                    var kind = py.str(join.args.get("kind") ?? "").ToUpperInvariant();
                    if (join.args.get("on") is not exp.Expr on || kind is not ("SEMI" or "ANTI")) continue;
                    var subquery = new exp.Select(expressions: new List<object?> { exp.Literal.number(1) },
                        from_: new exp.From(@this: ((exp.Expr)join.@this!).copy()),
                        where: new exp.Where(@this: on.copy()));
                    exp.Expr exists = new exp.Exists(@this: subquery);
                    if (kind == "ANTI") exists = new exp.Not(@this: exists);
                    join.pop();
                    if (select.args.get("where") is exp.Where where && where.@this is exp.Expr existing)
                    {
                        var lhs = existing is exp.Connector ? new exp.Paren(@this: existing) : existing;
                        exists = new exp.And(@this: lhs, expression: exists);
                    }
                    select.set("where", new exp.Where(@this: exists));
                }
            }
            return expression;
        }

        // py: sqlglot/transforms.py::eliminate_qualify #0000000000
        internal static exp.Expr eliminate_qualify(exp.Expr expression)
        {
            // Convert SELECT statements that contain the QUALIFY clause into subqueries, filtered equivalently.
            if (expression is exp.Select select && py.truthy(select.args.get("qualify")))
            {
                var parent = select.parent;
                if (parent is exp.Insert && parent.parent is exp.MultitableInserts)
                    // A FROM-first multi-table INSERT branch shares its parent's FROM, so it can't be wrapped
                    throw new errors.UnsupportedError("QUALIFY is not supported in a FROM-first multi-table INSERT branch");

                List<exp.Expr> Selects() => ((System.Collections.IEnumerable)(select.args.get("expressions") ?? new List<object?>())).Cast<exp.Expr>().ToList();
                List<string> NamedSelects() => Selects().Select(x => x.output_name).ToList();

                var taken = new HashSet<string>(NamedSelects());
                foreach (var item in Selects())
                {
                    if (item.alias_or_name.Length == 0)
                    {
                        var alias = helper.find_new_name(taken, "_c");
                        item.replace(generator.Generator.alias_(item, alias));
                        taken.Add(alias);
                    }
                }

                exp.Expr SelectAliasOrName(exp.Expr item)
                {
                    var alias_or_name = item.alias_or_name;
                    var identifier = item.args.get("alias") ?? item.args.get("this");
                    if (identifier is exp.Identifier id)
                        return new exp.Column(@this: exp.to_identifier(alias_or_name, quoted: id.args.get("quoted") as bool?));
                    return SqlGlot.ParseOne(alias_or_name);
                }

                var outer_selects = new exp.Select(expressions: Selects().Select(x => (object?)SelectAliasOrName(x)).ToList());
                var qualify_filters = (exp.Expr)((exp.Expr)select.args["qualify"]!).pop().args["this"]!;
                var expression_by_alias = new Dictionary<string, exp.Expr>();
                foreach (var item in Selects())
                    if (item is exp.Alias) expression_by_alias[item.alias] = (exp.Expr)item.args["this"]!;

                var select_candidates = select.is_star ? new[] { typeof(exp.Window) } : new[] { typeof(exp.Window), typeof(exp.Column) };
                foreach (var select_candidate in qualify_filters.find_all(select_candidates).ToList())
                {
                    if (select_candidate is exp.Window)
                    {
                        if (expression_by_alias.Count > 0)
                            foreach (var column in select_candidate.find_all<exp.Column>().ToList())
                                if (expression_by_alias.TryGetValue(column.name, out var expr)) column.replace(expr);

                        var alias = helper.find_new_name(NamedSelects(), "_w");
                        var projections = ((System.Collections.IEnumerable)(select.args.get("expressions") ?? new List<object?>())).Cast<object?>().ToList();
                        projections.Add(generator.Generator.alias_(select_candidate, alias));
                        select.set("expressions", projections);
                        var replacement = new exp.Column(@this: exp.to_identifier(alias));

                        if (select_candidate.parent is exp.Qualify) qualify_filters = replacement;
                        else select_candidate.replace(replacement);
                    }
                    else if (!NamedSelects().Contains(select_candidate.name) && select_candidate.find_ancestor(typeof(exp.Window)) is null)
                    {
                        // A column that is only read by a window function is computed by the window
                        // itself, so projecting it in the subquery is redundant and can even produce
                        // invalid SQL, e.g. when the query is grouped
                        var projections = ((System.Collections.IEnumerable)(select.args.get("expressions") ?? new List<object?>())).Cast<object?>().ToList();
                        projections.Add(select_candidate.copy());
                        select.set("expressions", projections);
                    }
                }

                var subquery = new exp.Subquery(@this: select, alias: new exp.TableAlias(@this: exp.to_identifier("_t")));
                outer_selects.set("from_", new exp.From(@this: subquery));
                outer_selects.set("where", new exp.Where(@this: qualify_filters));
                return outer_selects;
            }

            return expression;
        }
    }
}
