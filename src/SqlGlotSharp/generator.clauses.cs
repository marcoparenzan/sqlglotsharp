namespace SqlGlotSharp;

// Bounded query clause dependencies from sqlglot/generator.py, in upstream order.
// Child handlers and generic preprocessing retain their explicit NotPorted boundary.
public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.tablealias_sql #533e4a9533
        public virtual string tablealias_sql(exp.TableAlias expression)
        {
            var alias = sql(expression, "this");
            var columns = expressions(expression, key: "columns", flat: true);
            columns = columns.Length != 0 ? $"({columns})" : "";

            if (columns.Length != 0 && !SUPPORTS_TABLE_ALIAS_COLUMNS &&
                !(SUPPORTS_NAMED_CTE_COLUMNS && expression.parent is exp.CTE))
            {
                columns = "";
                unsupported("Named columns are not supported in table alias.");
            }

            if (alias.Length == 0 && !dialect.UNNEST_COLUMN_ONLY) alias = _next_name();
            return $"{alias}{columns}";
        }

        // py: sqlglot/generator.py::Generator.table_parts #c18468812a
        public virtual string table_parts(exp.Table expression) => string.Join(".", new[]
        {
            expression.args.get("catalog"),
            expression.args.get("db"),
            expression.args.get("this"),
        }.Where(part => part is not null).Select(part => sql(part)));

        // py: sqlglot/generator.py::Generator.table_sql #8cbbcbfd55
        public virtual string table_sql(exp.Table expression, string sep = " AS ")
        {
            var table = table_parts(expression);
            var only = py.truthy(expression.args.get("only")) ? "ONLY " : "";
            var partition = sql(expression, "partition");
            partition = partition.Length != 0 ? $" {partition}" : "";
            var version = sql(expression, "version");
            version = version.Length != 0 ? $" {version}" : "";
            var alias = sql(expression, "alias");
            alias = alias.Length != 0 ? $"{sep}{alias}" : "";

            var sample = sql(expression, "sample");
            var post_alias = "";
            var pre_alias = "";
            if (dialect.ALIAS_POST_TABLESAMPLE) pre_alias = sample;
            else post_alias = sample;
            if (dialect.ALIAS_POST_VERSION) pre_alias = $"{pre_alias}{version}";
            else post_alias = $"{post_alias}{version}";

            var hints = expressions(expression, key: "hints", sep: " ");
            hints = hints.Length != 0 && TABLE_HINTS ? $" {hints}" : "";
            var pivots = expressions(expression, key: "pivots", sep: "", flat: true);
            var joins = indent(expressions(expression, key: "joins", sep: "", flat: true), skip_first: true);
            var laterals = expressions(expression, key: "laterals", sep: "");

            var file_format = sql(expression, "format");
            var pattern = sql(expression, "pattern");
            if (file_format.Length != 0)
            {
                pattern = pattern.Length != 0 ? $", PATTERN => {pattern}" : "";
                file_format = $" (FILE_FORMAT => {file_format}{pattern})";
            }
            else if (pattern.Length != 0) file_format = $" (PATTERN => {pattern})";

            var ordinality = "";
            if (py.truthy(expression.args.get("ordinality")))
            {
                ordinality = $" WITH ORDINALITY{alias}";
                alias = "";
            }

            var when = sql(expression, "when");
            if (when.Length != 0)
            {
                if (HISTORICAL_DATA_POST_ALIAS) alias = $"{alias} {when}";
                else table = $"{table} {when}";
            }
            var changes = sql(expression, "changes");
            changes = changes.Length != 0 ? $" {changes}" : "";
            var rows_from = expressions(expression, key: "rows_from");
            if (rows_from.Length != 0) table = $"ROWS FROM {wrap(rows_from)}";

            var indexed = expression.args.get("indexed");
            var indexed_sql = indexed is not null
                ? py.truthy(indexed) ? $" INDEXED BY {sql(indexed)}" : " NOT INDEXED"
                : "";
            return $"{only}{table}{changes}{partition}{file_format}{pre_alias}{alias}{indexed_sql}{hints}{pivots}{post_alias}{joins}{laterals}{ordinality}";
        }

        // py: sqlglot/generator.py::Generator.from_sql #3484fa60c9
        public virtual string from_sql(exp.From expression) => $"{seg("FROM")} {sql(expression, "this")}";

        // py: sqlglot/generator.py::Generator.having_sql #fba3d10195
        public virtual string having_sql(exp.Having expression)
        {
            var @this = indent(sql(expression, "this"));
            return $"{seg("HAVING")}{sep()}{@this}";
        }

        // py: sqlglot/generator.py::Generator.order_sql #6090c06207
        public virtual string order_sql(exp.Order expression, bool flat = false)
        {
            var @this = sql(expression, "this");
            @this = @this.Length != 0 ? $"{@this} " : @this;
            var siblings = py.truthy(expression.args.get("siblings")) ? "SIBLINGS " : "";
            return op_expressions($"{@this}ORDER {siblings}BY", expression, flat: @this.Length != 0 || flat);
        }

        // py: sqlglot/generator.py::Generator._resolve_ordered_for_null_ordering_simulation
        /// <summary>
        /// Resolve a bare ORDER BY name against the enclosing SELECT projection. Returns the underlying
        /// expression of the uniquely-matching projection (Alias-stripped) for substitution into the
        /// NULLS FIRST/LAST CASE simulation, or null if no safe substitution applies.
        /// </summary>
        private exp.Expr? _resolve_ordered_for_null_ordering_simulation(exp.Ordered expression)
        {
            if (expression.args.get("this") is not exp.Column column || column.table.Length != 0) return null;

            // Only a query's own ORDER BY can reference its projection aliases
            var order = expression.parent;
            var ancestor = order is exp.Order ? order.parent : null;
            if (ancestor is not exp.Select select) return null;

            var column_name = column.name;
            var matched = generators.dialect_helpers.selects(select)
                .Where(p => p.output_name == column_name)
                .Select(p => p is exp.Alias ? (exp.Expr)p.args["this"]! : p).ToList();
            var match = matched.Count == 1 ? matched[0] : null;

            // Skip the substitution when it would be identical to the existing
            // reference (e.g. SELECT col FROM t ORDER BY col).
            if (match is exp.Column mc && mc.table.Length == 0 && mc.name == column_name) return null;

            return match;
        }

        // py: sqlglot/generator.py::Generator.ordered_sql #0000000000
        public virtual string ordered_sql(exp.Ordered expression)
        {
            var desc = expression.args.get("desc");
            var asc = !py.truthy(desc);
            var nulls_first = py.truthy(expression.args.get("nulls_first"));
            var nulls_last = !nulls_first;
            var nulls_are_large = dialect.NULL_ORDERING == "nulls_are_large";
            var nulls_are_small = dialect.NULL_ORDERING == "nulls_are_small";
            var nulls_are_last = dialect.NULL_ORDERING == "nulls_are_last";
            var @this = sql(expression, "this");
            var sort_order = py.truthy(desc) ? " DESC" : desc is false ? " ASC" : "";
            var nulls_sort_change = "";
            if (nulls_first && ((asc && nulls_are_large) || (py.truthy(desc) && nulls_are_small) || nulls_are_last))
                nulls_sort_change = " NULLS FIRST";
            else if (nulls_last && ((asc && nulls_are_small) || (py.truthy(desc) && nulls_are_large)) && !nulls_are_last)
                nulls_sort_change = " NULLS LAST";

            // If the NULLS FIRST/LAST clause is unsupported, we add another sort key to simulate it
            if (nulls_sort_change.Length != 0 && NULL_ORDERING_SUPPORTED != true)
            {
                var window = expression.find_ancestor(typeof(exp.Window), typeof(exp.Select));

                exp.Expr? window_this = null;
                exp.Expr? spec = null;
                if (window is exp.Window)
                {
                    window_this = window.args.get("this") as exp.Expr;
                    if (window_this is exp.IgnoreNulls or exp.RespectNulls) window_this = window_this.args.get("this") as exp.Expr;
                    spec = window.args.get("spec") as exp.Expr;
                }

                // Some window functions (e.g. LAST_VALUE, RANK) support NULLS FIRST/LAST
                // without a spec or with a ROWS spec, but not with RANGE
                if (!(window_this is not null && WINDOW_FUNCS_WITH_NULL_ORDERING.Cast<Type>().Any(t => t.IsInstanceOfType(window_this))
                    && (spec is null || py.upper(spec.text("kind")) == "ROWS")))
                {
                    if (window_this is not null && spec is not null)
                    {
                        unsupported($"'{nulls_sort_change.Trim()}' translation not supported in window function {window_this.sql_names().First()}");
                        nulls_sort_change = "";
                    }
                    else if (NULL_ORDERING_SUPPORTED == false
                        && ((asc && nulls_sort_change == " NULLS LAST") || (py.truthy(desc) && nulls_sort_change == " NULLS FIRST")))
                    {
                        // BigQuery does not allow these ordering/nulls combinations when used under
                        // an aggregation func or under a window containing one
                        var ancestor = expression.find_ancestor(typeof(exp.AggFunc), typeof(exp.Window), typeof(exp.Select));

                        if (ancestor is exp.Window) ancestor = ancestor.args.get("this") as exp.Expr;
                        if (ancestor is exp.AggFunc)
                        {
                            unsupported($"'{nulls_sort_change.Trim()}' translation not supported for aggregate function {ancestor.sql_names().First()} with {sort_order} sort order");
                            nulls_sort_change = "";
                        }
                    }
                    else if (NULL_ORDERING_SUPPORTED is null)
                    {
                        var this_expr = (exp.Expr)expression.args["this"]!;
                        if (this_expr.is_int)
                            unsupported($"'{nulls_sort_change.Trim()}' translation not supported with positional ordering");
                        else if (this_expr is not exp.Rand)
                        {
                            var resolved = _resolve_ordered_for_null_ordering_simulation(expression);
                            var target = resolved is not null ? sql(resolved) : @this;
                            var null_sort_order = nulls_sort_change == " NULLS FIRST" ? " DESC" : "";
                            @this = $"CASE WHEN {target} IS NULL THEN 1 ELSE 0 END{null_sort_order}, {target}";
                        }
                        nulls_sort_change = "";
                    }
                }
            }

            var with_fill = sql(expression, "with_fill");
            with_fill = with_fill.Length != 0 ? $" {with_fill}" : "";
            return $"{@this}{sort_order}{nulls_sort_change}{with_fill}";
        }

        // py: sqlglot/generator.py::Generator.qualify_sql #09c9d4f5d8
        public virtual string qualify_sql(exp.Qualify expression)
        {
            var @this = indent(sql(expression, "this"));
            return $"{seg("QUALIFY")}{sep()}{@this}";
        }

        // py: sqlglot/generator.py::Generator.where_sql #ad2386b049
        public virtual string where_sql(exp.Where expression)
        {
            var @this = indent(sql(expression, "this"));
            return $"{seg("WHERE")}{sep()}{@this}";
        }

        // py: sqlglot/generator.py::Generator.expressions #3b1dc1cbab
        public virtual string expressions(exp.Expr? expression = null, string? key = null,
            System.Collections.ICollection? sqls = null, bool flat = false, bool indent = true,
            bool skip_first = false, bool skip_last = false, string sep = ", ", string prefix = "",
            bool dynamic = false, bool new_line = false)
        {
            var expressions = expression is not null ? expression.args.get(py.truthy(key) ? key! : "expressions") : sqls;
            if (!py.truthy(expressions)) return "";
            var items = ((System.Collections.IEnumerable)expressions!).Cast<object?>().ToList();
            if (flat) return string.Join(sep, items.Select(e => sql(e)).Where(text => text.Length != 0));
            var num_sqls = items.Count;
            var result_sqls = new List<string>();
            for (var i = 0; i < num_sqls; i++)
            {
                var e = items[i];
                var sql = this.sql(e, comment: false);
                if (sql.Length == 0) continue;
                var comments = e is exp.Expr node ? maybe_comment("", node) : "";
                if (py.truthy(pretty))
                {
                    if (leading_comma) result_sqls.Add($"{(i > 0 ? sep : "")}{prefix}{sql}{comments}");
                    else result_sqls.Add($"{prefix}{sql}{(i + 1 < num_sqls ? comments.Length != 0 ? expressions__rstrip(sep) : sep : "")}{comments}");
                }
                else result_sqls.Add($"{prefix}{sql}{comments}{(i + 1 < num_sqls ? sep : "")}");
            }
            string result_sql;
            if (py.truthy(pretty) && (!dynamic || too_wide(result_sqls)))
            {
                if (new_line) { result_sqls.Insert(0, ""); result_sqls.Add(""); }
                result_sql = string.Join("\n", result_sqls.Select(expressions__rstrip));
            }
            else result_sql = string.Join("", result_sqls);
            return indent ? this.indent(result_sql, skip_first: skip_first, skip_last: skip_last) : result_sql;
        }

        // Python str.rstrip includes the four ASCII information separators as well as Unicode whitespace.
        private static string expressions__rstrip(string text)
        {
            var end = text.Length;
            while (end > 0 && (char.IsWhiteSpace(text[end - 1]) || text[end - 1] is >= '\u001c' and <= '\u001f')) end--;
            return text[..end];
        }

        // py: sqlglot/generator.py::Generator.op_expressions #feaf83b5c6
        public virtual string op_expressions(string op, exp.Expr expression, bool flat = false)
        {
            flat = flat || expression.parent is exp.Properties;
            var expressions_sql = expressions(expression, flat: flat);
            if (flat) return $"{op} {expressions_sql}";
            return $"{seg(op)}{(expressions_sql.Length != 0 ? sep() : "")}{expressions_sql}";
        }
    }
}
