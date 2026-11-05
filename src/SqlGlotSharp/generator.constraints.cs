namespace SqlGlotSharp;

// Bounded common CREATE constraints. Child clauses retain production dispatch (and
// explicit NotPorted failures); no string reconstruction, AST edits or option filtering.
public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.primarykeycolumnconstraint_sql
        public virtual string primarykeycolumnconstraint_sql(exp.PrimaryKeyColumnConstraint expression)
        {
            create_guard(expression, "desc", "options", "include");
            var desc = expression.args.get("desc");
            // Upstream returns before visiting include/options when desc is present.
            if (desc is not null) return $"PRIMARY KEY{(py.truthy(desc) ? " DESC" : " ASC")}";
            var options = expressions(expression, key: "options", flat: true, sep: " ");
            options = options.Length != 0 ? $" {options}" : "";
            var include = sql(expression, "include");
            return $"PRIMARY KEY{include}{options}";
        }

        // py: sqlglot/generator.py::Generator.uniquecolumnconstraint_sql
        public virtual string uniquecolumnconstraint_sql(exp.UniqueColumnConstraint expression)
        {
            create_guard(expression, "this", "index_type", "on_conflict", "nulls", "options", "include");
            var thisSql = sql(expression, "this");
            thisSql = thisSql.Length != 0 ? $" {thisSql}" : "";
            var indexType = expression.args.get("index_type");
            var indexSql = py.truthy(indexType) ? $" USING {py.str(indexType)}" : "";
            var conflict = sql(expression, "on_conflict");
            conflict = conflict.Length != 0 ? $" {conflict}" : "";
            var nulls = py.truthy(expression.args.get("nulls")) ? " NULLS NOT DISTINCT" : "";
            var options = expressions(expression, key: "options", flat: true, sep: " ");
            options = options.Length != 0 ? $" {options}" : "";
            var include = sql(expression, "include");
            return $"UNIQUE{nulls}{thisSql}{indexSql}{conflict}{include}{options}";
        }

        // py: sqlglot/generator.py::Generator.checkcolumnconstraint_sql
        public virtual string checkcolumnconstraint_sql(exp.CheckColumnConstraint expression)
        {
            create_guard(expression, "this", "enforced");
            var enforced = py.truthy(expression.args.get("enforced")) ? " ENFORCED" : "";
            return $"CHECK ({sql(expression, "this")}){enforced}";
        }

        // py: sqlglot/generator.py::Generator.constraint_sql
        public virtual string constraint_sql(exp.Constraint expression)
        {
            create_guard(expression, "this", "expressions");
            var thisSql = sql(expression, "this");
            var children = expressions(expression, flat: true);
            return $"CONSTRAINT {thisSql} {children}";
        }

        // py: sqlglot/generator.py::Generator.check_sql
        public virtual string check_sql(exp.Check expression)
        {
            create_guard(expression, "this");
            return $"CHECK ({sql(expression, "this")})";
        }

        // py: sqlglot/generator.py::Generator.foreignkey_sql
        public virtual string foreignkey_sql(exp.ForeignKey expression)
        {
            create_guard(expression, "this", "expressions", "reference", "delete", "update", "options");
            var thisSql = sql(expression, "this");
            thisSql = thisSql.Length != 0 ? $" {thisSql}" : "";
            var columns = expressions(expression, flat: true);
            columns = columns.Length != 0 ? $" ({columns})" : "";
            var reference = sql(expression, "reference");
            reference = reference.Length != 0 ? $" {reference}" : "";
            var delete = sql(expression, "delete");
            delete = delete.Length != 0 ? $" ON DELETE {delete}" : "";
            var update = sql(expression, "update");
            update = update.Length != 0 ? $" ON UPDATE {update}" : "";
            var options = expressions(expression, key: "options", flat: true, sep: " ");
            options = options.Length != 0 ? $" {options}" : "";
            return $"FOREIGN KEY{thisSql}{columns}{reference}{delete}{update}{options}";
        }

        // py: sqlglot/generator.py::Generator.primarykey_sql
        public virtual string primarykey_sql(exp.PrimaryKey expression)
        {
            create_guard(expression, "this", "expressions", "include", "options");
            var thisSql = sql(expression, "this");
            thisSql = thisSql.Length != 0 ? $" {thisSql}" : "";
            var columns = expressions(expression, flat: true);
            var include = sql(expression, "include");
            var options = expressions(expression, key: "options", flat: true, sep: " ");
            options = options.Length != 0 ? $" {options}" : "";
            return $"PRIMARY KEY{thisSql} ({columns}){include}{options}";
        }

        // py: sqlglot/generator.py::Generator.reference_sql
        public virtual string reference_sql(exp.Reference expression)
        {
            create_guard(expression, "this", "expressions", "options");
            var thisSql = sql(expression, "this");
            var columns = expressions(expression, flat: true);
            columns = columns.Length != 0 ? $"({columns})" : "";
            var options = expressions(expression, key: "options", flat: true, sep: " ");
            options = options.Length != 0 ? $" {options}" : "";
            return $"REFERENCES {thisSql}{columns}{options}";
        }

        // Bounded exact Property branch of Generator.property_sql / property_name.
        public virtual string constraint_storage_property_sql(exp.Property expression)
        {
            create_guard(expression, "this", "value");
            var name = expression.@this is exp.Dot ? sql(expression, "this") : expression.name;
            return $"{name}={sql(expression, "value")}";
        }

        // py: sqlglot/generator.py::Generator.indexparameters_sql
        // Constraint INCLUDE/storage/tablespace are represented by IndexParameters,
        // not IncludeProperty, in production parser ASTs.
        public virtual string indexparameters_sql(exp.IndexParameters expression)
        {
            create_guard(expression, "using", "columns", "partition_by", "where", "include", "with_storage", "tablespace", "on");
            var usingSql = sql(expression, "using");
            usingSql = usingSql.Length != 0 ? $" USING {usingSql}" : "";
            var columns = expressions(expression, key: "columns", flat: true);
            columns = columns.Length != 0 ? $"({columns})" : "";
            var partition = expressions(expression, key: "partition_by", flat: true);
            partition = partition.Length != 0 ? $" PARTITION BY {partition}" : "";
            var where = sql(expression, "where");
            var include = expressions(expression, key: "include", flat: true);
            include = include.Length != 0 ? $" INCLUDE ({include})" : "";
            var storage = expressions(expression, key: "with_storage", flat: true);
            storage = storage.Length != 0 ? $" WITH ({storage})" : "";
            var tablespace = sql(expression, "tablespace");
            if (tablespace.Length != 0)
                tablespace = $"{(expression.parent is exp.Index ? "" : " USING INDEX")} TABLESPACE {tablespace}";
            var on = sql(expression, "on");
            on = on.Length != 0 ? $" ON {on}" : "";
            return $"{usingSql}{columns}{include}{storage}{tablespace}{partition}{where}{on}";
        }

        // py: sqlglot/generator.py::Generator.includeproperty_sql
        public virtual string includeproperty_sql(exp.IncludeProperty expression)
        {
            create_guard(expression, "this", "column_def", "alias");
            var include = $"INCLUDE {sql(expression, "this")}";
            var column = sql(expression, "column_def");
            if (column.Length != 0) include = $"{include} {column}";
            var alias = sql(expression, "alias");
            if (alias.Length != 0) include = $"{include} AS {alias}";
            return include;
        }
    }
}
