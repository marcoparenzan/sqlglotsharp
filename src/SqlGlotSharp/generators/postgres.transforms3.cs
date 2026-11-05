namespace SqlGlotSharp.generators;

// MERGE, percentile and array handlers of sqlglot/generators/postgres.py (scope-walking helpers included).
public static partial class postgres
{
    public partial class PostgresGenerator
    {
        // py: sqlglot/optimizer/scope.py::_is_derived_table
        private static bool IsDerivedTable(exp.Expr expression) =>
            expression is exp.Subquery && (py.truthy(expression.alias) || py.truthy(expression.args.get("pivots"))
                || expression.args.get("this") is exp.Select or exp.SetOperation);

        // py: sqlglot/optimizer/scope.py::walk_in_scope
        internal static IEnumerable<exp.Expr> walk_in_scope(exp.Expr expression)
        {
            var stack = new Stack<exp.Expr>();
            stack.Push(expression);

            while (stack.Count > 0)
            {
                var node = stack.Pop();

                yield return node;

                // Only CTEs and Queries can start child scopes
                if (!ReferenceEquals(node, expression) && node is exp.CTE or exp.Query
                    && (node is exp.CTE
                        || (node.parent is exp.From or exp.Join && IsDerivedTable(node))
                        || node.parent is exp.UDTF
                        || node is exp.Select or exp.SetOperation))
                {
                    if (node is exp.Subquery or exp.UDTF)
                        foreach (var key in new[] { "joins", "laterals", "pivots" })
                            if (node.args.get(key) is System.Collections.IEnumerable items)
                                foreach (var arg in items.OfType<exp.Expr>())
                                    foreach (var inner in walk_in_scope(arg)) yield return inner;
                    continue;
                }

                foreach (var vs in node.args.Values.Reverse())
                {
                    if (vs is List<object?> list)
                    {
                        for (var i = list.Count - 1; i >= 0; i--)
                            if (list[i] is exp.Expr v) stack.Push(v);
                    }
                    else if (vs is exp.Expr single) stack.Push(single);
                }
            }
        }

        // py: sqlglot/optimizer/scope.py::find_all_in_scope
        internal static IEnumerable<exp.Expr> find_all_in_scope(exp.Expr expression, params Type[] types) =>
            walk_in_scope(expression).Where(node => types.Any(t => t.IsInstanceOfType(node)));

        // py: sqlglot/dialects/dialect.py::merge_without_target_sql
        private static string merge_without_target_sql(generator.Generator self, exp.Expr expression)
        {
            // Remove table refs from columns in when statements.
            var alias = ((exp.Expr)expression.args["this"]!).args.get("alias") as exp.Expr;

            string? Normalize(object? identifier) =>
                identifier is exp.Identifier id ? self.dialect.normalize_identifier(id).name : null;

            var targets = new HashSet<string?> { Normalize(((exp.Expr)expression.args["this"]!).args.get("this")) };

            if (alias is not null) targets.Add(Normalize(alias.args.get("this")));

            var whens = (exp.Expr)expression.args["whens"]!;
            foreach (var when in ((System.Collections.IEnumerable)whens.args["expressions"]!).OfType<exp.Expr>())
            {
                // only remove the target table names from certain parts of WHEN MATCHED / WHEN NOT MATCHED
                // they are still valid in the <condition>, the right hand side of each UPDATE and the VALUES part
                // (not the column list) of the INSERT
                if (when.args.get("then") is exp.Expr then)
                {
                    if (then is exp.Update)
                    {
                        foreach (var equals in find_all_in_scope(then, typeof(exp.EQ)).ToList())
                        {
                            var equal_lhs = equals.args.get("this");
                            if (equal_lhs is exp.Column column && targets.Contains(Normalize(column.args.get("table"))))
                                column.replace(new exp.Column(@this: column.args.get("this")));
                        }
                    }
                    if (then is exp.Insert)
                    {
                        var column_list = then.args.get("this");
                        if (column_list is exp.Tuple tuple)
                            foreach (var column in ((System.Collections.IEnumerable)tuple.args["expressions"]!).OfType<exp.Column>().ToList())
                                if (targets.Contains(Normalize(column.args.get("table"))))
                                    column.replace(new exp.Column(@this: column.args.get("this")));
                    }
                }
            }

            return self.merge_sql((exp.Merge)expression);
        }

        // py: sqlglot/transforms.py::add_within_group_for_percentiles (+ preprocess dispatch)
        private static string percentile_sql(generator.Generator self, exp.Expr expression)
        {
            var original = expression;
            if (expression is exp.PercentileCont or exp.PercentileDisc
                && expression.parent is not exp.WithinGroup && py.truthy(expression.args.get("expression")))
            {
                var column = ((exp.Expr)expression.args["this"]!).pop();
                expression.set("this", ((exp.Expr)expression.args["expression"]!).pop());
                var order = new exp.Order(expressions: new List<object?> { new exp.Ordered(@this: column) });
                expression = new exp.WithinGroup(@this: expression, expression: order);
            }

            return ReferenceEquals(expression, original) ? self.function_fallback_sql(expression) : self.sql(expression);
        }

        // py: sqlglot/expressions/builders.py::func (named function with parsed string arguments)
        private static exp.Expr FuncCall(string name, params object?[] args)
        {
            var converted = args.Select(arg => arg switch
            {
                exp.Expr node => node.copy(),
                string text => SqlGlot.ParseOne(text),
                long number => (object?)exp.Literal.number(number),
                int number => exp.Literal.number(number),
                _ => throw new ArgumentException("Unsupported func argument")
            }).ToList();

            return parser.Parser.FUNCTIONS_.TryGetValue(name.ToUpperInvariant(), out var builder)
                ? builder(converted) : new exp.Anonymous(@this: name, expressions: converted);
        }

