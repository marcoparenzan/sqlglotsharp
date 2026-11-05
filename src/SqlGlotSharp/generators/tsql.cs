using System.Text;
using Gen = SqlGlotSharp.generator.Generator;

namespace SqlGlotSharp.generators;

// Logic of sqlglot/generators/tsql.py; the class-level data tables live in tsql.data.g.cs.
public static partial class tsql
{
    public partial class TSQLGenerator
    {
        private static readonly PyDict<string, string> DATE_PART_UNMAPPING = new()
        {
            ["WEEKISO"] = "ISO_WEEK", ["DAYOFWEEK"] = "WEEKDAY", ["TIMEZONE_MINUTE"] = "TZOFFSET",
        };

        private static readonly HashSet<Type> BIT_TYPES =
            [typeof(exp.EQ), typeof(exp.NEQ), typeof(exp.Is), typeof(exp.In), typeof(exp.Select), typeof(exp.Alias)];

        private static readonly string[] SET_OP_MODIFIER_ARGS = ["limit", "offset", "order", "for_", "options"];

        private static readonly HashSet<string> OPTIONS_THAT_REQUIRE_EQUAL = ["MAX_GRANT_PERCENT", "MIN_GRANT_PERCENT", "LABEL"];

        private static string AsText(object? value) => value switch
        {
            null => "None",
            exp.Expr e => e.name,
            _ => py.str(value),
        };

        // ------------------------------------------------------------------ module-level helpers

        // py: sqlglot/generators/tsql.py::_format_sql
        private static string _format_sql(Gen self, exp.Expr expression)
        {
            var fmt = (exp.Expr)expression.args["format"]!;
            string fmt_sql;

            if (expression is not exp.NumberToStr)
            {
                if (fmt.is_string)
                {
                    var mapped_fmt = time.format_time(fmt.name, dialects.tsql.TSQL.INVERSE_TIME_MAPPING_);
                    fmt_sql = self.sql(exp.Literal.@string(mapped_fmt));
                }
                else
                {
                    var formatted = self.format_time(expression);
                    fmt_sql = !string.IsNullOrEmpty(formatted) ? formatted : self.sql(fmt);
                }
            }
            else fmt_sql = self.sql(fmt);

            return self.func("FORMAT", expression.args["this"], fmt_sql, expression.args.get("culture"));
        }

        // py: sqlglot/generators/tsql.py::_string_agg_sql
        private static string _string_agg_sql(Gen self, exp.Expr expression)
        {
            var @this = expression.args.get("this");
            var distinct = postgres.PostgresGenerator.find_all_in_scope(expression, typeof(exp.Distinct)).FirstOrDefault();
            if (distinct is not null)
            {
                // exp.Distinct can appear below an exp.Order or an exp.GroupConcat expression
                self.unsupported("T-SQL STRING_AGG doesn't support DISTINCT.");
                @this = distinct.pop().expressions[0];
            }

            var order = "";
            if (expression.args.get("this") is exp.Order ordered)
            {
                if (py.truthy(ordered.args.get("this"))) @this = ((exp.Expr)ordered.args["this"]!).pop();
                // Order has a leading space
                order = $" WITHIN GROUP ({self.sql(ordered)[1..]})";
            }

            var separator = expression.args.get("separator") ?? exp.Literal.@string(",");
            return $"STRING_AGG({self.format_args(@this, separator)}){order}";
        }

        // py: sqlglot/optimizer/qualify_columns.py::qualify_outputs (root scope of a Select: no outer columns)
        private static void qualify_outputs(exp.Expr query, dialects.dialect.Dialect dialect)
        {
            if (query is not exp.Select select) return;

            var selects = ((System.Collections.IEnumerable)(select.args.get("expressions") ?? new List<object?>())).Cast<exp.Expr>().ToList();
            var new_selections = new List<object?>();

            for (var i = 0; i < selects.Count; i++)
            {
                var selection = selects[i];
                if (selection is exp.QueryTransform) break;

                if (selection is exp.Subquery)
                {
                    if (selection.output_name.Length == 0)
                    {
                        var alias_identifier = exp.to_identifier($"_col_{i}");
                        dialect.normalize_identifier(alias_identifier);
                        selection.set("alias", new exp.TableAlias(@this: alias_identifier));
                    }
                }
                else if (selection is not (exp.Alias or exp.Aliases) && !selection.is_star)
                {
                    var unwrapped = selection.unnest();
                    object? source_identifier = unwrapped is exp.Column ? unwrapped.args.get("this")
                        : unwrapped is exp.Dot ? unwrapped.args.get("expression") : null;

                    var alias_name = selection.output_name.Length != 0 ? selection.output_name : $"_col_{i}";
                    var wrapped = selection is exp.Expr && selection.arg_types.ContainsKey("alias") && selection.GetType().Name != "Window"
                        ? selection : null;
                    if (wrapped is not null) wrapped.set("alias", exp.to_identifier(alias_name));
                    else selection = new exp.Alias(@this: selection, alias: exp.to_identifier(alias_name));

                    if (source_identifier is exp.Identifier source)
                    {
                        // The alias copies the exact spelling of an existing identifier, so folding it
                        // here would desync it from other occurrences of that identifier
                        if (py.truthy(source.args.get("quoted"))) ((exp.Expr)selection.args["alias"]!).set("quoted", true);
                    }
                    else dialect.normalize_identifier((exp.Identifier)selection.args["alias"]!);
                }

                new_selections.Add(selection);
            }

            if (new_selections.Count > 0) select.set("expressions", new_selections);
        }

