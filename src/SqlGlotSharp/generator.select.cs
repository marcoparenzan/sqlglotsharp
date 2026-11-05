namespace SqlGlotSharp;

// Bounded query assembly from sqlglot/generator.py. SELECT rewrites and the
// optimizer-dependent limit branch remain explicit failures (zero hashes).
// This does not implement preprocess or make generate operational.
public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.AFTER_HAVING_MODIFIER_TRANSFORMS #5a6c134dd8
        public static readonly PyDict<string, Func<Generator, exp.Expr, string>> AFTER_HAVING_MODIFIER_TRANSFORMS_ = new()
        {
            ["cluster"] = (self, e) => self.sql(e, "cluster"),
            ["distribute"] = (self, e) => self.sql(e, "distribute"),
            ["sort"] = (self, e) => self.sql(e, "sort"),
            ["windows"] = (self, e) => py.truthy(e.args.get("windows"))
                ? self.seg("WINDOW ") + self.expressions(e, key: "windows", flat: true) : "",
            ["qualify"] = (self, e) => self.sql(e, "qualify"),
        };
        public virtual PyDict<string, Func<Generator, exp.Expr, string>> AFTER_HAVING_MODIFIER_TRANSFORMS => AFTER_HAVING_MODIFIER_TRANSFORMS_;

        // py: sqlglot/generator.py::Generator.prepend_ctes #85e5b47679
        public virtual string prepend_ctes(exp.Expr expression, string sql)
        {
            var with_ = this.sql(expression, "with_");
            if (with_.Length != 0) sql = $"{with_}{sep()}{sql}";
            return sql;
        }

        // py: sqlglot/generator.py::Generator.fetch_sql #af504451a3
        public virtual string fetch_sql(exp.Fetch expression)
        {
            var direction = expression.args.get("direction");
            var direction_sql = py.truthy(direction) ? $" {direction}" : "";
            var count = sql(expression, "count");
            count = count.Length != 0 ? $" {count}" : "";
            var limit_options = sql(expression, "limit_options");
            limit_options = limit_options.Length != 0 ? limit_options : " ROWS ONLY";
            return $"{seg("FETCH")}{direction_sql}{count}{limit_options}";
        }

        // py: sqlglot/generator.py::Generator.limitoptions_sql #edc85ea761
        public virtual string limitoptions_sql(exp.LimitOptions expression)
        {
            var percent = py.truthy(expression.args.get("percent")) ? " PERCENT" : "";
            var rows = py.truthy(expression.args.get("rows")) ? " ROWS" : "";
            var with_ties = py.truthy(expression.args.get("with_ties")) ? " WITH TIES" : "";
            if (with_ties.Length == 0 && rows.Length != 0) with_ties = " ONLY";
            return $"{percent}{rows}{with_ties}";
        }

        // py: sqlglot/generator.py::Generator.groupingsets_sql #4de7aa476f
        public virtual string groupingsets_sql(exp.GroupingSets expression)
        {
            var grouping_sets = expressions(expression, indent: false);
            return $"GROUPING SETS {wrap(grouping_sets)}";
        }

        // py: sqlglot/generator.py::Generator.rollup_sql #216a924c88
        public virtual string rollup_sql(exp.Rollup expression)
        {
            var expressions = this.expressions(expression, indent: false);
            return expressions.Length != 0 ? $"ROLLUP {wrap(expressions)}" : "WITH ROLLUP";
        }

        // py: sqlglot/generator.py::Generator.cube_sql #8dde018210
        public virtual string cube_sql(exp.Cube expression)
        {
            var expressions = this.expressions(expression, indent: false);
            return expressions.Length != 0 ? $"CUBE {wrap(expressions)}" : "WITH CUBE";
        }

        // py: sqlglot/generator.py::Generator.group_sql #1da8afd18a
        public virtual string group_sql(exp.Group expression)
        {
            var group_by_all = expression.args.get("all");
            var modifier = group_by_all is true ? " ALL" : group_by_all is false ? " DISTINCT" : "";
            var group_by = op_expressions($"GROUP BY{modifier}", expression);
            var grouping_sets = expressions(expression, key: "grouping_sets");
            var cube = expressions(expression, key: "cube");
            var rollup = expressions(expression, key: "rollup");
            var groupings = helper.csv([
                grouping_sets.Length != 0 ? seg(grouping_sets) : "",
                cube.Length != 0 ? seg(cube) : "",
                rollup.Length != 0 ? seg(rollup) : "",
                py.truthy(expression.args.get("totals")) ? seg("WITH TOTALS") : "",
            ], sep: GROUPINGS_SEP);
            if (py.truthy(expression.expressions) && groupings.Length != 0 &&
                Strip(groupings) is not ("WITH CUBE" or "WITH ROLLUP"))
            {
                var add_separator = true;
                if (grouping_sets.Length != 0)
                {
                    if (SUPPORTS_GROUPING_SETS_AS_SUFFIX) add_separator = false;
                    else unsupported("GROUPING SETS without a comma after GROUP BY expressions is not supported");
                }
                if (add_separator) group_by = $"{group_by}{GROUPINGS_SEP}";
            }
            return $"{group_by}{groupings}";
        }

        // Expr.Limit.is_limit_all is not yet exposed by the shared AST. Literal
        // translation of that property at the call site, not a new AST implementation.
        private static bool query_modifiers__is_limit_all(exp.Limit expression) =>
            expression.args.get("expression") is exp.Var value && value.name.ToUpperInvariant() == "ALL";

        // Partial: _simplify_unless_literal's optimizer branch is not ported.
        // py: sqlglot/generator.py::Generator.limit_sql #0000000000
        public virtual string limit_sql(exp.Limit expression, bool top = false)
        {
            var @this = sql(expression, "this");
            if (query_modifiers__is_limit_all(expression) && !dialect.SUPPORTS_LIMIT_ALL) return @this;
            var args = new List<exp.Expr>();
            foreach (var k in new[] { "offset", "expression" })
            {
                var e = expression.args.get(k);
                if (py.truthy(e)) args.Add(LIMIT_ONLY_LITERALS ? _simplify_unless_literal((exp.Expr)e!)! : (exp.Expr)e!);
            }
            var args_sql = string.Join(", ", args.Select(e => sql(e)));
            args_sql = top && args.Any(e => !e.is_number) ? $"({args_sql})" : args_sql;
            var expressions = this.expressions(expression, flat: true);
            var limit_options = sql(expression, "limit_options");
            expressions = expressions.Length != 0 ? $" BY {expressions}" : "";
            return $"{@this}{seg(top ? "TOP" : "LIMIT")} {args_sql}{limit_options}{expressions}";
        }

        // py: sqlglot/generator.py::Generator.offset_sql #0000000000
        public virtual string offset_sql(exp.Offset expression)
        {
            var @this = sql(expression, "this");
            var value = expression.expression as exp.Expr;
            value = LIMIT_ONLY_LITERALS ? _simplify_unless_literal(value) : value;
            var expressions = this.expressions(expression, flat: true);
            expressions = expressions.Length != 0 ? $" BY {expressions}" : "";
            return $"{@this}{seg("OFFSET")} {sql(value)}{expressions}";
        }

        // py: sqlglot/generator.py::Generator.query_modifiers #c55f693f29
        public virtual string query_modifiers(exp.Expr expression, params string[] sqls)
        {
            var limit = expression.args.get("limit");
            if (LIMIT_FETCH == "LIMIT" && limit is exp.Fetch fetch)
            {
                var count = fetch.args.get("count");
                // "FETCH FIRST ROWS ONLY" without a count means one row per the SQL
                // standard; emitting a bare "LIMIT" here would produce invalid SQL.
                limit = new exp.Limit(expression: count is not null ? ((exp.Expr)count).copy() : exp.Literal.number(1));
            }
            else if (LIMIT_FETCH == "FETCH" && limit is exp.Limit lim && !query_modifiers__is_limit_all(lim))
                limit = new exp.Fetch(direction: "FIRST", count: lim.expression is exp.Expr count ? count.copy() : null);

            var parts = new List<string>(sqls);
            foreach (var join in expression.args.get("joins") as System.Collections.IEnumerable ?? Array.Empty<object>()) parts.Add(sql(join));
            parts.Add(sql(expression, "match"));
            foreach (var lateral in expression.args.get("laterals") as System.Collections.IEnumerable ?? Array.Empty<object>()) parts.Add(sql(lateral));
            parts.Add(sql(expression, "prewhere"));
            parts.Add(sql(expression, "where"));
            parts.Add(sql(expression, "connect"));
            parts.Add(sql(expression, "group"));
            parts.Add(sql(expression, "having"));
            foreach (var gen in AFTER_HAVING_MODIFIER_TRANSFORMS.Values) parts.Add(gen(this, expression));
            parts.Add(sql(expression, "order"));
            parts.AddRange(offset_limit_modifiers(expression, limit is exp.Fetch, limit as exp.Expr));
            parts.AddRange(after_limit_modifiers(expression));
            parts.Add(sql(expression, "for_"));
            parts.Add(options_modifier(expression));
            return helper.csv(parts, sep: "");
        }

        // py: sqlglot/generator.py::Generator.options_modifier #7fb4551aa6
        public virtual string options_modifier(exp.Expr expression)
        {
            var options = expressions(expression, key: "options");
            return options.Length != 0 ? $" {options}" : "";
        }

        // py: sqlglot/generator.py::Generator.offset_limit_modifiers #4b550c2d45
        public virtual List<string> offset_limit_modifiers(exp.Expr expression, bool fetch, exp.Expr? limit) =>
            [fetch ? sql(expression, "offset") : sql(limit), fetch ? sql(limit) : sql(expression, "offset")];

        // py: sqlglot/generator.py::Generator.after_limit_modifiers #6306b0b7eb
        public virtual List<string> after_limit_modifiers(exp.Expr expression)
        {
            var locks = expressions(expression, key: "locks", sep: " ");
            locks = locks.Length != 0 ? $" {locks}" : "";
            return [locks, sql(expression, "sample")];
        }

        // py: sqlglot/generator.py::Generator.select_sql #0000000000
        public virtual string select_sql(exp.Select expression)
        {
            var into = expression.args.get("into") as exp.Expr;
            if (!SUPPORTS_SELECT_INTO && py.truthy(into)) into!.pop();

            var hint = this.sql(expression, "hint");
            var distinct = this.sql(expression, "distinct");
            distinct = distinct.Length != 0 ? $" {distinct}" : "";
            var kind = this.sql(expression, "kind");
            var limit = expression.args.get("limit");
            string top;
            if (limit is exp.Limit lim && LIMIT_IS_TOP)
            {
                top = limit_sql(lim, top: true);
                lim.pop();
            }
            else top = "";
            var expressions = this.expressions(expression);
            if (kind.Length != 0)
            {
                if (SELECT_KINDS.Contains(kind)) kind = $" AS {kind}";
                else
                {
                    if (kind == "STRUCT")
                        expressions = this.expressions(sqls: new List<object?> { this.sql(new exp.Struct(expressions:
                            expression.expressions.Cast<exp.Expr>().Select(e => e is exp.Alias
                                ? (object)new exp.PropertyEQ(@this: e.args.get("alias"), expression: e.@this) : e).ToList())) });
                    kind = "";
                }
            }
            var operation_modifiers = this.expressions(expression, key: "operation_modifiers", sep: " ");
            operation_modifiers = operation_modifiers.Length != 0 ? $"{sep()}{operation_modifiers}" : "";
            var exclude = expression.args.get("exclude");
            if (!STAR_EXCLUDE_REQUIRES_DERIVED_TABLE && py.truthy(exclude))
            {
                var exclude_sql = this.expressions(sqls: (System.Collections.ICollection)exclude!, flat: true);
                expressions = $"{expressions}{seg("EXCLUDE")} ({exclude_sql})";
            }
            // We use LIMIT_IS_TOP as a proxy for whether DISTINCT should go first because tsql and Teradata
            // are the only dialects that use LIMIT_IS_TOP and both place DISTINCT first.
            var top_distinct = LIMIT_IS_TOP ? $"{distinct}{hint}{top}" : $"{top}{hint}{distinct}";
            expressions = expressions.Length != 0 ? $"{sep()}{expressions}" : expressions;
            var sql = query_modifiers(expression,
                $"SELECT{top_distinct}{operation_modifiers}{kind}{expressions}",
                this.sql(expression, "into", comment: false), this.sql(expression, "from_", comment: false));
            // If both the CTE and SELECT clauses have comments, generate the latter earlier
            if (py.truthy(expression.args.get("with_")))
            {
                sql = maybe_comment(sql, expression);
                expression.pop_comments();
            }
            sql = prepend_ctes(expression, sql);
            if (STAR_EXCLUDE_REQUIRES_DERIVED_TABLE && py.truthy(exclude))
            {
                expression.set("exclude", null);
                var subquery = new exp.Subquery(@this: expression);
                var star = new exp.Star(except_: exclude);
                sql = this.sql(new exp.Select(expressions: new List<object?> { star }, from_: new exp.From(@this: subquery)));
            }

            if (!SUPPORTS_SELECT_INTO && py.truthy(into))
            {
                string table_kind;
                if (py.truthy(into!.args.get("temporary"))) table_kind = " TEMPORARY";
                else if (SUPPORTS_UNLOGGED_TABLES && py.truthy(into.args.get("unlogged"))) table_kind = " UNLOGGED";
                else table_kind = "";
                sql = $"CREATE{table_kind} TABLE {this.sql(into.@this)} AS {sql}";
            }

            return sql;
        }

        // py: sqlglot/generator.py::Generator._simplify_unless_literal #0000000000
        public virtual exp.Expr? _simplify_unless_literal(exp.Expr? expression)
        {
            // optimizer.simplify is not ported. It is the identity (verified against the Python oracle) for
            // literals, leaf nodes and intervals over a literal, which are the only shapes accepted here.
            if (expression is null || expression is exp.Literal or exp.Column or exp.Identifier or exp.Var or exp.Null or exp.Boolean
                or exp.Placeholder or exp.Parameter) return expression;
            if (expression is exp.Interval interval && interval.args.get("this") is exp.Literal) return expression;
            if (expression is exp.Neg neg && neg.args.get("this") is exp.Literal) return expression;
            throw new NotPortedException("sqlglot/generator.py::Generator._simplify_unless_literal (optimizer)");
        }
    }
}
