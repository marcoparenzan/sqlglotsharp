namespace SqlGlotSharp;

public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.drop_sql #0000000000
        public virtual string drop_sql(exp.Drop expression)
        {
            var tables = expressions(expression, key: "tables", flat: true);
            var expressions_sql = expressions(expression, flat: true);
            expressions_sql = expressions_sql.Length != 0 ? $" ({expressions_sql})" : "";
            var kind = expression.args["kind"];
            kind = dialect.INVERSE_CREATABLE_KIND_MAPPING.get(py.str(kind)) ?? kind;
            var iceberg = py.truthy(expression.args.get("iceberg")) && SUPPORTS_DROP_ALTER_ICEBERG_PROPERTY ? " ICEBERG" : "";
            var exists_sql = py.truthy(expression.args.get("exists")) ? " IF EXISTS " : " ";
            var concurrently_sql = py.truthy(expression.args.get("concurrently")) ? " CONCURRENTLY" : "";
            var on_cluster = sql(expression, "cluster");
            on_cluster = on_cluster.Length != 0 ? $" {on_cluster}" : "";
            var temporary = py.truthy(expression.args.get("temporary")) ? " TEMPORARY" : "";
            var materialized = py.truthy(expression.args.get("materialized")) ? " MATERIALIZED" : "";
            var cascade = py.truthy(expression.args.get("cascade")) ? " CASCADE" : "";
            var restrict = py.truthy(expression.args.get("restrict")) ? " RESTRICT" : "";
            var constraints = py.truthy(expression.args.get("constraints")) ? " CONSTRAINTS" : "";
            var purge = py.truthy(expression.args.get("purge")) ? " PURGE" : "";
            var sync = py.truthy(expression.args.get("sync")) ? " SYNC" : "";
            var force = py.truthy(expression.args.get("force")) ? " FORCE" : "";
            return $"DROP{temporary}{materialized}{iceberg} {kind}{concurrently_sql}{exists_sql}{tables}{on_cluster}{expressions_sql}{cascade}{restrict}{constraints}{purge}{sync}{force}";
        }

        // py: sqlglot/generator.py::Generator.partitionrange_sql
        public virtual string partitionrange_sql(exp.PartitionRange expression)
        {
            var low = sql(expression, "this");
            var high = sql(expression, "expression");

            return $"{low} TO {high}";
        }

        // py: sqlglot/generator.py::Generator.truncatetable_sql
        public virtual string truncatetable_sql(exp.TruncateTable expression)
        {
            var target = py.truthy(expression.args.get("is_database")) ? "DATABASE" : "TABLE";
            var tables = $" {expressions(expression)}";
            var exists = py.truthy(expression.args.get("exists")) ? " IF EXISTS" : "";
            var on_cluster = sql(expression, "cluster");
            on_cluster = on_cluster.Length != 0 ? $" {on_cluster}" : "";
            var identity = sql(expression, "identity");
            identity = identity.Length != 0 ? $" {identity} IDENTITY" : "";
            var option = sql(expression, "option");
            option = option.Length != 0 ? $" {option}" : "";
            var partition = sql(expression, "partition");
            partition = partition.Length != 0 ? $" {partition}" : "";
            return $"TRUNCATE {target}{exists}{tables}{on_cluster}{identity}{option}{partition}";
        }
    }
}