        /// <summary>Ensures all (unnamed) output columns are aliased for CTEs and Subqueries.</summary>
        // py: sqlglot/generators/tsql.py::qualify_derived_table_outputs
        internal static exp.Expr qualify_derived_table_outputs(exp.Expr expression)
        {
            var alias = expression.args.get("alias");

            if (expression is exp.CTE or exp.Subquery
                && alias is exp.TableAlias table_alias
                && !(table_alias.args.get("columns") is List<object?> { Count: > 0 })
                && expression.args.get("this") is exp.Expr query && query is exp.Query)
            {
                // We keep track of the unaliased column projection indexes instead of the expressions
                // themselves, because the latter are going to be replaced by new nodes when the aliases
                // are added and hence we won't be able to reach these newly added Alias parents
                var selects = dialect_helpers.selects(query);
                var unaliased_column_indexes = Enumerable.Range(0, selects.Count)
                    .Where(i => selects[i] is exp.Column column && column.alias.Length == 0).ToList();

                qualify_outputs(query, new dialects.tsql.TSQL());

                // Preserve the quoting information of columns for newly added Alias nodes
                var query_selects = dialect_helpers.selects(query);
                foreach (var select_index in unaliased_column_indexes)
                {
                    if (query_selects[select_index] is exp.Alias aliased && aliased.args.get("this") is exp.Column column
                        && column.args.get("this") is exp.Identifier identifier)
                        ((exp.Expr)aliased.args["alias"]!).set("quoted", identifier.args.get("quoted"));
                }
            }

            return expression;
        }

        // py: sqlglot/generators/tsql.py::_json_extract_sql
        private static string _json_extract_sql(Gen self, exp.Expr expression)
        {
            // JSON_QUERY returns objects and arrays, JSON_VALUE returns scalars. A scalar-only
            // extraction maps to JSON_VALUE; a source that also returns non-scalar values as
            // text (e.g. SQLite's ->>) needs to try both, like a generic JSONExtract does
            if (expression is exp.JSONExtractScalar && py.truthy(expression.args.get("scalar_only")))
                return self.func("JSON_VALUE", expression.args["this"], expression.args["expression"]);

            var json_query = self.func("JSON_QUERY", expression.args["this"], expression.args["expression"]);
            if (py.truthy(expression.args.get("json_query"))) return json_query;

            var json_value = self.func("JSON_VALUE", expression.args["this"], expression.args["expression"]);
            return self.func("ISNULL", json_query, json_value);
        }

        // py: sqlglot/generators/tsql.py::_timestrtotime_sql
        private static string _timestrtotime_sql(Gen self, exp.Expr expression)
        {
            var sql = dialect_helpers.timestrtotime_sql(self, (exp.TimeStrToTime)expression);
            if (py.truthy(expression.args.get("zone")))
                // If there is a timezone, produce an expression like:
                // CAST('2020-01-01 12:13:14-08:00' AS DATETIMEOFFSET) AT TIME ZONE 'UTC'
                // If you dont have AT TIME ZONE 'UTC', wrapping that expression in another cast back to DATETIME2 just drops the timezone information
                return self.sql(new exp.AtTimeZone(@this: sql, zone: exp.Literal.@string("UTC")));
            return sql;
        }

        private static Func<Gen, exp.Expr, string> rename(string name) => (self, e) => self.rename_func(name, e);

        private static string sha_hash(Gen self, exp.Expr e, string kind) =>
            self.func("HASHBYTES", exp.Literal.@string(kind), e.args["this"]);