        // py: sqlglot/dialects/dialect.py::no_last_day_sql
        private static string no_last_day_sql(generator.Generator self, exp.Expr expression)
        {
            var trunc_curr_date = FuncCall("date_trunc", "month", expression.args["this"]);
            var plus_one_month = FuncCall("date_add", trunc_curr_date, 1L, "month");
            var minus_one_day = FuncCall("date_sub", plus_one_month, 1L, "day");

            return self.sql(self.exp_cast(minus_one_day, exp.DType.DATE));
        }

        // py: sqlglot/dialects/dialect.py::filter_array_using_unnest
        private static string filter_array_using_unnest(generator.Generator self, exp.Expr expression)
        {
            var cond = expression.args.get("expression") as exp.Expr;
            object alias;
            if (cond is exp.Lambda && cond.args["expressions"] is List<object?> { Count: 1 } lambda_args)
            {
                alias = lambda_args[0]!;
                cond = (exp.Expr)cond.args["this"]!;
            }
            else if (cond is exp.Predicate) alias = "_u";
            else if (expression is exp.ArrayRemove)
            {
                alias = "_u";
                cond = new exp.NEQ(@this: alias, expression: expression.args.get("expression"));
            }
            else
            {
                self.unsupported("Unsupported filter condition");
                return "";
            }

            var unnest = new exp.Unnest(expressions: new List<object?> { expression.args["this"] });
            var alias_identifier = exp.to_identifier(alias);
            unnest.set("alias", new exp.TableAlias(columns: new List<object?> { alias_identifier }));
            var select_item = alias is string ? (object?)SqlGlot.ParseOne((string)alias) : alias;
            var filtered = new exp.Select(expressions: new List<object?> { select_item },
                from_: new exp.From(@this: unnest), where: new exp.Where(@this: cond));
            return self.sql(new exp.Array(expressions: new List<object?> { filtered }));
        }

        internal static readonly IReadOnlyDictionary<Type, Func<generator.Generator, exp.Expr, string>> SCOPE_TRANSFORMS =
            new Dictionary<Type, Func<generator.Generator, exp.Expr, string>>
            {
                [typeof(exp.Merge)] = merge_without_target_sql,
                [typeof(exp.LastDay)] = no_last_day_sql,
                [typeof(exp.ArrayFilter)] = filter_array_using_unnest,
                [typeof(exp.PercentileCont)] = percentile_sql,
                [typeof(exp.PercentileDisc)] = percentile_sql,
                [typeof(exp.ToChar)] = (self, e) => py.truthy(e.args.get("format")) ? self.function_fallback_sql(e) : self.tochar_sql((exp.ToChar)e),
            };

        // py: sqlglot/generators/postgres.py::PostgresGenerator.AFTER_HAVING_MODIFIER_TRANSFORMS (module-level table: windows, qualify)
        public static readonly PyDict<string, Func<generator.Generator, exp.Expr, string>> AFTER_HAVING_MODIFIER_TRANSFORMS_PG = new()
        {
            ["windows"] = (self, e) => py.truthy(e.args.get("windows"))
                ? self.seg("WINDOW ") + self.expressions(e, key: "windows", flat: true) : "",
            ["qualify"] = (self, e) => self.sql(e, "qualify"),
        };
        public override PyDict<string, Func<generator.Generator, exp.Expr, string>> AFTER_HAVING_MODIFIER_TRANSFORMS => AFTER_HAVING_MODIFIER_TRANSFORMS_PG;

        // py: sqlglot/generators/postgres.py::PostgresGenerator.matchagainst_sql
        public override string matchagainst_sql(exp.MatchAgainst expression)
        {
            var @this = sql(expression, "this");
            var items = ((System.Collections.IEnumerable)expression.args["expressions"]!).Cast<object?>();
            var exprs = items.Select(e => $"{sql(e)} @@ {@this}").ToList();
            var text = string.Join(" OR ", exprs);
            return exprs.Count > 1 ? $"({text})" : text;
        }

        // py: sqlglot/generators/postgres.py::PostgresGenerator.schemacommentproperty_sql
        public virtual string schemacommentproperty_sql(exp.SchemaCommentProperty expression)
        {
            unsupported("Table comments are not supported in the CREATE statement");
            return "";
        }

        // py: sqlglot/generators/postgres.py::PostgresGenerator.arraycontains_sql
        public virtual string arraycontains_sql(exp.ArrayContains expression)
        {
            // Convert DuckDB's LIST_CONTAINS(array, value) to PostgreSQL:
            // CASE WHEN value IS NULL THEN NULL ELSE COALESCE(value = ANY(array), FALSE) END
            var value = (exp.Expr)expression.args["expression"]!;
            var array = (exp.Expr)expression.args["this"]!;

            var coalesce_expr = new exp.Coalesce(
                @this: Binop(typeof(exp.EQ), value, new exp.Any(@this: new exp.Paren(@this: array))),
                expressions: new List<object?> { new exp.Boolean(@this: false) });

            var case_expr = new exp.Case(
                ifs: new List<object?> { new exp.If(@this: new exp.Is(@this: value, expression: new exp.Null()), @true: new exp.Null()) },
                @default: coalesce_expr);

            return sql(case_expr);
        }
    }
}
