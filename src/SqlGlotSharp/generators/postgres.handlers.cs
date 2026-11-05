namespace SqlGlotSharp.generators;

public static partial class postgres
{
    public partial class PostgresGenerator
    {
        // py: sqlglot/generators/postgres.py::PostgresGenerator.computedcolumnconstraint_sql #f0e9fc951f
        public virtual string computedcolumnconstraint_sql(exp.ComputedColumnConstraint expression) =>
            $"GENERATED ALWAYS AS ({sql(expression, "this")}) STORED";

        // py: sqlglot/generators/postgres.py::PostgresGenerator.isascii_sql #43964f61c3
        public virtual string isascii_sql(exp.IsAscii expression) =>
            $"({sql(expression.@this)} ~ '^[[:ascii:]]*$')";

        // py: sqlglot/generators/postgres.py::PostgresGenerator.ignorenulls_sql #3a2beecb31
        public virtual string ignorenulls_sql(exp.IgnoreNulls expression)
        {
            // https://www.postgresql.org/docs/current/functions-window.html
            unsupported("PostgreSQL does not support IGNORE NULLS.");
            return sql(expression.@this);
        }

        // py: sqlglot/generators/postgres.py::PostgresGenerator.respectnulls_sql #a35129991f
        public virtual string respectnulls_sql(exp.RespectNulls expression)
        {
            // https://www.postgresql.org/docs/current/functions-window.html
            unsupported("PostgreSQL does not support RESPECT NULLS.");
            return sql(expression.@this);
        }

        // py: sqlglot/generators/postgres.py::PostgresGenerator.regexplike_sql #e13804e721
        public virtual string regexplike_sql(exp.RegexpLike expression)
        {
            var flag = expression.args.get("flag");
            if (!py.truthy(flag)) return binary(expression, "~");

            // Postgres has no flags argument for ~, only the case-insensitive ~* operator. Of the
            // case flags, the last one wins, e.g. 'ic' is case-sensitive and 'ci' is not
            var flag_expression = (exp.Expr)flag!;
            var flags = flag_expression.is_string ? flag_expression.name : "";
            if (!flag_expression.is_string || flags.Any(c => c is not ('i' or 'c')))
                unsupported("Only the case flags 'i' and 'c' are supported for ~ in Postgres");

            return binary(expression, flags.LastIndexOf('i') > flags.LastIndexOf('c') ? "~*" : "~");
        }

        // The upstream decorator is applied using the already-ported unsupported_args wrapper.
        // py: sqlglot/generators/postgres.py::PostgresGenerator.currentschema_sql #1848174258
        public virtual string currentschema_sql(exp.CurrentSchema expression) =>
            generator.unsupported_args("this")((_, _) => "CURRENT_SCHEMA")(this, expression);

        // py: sqlglot/generators/postgres.py::PostgresGenerator.placeholder_sql #c868a71401
        public override string placeholder_sql(exp.Placeholder expression)
        {
            if (py.truthy(expression.args.get("jdbc"))) return "?";

            var @this = py.truthy(expression.@this) ? $"({expression.name})" : "";
            return $"{NAMED_PLACEHOLDER_TOKEN}{@this}s";
        }

        // Partial TRANSFORMS: dependency-complete inline lambdas and the bounded SELECT
        // preprocessing chain. QUALIFY and Xor's bool_xor_sql rewrite remain explicit failures.
        // py: sqlglot/generators/postgres.py::PostgresGenerator.TRANSFORMS #0000000000
        internal static readonly IReadOnlyDictionary<Type, Func<generator.Generator, exp.Expr, string>> TRANSFORMS =
            new Dictionary<Type, Func<generator.Generator, exp.Expr, string>>
            {
                [typeof(exp.TryCast)] = (self, e) => self.cast_sql((exp.Cast)e), // upstream no_trycast_sql
                [typeof(exp.Select)] = (self, e) => select_preprocess_sql(self, e),
                [typeof(exp.ColumnDef)] = (self, e) => ((PostgresGenerator)self).create_column_def_sql((exp.ColumnDef)e),
                [typeof(exp.BitwiseXor)] = (self, e) => self.binary(e, "#"),
                [typeof(exp.RegexpILike)] = (self, e) => self.binary(e, "~*"),
                [typeof(exp.CurrentDate)] = (self, e) => dialects.dialect.no_paren_current_date_sql(self, (exp.CurrentDate)e),
                [typeof(exp.TimestampTrunc)] = dialects.dialect.timestamptrunc_sql(zone: true),
                [typeof(exp.CurrentTimestamp)] = (_, _) => "CURRENT_TIMESTAMP",
                [typeof(exp.CurrentUser)] = (_, _) => "CURRENT_USER",
                [typeof(exp.PartitionedByProperty)] = (self, e) => $"PARTITION BY {self.sql(e, "this")}",
                [typeof(exp.JSONExtract)] = (self, e) => ((PostgresGenerator)self).json_extract_sql(e, "JSON_EXTRACT_PATH", "->"),
                [typeof(exp.JSONExtractScalar)] = (self, e) => ((PostgresGenerator)self).json_extract_sql(e, "JSON_EXTRACT_PATH_TEXT", "->>"),
                [typeof(exp.JSONBExtract)] = (self, e) => ((PostgresGenerator)self).arrow_json_extract_sql(e, "#>"),
                [typeof(exp.JSONBExtractScalar)] = (self, e) => ((PostgresGenerator)self).arrow_json_extract_sql(e, "#>>"),
                [typeof(exp.JSONPathKey)] = (self, e) => ((PostgresGenerator)self).json_path_key_only_name((exp.JSONPathKey)e),
                [typeof(exp.JSONPathSubscript)] = (self, e) => ((PostgresGenerator)self).json_path_part(e.@this),
                [typeof(exp.JSONPathRoot)] = (_, _) => "",
                [typeof(exp.TableSpaceProperty)] = (self, e) => $"TABLESPACE {self.sql(e, "this")}",
                [typeof(exp.Substring)] = (self, e) =>
                {
                    var @this = self.sql(e, "this");
                    var start = self.sql(e, "start");
                    var length = self.sql(e, "length");
                    var from_part = start.Length != 0 ? $" FROM {start}" : "";
                    var for_part = length.Length != 0 ? $" FOR {length}" : "";
                    return $"SUBSTRING({@this}{from_part}{for_part})";
                },
                [typeof(exp.GroupConcat)] = (self, e) => self.groupconcat_sql((exp.GroupConcat)e, func_name: "STRING_AGG", within_group: false),
                [typeof(exp.Trim)] = (self, e) => self.dialect_trim_sql((exp.Trim)e),
                [typeof(exp.JSONObjectAgg)] = (self, e) => self.rename_func("JSON_OBJECT_AGG", e),
                [typeof(exp.FileFormatProperty)] = (self, e) => $"USING {self.sql(e, "this")}",
            };
    }
}