        // py: sqlglot/generators/tsql.py::TSQLGenerator.TRANSFORMS
        internal static readonly IReadOnlyDictionary<Type, Func<Gen, exp.Expr, string>> TSQL_TRANSFORMS =
            new Dictionary<Type, Func<Gen, exp.Expr, string>>
            {
                [typeof(exp.AnyValue)] = (self, e) => self.func("MAX", e.args["this"]),
                [typeof(exp.Atan2)] = rename("ATN2"),
                [typeof(exp.ArrayToString)] = rename("STRING_AGG"),
                [typeof(exp.AutoIncrementColumnConstraint)] = (_, _) => "IDENTITY",
                [typeof(exp.Ceil)] = rename("CEILING"),
                [typeof(exp.Chr)] = rename("CHAR"),
                [typeof(exp.DateAdd)] = dialect_helpers.date_delta_sql("DATEADD"),
                [typeof(exp.CurrentDate)] = rename("GETDATE"),
                [typeof(exp.CurrentTimestamp)] = rename("GETDATE"),
                [typeof(exp.CurrentTimestampLTZ)] = rename("SYSDATETIMEOFFSET"),
                [typeof(exp.DateStrToDate)] = (self, e) => self.sql(self.exp_cast((exp.Expr)e.args["this"]!, exp.DType.DATE)),
                [typeof(exp.Day)] = dialect_helpers.remove_ts_or_ds_to_date(),
                [typeof(exp.Filter)] = dialect_helpers.no_filter_sql,
                [typeof(exp.GeneratedAsIdentityColumnConstraint)] = (self, e) =>
                    $"IDENTITY({(self.sql(e, "start") is { Length: > 0 } start ? start : "1")}, {(self.sql(e, "increment") is { Length: > 0 } inc ? inc : "1")})",
                [typeof(exp.GroupConcat)] = _string_agg_sql,
                [typeof(exp.If)] = rename("IIF"),
                [typeof(exp.JSONExtract)] = _json_extract_sql,
                [typeof(exp.JSONExtractScalar)] = _json_extract_sql,
                [typeof(exp.LastDay)] = (self, e) => self.func("EOMONTH", e.args["this"]),
                [typeof(exp.Ln)] = rename("LOG"),
                [typeof(exp.Max)] = (self, e) => self.rename_func(py.truthy(e.args.get("expressions")) ? "GREATEST" : "MAX", e),
                [typeof(exp.MD5)] = (self, e) => sha_hash(self, e, "MD5"),
                [typeof(exp.Min)] = (self, e) => self.rename_func(py.truthy(e.args.get("expressions")) ? "LEAST" : "MIN", e),
                [typeof(exp.Month)] = dialect_helpers.remove_ts_or_ds_to_date(),
                [typeof(exp.NumberToStr)] = _format_sql,
                [typeof(exp.Repeat)] = rename("REPLICATE"),
                [typeof(exp.CurrentSchema)] = rename("SCHEMA_NAME"),
                [typeof(exp.Stddev)] = rename("STDEV"),
                [typeof(exp.StrPosition)] = (self, e) =>
                    dialect_helpers.strposition_sql(self, (exp.StrPosition)e, func_name: "CHARINDEX", supports_position: true),
                [typeof(exp.SHA)] = (self, e) => sha_hash(self, e, "SHA1"),
                [typeof(exp.SHA1Digest)] = (self, e) => sha_hash(self, e, "SHA1"),
                [typeof(exp.SHA2)] = (self, e) => sha_hash(self, e,
                    $"SHA2_{(e.args.TryGetValue("length", out var length) ? AsText(length) : "256")}"),
                [typeof(exp.TemporaryProperty)] = (_, _) => "",
                [typeof(exp.TimeStrToTime)] = _timestrtotime_sql,
                [typeof(exp.TimeToStr)] = _format_sql,
                [typeof(exp.TimestampAdd)] = dialect_helpers.date_delta_sql("DATEADD"),
                [typeof(exp.Trim)] = (self, e) => self.dialect_trim_sql((exp.Trim)e),
                [typeof(exp.TsOrDsAdd)] = dialect_helpers.date_delta_sql("DATEADD", cast: true),
                [typeof(exp.TsOrDsDiff)] = dialect_helpers.date_delta_sql("DATEDIFF"),
                [typeof(exp.TimestampTrunc)] = (self, e) => self.func("DATETRUNC", e.args.get("unit"), e.args["this"]),
                [typeof(exp.Trunc)] = (self, e) => self.func("ROUND", e.args["this"],
                    e.args.get("decimals") ?? exp.Literal.number(0), exp.Literal.number(1)),
                [typeof(exp.Uuid)] = (_, _) => "NEWID()",
                [typeof(exp.Year)] = dialect_helpers.remove_ts_or_ds_to_date(),
                [typeof(exp.DateFromParts)] = rename("DATEFROMPARTS"),
            };

        // py: sqlglot/transforms.py::preprocess (CTE / Subquery entries)
        internal static string derived_table_preprocess_sql(Gen self, exp.Expr expression)
        {
            try { expression = qualify_derived_table_outputs(expression); }
            catch (errors.UnsupportedError error) { self.unsupported(error.Message); }

            return expression switch
            {
                exp.CTE cte => self.cte_sql(cte),
                exp.Subquery subquery => self.subquery_sql(subquery),
                _ => throw new InvalidOperationException($"Expr type {expression.GetType().Name} requires a _sql method in order to be transformed."),
            };
        }

