using System.Collections.Concurrent;
using System.Reflection;

namespace SqlGlotSharp;

// Bounded dispatch from sqlglot/generator.py. Preprocessing remains explicitly unported.
public static partial class generator
{
    // py: sqlglot/generator.py::_DISPATCH_CACHE #0000000000
    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<Type, Func<Generator, exp.Expr, string>>> _DISPATCH_CACHE = new();

    // Partial: the upstream transform and unported-handler inventories are explicit failure
    // entries, not implementations. Only existing public *_sql handlers render SQL.
    // py: sqlglot/generator.py::_build_dispatch #0000000000
    public static IReadOnlyDictionary<Type, Func<Generator, exp.Expr, string>> _build_dispatch(Type cls)
    {
        var dispatch = new Dictionary<Type, Func<Generator, exp.Expr, string>>();
        // Local canonical extension is intentionally not part of the generated EXPR_CLASSES inventory.
        dispatch[typeof(exp.TimeBucket)] = (self, expression) => self.timebucket_sql((exp.TimeBucket)expression);
        foreach (var name in _build_dispatch__transforms.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var type = exp.EXPR_CLASSES.Values.Single(type => type.Name == name);
            dispatch[type] = (_, _) => throw new NotPortedException($"sqlglot/generator.py::Generator.TRANSFORMS[{name}]");
        }
        foreach (var (type, transform) in Generator.PROPERTY_TRANSFORMS) dispatch[type] = transform;
        dispatch[typeof(exp.JSONObject)] = (self, expression) => self._jsonobject_sql(expression);
        dispatch[typeof(exp.JSONObjectAgg)] = (self, expression) => self._jsonobject_sql(expression);
        // Upstream TRANSFORMS = {**JSON_PATH_PART_TRANSFORMS, ...}: the ten JSONPath parts render for real.
        foreach (var (type, transform) in Generator.JSON_PATH_PART_TRANSFORMS) dispatch[type] = transform;
        // These three upstream TRANSFORMS delegate directly to set_operations.
        foreach (var type in new[] { typeof(exp.Union), typeof(exp.Intersect), typeof(exp.Except) })
            dispatch[type] = (self, expression) => self.set_operations((exp.SetOperation)expression);
        foreach (var (type, transform) in Generator.AUTO_TRANSFORMS) dispatch[type] = transform;
        foreach (var (type, transform) in Generator.SIMPLE_TRANSFORMS) dispatch[type] = transform;
        dispatch[typeof(exp.DefaultColumnConstraint)] = (self, expression) => self.defaultcolumnconstraint_sql((exp.DefaultColumnConstraint)expression);
        // Constraint storage and generic DDL properties share upstream rendering.
        dispatch[typeof(exp.Property)] = (self, expression) => expression.parent is exp.IndexParameters && expression.arg_key == "with_storage"
            ? self.constraint_storage_property_sql((exp.Property)expression) : self.property_sql(expression);
        dispatch[typeof(exp.Drop)] = (self, expression) => self.drop_sql((exp.Drop)expression);
        dispatch[typeof(exp.ExcludeColumnConstraint)] = (self, expression) => $"EXCLUDE {self.sql(expression, "this").TrimStart()}";
        dispatch[typeof(exp.WithOperator)] = (self, expression) => $"{self.sql(expression, "this")} WITH {self.sql(expression, "op")}";
        dispatch[typeof(exp.SwapTable)] = (self, expression) => $"SWAP WITH {self.sql(expression, "this")}";
        dispatch[typeof(exp.Variadic)] = (self, expression) => $"VARIADIC {self.sql(expression, "this")}";
        dispatch[typeof(exp.IntervalSpan)] = (self, expression) =>
            $"{self.sql(expression, "this")} TO {self.sql(expression, "expression")}";
        // PostgreSQL transforms take precedence over both base handlers and Func fallback.
        // The inventory remains complete; dependency-complete transforms replace only their own failures.
        if (typeof(generators.postgres.PostgresGenerator).IsAssignableFrom(cls))
        {
            dispatch.Remove(typeof(exp.CommentColumnConstraint)); // upstream excludes this base transform
            foreach (var name in _build_dispatch__postgres_transforms.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var type = exp.EXPR_CLASSES.Values.Single(type => type.Name == name);
                dispatch[type] = (_, _) => throw new NotPortedException(
                    $"sqlglot/generators/postgres.py::PostgresGenerator.TRANSFORMS[{type.Name}]");
            }
            foreach (var (type, transform) in generators.postgres.PostgresGenerator.TRANSFORMS)
                dispatch[type] = transform;
            foreach (var (type, transform) in generators.postgres.PostgresGenerator.MORE_TRANSFORMS)
                dispatch[type] = transform;
            foreach (var (type, transform) in generators.postgres.PostgresGenerator.DIALECT_HELPER_TRANSFORMS)
                dispatch[type] = transform;
            foreach (var (type, transform) in generators.postgres.PostgresGenerator.SCOPE_TRANSFORMS)
                dispatch[type] = transform;
            dispatch[typeof(exp.AlterSet)] = (self, expression) => self.alterset_sql((exp.AlterSet)expression);
            dispatch[typeof(exp.ByteString)] = (self, expression) => self.bytestring_sql((exp.ByteString)expression);
            dispatch[typeof(exp.Cast)] = (self, expression) => self.cast_sql((exp.Cast)expression);
            dispatch[typeof(exp.DataType)] = (self, expression) => self.datatype_sql((exp.DataType)expression);
            dispatch[typeof(exp.Interval)] = (self, expression) => self.interval_sql((exp.Interval)expression);
            // PostgreSQL overrides ported from generators/postgres.py (unnest_sql stays an explicit boundary).
            dispatch[typeof(exp.Lateral)] = (self, expression) => self.lateral_sql((exp.Lateral)expression);
            dispatch[typeof(exp.Array)] = (self, expression) => ((generators.postgres.PostgresGenerator)self).array_sql((exp.Array)expression);
            dispatch[typeof(exp.Bracket)] = (self, expression) => self.bracket_sql((exp.Bracket)expression);
            dispatch[typeof(exp.CommentColumnConstraint)] = (self, expression) => ((generators.postgres.PostgresGenerator)self).commentcolumnconstraint_sql((exp.CommentColumnConstraint)expression);
            dispatch[typeof(exp.MatchAgainst)] = (self, expression) => ((generators.postgres.PostgresGenerator)self).matchagainst_sql((exp.MatchAgainst)expression);
            dispatch[typeof(exp.SchemaCommentProperty)] = (self, expression) => ((generators.postgres.PostgresGenerator)self).schemacommentproperty_sql((exp.SchemaCommentProperty)expression);
            dispatch[typeof(exp.ArrayContains)] = (self, expression) => ((generators.postgres.PostgresGenerator)self).arraycontains_sql((exp.ArrayContains)expression);
            dispatch[typeof(exp.Unnest)] = (self, expression) => self.unnest_sql((exp.Unnest)expression);
            // Unported dialect handlers must not silently inherit a base implementation or
            // become ordinary Func fallbacks (e.g. CurrentSchema, RegexpLike, IsAscii).
            foreach (var key in _build_dispatch__postgres_handlers.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                if (exp.EXPR_CLASSES.TryGetValue(key, out var type) && !dispatch.ContainsKey(type))
                    dispatch[type] = (_, _) => throw new NotPortedException(
                        $"sqlglot/generators/postgres.py::PostgresGenerator.{key}_sql");
        }

        // T-SQL: TRANSFORMS = {**Generator.TRANSFORMS (minus ReturnsProperty), overrides}; the CTE / Select / Subquery
        // entries are transforms.preprocess chains that end in the matching *_sql handler.
        if (typeof(generators.tsql.TSQLGenerator).IsAssignableFrom(cls))
        {
            dispatch.Remove(typeof(exp.ReturnsProperty));
            foreach (var (type, transform) in generators.tsql.TSQLGenerator.TSQL_TRANSFORMS)
                dispatch[type] = transform;
            dispatch[typeof(exp.CTE)] = generators.tsql.TSQLGenerator.derived_table_preprocess_sql;
            dispatch[typeof(exp.Subquery)] = generators.tsql.TSQLGenerator.derived_table_preprocess_sql;
            dispatch[typeof(exp.Select)] = generators.tsql.TSQLGenerator.select_preprocess_sql;
            if (typeof(generators.fabric.FabricGenerator).IsAssignableFrom(cls))
                dispatch[typeof(exp.Create)] = generators.fabric.FabricGenerator.create_preprocess_sql;
        }

        // py: sqlglot/dialects/dialect.py::_Dialect.__new__ drops the transforms of unsupported JSONPath parts.
        var supported = SupportedJsonPathParts(cls);
        foreach (var part in Generator.JSON_PATH_PART_TRANSFORMS.Keys.Where(part => !supported.Contains(part)))
            dispatch.Remove(part);

        foreach (var attr in cls.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(method => method.Name, StringComparer.Ordinal))
        {
            var attr_name = attr.Name;
            if (!attr_name.EndsWith("_sql", StringComparison.Ordinal) || attr_name.StartsWith('_')) continue;
            var expr_key = attr_name[..^4];
            if (exp.EXPR_CLASSES.TryGetValue(expr_key, out var expr_cls) && !dispatch.ContainsKey(expr_cls))
            {
                dispatch[expr_cls] = (self, expression) =>
                {
                    // Reflection is only used at the upstream getattr boundary. Preserve errors.
                    try
                    {
                        // Match getattr(self, handler)(expression): connector stack, table sep
                        // and order flat retain their upstream defaults at the dispatch boundary.
                        var parameters = attr.GetParameters();
                        var arguments = parameters[0].ParameterType.IsArray
                            ? new object?[] { new object?[] { expression } }
                            : new object?[parameters.Length];
                        if (!parameters[0].ParameterType.IsArray)
                        {
                            arguments[0] = expression;
                            for (var i = 1; i < parameters.Length; i++)
                                arguments[i] = parameters[i].DefaultValue;
                        }
                        return (string)attr.Invoke(self, arguments)!;
                    }
                    catch (TargetInvocationException error) when (error.InnerException is not null)
                    {
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                        throw;
                    }
                };
            }
        }

        // Reflection includes join_sql / with_sql / cte_sql and the bounded hint helpers.
        // Keep missing child handlers as failure entries, not a SQL fallback.
        // Missing Python node handlers are failure entries; nodes with no Python handler
        // remain absent so sql can select Func/Property fallback or the unknown-type error.
        foreach (var key in _build_dispatch__handlers.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            if (exp.EXPR_CLASSES.TryGetValue(key, out var type) && !dispatch.ContainsKey(type))
                dispatch[type] = (_, _) => throw new NotPortedException($"sqlglot/generator.py::Generator.{key}_sql");

        return dispatch;
    }

    // The nearest SUPPORTED_JSON_PATH_PARTS_ declaration along the class chain (a ClassVar override upstream).
    private static HashSet<Type> SupportedJsonPathParts(Type cls)
    {
        for (var type = cls; type is not null; type = type.BaseType)
            if (type.GetField("SUPPORTED_JSON_PATH_PARTS_", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)?.GetValue(null) is HashSet<Type> parts)
                return parts;
        throw new InvalidOperationException($"SUPPORTED_JSON_PATH_PARTS not found for {cls.FullName}");
    }

    // Upstream inventories used ONLY to preserve explicit failure paths, never to render SQL.
    // They are checked against the Python oracle in GeneratorDispatchTests.
    private const string _build_dispatch__transforms = """
        JSONPathFilter JSONPathKey JSONPathRecursive JSONPathRoot JSONPathScript JSONPathSelector JSONPathSlice JSONPathSubscript JSONPathUnion JSONPathWildcard
        Adjacent AllowedValuesProperty AnalyzeColumns AnalyzeWith ArrayContainedBy ArrayContainsAll ArrayOverlaps AssumeColumnConstraint AutoRefreshProperty BackupProperty BinaryColumnConstraint CaseSpecificColumnConstraint CalledOnNullInputProperty Ceil CharacterSetColumnConstraint CharacterSetProperty ClusteredColumnConstraint CollateColumnConstraint CommentColumnConstraint ConnectByRoot ConvertToCharset CopyGrantsProperty CopyTagsProperty CredentialsProperty CurrentCatalog SessionUser DateFormatColumnConstraint DefaultColumnConstraint ApiProperty ApplicationProperty CatalogProperty ComputeProperty DatabaseProperty DynamicProperty EmptyProperty EncodeColumnConstraint EndStatement EnviromentProperty HandlerProperty ParameterStyleProperty EphemeralColumnConstraint ExcludeColumnConstraint ExecuteAsProperty Except ExternalProperty Floor Get GlobalProperty HeapProperty HybridProperty IcebergProperty InheritsProperty InlineLengthColumnConstraint InputModelProperty Intersect IntervalSpan Int64 JSONBContainsAnyTopKeys JSONBContainsAllTopKeys JSONBContainsTopKey JSONBDeleteAtPath JSONBPathExists JSONObject JSONObjectAgg LanguageProperty LocationProperty LogProperty MaskingProperty MaterializedProperty NetFunc NetworkProperty NonClusteredColumnConstraint NoPrimaryIndexProperty NotForReplicationColumnConstraint OnCommitProperty OnProperty OnUpdateColumnConstraint Operator OutputModelProperty ExtendsLeft ExtendsRight PathColumnConstraint PartitionedByBucket PartitionByTruncate PivotAny PositionalColumn ProjectionPolicyColumnConstraint InvisibleColumnConstraint ZeroFillColumnConstraint Put RemoteWithConnectionModelProperty ReturnsProperty RowAccessProperty SafeFunc SampleProperty SecureProperty SecurityIntegrationProperty SetConfigProperty SetProperty SettingsProperty SharingProperty SqlReadWriteProperty SqlSecurityProperty StabilityProperty Stream StreamingTableProperty StrictProperty SwapTable TableColumn Tags TemporaryProperty TitleColumnConstraint ToMap ToTableProperty TransformModelProperty TransientProperty VirtualProperty TriggerExecute Union UnloggedProperty UsingTemplateProperty UsingData UppercaseColumnConstraint UtcDate UtcTime UtcTimestamp Variadic VarMap ViewAttributeProperty VolatileProperty WithJournalTableProperty WithProcedureOptions WithSchemaBindingProperty WithOperator ForceProperty
        """;

    private const string _build_dispatch__postgres_transforms = """
        AnyValue ArrayConcat ArrayFilter ArrayAppend ArrayPrepend BitwiseAndAgg BitwiseOrAgg BitwiseXor BitwiseXorAgg ColumnDef CurrentDate CurrentTimestamp CurrentUser CurrentVersion DateAdd DateDiff DateStrToDate DateSub DatetimeAdd DatetimeSub Day Explode ExplodingGenerateSeries GenerateSeries Getbit GroupConcat IntDiv JSONArrayAgg JSONExtract JSONExtractScalar JSONBExtract JSONBExtractScalar ParseJSON JSONPathKey JSONPathRoot JSONPathSubscript LastDay LogicalOr LogicalAnd Max MapFromEntries Min Merge Month PartitionedByProperty PercentileCont PercentileDisc Pivot Rand RegexpILike RegexpReplace Round Select SHA2 SHA2Digest StrPosition StrToDate StrToTime StructExtract Substring TimeFromParts TimestampFromParts TimestampTrunc TimestampAdd TimestampSub TimeStrToTime TimeToStr ToChar Trim TryCast TsOrDsAdd TsOrDsDiff UnixToTime Uuid TimeToUnix VariancePop Variance Xor Year Unicode Levenshtein JSONObjectAgg JSONBObjectAgg CountIf FileFormatProperty TableSpaceProperty
        """;

    private const string _build_dispatch__postgres_handlers = """
        lateral bytestring schemacommentproperty commentcolumnconstraint columndef unnest bracket matchagainst alterset datatype cast array interval arraycontains
        """;

    private const string _build_dispatch__handlers = """
        uncache cache characterset column pseudocolumn columnposition columndef columnconstraint computedcolumnconstraint autoincrementcolumnconstraint compresscolumnconstraint generatedasidentitycolumnconstraint generatedasrowcolumnconstraint periodforsystemtimeconstraint notnullcolumnconstraint primarykeycolumnconstraint uniquecolumnconstraint inoutcolumnconstraint createable create sequenceproperties triggerproperties triggerreferencing triggerevent clone describe heredoc with cte tablealias bitstring hexstring bytestring unicodestring rawstring datatypeparam datatype directory delete drop fetch limitoptions filter per hint indexparameters index dynamicidentifier identifier hex lowerhex inputoutputformat national partition properties property uuidproperty likeproperty fallbackproperty journalproperty freespaceproperty checksumproperty mergeblockratioproperty moduleproperty datablocksizeproperty blockcompressionproperty isolatedloadingproperty partitionboundspec partitionedofproperty lockingproperty lockingstatement withdataproperty withsystemversioningproperty insert introducer kill pseudotype objectidentifier onconflict returning rowformatdelimitedproperty withtablehint indextablehint historicaldata table tablefromrows tablesample pivot version tuple update values var into from groupingsets rollup rollupindex rollupproperty cube group having connectto disconnect connect prior join nearest lambda lateral limit offset setitem set queryband pragma lock literal loaddata null boolean booland boolor order withfill cluster clusterproperty distribute sort ordered matchrecognizemeasure matchrecognize forclause queryoption select schema schema_columns star parameter sessionparameter placeholder subquery qualify unnest prewhere where window partition_by partitionbyexcluding windowspec withingroup between bracket all any exists case constraint nextvaluefor extract trim concat concatws check foreignkey primarykey timeserieskey if matchagainst jsonkeyvalue jsonpath formatjson formatphrase jsonarray jsonarrayagg jsoncolumndef jsonschema jsontable openjsoncolumndef openjson in interval return reference anonymous paren neg not alias pivotalias aliases atindex attimezone fromtimezone fromiso8601date fromiso8601timestamp fromiso8601timestampnanos add and or xor connector bitwiseand bitwiseleftshift bitwisenot bitwiseor bitwiserightshift bitwisexor cast strtotime strtodate parsedatetime currentdate collate command comment mergetreettlaction mergetreettl transaction commit rollback altercolumn modifycolumn alterindex alterdiststyle altersortkey alterrename renamecolumn alterset alter altersession add_column droppartition dropprimarykey addconstraint replacecolumns addpartition distinct ignorenulls respectnulls havingmax intdiv dpipe div safedivide overlaps distance distancend dot eq propertyeq escape glob gt gte is like ilike match similarto lt lte mod mul neq nullsafeeq nullsafeneq sub trycast jsoncast try log use function_fallback tag token userdefinedfunction macrooverloads macrooverload joinhint kwarg when whens merge tochar tonumber dictproperty dictrange dictsubproperty aggregatekeyproperty duplicatekeyproperty uniquekeyproperty distributedbyproperty oncluster clusteredbyproperty anyvalue querytransform indexconstraintoption checkcolumnconstraint indexcolumnconstraint nvl2 nthvalue comprehension columnprefix opclass predict generateembedding generatetext generatetable generatebool generateint generatedouble mltranslate mlforecast aiforecast featuresattime vectorsearch forin refresh toarray tsordstotime tsordstotimestamp tsordstodatetime tsordstodate unixdate lastday dateadd arrayinsert arrayany struct partitionrange truncatetable convert copyparameter credentials copy semicolon datadeletionproperty maskingpolicycolumnconstraint gapfill scoperesolution parsejson rand changes pad summarize explodinggenerateseries converttimezone json jsonvalue skipjsoncolumn conditionalinsert multitableinserts oncondition jsonextractquote jsonexists arrayagg slice apply grant revoke grantprivilege grantprincipal columns overlay todouble string median overflowtruncatebehavior unixseconds arraysort arraysize attach detach attachoption watermarkcolumnconstraint encodeproperty includeproperty xmlelement xmlkeyvalueoption partitionbyrangeproperty partitionbyrangepropertydynamic unpivotcolumns analyzesample analyzestatistics analyzehistogram analyzedelete analyzelistchainedrows analyzevalidate analyze xmltable xmlnamespace export declare declareitem recursivewithsearch parameterizedagg anonymousaggfunc combinedaggfunc combinedparameterizedagg show install liststage get_put translatecharacters decodecase semanticview getextract datefromunixdate space buildproperty refreshtriggerproperty modelattribute directorystage uuid initcap localtime localtimestamp weekstart chr weightstring block functionspecification storedprocedure ifblock casestatement whileblock loopblock repeatblock leave iterate execute executesql altermodifysqlsecurity usingproperty renameindex
        """;

    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.sanitize_comment #f272fd2bc0
        public virtual string sanitize_comment(string comment)
        {
            comment = Strip(comment[0].ToString()).Length != 0 ? " " + comment : comment;
            comment = Strip(comment[^1].ToString()).Length != 0 ? comment + " " : comment;
            // Escape block comment markers to prevent premature closure or unintended nesting.
            // This is necessary because single-line comments (--) are converted to block comments
            // (/* */) on output, and any */ in the original text would close the comment early.
            comment = comment.Replace("*/", "* /", StringComparison.Ordinal).Replace("/*", "/ *", StringComparison.Ordinal);
            return comment;
        }

        // py: sqlglot/generator.py::Generator.maybe_comment #a5607fa445
        public virtual string maybe_comment(string sql, exp.Expr? expression = null,
            List<string>? comments = null, bool separated = false)
        {
            comments = this.comments ? comments ?? expression?.comments : null;
            var excluded = EXCLUDE_COMMENTS;
            if (comments is not { Count: > 0 } || expression is not null &&
                (excluded.Item1.IsInstanceOfType(expression) || excluded.Item2.IsInstanceOfType(expression))) return sql;

            var comments_list = comments.Where(comment => comment.Length != 0)
                .Select(comment => $"/*{_replace_line_breaks(sanitize_comment(comment))}*/").ToList();
            if (comments_list.Count == 0) return sql;
            if (separated || expression is not null && WITH_SEPARATED_COMMENTS.Any(type => type.IsInstanceOfType(expression)))
            {
                var comments_sql = string.Join(sep(), comments_list);
                return sql.Length == 0 || Strip(sql[0].ToString()).Length == 0
                    ? $"{sep()}{comments_sql}{sql}" : $"{comments_sql}{sep()}{sql}";
            }
            return $"{sql} {string.Join(" ", comments_list)}";
        }
    }
}
