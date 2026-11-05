namespace SqlGlotSharp.generators;

public static partial class postgres
{
    public partial class PostgresGenerator
    {
        // py: sqlglot/generators/postgres.py::PostgresGenerator.unnest_sql
        public override string unnest_sql(exp.Unnest expression)
        {
            if (expression.expressions.Count == 1)
            {
                var arg = (exp.Expr)expression.expressions[0]!;
                if (arg is exp.GenerateDateArray)
                {
                    var series_args = new PyDict<string, object?>();
                    foreach (var (key, value) in arg.args) series_args[key] = value;
                    exp.Expr generate_series = new exp.GenerateSeries(series_args);
                    if (expression.parent is exp.From or exp.Join)
                    {
                        // SELECT value::date FROM GENERATE_SERIES(...) AS _t(value)
                        var table = new exp.Table(@this: generate_series);
                        table.set("alias", new exp.TableAlias(@this: exp.to_identifier("_t"),
                            columns: new List<object?> { exp.to_identifier("value") }));
                        var select = new exp.Select(
                            expressions: new List<object?> { new exp.Cast(@this: new exp.Column(@this: exp.to_identifier("value")),
                                to: exp.DataType.build(exp.DType.DATE)) },
                            from_: new exp.From(@this: table));
                        var alias = expression.args.get("alias") as exp.Expr
                            ?? new exp.TableAlias(@this: exp.to_identifier("_unnested_generate_series"));
                        generate_series = new exp.Subquery(@this: select, alias: alias);
                    }
                    return sql(generate_series);
                }

                var @this = optimizer.annotate_types_module.annotate_types(arg, dialect: dialect);
                if (@this.is_type("array<json>"))
                {
                    exp.Expr unwrapped = @this;
                    while (unwrapped is exp.Cast) unwrapped = (exp.Expr)unwrapped.args["this"]!;

                    var arg_as_json = sql(dialect_helpers.cast(unwrapped, exp.DataType.build(exp.DType.JSON)));
                    var alias_sql = sql(expression, "alias");
                    alias_sql = alias_sql.Length != 0 ? $" AS {alias_sql}" : "";

                    if (py.truthy(expression.args.get("offset"))) unsupported("Unsupported JSON_ARRAY_ELEMENTS with offset");

                    return $"JSON_ARRAY_ELEMENTS({arg_as_json}){alias_sql}";
                }
            }

            return base.unnest_sql(expression);
        }
    }
}