        // py: sqlglot/transforms.py::preprocess (Select entry)
        internal static string select_preprocess_sql(Gen self, exp.Expr expression)
        {
            try
            {
                expression = dialect_helpers.eliminate_distinct_on(expression);
                expression = postgres.PostgresGenerator.eliminate_semi_and_anti_joins(expression);
                expression = postgres.PostgresGenerator.eliminate_qualify(expression);
                expression = dialect_helpers.unnest_generate_date_array_using_recursive_cte(expression);
            }
            catch (errors.UnsupportedError error)
            {
                self.unsupported(error.Message);
            }

            return expression is exp.Select select
                ? self.select_sql(select)
                : throw new InvalidOperationException($"Expr type {expression.GetType().Name} requires a _sql method in order to be transformed.");
        }

        // ------------------------------------------------------------------ methods

        // py: sqlglot/generators/tsql.py::TSQLGenerator.AFTER_HAVING_MODIFIER_TRANSFORMS (= the module-level dict: windows, qualify)
        public override PyDict<string, Func<Gen, exp.Expr, string>> AFTER_HAVING_MODIFIER_TRANSFORMS =>
            postgres.PostgresGenerator.AFTER_HAVING_MODIFIER_TRANSFORMS_PG;

        // py: sqlglot/generators/tsql.py::TSQLGenerator.scope_resolution
        public override string scope_resolution(string rhs, string scope_name) => $"{scope_name}::{rhs}";

