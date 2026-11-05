using System.Text.RegularExpressions;
using Gen = SqlGlotSharp.generator.Generator;

namespace SqlGlotSharp.generators;

// Shared helpers of sqlglot/dialects/dialect.py and sqlglot/transforms.py used by the T-SQL generator.
public static class dialect_helpers
{
    private static readonly Regex SAFE_IDENTIFIER_RE = new(@"^[_a-zA-Z][\w]*$");

    private static List<exp.Expr> ExprList(exp.Expr node, string key) =>
        ((System.Collections.IEnumerable)(node.args.get(key) ?? new List<object?>())).Cast<exp.Expr>().ToList();

    // py: sqlglot/expressions/query.py::{Select,Subquery,SetOperation}.selects
    public static List<exp.Expr> selects(exp.Expr query)
    {
        while (true)
        {
            switch (query)
            {
                case exp.Select: return ExprList(query, "expressions");
                case exp.Subquery: query = (exp.Expr)query.args["this"]!; break;
                case exp.SetOperation: query = ((exp.Expr)query.args["this"]!).unnest(); break;
                default: return new List<exp.Expr>();
            }
        }
    }

    // py: sqlglot/expressions/query.py::{Select,SetOperation}.named_selects
    public static List<string> named_selects(exp.Expr query)
    {
        while (query is exp.SetOperation) query = ((exp.Expr)query.args["this"]!).unnest();
        return selects(query).Where(e => e.alias_or_name.Length != 0).Select(e => e.output_name).ToList();
    }

    // py: sqlglot/expressions/builders.py::cast (default dialect: the base generator's TYPE_MAPPING decides equivalence)
    public static exp.Expr cast(object? expression, exp.DataType data_type)
    {
        var expr = expression is exp.Expr e ? e.copy() : (exp.Expr)expression!;
        // dont re-cast if the expression is already a cast to the correct type
        if (expr is exp.Cast existing)
        {
            var existing_type = ((exp.DataType)existing.args["to"]!).@this;
            var equivalent = existing_type is exp.DType et && data_type.@this is exp.DType nt
                && (Gen.TYPE_MAPPING_.TryGetValue(et, out var em) ? em : et.ToString())
                == (Gen.TYPE_MAPPING_.TryGetValue(nt, out var nm) ? nm : nt.ToString());
            if (expr.is_type(data_type) || equivalent) return expr;
        }

        var result = new exp.Cast(@this: expr, to: data_type);
        result.type = data_type;
        return result;
    }

    // py: sqlglot/dialects/dialect.py::unit_to_var
    public static exp.Expr? unit_to_var(exp.Expr expression, string @default = "DAY")
    {
        var unit = expression.args.get("unit") as exp.Expr;

        if (unit is exp.Var or exp.Placeholder or exp.WeekStart or exp.Column) return unit;

        var value = unit is not null ? unit.name : @default;
        return value.Length != 0 ? new exp.Var(@this: value) : null;
    }

    // py: sqlglot/dialects/dialect.py::date_delta_sql
    public static Func<Gen, exp.Expr, string> date_delta_sql(string name, bool cast = false) => (self, expression) =>
    {
        if (cast && expression is exp.TsOrDsAdd)
            expression = generators.postgres.PostgresGenerator.ts_or_ds_add_cast(self, expression);

        return self.func(name, unit_to_var(expression), expression.args["expression"], expression.args["this"]);
    };

    // py: sqlglot/dialects/dialect.py::remove_ts_or_ds_to_date
    public static Func<Gen, exp.Expr, string> remove_ts_or_ds_to_date(Func<Gen, exp.Expr, string>? to_sql = null,
        params string[] args) => (self, expression) =>
    {
        foreach (var arg_key in args.Length == 0 ? ["this"] : args)
        {
            var arg = expression.args.get(arg_key);
            if (arg is exp.TsOrDsToDate or exp.TsOrDsToTimestamp && !py.truthy(((exp.Expr)arg).args.get("format")))
                expression.set(arg_key, ((exp.Expr)arg).args.get("this"));
        }

        return to_sql is not null ? to_sql(self, expression) : self.function_fallback_sql(expression);
    };

