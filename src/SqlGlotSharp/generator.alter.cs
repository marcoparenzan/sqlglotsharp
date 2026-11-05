namespace SqlGlotSharp;

public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.command_sql #0000000000
        public virtual string command_sql(exp.Command expression) =>
            $"{sql(expression, "this")} {expression.text("expression").Trim()}";

        internal static void alter_name_guard(exp.Expr expression, string key)
        {
            if (expression.args.get(key) is exp.Identifier id)
            {
                create_guard(id, "this", "quoted");
                if (id.@this is string { Length: > 0 }) return;
            }
            else if (expression.args.get(key) is exp.Column column)
            {
                create_guard(column, "this", "table", "db", "catalog");
                alter_name_guard(column, "this");
                foreach (var part in new[] { "table", "db", "catalog" })
                    if (column.args.get(part) is not null) alter_name_guard(column, part);
                return;
            }
            throw new NotPortedException($"Generator.{expression.key}_sql / {key} name type or missing name");
        }

        internal static void alter_table_guard(exp.Expr expression, string key)
        {
            if (expression.args.get(key) is not exp.Table table)
                throw new NotPortedException($"Generator.{expression.key}_sql / {key} table type or missing table");
            create_guard(table, "this", "db", "catalog");
            alter_name_guard(table, "this");
            foreach (var part in new[] { "db", "catalog" })
                if (table.args.get(part) is not null) alter_name_guard(table, part);
        }

        internal static List<object?> alter_list(exp.Expr expression, string key, bool required = true)
        {
            if (expression.args.get(key) is null && !required) return [];
            if (expression.args.get(key) is not List<object?> list || required && list.Count == 0)
                throw new NotPortedException($"Generator.{expression.key}_sql / {key} list type or missing actions");
            return list;
        }

        internal static void alter_bool_guard(exp.Expr expression, params string[] keys)
        {
            foreach (var key in keys)
                if (expression.args.get(key) is not (null or bool))
                    throw new NotPortedException($"Generator.{expression.key}_sql / {key} boolean type");
        }

        // py: sqlglot/generator.py::Generator.add_column_sql #0000000000
        public virtual string add_column_sql(exp.Expr expression)
        {
            var column_text = "";
            if (ALTER_TABLE_INCLUDE_COLUMN_KEYWORD)
            {
                if (expression is exp.ColumnDef) column_text = " COLUMN";
                else if (expression is exp.Schema) column_text = " COLUMNS";
            }

            var text = expression is exp.Schema && !ALTER_TABLE_ADD_COLUMNS_WRAPPED
                ? expressions(expression, flat: true) : sql(expression);

            return $"ADD{column_text} {text}";
        }

        public virtual string altercolumn_sql(exp.AlterColumn expression)
        {
            var @this = sql(expression, "this");

            var exists = "";
            if (py.truthy(expression.args.get("exists")))
            {
                if (SUPPORTS_ALTER_COLUMN_IF_EXISTS) exists = " IF EXISTS";
                else unsupported("ALTER COLUMN IF EXISTS is not supported by this dialect");
            }

            var dtype = sql(expression, "dtype");
            if (dtype.Length != 0)
            {
                var collate = sql(expression, "collate");
                collate = collate.Length != 0 ? $" COLLATE {collate}" : "";
                var @using = sql(expression, "using");
                @using = @using.Length != 0 ? $" USING {@using}" : "";
                var alter_set_type = ALTER_SET_TYPE.Length != 0 ? ALTER_SET_TYPE + " " : "";
                var null_constraint = _alter_column_null_constraint_sql(expression);

                return $"ALTER COLUMN{exists} {@this} {alter_set_type}{dtype}{collate}{@using}{null_constraint}";
            }

            var @default = sql(expression, "default");
            if (@default.Length != 0) return $"ALTER COLUMN{exists} {@this} SET DEFAULT {@default}";

            var comment = sql(expression, "comment");
            if (comment.Length != 0) return $"ALTER COLUMN{exists} {@this} COMMENT {comment}";

            var visible = expression.args.get("visible");
            if (py.truthy(visible)) return $"ALTER COLUMN{exists} {@this} SET {py.str(visible)}";

            var allow_null = expression.args.get("allow_null");
            var drop = expression.args.get("drop");

            if (!py.truthy(drop) && !py.truthy(allow_null)) unsupported("Unsupported ALTER COLUMN syntax");

            if (allow_null is not null)
            {
                var keyword = py.truthy(drop) ? "DROP" : "SET";
                return $"ALTER COLUMN{exists} {@this} {keyword} NOT NULL";
            }

            return $"ALTER COLUMN{exists} {@this} DROP DEFAULT";
        }

        public virtual string _alter_column_null_constraint_sql(exp.AlterColumn expression)
        {
            var allowNull = expression.args.get("allow_null");
            if (allowNull is null) return "";
            if (!SUPPORTS_ALTER_COLUMN_NULLABILITY)
            {
                unsupported("ALTER COLUMN cannot set nullability along with a type");
                return "";
            }
            return py.truthy(allowNull) ? " NULL" : " NOT NULL";
        }

        public virtual string renamecolumn_sql(exp.RenameColumn expression)
        {
            create_guard(expression, "this", "to", "exists");
            alter_name_guard(expression, "this"); alter_name_guard(expression, "to");
            alter_bool_guard(expression, "exists");
            return $"RENAME COLUMN{(py.truthy(expression.args.get("exists")) ? " IF EXISTS" : "")} {sql(expression, "this")} TO {sql(expression, "to")}";
        }

        public virtual string alterrename_sql(exp.AlterRename expression, bool include_to = true)
        {
            create_guard(expression, "this");
            alter_table_guard(expression, "this");
            if (!RENAME_TABLE_WITH_DB)
            {
                expression = (exp.AlterRename)expression.copy();
                var table = (exp.Table)expression.@this!;
                // Upstream table_(n.this) copies the name and removes qualifiers and table comments.
                expression.set("this", new exp.Table(@this: ((exp.Expr)table.@this!).copy()));
            }
            return $"RENAME{(include_to ? " TO" : "")} {sql(expression, "this")}";
        }

        internal static void alter_set_guard(exp.AlterSet expression)
        {
            create_guard(expression, "expressions");
            foreach (var item in alter_list(expression, "expressions"))
            {
                if (item is exp.Property property && property.GetType() == typeof(exp.Property))
                {
                    create_guard(property, "this", "value");
                    if (property.args.get("this") is not exp.Expr || property.args.get("value") is not exp.Expr)
                        throw new NotPortedException("Generator.alterset_sql / property fields");
                }
                else if (item is exp.EQ eq)
                {
                    create_guard(eq, "this", "expression");
                    if (eq.@this is not exp.Expr || eq.expression is not exp.Expr)
                        throw new NotPortedException("Generator.alterset_sql / assignment fields");
                }
                else throw new NotPortedException($"Generator.alterset_sql / expressions type {item?.GetType().Name ?? "null"}");
            }
        }

        // py: sqlglot/generator.py::Generator.alterset_sql #0000000000
        public virtual string alterset_sql(exp.AlterSet expression)
        {
            var exprs = expressions(expression, flat: true);
            if (ALTER_SET_WRAPPED) exprs = $"({exprs})";

            return $"SET {exprs}";
        }

        // py: sqlglot/generator.py::Generator.partition_sql #0000000000
        public virtual string partition_sql(exp.Partition expression)
        {
            var partition_keyword = py.truthy(expression.args.get("subpartition")) ? "SUBPARTITION" : "PARTITION";
            return $"{partition_keyword}({expressions(expression, flat: true)})";
        }

        // py: sqlglot/generator.py::Generator.droppartition_sql #0000000000
        public virtual string droppartition_sql(exp.DropPartition expression)
        {
            var exprs = expressions(expression);
            var exists = py.truthy(expression.args.get("exists")) ? " IF EXISTS " : " ";
            return $"DROP{exists}{exprs}";
        }

        // py: sqlglot/generator.py::Generator.addpartition_sql #0000000000
        public virtual string addpartition_sql(exp.AddPartition expression)
        {
            var exists = py.truthy(expression.args.get("exists")) ? "IF NOT EXISTS " : "";
            var location = sql(expression, "location");
            location = location.Length != 0 ? $" {location}" : "";
            return $"ADD {exists}{sql(expression.@this)}{location}";
        }

        // py: sqlglot/generator.py::Generator.alterdiststyle_sql #0000000000
        public virtual string alterdiststyle_sql(exp.AlterDistStyle expression)
        {
            var @this = sql(expression, "this");
            if (expression.@this is not exp.Var) @this = $"KEY DISTKEY {@this}";
            return $"ALTER DISTSTYLE {@this}";
        }

        // py: sqlglot/generator.py::Generator.altersortkey_sql #0000000000
        public virtual string altersortkey_sql(exp.AlterSortKey expression)
        {
            var compound = py.truthy(expression.args.get("compound")) ? " COMPOUND" : "";
            var @this = sql(expression, "this");
            var exprs = expressions(expression, flat: true);
            exprs = exprs.Length != 0 ? $"({exprs})" : "";
            return $"ALTER{compound} SORTKEY {(@this.Length != 0 ? @this : exprs)}";
        }
    }
}