        // py: sqlglot/generators/tsql.py::TSQLGenerator.select_sql
        public override string select_sql(exp.Select expression)
        {
            _prepare_limit_offset(expression);

            // Handles transpiling a query like the following to T-SQL:
            //   SELECT 1 AS x ORDER BY x NULLS FIRST FETCH FIRST 1 ROWS ONLY UNION ALL SELECT 2 AS x
            if (expression.parent is exp.SetOperation && expression.args.get("limit") is exp.Fetch)
                return sql(dialect_helpers.select_star_from_subquery(expression, "_l_0"));

            return base.select_sql(expression);
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.set_operations
        public override string set_operations(exp.SetOperation expression)
        {
            var limit = expression.args.get("limit");
            var offset = expression.args.get("offset");
            var order = expression.args.get("order") as exp.Order;

            // Set operations cannot order by the CASE used to emulate null ordering,
            // or by expressions that aren't in their select list.
            var wrap_order = false;
            if (order is not null)
            {
                var selects = dialect_helpers.selects(expression).Select(select => select.unalias().unnest()).ToList();
                foreach (var ordered in ((System.Collections.IEnumerable)order.args["expressions"]!).Cast<exp.Expr>())
                {
                    var @this = ((exp.Expr)ordered.args["this"]!).unnest();
                    if (@this.is_int) continue;

                    var desc = py.truthy(ordered.args.get("desc"));
                    var nulls_first = py.truthy(ordered.args.get("nulls_first"));

                    var emulate_null_ordering = (desc && nulls_first) || (!desc && !nulls_first);
                    if (emulate_null_ordering || (@this is not exp.Column && !selects.Any(s => s.Equals(@this))))
                    {
                        wrap_order = true;
                        break;
                    }
                }
            }

            if (wrap_order
                || (limit is exp.Limit && !py.truthy(offset))
                || (order is null && (py.truthy(offset) || limit is exp.Fetch)))
            {
                var select = (exp.Select)_move_ctes_to_top_level(dialect_helpers.select_star_from_subquery(expression, "_l_0"));
                foreach (var arg in SET_OP_MODIFIER_ARGS)
                {
                    var value = expression.args.get(arg);
                    if (py.truthy(value))
                    {
                        expression.set(arg, null);
                        select.set(arg, value);
                    }
                }

                return sql(select);
            }

            _prepare_limit_offset(expression);
            return base.set_operations(expression);
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator._prepare_limit_offset
        private void _prepare_limit_offset(exp.Expr expression)
        {
            var limit = expression.args.get("limit");
            var offset = expression.args.get("offset");

            if (limit is exp.Fetch && !py.truthy(offset))
            {
                // Dialects like Oracle can FETCH directly from a row set but
                // T-SQL requires an ORDER BY + OFFSET clause in order to FETCH
                offset = new exp.Offset(expression: exp.Literal.number(0));
                expression.set("offset", offset);
            }

            if (py.truthy(offset))
            {
                if (!py.truthy(expression.args.get("order")))
                {
                    // ORDER BY is required in order to use OFFSET in a query, so we use
                    // a noop order by, since we don't really care about the order.
                    // See: https://www.microsoftpressstore.com/articles/article.aspx?p=2314819
                    var noop = new exp.Subquery(@this: new exp.Select(expressions: new List<object?> { new exp.Null() }));
                    expression.set("order", new exp.Order(expressions: new List<object?> { noop }));
                }

                if (limit is exp.Limit lim && !dialect_helpers.is_limit_all(lim))
                    // TOP and OFFSET can't be combined, we need use FETCH instead of TOP
                    // we replace here because otherwise TOP would be generated in select_sql
                    lim.replace(new exp.Fetch(direction: "FIRST", count: lim.args.get("expression")));
            }
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.convert_sql
        public string convert_sql(exp.Convert expression)
        {
            var name = py.truthy(expression.args.get("safe")) ? "TRY_CONVERT" : "CONVERT";
            return func(name, expression.args["this"], expression.args["expression"], expression.args.get("style"));
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.queryoption_sql
        public override string queryoption_sql(exp.QueryOption expression)
        {
            var option = sql(expression, "this");
            var value = sql(expression, "expression");
            if (value.Length != 0)
            {
                var optional_equal_sign = OPTIONS_THAT_REQUIRE_EQUAL.Contains(option) ? "= " : "";
                return $"{option} {optional_equal_sign}{value}";
            }
            return option;
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.lateral_op
        public override string lateral_op(exp.Lateral expression)
        {
            var cross_apply = expression.args.get("cross_apply");
            if (cross_apply is true) return "CROSS APPLY";
            if (cross_apply is false) return "OUTER APPLY";

            // TODO: perhaps we can check if the parent is a Join and transpile it appropriately
            unsupported("LATERAL clause is not supported.");
            return "LATERAL";
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.splitpart_sql
        public string splitpart_sql(exp.SplitPart expression)
        {
            var @this = (exp.Expr)expression.args["this"]!;
            var split_count = @this.name.Split('.').Length;
            var delimiter = expression.args.get("delimiter") as exp.Expr;
            var part_index = expression.args.get("part_index") as exp.Expr;

            if (!(@this is exp.Literal && delimiter is exp.Literal && part_index is exp.Literal)
                || (delimiter is not null && delimiter.name != ".")
                || part_index is null
                || split_count > 4)
            {
                unsupported("SPLIT_PART can be transpiled to PARSENAME only for '.' delimiter and literal values");
                return "";
            }

            return func("PARSENAME", @this, exp.Literal.number(split_count + 1 - Convert.ToInt64(part_index.to_py())));
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.extract_sql
        public override string extract_sql(exp.Extract expression)
        {
            var part = (exp.Expr)expression.args["this"]!;
            object name = DATE_PART_UNMAPPING.get(py.upper(part.name)) ?? (object)part;

            return func("DATEPART", name, expression.args["expression"]);
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.timefromparts_sql
        public string timefromparts_sql(exp.TimeFromParts expression)
        {
            var nano = expression.args.get("nano") as exp.Expr;
            if (nano is not null)
            {
                nano.pop();
                unsupported("Specifying nanoseconds is not supported in TIMEFROMPARTS.");
            }

            if (expression.args.get("fractions") is null) expression.set("fractions", exp.Literal.number(0));
            if (expression.args.get("precision") is null) expression.set("precision", exp.Literal.number(0));

            return rename_func("TIMEFROMPARTS", expression);
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.timestampfromparts_sql
        public string timestampfromparts_sql(exp.TimestampFromParts expression)
        {
            var zone = expression.args.get("zone") as exp.Expr;
            if (zone is not null)
            {
                zone.pop();
                unsupported("Time zone is not supported in DATETIMEFROMPARTS.");
            }

            var nano = expression.args.get("nano") as exp.Expr;
            if (nano is not null)
            {
                nano.pop();
                unsupported("Specifying nanoseconds is not supported in DATETIMEFROMPARTS.");
            }

            if (expression.args.get("milli") is null) expression.set("milli", exp.Literal.number(0));

            return rename_func("DATETIMEFROMPARTS", expression);
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.setitem_sql
        public override string setitem_sql(exp.SetItem expression)
        {
            if (expression.args.get("this") is exp.EQ eq && eq.args["this"] is not exp.Parameter)
                // T-SQL does not use '=' in SET command, except when the LHS is a variable.
                return $"{sql(eq.args["this"])} {sql(eq.args["expression"])}";

            return base.setitem_sql(expression);
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.boolean_sql
        public override string boolean_sql(exp.Boolean expression)
        {
            if ((expression.parent is not null && BIT_TYPES.Contains(expression.parent.GetType()))
                || expression.find_ancestor(typeof(exp.Values), typeof(exp.Select)) is exp.Values)
                return py.truthy(expression.args.get("this")) ? "1" : "0";

            return py.truthy(expression.args.get("this")) ? "(1 = 1)" : "(1 = 0)";
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.is_sql
        public override string is_sql(exp.Is expression)
        {
            var negate = py.truthy(expression.args.get("negate"));
            if (expression.args["expression"] is exp.Boolean)
                return binary(expression, negate ? "<>" : "=");
            return binary(expression, negate ? "IS NOT" : "IS");
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.createable_sql
        public override string createable_sql(exp.Create expression, Dictionary<exp.PropertiesLocation, List<object?>> locations)
        {
            var sql_ = sql(expression, "this");
            var properties = expression.args.get("properties") as exp.Expr;

            var start = _identifier_start;
            if (!sql_.StartsWith('#')
                && !sql_.StartsWith($"{start}#", StringComparison.Ordinal)
                && properties is not null
                && ((System.Collections.IEnumerable)(properties.args.get("expressions") ?? new List<object?>())).Cast<object?>()
                    .Any(prop => prop is exp.TemporaryProperty))
                sql_ = sql_.StartsWith(start, StringComparison.Ordinal) ? $"{start}#{sql_[start.Length..]}" : $"#{sql_}";

            return sql_;
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.create_sql
        public override string create_sql(exp.Create expression)
        {
            var kind = py.str(expression.args.get("kind") ?? "").ToUpperInvariant();
            var exists = expression.args.get("exists");
            expression.set("exists", null);

            var like_property = expression.find(typeof(exp.LikeProperty));
            var ctas_expression = like_property is not null ? like_property.args.get("this") : expression.args.get("expression");

            if (kind == "VIEW")
            {
                ((exp.Expr)expression.args["this"]!).set("catalog", null);
                var with_ = expression.args.get("with_") as exp.Expr;
                if (ctas_expression is not null && with_ is not null)
                    // We've already preprocessed the Create expression to bubble up any nested CTEs,
                    // but CREATE VIEW actually requires the WITH clause to come after it so we need
                    // to amend the AST by moving the CTEs to the CREATE VIEW statement's query.
                    ((exp.Expr)ctas_expression).set("with_", with_.pop());
            }
            else if (kind == "FUNCTION"
                && ctas_expression is exp.Return ret
                && ((exp.Expr)ret.args["this"]!).unnest() is exp.Query body
                && expression.args.get("with_") is exp.Expr with_fn)
            {
                // Similar to the VIEW branch, the table-valued functions require the WITH clause
                // to stay inside the RETURN body, so we move back any CTEs that were bubbled up.
                ((exp.Expr)body).set("with_", with_fn.pop());
            }

            var table = expression.find(typeof(exp.Table)) as exp.Table;

            string sql_;
            // Convert CTAS statement to SELECT .. INTO ..
            if (kind == "TABLE" && ctas_expression is not null)
            {
                var ctas = (exp.Expr)ctas_expression;
                if (ctas is exp.Select or exp.SetOperation) ctas = dialect_helpers.subquery(ctas, null);

                var properties = expression.args.get("properties") as exp.Expr ?? new exp.Properties();
                var is_temp = ((System.Collections.IEnumerable)(properties.args.get("expressions") ?? new List<object?>()))
                    .Cast<object?>().Any(p => p is exp.TemporaryProperty);

                var select_into = new exp.Select(expressions: new List<object?> { new exp.Star() },
                    from_: new exp.From(@this: dialect_helpers.table_alias_of(ctas, "temp")));
                select_into.set("into", new exp.Into(@this: table, temporary: is_temp));

                if (like_property is not null)
                    select_into.set("limit", new exp.Limit(expression: exp.Literal.number(0)));

                sql_ = sql(select_into);
            }
            else sql_ = base.create_sql(expression);

            if (py.truthy(exists))
            {
                var identifier = sql(exp.Literal.@string(table is not null ? dialect_helpers.table_name(table) : ""));
                var sql_with_ctes = prepend_ctes(expression, sql_);
                var sql_literal = sql(exp.Literal.@string(sql_with_ctes));
                if (kind == "SCHEMA")
                    return $"IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = {identifier}) EXEC({sql_literal})";
                if (kind == "TABLE")
                {
                    var where = dialect_helpers.and_(
                        dialect_helpers.column_eq("TABLE_NAME", table!.name),
                        table.args.get("db") is exp.Expr db ? dialect_helpers.column_eq("TABLE_SCHEMA", table.text("db")) : null,
                        table.args.get("catalog") is exp.Expr catalog ? dialect_helpers.column_eq("TABLE_CATALOG", table.text("catalog")) : null);
                    return $"IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE {sql(where)}) EXEC({sql_literal})";
                }
                if (kind == "INDEX")
                {
                    var index = sql(exp.Literal.@string(((exp.Expr)expression.args["this"]!).text("this")));
                    return $"IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id({identifier}) AND name = {index}) EXEC({sql_literal})";
                }
            }
            else if (py.truthy(expression.args.get("replace")))
            {
                var at = sql_.IndexOf("CREATE OR REPLACE ", StringComparison.Ordinal);
                if (at >= 0) sql_ = sql_[..at] + "CREATE OR ALTER " + sql_[(at + "CREATE OR REPLACE ".Length)..];
            }

            return prepend_ctes(expression, sql_);
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.into_sql
        public override string into_sql(exp.Into expression)
        {
            foreach (var key in new[] { "unlogged", "expressions" })
                if (py.truthy(expression.args.get(key)))
                    unsupported($"Argument '{key}' is not supported for expression 'Into' when targeting {dialect.GetType().Name}.");

            if (py.truthy(expression.args.get("temporary")))
            {
                // If the Into expression has a temporary property, push this down to the Identifier
                if (expression.find(typeof(exp.Table)) is exp.Table table && table.args.get("this") is exp.Identifier id)
                    id.set("temporary", true);
            }

            return $"{seg("INTO")} {sql(expression, "this")}";
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.count_sql
        public string count_sql(exp.Count expression) =>
            rename_func(py.truthy(expression.args.get("big_int")) ? "COUNT_BIG" : "COUNT", expression);

        // py: sqlglot/generators/tsql.py::TSQLGenerator.countif_sql
        public string countif_sql(exp.CountIf expression) =>
            // T-SQL has no COUNT_IF, so count the rows where the predicate holds instead
            func("COUNT", new exp.If(@this: expression.args["this"], @true: exp.Literal.number(1), @false: new exp.Null()));

        // py: sqlglot/generators/tsql.py::TSQLGenerator.datediff_sql
        public string datediff_sql(exp.DateDiff expression) =>
            dialect_helpers.date_delta_sql(py.truthy(expression.args.get("big_int")) ? "DATEDIFF_BIG" : "DATEDIFF")(this, expression);

        // py: sqlglot/generators/tsql.py::TSQLGenerator.offset_sql
        public override string offset_sql(exp.Offset expression) => $"{base.offset_sql(expression)} ROWS";

        // py: sqlglot/generators/tsql.py::TSQLGenerator.version_sql
        public override string version_sql(exp.Version expression)
        {
            var name = expression.name == "TIMESTAMP" ? "SYSTEM_TIME" : expression.name;
            var @this = $"FOR {name}";
            var expr = expression.args.get("expression") as exp.Expr;
            var kind = expression.text("kind");
            string expr_sql;
            if (kind is "FROM" or "BETWEEN")
            {
                var args = expr!.expressions;
                var sep_ = kind == "FROM" ? "TO" : "AND";
                expr_sql = $"{sql(args.Count > 0 ? args[0] : null)} {sep_} {sql(args.Count > 1 ? args[1] : null)}";
            }
            else expr_sql = sql(expr);

            expr_sql = expr_sql.Length != 0 ? $" {expr_sql}" : "";
            return $"{@this} {kind}{expr_sql}";
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.returnsproperty_sql
        public string returnsproperty_sql(exp.ReturnsProperty expression)
        {
            var table = expression.args.get("table");
            var table_sql = table is not null ? $"{sql(table)} " : "";
            return $"RETURNS {table_sql}{sql(expression, "this")}";
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.returning_sql
        public override string returning_sql(exp.Returning expression)
        {
            var into = sql(expression, "into");
            into = into.Length != 0 ? seg($"INTO {into}") : "";
            return $"{seg("OUTPUT")} {expressions(expression, flat: true)}{into}";
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.transaction_sql
        public override string transaction_sql(exp.Transaction expression)
        {
            var @this = sql(expression, "this");
            @this = @this.Length != 0 ? $" {@this}" : "";
            var mark = sql(expression, "mark");
            mark = mark.Length != 0 ? $" WITH MARK {mark}" : "";
            return $"BEGIN TRANSACTION{@this}{mark}";
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.commit_sql
        public override string commit_sql(exp.Commit expression)
        {
            var @this = sql(expression, "this");
            @this = @this.Length != 0 ? $" {@this}" : "";
            var durability = expression.args.get("durability");
            var durability_sql = durability is not null
                ? $" WITH (DELAYED_DURABILITY = {(py.truthy(durability) ? "ON" : "OFF")})"
                : "";
            return $"COMMIT TRANSACTION{@this}{durability_sql}";
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.rollback_sql
        public override string rollback_sql(exp.Rollback expression)
        {
            var @this = sql(expression, "this");
            @this = @this.Length != 0 ? $" {@this}" : "";
            return $"ROLLBACK TRANSACTION{@this}";
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.identifier_sql
        public override string identifier_sql(exp.Identifier expression)
        {
            var identifier = base.identifier_sql(expression);

            string prefix;
            if (py.truthy(expression.args.get("global_"))) prefix = "##";
            else if (py.truthy(expression.args.get("temporary"))) prefix = "#";
            else return identifier;

            var start = _identifier_start;
            if (py.truthy(expression.args.get("quoted")) && identifier.StartsWith(start, StringComparison.Ordinal))
                return $"{start}{prefix}{identifier[start.Length..]}";

            return $"{prefix}{identifier}";
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.constraint_sql
        public override string constraint_sql(exp.Constraint expression)
        {
            var @this = sql(expression, "this");
            var expressions_ = expressions(expression, flat: true, sep: " ");
            return $"CONSTRAINT {@this} {expressions_}";
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.length_sql
        public string length_sql(exp.Length expression) => _uncast_text(expression, "LEN");

        // py: sqlglot/generators/tsql.py::TSQLGenerator.right_sql
        public string right_sql(exp.Right expression) => _uncast_text(expression, "RIGHT");

        // py: sqlglot/generators/tsql.py::TSQLGenerator.left_sql
        public string left_sql(exp.Left expression) => _uncast_text(expression, "LEFT");

        // py: sqlglot/generators/tsql.py::TSQLGenerator._uncast_text
        private string _uncast_text(exp.Expr expression, string name)
        {
            var @this = expression.args.get("this");
            string this_sql = @this is exp.Cast cast && cast.is_type(exp.DType.TEXT) ? sql(cast, "this") : sql(@this);
            var expression_sql = sql(expression, "expression");
            return func(name, this_sql, expression_sql.Length != 0 ? expression_sql : null);
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.partition_sql
        public override string partition_sql(exp.Partition expression) =>
            $"WITH (PARTITIONS({expressions(expression, flat: true)}))";

        // py: sqlglot/generators/tsql.py::TSQLGenerator.alter_sql
        public override string alter_sql(exp.Alter expression)
        {
            var actions = expression.args.get("actions") as List<object?> ?? new List<object?>();
            if (actions.Count == 1 && actions[0] is exp.AlterRename rename)
            {
                var old_name = sql(exp.Literal.@string(sql(expression.args["this"])));
                var new_name = sql(exp.Literal.@string(((exp.Expr)rename.args["this"]!).name));
                return $"EXEC sp_rename {old_name}, {new_name}";
            }
            return base.alter_sql(expression);
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.drop_sql
        public override string drop_sql(exp.Drop expression)
        {
            if (py.str(expression.args["kind"]) == "VIEW")
                foreach (var table in ((System.Collections.IEnumerable)(expression.args.get("tables") ?? new List<object?>())).Cast<exp.Expr>())
                    table.set("catalog", null);
            return base.drop_sql(expression);
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.options_modifier
        public override string options_modifier(exp.Expr expression)
        {
            var options = expressions(expression, key: "options");
            return options.Length != 0 ? $" OPTION{wrap(options)}" : "";
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.dpipe_sql
        public override string dpipe_sql(exp.DPipe expression)
        {
            exp.Expr? result = null;
            foreach (var part in expression.flatten())
                result = result is null ? part : new exp.Add(@this: result, expression: part);
            return sql(result);
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.isascii_sql
        public string isascii_sql(exp.IsAscii expression) =>
            $"(PATINDEX(CONVERT(VARCHAR(MAX), 0x255b5e002d7f5d25) COLLATE Latin1_General_BIN, {sql(expression.args["this"])}) = 0)";

        // py: sqlglot/generators/tsql.py::TSQLGenerator.columndef_sql
        public override string columndef_sql(exp.ColumnDef expression, string sep = " ")
        {
            var @this = base.columndef_sql(expression, sep);
            var @default = sql(expression, "default");
            @default = @default.Length != 0 ? $" = {@default}" : "";
            var output = sql(expression, "output");
            output = output.Length != 0 ? $" {output}" : "";
            return $"{@this}{@default}{output}";
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.coalesce_sql
        public string coalesce_sql(exp.Coalesce expression) =>
            rename_func(py.truthy(expression.args.get("is_null")) ? "ISNULL" : "COALESCE", expression);

        // py: sqlglot/generators/tsql.py::TSQLGenerator.storedprocedure_sql
        public string storedprocedure_sql(exp.StoredProcedure expression)
        {
            var @this = sql(expression, "this");
            var expressions_ = expressions(expression);
            expressions_ = py.truthy(expression.args.get("wrapped")) ? wrap(expressions_) : $" {expressions_}";
            return expressions_.Trim().Length != 0 ? $"{@this}{expressions_}" : @this;
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.ifblock_sql
        public string ifblock_sql(exp.IfBlock expression)
        {
            var @this = sql(expression, "this");
            var @true = sql(expression, "true");
            @true = @true.Length != 0 ? $" {@true}" : " ";
            var @false = sql(expression, "false");
            @false = @false.Length != 0 ? $"; ELSE BEGIN {@false}" : "";
            return $"IF {@this} BEGIN{@true}{@false}";
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.whileblock_sql
        public string whileblock_sql(exp.WhileBlock expression)
        {
            var @this = sql(expression, "this");
            var body = sql(expression, "body");
            body = body.Length != 0 ? $" {body}" : " ";
            return $"WHILE {@this} BEGIN{body}";
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.execute_sql
        public string execute_sql(exp.Execute expression)
        {
            var @this = sql(expression, "this");
            var expressions_ = expressions(expression);
            expressions_ = expressions_.Length != 0 ? $" {expressions_}" : "";
            var return_status = sql(expression, "return_status");
            return_status = return_status.Length != 0 ? $"{return_status} = " : "";
            return $"EXECUTE {return_status}{@this}{expressions_}";
        }

        // py: sqlglot/generators/tsql.py::TSQLGenerator.executesql_sql
        public string executesql_sql(exp.ExecuteSql expression) => execute_sql(expression);
    }
}