    // py: sqlglot/dialects/dialect.py::timestrtotime_sql
    public static string timestrtotime_sql(Gen self, exp.TimeStrToTime expression)
    {
        var builder = py.truthy(expression.args.get("zone")) ? exp.DType.TIMESTAMPTZ : exp.DType.TIMESTAMP;
        var datatype = exp.DataType.build(builder);
        return self.sql(self.exp_cast(((exp.Expr)expression.args["this"]!).copy(), datatype));
    }

    // py: sqlglot/dialects/dialect.py::strposition_sql
    public static string strposition_sql(Gen self, exp.StrPosition expression, string func_name = "STRPOS",
        bool supports_position = false, bool supports_occurrence = false, bool use_ansi_position = true)
    {
        object? @string = expression.args.get("this");
        var substr = expression.args.get("substr");
        var position = expression.args.get("position") as exp.Expr;
        var occurrence = expression.args.get("occurrence");
        var zero = exp.Literal.number(0);
        var one = exp.Literal.number(1);

        if (supports_occurrence && py.truthy(occurrence) && supports_position && position is null) position = one;

        var transpile_position = position is not null && !supports_position;
        if (transpile_position) @string = new exp.Substring(@this: @string, start: position);

        exp.Expr func;
        if (func_name == "POSITION" && use_ansi_position)
            func = new exp.Anonymous(@this: func_name, expressions: new List<object?> { new exp.In(@this: substr, field: @string) });
        else
        {
            var args = func_name is "LOCATE" or "CHARINDEX"
                ? new List<object?> { substr, @string } : new List<object?> { @string, substr };
            if (supports_position) args.Add(position);
            if (py.truthy(occurrence))
            {
                if (supports_occurrence) args.Add(occurrence);
                else self.unsupported($"{func_name} does not support the occurrence parameter.");
            }
            func = new exp.Anonymous(@this: func_name, expressions: args);
        }

        if (transpile_position)
        {
            var func_with_offset = new exp.Sub(@this: generators.postgres.PostgresGenerator.Binop(typeof(exp.Add), func, position!), expression: one);
            var func_wrapped = new exp.If(@this: generators.postgres.PostgresGenerator.Binop(typeof(exp.EQ), func, zero),
                @true: zero, @false: func_with_offset);
            return self.sql(func_wrapped);
        }

        return self.sql(func);
    }

