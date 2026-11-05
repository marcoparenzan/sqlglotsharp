namespace SqlGlotSharp;

// DDL rendering of the base generator: CREATE, property locations and the property/index/trigger/
// function handlers of generator.py. Properties without a handler in TRANSFORMS keep their explicit
// NotPorted entry (see generator.dispatch.cs).
public static partial class generator
{
    public partial class Generator
    {
        private static Func<Generator, exp.Expr, string> Lit(string text) => (_, _) => text;
        private static Func<Generator, exp.Expr, string> Pre(string prefix) => (self, e) => $"{prefix}{self.sql(e, "this")}";

        // py: sqlglot/generator.py::Generator.TRANSFORMS (property subset) #0000000000
        internal static readonly Dictionary<Type, Func<Generator, exp.Expr, string>> PROPERTY_TRANSFORMS = new()
        {
            [typeof(exp.AllowedValuesProperty)] = (self, e) => $"ALLOWED_VALUES {self.expressions(e, flat: true)}",
            [typeof(exp.AutoRefreshProperty)] = Pre("AUTO REFRESH "),
            [typeof(exp.BackupProperty)] = Pre("BACKUP "),
            [typeof(exp.CalledOnNullInputProperty)] = Lit("CALLED ON NULL INPUT"),
            [typeof(exp.CharacterSetProperty)] = (self, e) =>
                $"{(py.truthy(e.args.get("default")) ? "DEFAULT " : "")}CHARACTER SET={self.sql(e, "this")}",
            [typeof(exp.CopyGrantsProperty)] = Lit("COPY GRANTS"),
            [typeof(exp.CopyTagsProperty)] = Lit("COPY TAGS"),
            [typeof(exp.CredentialsProperty)] = (self, e) => $"CREDENTIALS=({self.expressions(e, "expressions", sep: " ")})",
            [typeof(exp.ApiProperty)] = Lit("API"),
            [typeof(exp.ApplicationProperty)] = Lit("APPLICATION"),
            [typeof(exp.CatalogProperty)] = Lit("CATALOG"),
            [typeof(exp.ComputeProperty)] = Lit("COMPUTE"),
            [typeof(exp.DatabaseProperty)] = Lit("DATABASE"),
            [typeof(exp.DynamicProperty)] = Lit("DYNAMIC"),
            [typeof(exp.EmptyProperty)] = Lit("EMPTY"),
            [typeof(exp.EnviromentProperty)] = (self, e) => $"ENVIRONMENT ({self.expressions(e, flat: true)})",
            [typeof(exp.HandlerProperty)] = Pre("HANDLER "),
            [typeof(exp.ParameterStyleProperty)] = Pre("PARAMETER STYLE "),
            [typeof(exp.ExecuteAsProperty)] = (self, e) => self.naked_property((exp.Property)e),
            [typeof(exp.ExternalProperty)] = Lit("EXTERNAL"),
            [typeof(exp.GlobalProperty)] = Lit("GLOBAL"),
            [typeof(exp.HeapProperty)] = Lit("HEAP"),
            [typeof(exp.HybridProperty)] = Lit("HYBRID"),
            [typeof(exp.IcebergProperty)] = Lit("ICEBERG"),
            [typeof(exp.InheritsProperty)] = (self, e) => $"INHERITS ({self.expressions(e, flat: true)})",
            [typeof(exp.InputModelProperty)] = Pre("INPUT"),
            [typeof(exp.LanguageProperty)] = (self, e) => self.naked_property((exp.Property)e),
            [typeof(exp.LocationProperty)] = (self, e) => self.naked_property((exp.Property)e),
            [typeof(exp.LogProperty)] = (_, e) => $"{(py.truthy(e.args.get("no")) ? "NO " : "")}LOG",
            [typeof(exp.MaskingProperty)] = Lit("MASKING"),
            [typeof(exp.MaterializedProperty)] = Lit("MATERIALIZED"),
            [typeof(exp.NetworkProperty)] = Lit("NETWORK"),
            [typeof(exp.NoPrimaryIndexProperty)] = Lit("NO PRIMARY INDEX"),
            [typeof(exp.OnCommitProperty)] = (_, e) => $"ON COMMIT {(py.truthy(e.args.get("delete")) ? "DELETE" : "PRESERVE")} ROWS",
            [typeof(exp.OnProperty)] = Pre("ON "),
            [typeof(exp.OutputModelProperty)] = Pre("OUTPUT"),
            [typeof(exp.RemoteWithConnectionModelProperty)] = Pre("REMOTE WITH CONNECTION "),
            [typeof(exp.ReturnsProperty)] = (self, e) => py.truthy(e.args.get("null"))
                ? "RETURNS NULL ON NULL INPUT" : self.naked_property((exp.Property)e),
            [typeof(exp.RowAccessProperty)] = Lit("ROW ACCESS"),
            [typeof(exp.SampleProperty)] = Pre("SAMPLE BY "),
            [typeof(exp.SecureProperty)] = Lit("SECURE"),
            [typeof(exp.SecurityIntegrationProperty)] = Lit("SECURITY"),
            [typeof(exp.SetConfigProperty)] = (self, e) => self.sql(e, "this"),
            [typeof(exp.SetProperty)] = (_, e) => $"{(py.truthy(e.args.get("multi")) ? "MULTI" : "")}SET",
            [typeof(exp.SettingsProperty)] = (self, e) => $"SETTINGS{self.seg("")}{self.expressions(e)}",
            [typeof(exp.SharingProperty)] = Pre("SHARING="),
            [typeof(exp.SqlReadWriteProperty)] = (_, e) => e.name,
            [typeof(exp.SqlSecurityProperty)] = Pre("SQL SECURITY "),
            [typeof(exp.StabilityProperty)] = (_, e) => e.name,
            [typeof(exp.StreamingTableProperty)] = Lit("STREAMING"),
            [typeof(exp.StrictProperty)] = Lit("STRICT"),
            [typeof(exp.TemporaryProperty)] = Lit("TEMPORARY"),
            [typeof(exp.ToTableProperty)] = (self, e) => $"TO {self.sql(e.@this)}",
            [typeof(exp.TransformModelProperty)] = (self, e) => self.func("TRANSFORM", e.expressions.ToArray()),
            [typeof(exp.TransientProperty)] = Lit("TRANSIENT"),
            [typeof(exp.VirtualProperty)] = Lit("VIRTUAL"),
            [typeof(exp.TriggerExecute)] = Pre("EXECUTE FUNCTION "),
            [typeof(exp.UnloggedProperty)] = Lit("UNLOGGED"),
            [typeof(exp.UsingTemplateProperty)] = Pre("USING TEMPLATE "),
            [typeof(exp.ViewAttributeProperty)] = Pre("WITH "),
            [typeof(exp.VolatileProperty)] = Lit("VOLATILE"),
            [typeof(exp.WithJournalTableProperty)] = Pre("WITH JOURNAL TABLE="),
            [typeof(exp.WithProcedureOptions)] = (self, e) => $"WITH {self.expressions(e, flat: true)}",
            [typeof(exp.WithSchemaBindingProperty)] = Pre("WITH SCHEMA "),
            [typeof(exp.ForceProperty)] = Lit("FORCE"),
        };

