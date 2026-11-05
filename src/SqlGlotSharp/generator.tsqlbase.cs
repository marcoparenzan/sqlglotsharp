namespace SqlGlotSharp;

// Base-generator handlers first needed by T-SQL: XML options, DECLARE, JSON_ARRAYAGG, temporal/data-retention properties.
public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.xmlkeyvalueoption_sql
        public virtual string xmlkeyvalueoption_sql(exp.XMLKeyValueOption expression)
        {
            var @this = sql(expression, "this");
            var expr = sql(expression, "expression");
            expr = expr.Length != 0 ? $"({expr})" : "";
            return $"{@this}{expr}";
        }

        // py: sqlglot/generator.py::Generator.declare_sql
        public virtual string declare_sql(exp.Declare expression)
        {
            var replace = py.truthy(expression.args.get("replace")) ? "OR REPLACE " : "";
            return $"DECLARE {replace}{expressions(expression, flat: true)}";
        }

        // py: sqlglot/generator.py::Generator.declareitem_sql
        public virtual string declareitem_sql(exp.DeclareItem expression)
        {
            var variables = expressions(expression, "this");
            var @default = sql(expression, "default");
            @default = @default.Length != 0 ? $" {DECLARE_DEFAULT_ASSIGNMENT} {@default}" : "";

            var kind = sql(expression, "kind");
            if (expression.args.get("kind") is exp.Schema) kind = $"TABLE {kind}";

            kind = kind.Length != 0 ? $" {kind}" : "";

            return $"{variables}{kind}{@default}";
        }

        // py: sqlglot/generator.py::Generator.jsonarrayagg_sql
        public virtual string jsonarrayagg_sql(exp.JSONArrayAgg expression)
        {
            var @this = sql(expression, "this");
            var order = sql(expression, "order");
            var null_handling = expression.args.get("null_handling");
            var null_handling_sql = py.truthy(null_handling) ? $" {null_handling}" : "";
            var return_type = sql(expression, "return_type");
            return_type = return_type.Length != 0 ? $" RETURNING {return_type}" : "";
            var strict = py.truthy(expression.args.get("strict")) ? " STRICT" : "";
            return func("JSON_ARRAYAGG", [@this], prefix: "(", suffix: $"{order}{null_handling_sql}{return_type}{strict})");
        }

        // py: sqlglot/generator.py::Generator.openjsoncolumndef_sql
        public virtual string openjsoncolumndef_sql(exp.OpenJSONColumnDef expression)
        {
            var @this = sql(expression, "this");
            var kind = sql(expression, "kind");
            var path = sql(expression, "path");
            path = path.Length != 0 ? $" {path}" : "";
            var as_json = py.truthy(expression.args.get("as_json")) ? " AS JSON" : "";
            return $"{@this} {kind}{path}{as_json}";
        }

        // py: sqlglot/generator.py::Generator.openjson_sql
        public virtual string openjson_sql(exp.OpenJSON expression)
        {
            var @this = sql(expression, "this");
            var path = sql(expression, "path");
            path = path.Length != 0 ? $", {path}" : "";
            var expressions_ = expressions(expression);
            var with_ = expressions_.Length != 0
                ? $" WITH ({seg(indent(expressions_), sep: "")}{seg(")", sep: "")}"
                : "";
            return $"OPENJSON({@this}{path}){with_}";
        }

        // py: sqlglot/generator.py::Generator.explodinggenerateseries_sql
        public virtual string explodinggenerateseries_sql(exp.ExplodingGenerateSeries expression)
        {
            var series_args = new PyDict<string, object?>();
            foreach (var (key, value) in expression.args) series_args[key] = value;
            var generate_series = new exp.GenerateSeries(series_args);

            var parent = expression.parent;
            if (parent is exp.Alias or exp.TableAlias) parent = parent.parent;

            if (SUPPORTS_EXPLODING_PROJECTIONS && parent is not (exp.Table or exp.Unnest))
                return sql(new exp.Unnest(expressions: new List<object?> { generate_series }));

            if (parent is exp.Select) unsupported("GenerateSeries projection unnesting is not supported.");

            return sql(generate_series);
        }

        // py: sqlglot/generator.py::Generator.nextvaluefor_sql
        public virtual string nextvaluefor_sql(exp.NextValueFor expression)
        {
            var order = expression.args.get("order") as exp.Order;
            var order_sql = order is not null ? $" OVER ({this.order_sql(order, flat: true)})" : "";
            return $"NEXT VALUE FOR {sql(expression, "this")}{order_sql}";
        }

        // py: sqlglot/generator.py::Generator.weekstart_name
        public virtual string weekstart_name(exp.WeekStart expression)
        {
            // WEEK(<day>) is BigQuery-only syntax, so it degrades to the plain WEEK unit
            var @this = py.upper(((exp.Expr)expression.args["this"]!).name);

            long? dow_from_week_start_day = dialects.dialect.WEEK_START_DAY_TO_DOW.TryGetValue(@this, out var d) ? d : null;
            var dow_from_week_offset = dialects.dialect.week_offset_to_dow(dialect.WEEK_OFFSET);

            if (dow_from_week_start_day != dow_from_week_offset)
                unsupported($"WEEK({@this}) is not supported; falling back to the default week start day");

            return "WEEK";
        }

        // py: sqlglot/generator.py::Generator.weekstart_sql
        public virtual string weekstart_sql(exp.WeekStart expression)
        {
            var name = weekstart_name(expression);

            // DateTrunc stores string literal units, whereas TimeUnit expressions store keywords
            if (expression.parent is exp.DateTrunc) return sql(exp.Literal.@string(name));

            return name;
        }

        // py: sqlglot/generator.py::Generator.withsystemversioningproperty_sql
        public virtual string withsystemversioningproperty_sql(exp.WithSystemVersioningProperty expression)
        {
            var @this = sql(expression, "this");
            @this = @this.Length != 0 ? $"HISTORY_TABLE={@this}" : "";
            var data_consistency = sql(expression, "data_consistency");
            string? data_consistency_sql = data_consistency.Length != 0 ? $"DATA_CONSISTENCY_CHECK={data_consistency}" : null;
            var retention_period = sql(expression, "retention_period");
            string? retention_period_sql = retention_period.Length != 0 ? $"HISTORY_RETENTION_PERIOD={retention_period}" : null;

            string on_sql = @this.Length != 0
                ? func("ON", @this, data_consistency_sql, retention_period_sql)
                : py.truthy(expression.args.get("on")) ? "ON" : "OFF";

            var sql_ = $"SYSTEM_VERSIONING={on_sql}";

            return py.truthy(expression.args.get("with_")) ? $"WITH({sql_})" : sql_;
        }

        // py: sqlglot/generator.py::Generator.datadeletionproperty_sql
        public virtual string datadeletionproperty_sql(exp.DataDeletionProperty expression)
        {
            var on_sql = py.truthy(expression.args.get("on")) ? "ON" : "OFF";
            var filter_col = sql(expression, "filter_column");
            string? filter_col_sql = filter_col.Length != 0 ? $"FILTER_COLUMN={filter_col}" : null;
            var retention_period = sql(expression, "retention_period");
            string? retention_period_sql = retention_period.Length != 0 ? $"RETENTION_PERIOD={retention_period}" : null;

            if (filter_col_sql is not null || retention_period_sql is not null)
                on_sql = func("ON", filter_col_sql, retention_period_sql);

            return $"DATA_DELETION={on_sql}";
        }
    }
}
