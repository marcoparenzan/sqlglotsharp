namespace SqlGlotSharp;

// ALTER, JSON_VALUE/EXISTS, PAD, PIVOT and a few name-only handlers of the base generator.
public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.alter_sql #0000000000
        public virtual string alter_sql(exp.Alter expression)
        {
            var actions = (expression.args.get("actions") as List<object?>) ?? [];

            string actions_sql;
            if (!dialect.ALTER_TABLE_ADD_REQUIRED_FOR_EACH_COLUMN && actions.Count > 0 && actions[0] is exp.ColumnDef)
            {
                actions_sql = expressions(expression, key: "actions", flat: true);
                actions_sql = $"ADD {actions_sql}";
            }
            else
            {
                List<object?> actions_list = [];
                foreach (var action in actions)
                {
                    string action_sql;
                    if (action is exp.ColumnDef or exp.Schema) action_sql = add_column_sql((exp.Expr)action);
                    else
                    {
                        action_sql = sql(action);
                        if (action is exp.Query) action_sql = $"AS {action_sql}";
                    }

                    actions_list.Add(action_sql);
                }

                actions_sql = format_args(actions_list.ToArray()).TrimStart('\n');
            }

            var iceberg = py.truthy(expression.args.get("iceberg")) && SUPPORTS_DROP_ALTER_ICEBERG_PROPERTY ? "ICEBERG " : "";
            var exists = py.truthy(expression.args.get("exists")) ? " IF EXISTS" : "";
            var on_cluster = sql(expression, "cluster");
            on_cluster = on_cluster.Length != 0 ? $" {on_cluster}" : "";
            var only = py.truthy(expression.args.get("only")) ? " ONLY" : "";
            var options = (expression.args.get("options") as List<object?>) ?? [];
            var options_sql = expressions(sqls: options.Where(o => !ALTER_PARTITION_OPTIONS.OfType<Type>().Any(t => t.IsInstanceOfType(o))).ToList());
            options_sql = options_sql.Length != 0 && actions_sql.Length != 0 ? $", {options_sql}" : options_sql;
            var partitions_sql = expressions(sqls: options.Where(o => ALTER_PARTITION_OPTIONS.OfType<Type>().Any(t => t.IsInstanceOfType(o))).ToList(),
                sep: " ", flat: true);
            if (partitions_sql.Length != 0 && (actions_sql.Length != 0 || options_sql.Length != 0)) partitions_sql = $" {partitions_sql}";
            var kind = sql(expression, "kind");
            var not_valid = py.truthy(expression.args.get("not_valid")) ? " NOT VALID" : "";
            var check = py.truthy(expression.args.get("check")) ? " WITH CHECK" : "";
            var cascade = py.truthy(expression.args.get("cascade")) && dialect.ALTER_TABLE_SUPPORTS_CASCADE ? " CASCADE" : "";
            var @this = sql(expression, "this");
            @this = @this.Length != 0 ? $" {@this}" : "";

            return $"ALTER {iceberg}{kind}{exists}{only}{@this}{on_cluster}{check}{sep()}{actions_sql}{not_valid}{options_sql}{partitions_sql}{cascade}";
        }

        // py: sqlglot/generator.py::Generator.jsonvalue_sql #0000000000
        public virtual string jsonvalue_sql(exp.JSONValue expression)
        {
            var path = sql(expression, "path");
            var returning = sql(expression, "returning");
            returning = returning.Length != 0 ? $" RETURNING {returning}" : "";

            var on_condition = sql(expression, "on_condition");
            on_condition = on_condition.Length != 0 ? $" {on_condition}" : "";

            return func("JSON_VALUE", expression.@this, $"{path}{returning}{on_condition}");
        }

        // py: sqlglot/generator.py::Generator.jsonexists_sql #0000000000
        public virtual string jsonexists_sql(exp.JSONExists expression)
        {
            var @this = sql(expression, "this");
            var path = sql(expression, "path");

            var passing = expressions(expression, "passing");
            passing = passing.Length != 0 ? $" PASSING {passing}" : "";

            var on_condition = sql(expression, "on_condition");
            on_condition = on_condition.Length != 0 ? $" {on_condition}" : "";

            path = $"{path}{passing}{on_condition}";

            return func("JSON_EXISTS", @this, path);
        }

        // py: sqlglot/generator.py::Generator.oncondition_sql #0000000000
        public virtual string oncondition_sql(exp.OnCondition expression)
        {
            // Static options like "NULL ON ERROR" are stored as strings, in contrast to "DEFAULT <expr> ON ERROR"
            var empty_arg = expression.args.get("empty");
            var empty = empty_arg is exp.Expr ? $"DEFAULT {sql(empty_arg)} ON EMPTY" : sql(expression, "empty");

            var error_arg = expression.args.get("error");
            var error = error_arg is exp.Expr ? $"DEFAULT {sql(error_arg)} ON ERROR" : sql(expression, "error");

            if (error.Length != 0 && empty.Length != 0)
            {
                error = dialect.ON_CONDITION_EMPTY_BEFORE_ERROR ? $"{empty} {error}" : $"{error} {empty}";
                empty = "";
            }

            var @null = sql(expression, "null");

            return $"{empty}{error}{@null}";
        }

        // py: sqlglot/generator.py::Generator.jsoncolumndef_sql #0000000000
        public virtual string jsoncolumndef_sql(exp.JSONColumnDef expression)
        {
            var path = sql(expression, "path");
            path = path.Length != 0 ? $" PATH {path}" : "";
            var nested_schema = sql(expression, "nested_schema");

            if (nested_schema.Length != 0) return $"NESTED{path} {nested_schema}";

            var @this = sql(expression, "this");
            var kind = sql(expression, "kind");
            kind = kind.Length != 0 ? $" {kind}" : "";
            var format_json = py.truthy(expression.args.get("format_json")) ? " FORMAT JSON" : "";

            var ordinality = py.truthy(expression.args.get("ordinality")) ? " FOR ORDINALITY" : "";
            return $"{@this}{kind}{format_json}{path}{ordinality}";
        }

        // py: sqlglot/generator.py::Generator.pad_sql #0000000000
        public virtual string pad_sql(exp.Pad expression)
        {
            var prefix = py.truthy(expression.args.get("is_left")) ? "L" : "R";

            string? fill_pattern = sql(expression, "fill_pattern");
            if (fill_pattern.Length == 0) fill_pattern = null;
            if (fill_pattern is null && PAD_FILL_PATTERN_IS_REQUIRED) fill_pattern = "' '";

            return func($"{prefix}PAD", expression.@this, expression.args.get("expression"), fill_pattern);
        }

        // py: sqlglot/generator.py::Generator.objectidentifier_sql #0000000000
        public virtual string objectidentifier_sql(exp.ObjectIdentifier expression) => expression.name;

        // py: sqlglot/generator.py::Generator.pseudotype_sql #0000000000
        public virtual string pseudotype_sql(exp.PseudoType expression) => expression.name;

        // py: sqlglot/generator.py::Generator.pivotalias_sql #0000000000
        public virtual string pivotalias_sql(exp.PivotAlias expression)
        {
            var alias = expression.args["alias"];

            var parent = expression.parent;
            var pivot = parent?.parent;

            if (pivot is exp.Pivot && py.truthy(pivot.args.get("unpivot")))
            {
                var identifier_alias = alias is exp.Identifier;
                var literal_alias = alias is exp.Literal;
                var aliasNode = (exp.Expr)alias!;

                if (identifier_alias && !UNPIVOT_ALIASES_ARE_IDENTIFIERS)
                    aliasNode.replace(exp.Literal.@string(aliasNode.output_name));
                else if (!identifier_alias && literal_alias && UNPIVOT_ALIASES_ARE_IDENTIFIERS)
                    aliasNode.replace(exp.to_identifier(aliasNode.output_name));
            }

            return alias_sql(expression);
        }

        // py: sqlglot/generator.py::Generator.pivot_sql #0000000000
        public virtual string pivot_sql(exp.Pivot expression)
        {
            var expressions_ = expressions(expression, flat: true);
            var unpivot = py.truthy(expression.args.get("unpivot"));
            var fields_ = (expression.args.get("fields") as List<object?>) ?? [];
            var direction = unpivot ? "UNPIVOT" : "PIVOT";

            var group = sql(expression, "group");

            if (py.truthy(expression.@this))
            {
                var @this = sql(expression, "this");
                string sql_;
                if (expressions_.Length == 0) sql_ = $"UNPIVOT {@this}";
                else
                {
                    var on = $"{seg("ON")} {expressions_}";
                    var into = sql(expression, "into");
                    into = into.Length != 0 ? $"{seg("INTO")} {into}" : "";
                    var @using = expressions(expression, key: "using", flat: true);
                    @using = @using.Length != 0 ? $"{seg("USING")} {@using}" : "";
                    sql_ = $"{direction} {@this}{on}{into}{@using}{group}";
                }
                return prepend_ctes(expression, sql_);
            }

            if (!unpivot)
            {
                // Wrap IN-list values with explicit aliases where the target dialect would differ
                var new_field_exprs = _pivot_in_value_aliases(expression);
                if (new_field_exprs is not null) ((exp.Expr)fields_[0]!).set("expressions", new_field_exprs);
            }

            var alias = sql(expression, "alias");
            if (alias.Length != 0) alias = PIVOT_ALIAS_WITH_AS ? $" AS {alias}" : $" {alias}";

            var fields = expressions(expression, "fields", sep: " ", dynamic: true, new_line: true, skip_first: true, skip_last: true);

            var include_nulls = expression.args.get("include_nulls");
            var nulls = include_nulls is null ? "" : py.truthy(include_nulls) ? " INCLUDE NULLS " : " EXCLUDE NULLS ";

            var default_on_null = sql(expression, "default_on_null");
            default_on_null = default_on_null.Length != 0 ? $" DEFAULT ON NULL ({default_on_null})" : "";
            var result = $"{seg(direction)}{nulls}({expressions_} FOR {fields}{default_on_null}{group}){alias}";
            return prepend_ctes(expression, result);
        }

        // py: sqlglot/generator.py::Generator._pivot_in_value_aliases #0000000000
        // Bounded: only the "source and target naming agree" outcome (None) is supported; rewriting the
        // IN-list for a different naming scheme is an explicit dependency.
        private List<object?>? _pivot_in_value_aliases(exp.Pivot expression)
        {
            var columns = expression.args.get("columns") as List<object?>;
            if (columns is not { Count: > 0 } || ((expression.args.get("fields") as List<object?>)?.Count ?? 0) != 1) return null;

            var args = expression.args;
            var parser_ = dialect.parser();

            var same = Equals(args.get("identify_pivot_strings") ?? parser_.IDENTIFY_PIVOT_STRINGS, parser_.IDENTIFY_PIVOT_STRINGS)
                && Equals(args.get("prefixed_pivot_columns") ?? parser_.PREFIXED_PIVOT_COLUMNS, parser_.PREFIXED_PIVOT_COLUMNS)
                && Equals(args.get("pivot_column_naming") ?? parser_.PIVOT_COLUMN_NAMING, parser_.PIVOT_COLUMN_NAMING);
            if (same) return null;

            throw new NotPortedException("sqlglot/generator.py::Generator._pivot_in_value_aliases (cross-dialect pivot naming)");
        }
    }
}