        // py: sqlglot/generator.py::Generator.createable_sql #0000000000
        public virtual string createable_sql(exp.Create expression, Dictionary<exp.PropertiesLocation, List<object?>> locations) =>
            sql(expression, "this");

        // py: sqlglot/generator.py::Generator.create_sql #0000000000
        public virtual string create_sql(exp.Create expression)
        {
            var kind = sql(expression, "kind");
            kind = dialect.INVERSE_CREATABLE_KIND_MAPPING.TryGetValue(kind, out var mapped) && py.truthy(mapped) ? (string)mapped! : kind;

            var properties = expression.args.get("properties") as exp.Properties;

            if (kind == "TRIGGER" && properties is { expressions.Count: > 0 }
                && properties.expressions[0] is exp.TriggerProperties first && py.truthy(first.args.get("constraint")))
                kind = $"CONSTRAINT {kind}";

            var properties_locs = properties is not null ? locate_properties(properties) : [];
            List<object?> Locs(exp.PropertiesLocation location) => properties_locs.TryGetValue(location, out var list) ? list : [];

            var @this = createable_sql(expression, properties_locs);

            var properties_sql = "";
            if (Locs(exp.PropertiesLocation.POST_SCHEMA).Count > 0 || Locs(exp.PropertiesLocation.POST_WITH).Count > 0)
            {
                var props_ast = new exp.Properties(expressions:
                    Locs(exp.PropertiesLocation.POST_SCHEMA).Concat(Locs(exp.PropertiesLocation.POST_WITH)).ToList())
                { parent = expression };
                properties_sql = sql(props_ast);

                if (Locs(exp.PropertiesLocation.POST_SCHEMA).Count > 0) properties_sql = sep() + properties_sql;
                else if (!py.truthy(pretty))
                    // Standalone POST_WITH properties need a leading whitespace in non-pretty mode
                    properties_sql = $" {properties_sql}";
            }

            var begin = py.truthy(expression.args.get("begin")) ? " BEGIN" : "";

            var expression_sql = sql(expression, "expression");
            if (expression_sql.Length != 0)
            {
                expression_sql = $"{begin}{sep()}{expression_sql}";

                if (expression.expression is not exp.MacroOverloads
                    && (CREATE_FUNCTION_RETURN_AS || expression.expression is not exp.Return))
                {
                    var postalias_props_sql = "";
                    if (Locs(exp.PropertiesLocation.POST_ALIAS).Count > 0)
                        postalias_props_sql = this.properties(
                            new exp.Properties(expressions: Locs(exp.PropertiesLocation.POST_ALIAS)), wrapped: false);
                    postalias_props_sql = postalias_props_sql.Length != 0 ? $" {postalias_props_sql}" : "";
                    expression_sql = $" AS{postalias_props_sql}{expression_sql}";
                }
            }

            var postindex_props_sql = "";
            if (Locs(exp.PropertiesLocation.POST_INDEX).Count > 0)
                postindex_props_sql = this.properties(
                    new exp.Properties(expressions: Locs(exp.PropertiesLocation.POST_INDEX)), wrapped: false, prefix: " ");

            var indexes = expressions(expression, key: "indexes", indent: false, sep: " ");
            indexes = indexes.Length != 0 ? $" {indexes}" : "";
            var index_sql = indexes + postindex_props_sql;

            var replace = py.truthy(expression.args.get("replace")) ? " OR REPLACE" : "";
            var refresh = py.truthy(expression.args.get("refresh")) ? " OR REFRESH" : "";
            var unique = py.truthy(expression.args.get("unique")) ? " UNIQUE" : "";

            var clustered = expression.args.get("clustered");
            var clustered_sql = clustered is null ? "" : py.truthy(clustered) ? " CLUSTERED COLUMNSTORE" : " NONCLUSTERED COLUMNSTORE";

            var postcreate_props_sql = "";
            if (Locs(exp.PropertiesLocation.POST_CREATE).Count > 0)
                postcreate_props_sql = this.properties(
                    new exp.Properties(expressions: Locs(exp.PropertiesLocation.POST_CREATE)), sep: " ", prefix: " ", wrapped: false);

            var modifiers = clustered_sql + replace + refresh + unique + postcreate_props_sql;

            var postexpression_props_sql = "";
            if (Locs(exp.PropertiesLocation.POST_EXPRESSION).Count > 0)
                postexpression_props_sql = this.properties(
                    new exp.Properties(expressions: Locs(exp.PropertiesLocation.POST_EXPRESSION)), sep: " ", prefix: " ", wrapped: false);

            var concurrently = py.truthy(expression.args.get("concurrently")) ? " CONCURRENTLY" : "";
            var exists_sql = py.truthy(expression.args.get("exists")) ? " IF NOT EXISTS" : "";
            var no_schema_binding = py.truthy(expression.args.get("no_schema_binding")) ? " WITH NO SCHEMA BINDING" : "";

            var clone = sql(expression, "clone");
            clone = clone.Length != 0 ? $" {clone}" : "";

            var properties_expression = EXPRESSION_PRECEDES_PROPERTIES_CREATABLES.Contains(kind)
                ? $"{expression_sql}{properties_sql}" : $"{properties_sql}{expression_sql}";

            expression_sql = $"CREATE{modifiers} {kind}{concurrently}{exists_sql} {@this}{properties_expression}{postexpression_props_sql}{index_sql}{no_schema_binding}{clone}";
            return prepend_ctes(expression, expression_sql);
        }

