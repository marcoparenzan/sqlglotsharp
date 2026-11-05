namespace SqlGlotSharp;

public static partial class generator
{
    public partial class Generator
    {
        // Bounded INSERT closure. Child SQL always goes through production dispatch:
        // missing Directory / storage / settings handlers fail explicitly, never omit clauses.
        // py: sqlglot/generator.py::Generator.insert_sql #0000000000
        public virtual string insert_sql(exp.Insert expression)
        {
            var hint = sql(expression, "hint");
            var overwrite = py.truthy(expression.args.get("overwrite"));
            var target = expression.@this is exp.Directory
                ? overwrite ? " OVERWRITE" : " INTO"
                : overwrite ? INSERT_OVERWRITE : " INTO";
            var stored = sql(expression, "stored");
            stored = stored.Length != 0 ? $" {stored}" : "";
            var alternative = expression.args.get("alternative");
            var alternative_sql = py.truthy(alternative) ? $" OR {alternative}" : "";
            var ignore = py.truthy(expression.args.get("ignore")) ? " IGNORE" : "";
            if (py.truthy(expression.args.get("is_function"))) target = $"{target} FUNCTION";
            target = $"{target} {sql(expression, "this")}";
            var exists = py.truthy(expression.args.get("exists")) ? " IF EXISTS" : "";
            var where = sql(expression, "where");
            where = where.Length != 0 ? $"{sep()}REPLACE WHERE {where}" : "";
            var using_sql = expressions(expression, key: "using", flat: true);
            using_sql = using_sql.Length != 0 ? $"{sep()}REPLACE USING ({using_sql})" : "";
            var body = $"{sep()}{sql(expression, "expression")}";
            var conflict = sql(expression, "conflict");
            conflict = conflict.Length != 0 ? $" {conflict}" : "";
            var by_name = py.truthy(expression.args.get("by_name")) ? " BY NAME" : "";
            var default_values = py.truthy(expression.args.get("default")) ? "DEFAULT VALUES" : "";
            var returning = sql(expression, "returning");
            body = RETURNING_END ? $"{body}{conflict}{default_values}{returning}"
                : $"{returning}{body}{conflict}";
            var partition = sql(expression, "partition");
            partition = partition.Length != 0 ? $" {partition}" : "";
            var settings = sql(expression, "settings");
            settings = settings.Length != 0 ? $" {settings}" : "";
            var source = sql(expression, "source");
            source = source.Length != 0 ? $"TABLE {source}" : "";
            var result = $"INSERT{hint}{alternative_sql}{ignore}{target}{stored}{by_name}{exists}{partition}{settings}{where}{using_sql}{body}{source}";
            return prepend_ctes(expression, result);
        }

        // py: sqlglot/generator.py::Generator.onconflict_sql #0000000000
        public virtual string onconflict_sql(exp.OnConflict expression)
        {
            var conflict = py.truthy(expression.args.get("duplicate")) ? "ON DUPLICATE KEY" : "ON CONFLICT";
            var constraint = sql(expression, "constraint");
            constraint = constraint.Length != 0 ? $" ON CONSTRAINT {constraint}" : "";
            var keys = expressions(expression, key: "conflict_keys", flat: true);
            if (keys.Length != 0) keys = $"({keys})";
            var index_predicate = sql(expression, "index_predicate");
            keys = $"{keys}{index_predicate} ";
            var action = sql(expression, "action");
            var assignments = expressions(expression, flat: true);
            if (assignments.Length != 0)
                assignments = $" {(DUPLICATE_KEY_UPDATE_WITH_SET ? "SET " : "")}{assignments}";
            var where = sql(expression, "where");
            return $"{conflict}{constraint}{keys}{action}{assignments}{where}";
        }

        // py: sqlglot/generator.py::Generator.returning_sql #0000000000
        public virtual string returning_sql(exp.Returning expression) =>
            $"{seg("RETURNING")} {expressions(expression, flat: true)}";

        // py: sqlglot/generator.py::Generator.schema_sql #0000000000
        public virtual string schema_sql(exp.Schema expression)
        {
            var target = sql(expression, "this");
            var columns = schema_columns_sql(expression);
            return target.Length != 0 && columns.Length != 0 ? $"{target} {columns}"
                : target.Length != 0 ? target : columns;
        }

        // py: sqlglot/generator.py::Generator.schema_columns_sql #0000000000
        public virtual string schema_columns_sql(exp.Expr expression) => expression.expressions.Count != 0
            ? $"({sep("")}{expressions(expression)}{seg(")", sep: "")}" : "";
    }
}
