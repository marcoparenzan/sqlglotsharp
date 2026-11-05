namespace SqlGlotSharp;

// ARRAY_AGG null filter, ROW FORMAT, DISTRIBUTE BY, RESPECT/IGNORE NULLS (generator.py).
public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.arrayagg_sql #0000000000
        public virtual string arrayagg_sql(exp.ArrayAgg expression)
        {
            var array_agg = function_fallback_sql(expression);
            var column_expr = expression.args.get("this") as exp.Expr;
            if (column_expr is exp.Order) column_expr = (exp.Expr?)column_expr.args.get("this");

            return _add_arrayagg_null_filter(array_agg, expression, column_expr!);
        }

        // py: sqlglot/generator.py::Generator._add_arrayagg_null_filter #0000000000
        public virtual string _add_arrayagg_null_filter(string array_agg_sql, exp.Expr array_agg_expr, exp.Expr column_expr)
        {
            // Add a NULL FILTER on the column to mimic the results going from a dialect that excludes nulls
            // on ARRAY_AGG (e.g Spark) to one that does not (e.g. DuckDB)
            if (!(dialect.ARRAY_AGG_INCLUDES_NULLS && py.truthy(array_agg_expr.args.get("nulls_excluded"))))
                return array_agg_sql;

            var parent = array_agg_expr.parent;
            if (parent is exp.Filter)
            {
                var parent_cond = (exp.Expr)((exp.Expr)parent.args["expression"]!).args["this"]!;
                var not_null = new exp.Not(@this: new exp.Is(@this: column_expr, expression: new exp.Null()));
                parent_cond.replace(binop(typeof(exp.And), parent_cond, not_null));
            }
            else if (column_expr.find<exp.Column>() is not null)
            {
                // Do not add the filter if the input is not a column (e.g. literal, struct etc)
                // DISTINCT is already present in the agg function, do not propagate it to FILTER as well
                var this_sql = column_expr is exp.Distinct ? expressions(column_expr) : sql(column_expr);
                array_agg_sql = $"{array_agg_sql} FILTER(WHERE {this_sql} IS NOT NULL)";
            }

            return array_agg_sql;
        }

        // py: sqlglot/generator.py::Generator.ignorenulls_sql #0000000000
        public virtual string ignorenulls_sql(exp.IgnoreNulls expression) => _embed_ignore_nulls(expression, "IGNORE NULLS");

        // py: sqlglot/generator.py::Generator.respectnulls_sql #0000000000
        public virtual string respectnulls_sql(exp.RespectNulls expression) => _embed_ignore_nulls(expression, "RESPECT NULLS");

        // py: sqlglot/generator.py::Generator._embed_ignore_nulls #0000000000
        public virtual string _embed_ignore_nulls(exp.Expr expression, string text)
        {
            var @this = expression.args["this"] as exp.Expr;
            if (@this is not null && RESPECT_IGNORE_NULLS_UNSUPPORTED_EXPRESSIONS.OfType<Type>().Any(t => t.IsInstanceOfType(@this)))
            {
                unsupported($"RESPECT/IGNORE NULLS is not supported for {@this.key} in {dialect.GetType().Name}");
                return sql(@this);
            }

            if (IGNORE_NULLS_IN_FUNC && !py.truthy(expression.meta.get("inline")))
            {
                if (IGNORE_NULLS_BEFORE_ORDER)
                {
                    // The first modifier here will be the one closest to the AggFunc's arg
                    var mods = generators.postgres.PostgresGenerator.find_all_in_scope(expression, typeof(exp.HavingMax), typeof(exp.Order), typeof(exp.Limit))
                        .OrderBy(x => x is exp.HavingMax ? 0 : x is exp.Order ? 1 : 2).ToList();

                    if (mods.Count > 0)
                    {
                        var mod = mods[0];
                        var inner = (exp.Expr)Activator.CreateInstance(expression.GetType(), [new PyDict<string, object?> { ["this"] = ((exp.Expr)mod.args["this"]!).copy() }])!;
                        inner.meta["inline"] = true;
                        ((exp.Expr)mod.args["this"]!).replace(inner);
                        return sql(expression.args["this"]);
                    }
                }

                var agg_func = expression.find([typeof(exp.AggFunc)]);

                if (agg_func is not null)
                {
                    var agg_func_sql = sql(agg_func, comment: false);
                    agg_func_sql = agg_func_sql[..^1] + $" {text})";
                    return maybe_comment(agg_func_sql, comments: agg_func.comments);
                }
            }

            return $"{sql(expression, "this")} {text}";
        }

        // py: sqlglot/generator.py::Generator.rowformatdelimitedproperty_sql #0000000000
        public virtual string rowformatdelimitedproperty_sql(exp.RowFormatDelimitedProperty expression)
        {
            var fields = sql(expression, "fields");
            fields = fields.Length != 0 ? $" FIELDS TERMINATED BY {fields}" : "";
            var escaped = sql(expression, "escaped");
            escaped = escaped.Length != 0 ? $" ESCAPED BY {escaped}" : "";
            var items = sql(expression, "collection_items");
            items = items.Length != 0 ? $" COLLECTION ITEMS TERMINATED BY {items}" : "";
            var keys = sql(expression, "map_keys");
            keys = keys.Length != 0 ? $" MAP KEYS TERMINATED BY {keys}" : "";
            var lines = sql(expression, "lines");
            lines = lines.Length != 0 ? $" LINES TERMINATED BY {lines}" : "";
            var @null = sql(expression, "null");
            @null = @null.Length != 0 ? $" NULL DEFINED AS {@null}" : "";
            return $"ROW FORMAT DELIMITED{fields}{escaped}{items}{keys}{lines}{@null}";
        }

        // py: sqlglot/generator.py::Generator.distribute_sql #0000000000
        public virtual string distribute_sql(exp.Distribute expression) => op_expressions("DISTRIBUTE BY", expression);

        // py: sqlglot/generator.py::Generator.version_sql #0000000000
        public virtual string version_sql(exp.Version expression)
        {
            var @this = $"FOR {expression.name}";
            var kind = expression.text("kind");
            var expr = sql(expression, "expression");
            return $"{@this} {kind} {expr}";
        }

        // py: sqlglot/generator.py::Generator.historicaldata_sql #0000000000
        public virtual string historicaldata_sql(exp.HistoricalData expression)
        {
            var @this = sql(expression, "this");
            var kind = sql(expression, "kind");
            var expr = sql(expression, "expression");
            return $"{@this} ({kind} => {expr})";
        }

        // py: sqlglot/generator.py::Generator.queryoption_sql #0000000000
        public virtual string queryoption_sql(exp.QueryOption expression)
        {
            unsupported("Unsupported query option.");
            return "";
        }
    }
}