        // py: sqlglot/generator.py::Generator.locate_properties #0000000000
        public virtual Dictionary<exp.PropertiesLocation, List<object?>> locate_properties(exp.Properties properties)
        {
            Dictionary<exp.PropertiesLocation, List<object?>> properties_locs = [];
            foreach (var value in properties.expressions)
            {
                var p = (exp.Expr)value!;
                var p_loc = PROPERTIES_LOCATION[p.GetType()];
                if (p_loc != exp.PropertiesLocation.UNSUPPORTED)
                {
                    if (!properties_locs.TryGetValue(p_loc, out var list)) properties_locs[p_loc] = list = [];
                    list.Add(p);
                }
                else unsupported($"Unsupported property {p.key}");
            }

            return properties_locs;
        }

        // py: sqlglot/generator.py::Generator.properties_sql #0000000000
        public virtual string properties_sql(exp.Properties expression)
        {
            List<object?> root_properties = [], with_properties = [];

            foreach (var value in expression.expressions)
            {
                var p = (exp.Expr)value!;
                var p_loc = PROPERTIES_LOCATION[p.GetType()];
                if (p_loc == exp.PropertiesLocation.POST_WITH) with_properties.Add(p);
                else if (p_loc == exp.PropertiesLocation.POST_SCHEMA) root_properties.Add(p);
            }

            var root_props_ast = new exp.Properties(expressions: root_properties) { parent = expression.parent };
            var with_props_ast = new exp.Properties(expressions: with_properties) { parent = expression.parent };

            var root_props = root_properties_sql(root_props_ast);
            var with_props = with_properties_sql(with_props_ast);

            if (root_props.Length != 0 && with_props.Length != 0 && !py.truthy(pretty)) with_props = " " + with_props;

            return root_props + with_props;
        }

