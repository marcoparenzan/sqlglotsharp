namespace SqlGlotSharp;

public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.set_operation #6adebb0130
        public virtual string set_operation(exp.SetOperation expression)
        {
            var op_type = expression.GetType();
            var op_name = expression.key.ToUpperInvariant();
            var distinct = expression.args.get("distinct");
            if (distinct is false && op_type is not null &&
                (op_type == typeof(exp.Except) || op_type == typeof(exp.Intersect)) &&
                !EXCEPT_INTERSECT_SUPPORT_ALL_CLAUSE)
                unsupported($"{op_name} ALL is not supported");

            object? default_distinct = dialect.SET_OP_DISTINCT_BY_DEFAULT[op_type!];
            if (distinct is null)
            {
                distinct = default_distinct;
                if (distinct is null) unsupported($"{op_name} requires DISTINCT or ALL to be specified");
            }
            var distinct_or_all = Equals(distinct, default_distinct) ? "" : py.truthy(distinct) ? " DISTINCT" : " ALL";
            // SetOperation.side/kind are text(key).upper() upstream; the shared AST
            // does not yet expose those convenience properties.
            var side_kind = string.Join(" ", new[] { expression.text("side").ToUpperInvariant(), expression.text("kind").ToUpperInvariant() }
                .Where(value => value.Length != 0));
            if (side_kind.Length != 0) side_kind += " ";
            var by_name = py.truthy(expression.args.get("by_name")) ? " BY NAME" : "";
            var on = expressions(expression, key: "on", flat: true);
            if (on.Length != 0) on = $" ON ({on})";
            return $"{side_kind}{op_name}{distinct_or_all}{by_name}{on}";
        }

        // Partial only at _move_ctes_to_top_level: the optimizer-dependent rewrite
        // is an explicit dependency, not a simplified alternate query.
        // py: sqlglot/generator.py::Generator.set_operations #0000000000
        public virtual string set_operations(exp.SetOperation expression)
        {
            if (!py.truthy(SET_OP_MODIFIERS) &&
                new[] { "limit", "order", "offset" }.Any(key => py.truthy(expression.args.get(key))))
                throw new NotPortedException("sqlglot/generator.py::Generator._move_ctes_to_top_level");

            var sqls = new List<string>();
            var stack = new Stack<object?>();
            stack.Push(expression);
            var depth = 0;
            var open_parens = "";
            while (stack.TryPop(out var node))
            {
                if (node is int closing_depth)
                {
                    depth = closing_depth;
                    sqls[^1] += sep("") + indent(")", level: depth, pad: 0);
                }
                else if (node is exp.SetOperation operation)
                {
                    stack.Push(operation.expression);
                    stack.Push(maybe_comment(set_operation(operation), comments: operation.comments, separated: true));
                    var left = operation.@this;
                    if (!SET_OP_PARENTHESIZED_OPERANDS && !dialect.INTERSECT_BINDS_TIGHTER_THAN_UNION_AND_EXCEPT)
                    {
                        var wrappers = new List<exp.Subquery>();
                        var inner = left;
                        while (inner is exp.Subquery wrapper && set_operations__is_wrapper(wrapper))
                        {
                            wrappers.Add(wrapper);
                            inner = wrapper.@this;
                        }
                        if (inner is exp.SetOperation inner_operation && !py.truthy(inner_operation.args.get("with_")) &&
                            !set_operations__has_modifiers(inner_operation))
                        {
                            foreach (var wrapper in wrappers) inner_operation.add_comments(wrapper.pop_comments());
                            left = inner_operation;
                        }
                    }
                    else if (SET_OP_PARENTHESIZED_OPERANDS && left is exp.Subquery wrapper &&
                        set_operations__is_wrapper(wrapper) && !py.truthy(wrapper.comments) &&
                        wrapper.@this is exp.SetOperation inner_operation && !py.truthy(inner_operation.args.get("with_")) &&
                        !set_operations__has_modifiers(inner_operation))
                    {
                        open_parens += indent("(", level: depth, pad: 0) + sep("");
                        stack.Push(depth);
                        depth++;
                        left = inner_operation;
                    }
                    stack.Push(left);
                }
                else
                {
                    if (!SET_OP_LIMITS && node is exp.Select select && py.truthy(select.args.get("limit")))
                    {
                        node = new exp.Subquery(@this: select);
                        if (!SET_OP_PARENTHESIZED_OPERANDS) node = set_operations__select_from((exp.Subquery)node);
                    }
                    else if (!SET_OP_PARENTHESIZED_OPERANDS && node is exp.Subquery subquery && set_operations__unnest(subquery) is exp.SetOperation)
                        node = set_operations__select_from(subquery);
                    sqls.Add(open_parens + indent(sql(node), level: depth, pad: 0));
                    open_parens = "";
                }
            }
            var this_sql = string.Join(sep(), sqls);
            this_sql = query_modifiers(expression, this_sql);
            return prepend_ctes(expression, this_sql);
        }

        // Local equivalents of Subquery.is_wrapper, QUERY_MODIFIERS and the
        // copy=False builders. No AST members are claimed as ported here.
        private static object? set_operations__unnest(exp.Subquery expression)
        {
            object? inner = expression;
            while (inner is exp.Subquery subquery) inner = subquery.@this;
            return inner;
        }
        private static bool set_operations__is_wrapper(exp.Subquery expression) =>
            expression.args.All(pair => pair.Key == "this" || pair.Value is null);
        private static bool set_operations__has_modifiers(exp.Expr expression) =>
            "match laterals joins connect pivots prewhere where group having qualify windows distribute sort cluster order limit offset locks sample settings format options for_"
                .Split(' ').Any(key => py.truthy(expression.args.get(key)));
        private static exp.Select set_operations__select_from(exp.Subquery expression) =>
            new(expressions: new List<object?> { new exp.Star() }, from_: new exp.From(@this: expression));

        // py: sqlglot/generator.py::Generator.subquery_sql #c484d4619e
        public virtual string subquery_sql(exp.Subquery expression, string sep = " AS ")
        {
            var alias = sql(expression, "alias");
            if (alias.Length != 0) alias = sep + alias;
            var sample = sql(expression, "sample");
            if (dialect.ALIAS_POST_TABLESAMPLE && sample.Length != 0)
            {
                alias = sample + alias;
                expression.set("sample", null);
            }
            var pivots = expressions(expression, key: "pivots", sep: "", flat: true);
            var this_sql = query_modifiers(expression, wrap(expression), alias, pivots);
            return prepend_ctes(expression, this_sql);
        }
    }
}