    // py: sqlglot/dialects/dialect.py::no_filter_sql
    public static string no_filter_sql(Gen self, exp.Expr expression)
    {
        // Rewrite `AGG(x) FILTER (WHERE cond)` into an equivalent conditional aggregation, i.e. wrap
        // the input values in an IF, which works because aggregate functions skip NULLs
        var agg = (exp.Expr)expression.args["this"]!;
        // IGNORE NULLS can wrap the aggregate, but the condition still belongs to its arguments
        if (agg is exp.IgnoreNulls) agg = (exp.Expr)agg.args["this"]!;

        exp.Expr? agg_arg = agg is exp.Anonymous ? agg.expressions.OfType<exp.Expr>().FirstOrDefault() : agg.args.get("this") as exp.Expr;
        var cond = (exp.Expr)((exp.Expr)expression.args["expression"]!).args["this"]!;

        if (agg is exp.WithinGroup)
        {
            // Ordered-set aggregates take their input from the ORDER BY key, so the
            // condition has to wrap that instead of the aggregate's own argument
            if (agg_arg is exp.Mode or exp.PercentileCont or exp.PercentileDisc)
            {
                foreach (var ordered in ((exp.Expr)agg.args["expression"]!).expressions.OfType<exp.Expr>())
                {
                    var key = (exp.Expr)ordered.args["this"]!;
                    key.replace(new exp.If(@this: cond.copy(), @true: key.copy(), @false: new exp.Null()));
                }

                return self.sql(expression.args["this"]);
            }

            // Besides the ordered-set aggregates, these are the only functions that take WITHIN GROUP
            // and the input values as arguments, so anything else can't be rewritten correctly
            if (agg_arg is exp.ArrayAgg or exp.GroupConcat) agg_arg = agg_arg.args.get("this") as exp.Expr;
            else
            {
                self.unsupported("Unable to rewrite FILTER into the aggregate's arguments");
                return self.sql(expression.args["this"]);
            }
        }

        // Folding the condition into COUNT_IF's predicate avoids an IF that returns a predicate, which
        // dialects without a boolean type (e.g. T-SQL) reject
        if (agg is exp.CountIf && agg_arg is not exp.Distinct)
            return self.sql(new exp.CountIf(@this: and_(cond, agg_arg)));

        // `COUNT(*/t.*) FILTER (WHERE cond)` counts qualifying rows, but a star can't be an IF
        // argument: `IF(cond, *, NULL)` expands to multiple columns once the table has 2+ of
        // them. Each dialect generates CountIf in its own way instead, e.g. COUNT_IF or COUNTIF.
        if (agg is exp.Count && (agg_arg is null || agg_arg.is_star))
            return self.sql(new exp.CountIf(@this: cond));

        // `DISTINCT` and `ORDER BY` are part of the aggregate's own argument list, so the
        // condition has to wrap the values underneath them rather than the whole clause --
        // `IF(cond, DISTINCT x, NULL)` is not a call any dialect accepts.
        if (agg_arg is exp.Order) agg_arg = agg_arg.args.get("this") as exp.Expr;

        var targets = agg_arg is exp.Distinct ? agg_arg.expressions.OfType<exp.Expr>().ToList() : new List<exp.Expr> { agg_arg! };

        foreach (var target in targets.ToList())
            target.replace(new exp.If(@this: cond.copy(), @true: target.copy(), @false: new exp.Null()));

        // In dialects where ARRAY_AGG keeps NULLs (e.g. BigQuery, which even fails on them), the
        // rows that don't satisfy the condition have to be dropped explicitly
        if (agg is exp.ArrayAgg && self.dialect.ARRAY_AGG_INCLUDES_NULLS)
        {
            // IGNORE NULLS already drops them, so no extra FILTER (WHERE x IS NOT NULL) is needed
            agg.set("nulls_excluded", null);
            if (ReferenceEquals(agg, expression.args["this"]))
                return self.sql(new exp.IgnoreNulls(@this: agg));
        }

        return self.sql(expression.args["this"]);
    }

    // Expression-only equivalent of exp.and_ used by the dialect helpers (None operands are skipped); not an anchor.
    public static exp.Expr? and_(params exp.Expr?[] expressions)
    {
        var conditions = expressions.Where(e => e is not null).Select(e => e!).ToList();
        if (conditions.Count == 0) return null;
        var @this = conditions[0];
        if (conditions.Count > 1 && @this is exp.Connector) @this = new exp.Paren(@this: @this);
        foreach (var expression in conditions.Skip(1))
            @this = new exp.And(@this: @this, expression: expression is exp.Connector ? new exp.Paren(@this: expression) : expression);
        return @this;
    }

    // Expression-only equivalent of exp.or_ used by the dialect helpers (None operands are skipped); not an anchor.
    public static exp.Expr? or_(params exp.Expr?[] expressions) => Combine(expressions, typeof(exp.Or));

    // Expression-only equivalent of exp.xor used by the dialect helpers; not an anchor.
    public static exp.Expr? xor_(params exp.Expr?[] expressions) => Combine(expressions, typeof(exp.Xor));

    private static exp.Expr? Combine(exp.Expr?[] expressions, Type connector)
    {
        var conditions = expressions.Where(e => e is not null).Select(e => e!).ToList();
        if (conditions.Count == 0) return null;
        var @this = conditions[0];
        if (conditions.Count > 1 && @this is exp.Connector) @this = new exp.Paren(@this: @this);
        foreach (var expression in conditions.Skip(1))
            @this = (exp.Expr)Activator.CreateInstance(connector, [new PyDict<string, object?>
            {
                ["this"] = @this, ["expression"] = expression is exp.Connector ? new exp.Paren(@this: expression) : expression,
            }])!;
        return @this;
    }