        private string root_properties_sql(exp.Properties properties) => root_properties(properties);
        private string with_properties_sql(exp.Properties properties) => with_properties(properties);

        // py: sqlglot/generator.py::Generator.property_sql #0000000000
        public virtual string property_sql(exp.Expr expression)
        {
            var property_cls = expression.GetType();
            if (property_cls == typeof(exp.Property))
                return $"{property_name((exp.Property)expression)}={sql(expression, "value")}";

            if (!exp.Properties.PROPERTY_TO_NAME_.TryGetValue(property_cls, out var property_name_))
            {
                unsupported($"Unsupported property {expression.key}");
                return "";
            }

            return $"{property_name_}={sql(expression, "this")}";
        }

        // py: sqlglot/generator.py::Generator.naked_property #0000000000
        public virtual string naked_property(exp.Property expression)
        {
            if (!exp.Properties.PROPERTY_TO_NAME_.TryGetValue(expression.GetType(), out var property_name_))
            {
                unsupported($"Unsupported property {expression.GetType().Name}");
                property_name_ = "None";
            }
            return $"{property_name_} {sql(expression, "this")}";
        }

        // py: sqlglot/generator.py::Generator.sequenceproperties_sql #0000000000
        public virtual string sequenceproperties_sql(exp.SequenceProperties expression)
        {
            var start = sql(expression, "start");
            start = start.Length != 0 ? $"START WITH {start}" : "";
            var increment = sql(expression, "increment");
            increment = increment.Length != 0 ? $" INCREMENT BY {increment}" : "";
            var minvalue = sql(expression, "minvalue");
            minvalue = minvalue.Length != 0 ? $" MINVALUE {minvalue}" : "";
            var maxvalue = sql(expression, "maxvalue");
            maxvalue = maxvalue.Length != 0 ? $" MAXVALUE {maxvalue}" : "";
            var owned = sql(expression, "owned");
            owned = owned.Length != 0 ? $" OWNED BY {owned}" : "";

            var cache = expression.args.get("cache");
            var cache_str = cache is null ? "" : cache is true ? " CACHE" : $" CACHE {py.str(cache is exp.Expr ? sql(cache) : cache)}";

            var options = expressions(expression, key: "options", flat: true, sep: " ");
            options = options.Length != 0 ? $" {options}" : "";

            return $"{start}{increment}{minvalue}{maxvalue}{cache_str}{options}{owned}".TrimStart();
        }

        // py: sqlglot/generator.py::Generator.triggerproperties_sql #0000000000
        public virtual string triggerproperties_sql(exp.TriggerProperties expression)
        {
            var timing = expression.args.get("timing") as string ?? "";
            var events = string.Join(" OR ", (expression.args.get("events") as IEnumerable<object?> ?? []).Select(e => sql(e)));
            var timing_events = timing.Length != 0 || events.Length != 0 ? $"{timing} {events}".Trim() : "";

            List<string> parts = [timing_events, "ON", sql(expression, "table")];

            if (expression.args.get("referenced_table") is { } referenced_table && py.truthy(referenced_table))
                parts.AddRange(["FROM", sql(referenced_table)]);

            if (expression.args.get("deferrable") is string deferrable && deferrable.Length != 0) parts.Add(deferrable);

            if (expression.args.get("initially") is string initially && initially.Length != 0) parts.Add($"INITIALLY {initially}");

            if (py.truthy(expression.args.get("referencing"))) parts.Add(sql(expression.args.get("referencing")));

            if (expression.args.get("for_each") is string for_each && for_each.Length != 0) parts.Add($"FOR EACH {for_each}");

            if (py.truthy(expression.args.get("when"))) parts.Add($"WHEN ({sql(expression.args.get("when"))})");

            parts.Add(sql(expression, "execute"));

            return string.Join(sep(), parts);
        }

        // py: sqlglot/generator.py::Generator.triggerreferencing_sql #0000000000
        public virtual string triggerreferencing_sql(exp.TriggerReferencing expression)
        {
            List<string> parts = [];

            if (py.truthy(expression.args.get("old"))) parts.Add($"OLD TABLE AS {sql(expression.args.get("old"))}");
            if (py.truthy(expression.args.get("new"))) parts.Add($"NEW TABLE AS {sql(expression.args.get("new"))}");

            return $"REFERENCING {string.Join(" ", parts)}";
        }

        // py: sqlglot/generator.py::Generator.triggerevent_sql #0000000000
        public virtual string triggerevent_sql(exp.TriggerEvent expression)
        {
            if (py.truthy(expression.args.get("columns")))
                return $"{py.str(expression.@this)} OF {expressions(expression, key: "columns", flat: true)}";

            return sql(expression, "this");
        }

        // py: sqlglot/generator.py::Generator.clone_sql #0000000000
        public virtual string clone_sql(exp.Clone expression)
        {
            var @this = sql(expression, "this");
            var shallow = py.truthy(expression.args.get("shallow")) ? "SHALLOW " : "";
            var keyword = py.truthy(expression.args.get("copy")) && SUPPORTS_TABLE_COPY ? "COPY" : "CLONE";
            return $"{shallow}{keyword} {@this}";
        }

        // py: sqlglot/generator.py::Generator.heredoc_sql #0000000000
        public virtual string heredoc_sql(exp.Heredoc expression)
        {
            var tag = sql(expression, "tag");
            return $"${tag}${sql(expression, "this")}${tag}$";
        }

        // py: sqlglot/generator.py::Generator.index_sql #0000000000
        public virtual string index_sql(exp.Index expression)
        {
            var unique = py.truthy(expression.args.get("unique")) ? "UNIQUE " : "";
            var primary = py.truthy(expression.args.get("primary")) ? "PRIMARY " : "";
            var amp = py.truthy(expression.args.get("amp")) ? "AMP " : "";
            var name = sql(expression, "this");
            name = name.Length != 0 ? $"{name} " : "";
            var table = sql(expression, "table");
            table = table.Length != 0 ? $"{INDEX_ON} {table}" : "";

            var index = table.Length == 0 ? "INDEX " : "";

            var @params = sql(expression, "params");
            return $"{unique}{primary}{amp}{index}{name}{table}{@params}";
        }

        // py: sqlglot/generator.py::Generator.likeproperty_sql #0000000000
        public virtual string likeproperty_sql(exp.LikeProperty expression)
        {
            if (!SUPPORTS_CREATE_TABLE_LIKE)
                throw new NotPortedException("sqlglot/generator.py::Generator.likeproperty_sql (exp.select(...).from_(...).limit(0))");

            var options = string.Join(" ", expression.expressions.Select(e => $"{((exp.Expr)e!).name} {sql(e, "value")}"));
            options = options.Length != 0 ? $" {options}" : "";

            var like = $"LIKE {sql(expression, "this")}{options}";
            if (LIKE_PROPERTY_INSIDE_SCHEMA && expression.parent is not exp.Schema) like = $"({like})";

            return like;
        }