    // py: sqlglot/expressions/query.py::Table.parts
    public static List<exp.Expr> table_parts(exp.Table table)
    {
        var parts = new List<exp.Expr>();
        foreach (var arg in new[] { "catalog", "db", "this" })
        {
            var part = table.args.get(arg);
            if (part is exp.Dot dot) parts.AddRange(dot.flatten());
            else if (part is exp.Expr e) parts.Add(e);
        }
        return parts;
    }

    // py: sqlglot/expressions/builders.py::column(...).eq(value)
    public static exp.Expr column_eq(string column, string value) =>
        new exp.EQ(@this: new exp.Column(@this: exp.to_identifier(column)), expression: exp.Literal.@string(value));

    // py: sqlglot/expressions/core.py::table_name
    public static string table_name(exp.Table table)
    {
        var parts = new List<exp.Expr>();
        foreach (var arg in new[] { "catalog", "db", "this" })
        {
            var part = table.args.get(arg);
            if (part is exp.Dot dot) parts.AddRange(dot.flatten());
            else if (part is exp.Expr e) parts.Add(e);
        }

        return string.Join(".", parts.Select(part =>
            SAFE_IDENTIFIER_RE.IsMatch(part.name) ? part.name : $"\"{part.name.Replace("\"", "\"\"")}\""));
    }

    // py: sqlglot/expressions/query.py::Query.subquery
    public static exp.Subquery subquery(exp.Expr query, string? alias, bool copy = true)
    {
        var instance = copy ? query.copy() : query;
        return new exp.Subquery(@this: instance,
            alias: alias is not null ? new exp.TableAlias(@this: exp.to_identifier(alias)) : null);
    }

    // py: exp.select("*").from_(expression.subquery(alias, copy=False), copy=False)
    public static exp.Select select_star_from_subquery(exp.Expr expression, string alias) =>
        new exp.Select(expressions: new List<object?> { new exp.Star() },
            from_: new exp.From(@this: subquery(expression, alias, copy: false)));

    // py: exp.alias_(expression, alias, table=True)
    public static exp.Expr table_alias_of(exp.Expr expression, string alias)
    {
        var copy = expression.copy();
        copy.set("alias", new exp.TableAlias(@this: exp.to_identifier(alias)));
        return copy;
    }

    // py: sqlglot/expressions/query.py::Limit.is_limit_all
    public static bool is_limit_all(exp.Limit limit) =>
        limit.args.get("expression") is exp.Var value && value.name.ToUpperInvariant() == "ALL";

    private static exp.Expr AliasWindow(exp.Expr expression, string alias, bool? quoted = null)
    {
        var identifier = exp.to_identifier(alias);
        if (quoted is not null) identifier.set("quoted", quoted);
        var node = expression.copy();
        if (node.arg_types.ContainsKey("alias") && node.GetType().Name != "Window")
        {
            node.set("alias", identifier);
            return node;
        }
        return new exp.Alias(@this: node, alias: identifier);
    }

    // py: sqlglot/transforms.py::eliminate_distinct_on
    /// <summary>Convert SELECT DISTINCT ON statements to a subquery with a window function.</summary>
    public static exp.Expr eliminate_distinct_on(exp.Expr expression)
    {
        if (expression is exp.Select select && select.args.get("distinct") is exp.Distinct distinct
            && distinct.args.get("on") is exp.Tuple)
        {
            var row_number_window_alias = helper.find_new_name(named_selects(select), "_row_number");

            var distinct_cols = ((exp.Expr)((exp.Expr)select.args["distinct"]!).pop().args["on"]!).expressions.OfType<exp.Expr>().ToList();
            var window = new exp.Window(@this: new exp.RowNumber(), partition_by: distinct_cols.Cast<object?>().ToList());

            if (select.args.get("order") is exp.Order order) window.set("order", order.pop());
            else window.set("order", new exp.Order(expressions: distinct_cols.Select(c => (object?)c.copy()).ToList()));

            var current = ExprList(select, "expressions");
            current.Add(AliasWindow(window, row_number_window_alias));
            select.set("expressions", current.Cast<object?>().ToList());

            // We add aliases to the projections so that we can safely reference them in the outer query
            var new_selects = new List<object?>();
            var taken_names = new HashSet<string> { row_number_window_alias };
            var selects = dialect_helpers.selects(select);
            foreach (var original in selects.Take(selects.Count - 1))
            {
                var item = original;
                if (item.is_star)
                {
                    new_selects = [new exp.Star()];
                    break;
                }

                if (item is not exp.Alias)
                {
                    var alias = helper.find_new_name(taken_names, item.output_name.Length != 0 ? item.output_name : "_col");
                    bool? quoted = item is exp.Column ? item.args.get("this") is exp.Expr id && id.args.get("quoted") is bool q ? q : null : null;
                    item = (exp.Expr)item.replace(AliasWindow(item, alias, quoted))!;
                }

                taken_names.Add(item.output_name);
                new_selects.Add(item.args["alias"]);
            }

            return new exp.Select(expressions: new_selects,
                from_: new exp.From(@this: subquery(select, "_t", copy: false)),
                where: new exp.Where(@this: new exp.EQ(@this: new exp.Column(@this: exp.to_identifier(row_number_window_alias)),
                    expression: exp.Literal.number(1))));
        }

        return expression;
    }

    // py: sqlglot/transforms.py::unnest_generate_date_array_using_recursive_cte
    public static exp.Expr unnest_generate_date_array_using_recursive_cte(exp.Expr expression)
    {
        if (expression is exp.Select select)
        {
            var count = 0;
            var recursive_ctes = new List<object?>();

            foreach (var unnest in select.find_all<exp.Unnest>().ToList())
            {
                if (unnest.parent is not (exp.From or exp.Join)
                    || unnest.expressions.Count != 1
                    || unnest.expressions[0] is not exp.GenerateDateArray generate_date_array)
                    continue;

                var start = generate_date_array.args.get("start") as exp.Expr;
                var end = generate_date_array.args.get("end") as exp.Expr;
                var step = generate_date_array.args.get("step") as exp.Interval;

                if (start is null || end is null || step is null) continue;

                var alias = unnest.args.get("alias") as exp.TableAlias;
                exp.Expr column_name = alias is not null && alias.args.get("columns") is List<object?> { Count: > 0 } cols
                    ? (exp.Expr)cols[0]! : exp.to_identifier("date_value");

                var start_cast = cast(start, exp.DataType.build("date"));
                var date_add = new exp.DateAdd(@this: column_name.copy(), expression: exp.Literal.number(step.name),
                    unit: step.args.get("unit"));
                exp.Expr cast_date_add = cast(date_add, exp.DataType.build("date"));

                var cte_name = "_generated_dates" + (count != 0 ? $"_{count}" : "");

                var base_query = new exp.Select(expressions: new List<object?> { new exp.Alias(@this: start_cast, alias: column_name.copy()) });
                var recursive_query = new exp.Select(expressions: new List<object?> { cast_date_add.copy() },
                    from_: new exp.From(@this: new exp.Table(@this: exp.to_identifier(cte_name))),
                    where: new exp.Where(@this: new exp.LTE(@this: cast_date_add.copy(),
                        expression: cast(end, exp.DataType.build("date")))));
                var cte_query = new exp.Union(@this: base_query, expression: recursive_query, distinct: false);

                var generate_dates_query = new exp.Select(expressions: new List<object?> { column_name.copy() },
                    from_: new exp.From(@this: new exp.Table(@this: exp.to_identifier(cte_name))));
                unnest.replace(subquery(generate_dates_query, cte_name, copy: false));

                recursive_ctes.Add(new exp.CTE(@this: cte_query,
                    alias: new exp.TableAlias(@this: exp.to_identifier(cte_name),
                        columns: new List<object?> { column_name.copy() })));
                count++;
            }

            if (recursive_ctes.Count > 0)
            {
                var with_expression = select.args.get("with_") as exp.With ?? new exp.With();
                with_expression.set("recursive", true);
                recursive_ctes.AddRange(ExprList(with_expression, "expressions"));
                with_expression.set("expressions", recursive_ctes);
                select.set("with_", with_expression);
            }
        }

        return expression;
    }
}