        // py: sqlglot/generator.py::Generator.partitionboundspec_sql #0000000000
        public virtual string partitionboundspec_sql(exp.PartitionBoundSpec expression)
        {
            if (expression.@this is System.Collections.IList)
                return $"IN ({expressions(expression, key: "this", flat: true)})";
            if (py.truthy(expression.@this))
            {
                var modulus = sql(expression, "this");
                var remainder = sql(expression, "expression");
                return $"WITH (MODULUS {modulus}, REMAINDER {remainder})";
            }

            var from_expressions = expressions(expression, key: "from_expressions", flat: true);
            var to_expressions = expressions(expression, key: "to_expressions", flat: true);
            return $"FROM ({from_expressions}) TO ({to_expressions})";
        }

        // py: sqlglot/generator.py::Generator.partitionedofproperty_sql #0000000000
        public virtual string partitionedofproperty_sql(exp.PartitionedOfProperty expression)
        {
            var @this = sql(expression, "this");

            var for_values_or_default = expression.expression is exp.PartitionBoundSpec spec
                ? $" FOR VALUES {sql(spec)}" : " DEFAULT";

            return $"PARTITION OF {@this}{for_values_or_default}";
        }

        // py: sqlglot/generator.py::Generator.withdataproperty_sql #0000000000
        public virtual string withdataproperty_sql(exp.WithDataProperty expression)
        {
            var data_sql = $"WITH {(py.truthy(expression.args.get("no")) ? "NO " : "")}DATA";
            var statistics = expression.args.get("statistics");
            var statistics_sql = statistics is null ? "" : $" AND {(!py.truthy(statistics) ? "NO " : "")}STATISTICS";
            return $"{data_sql}{statistics_sql}";
        }

        // py: sqlglot/generator.py::Generator.userdefinedfunction_sql #0000000000
        public virtual string userdefinedfunction_sql(exp.UserDefinedFunction expression)
        {
            var @this = sql(expression, "this");
            var expressions_ = no_identify(() => expressions(expression));
            expressions_ = py.truthy(expression.args.get("wrapped")) ? wrap(expressions_) : $" {expressions_}";
            return expressions_.Trim().Length != 0 ? $"{@this}{expressions_}" : @this;
        }

        // py: sqlglot/generator.py::Generator.return_sql #0000000000
        public virtual string return_sql(exp.Return expression) => $"RETURN {sql(expression, "this")}";

        // py: sqlglot/generator.py::Generator.macrooverloads_sql #0000000000
        public virtual string macrooverloads_sql(exp.MacroOverloads expression) => expressions(expression, flat: true);

        // py: sqlglot/generator.py::Generator.macrooverload_sql #0000000000
        public virtual string macrooverload_sql(exp.MacroOverload expression)
        {
            var @params = no_identify(() => expressions(expression, flat: true));
            var body = sql(expression, "this");
            var prefix = py.truthy(expression.args.get("is_table")) ? "TABLE " : "";
            return $"({@params}) AS {prefix}{body}";
        }

        // py: sqlglot/generator.py::Generator.addconstraint_sql #0000000000
        public virtual string addconstraint_sql(exp.AddConstraint expression) => $"ADD {expressions(expression, indent: false)}";

        // py: sqlglot/generator.py::Generator.block_sql #0000000000
        public virtual string block_sql(exp.Block expression)
        {
            var expressions_ = expressions(expression, sep: "; ", flat: true);
            var begin = py.truthy(expression.args.get("begin")) ? "BEGIN " : "";
            return expressions_.Length != 0 ? $"{begin}{expressions_}" : "";
        }

        // py: sqlglot/generator.py::Generator.comment_sql #0000000000
        public virtual string comment_sql(exp.Comment expression)
        {
            var @this = sql(expression, "this");
            var kind = py.str(expression.args["kind"]);
            var materialized = py.truthy(expression.args.get("materialized")) ? " MATERIALIZED" : "";
            var exists_sql = py.truthy(expression.args.get("exists")) ? " IF EXISTS " : " ";
            var expression_sql = sql(expression, "expression");
            return $"COMMENT{exists_sql}ON{materialized} {kind} {@this} IS {expression_sql}";
        }
    }
}
