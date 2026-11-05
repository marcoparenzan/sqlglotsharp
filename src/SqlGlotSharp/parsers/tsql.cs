using System.Text.RegularExpressions;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;
using Parser = SqlGlotSharp.parser.Parser;

namespace SqlGlotSharp.parsers;

// Logic of sqlglot/parsers/tsql.py; the class-level data tables live in tsql.data.g.cs.
public static partial class tsql
{
    public partial class TSQLParser
    {
        // py: sqlglot/parsers/tsql.py::FULL_FORMAT_TIME_MAPPING
        internal static readonly PyDict<string, string> FULL_FORMAT_TIME_MAPPING = new()
        {
            ["weekday"] = "%A", ["dw"] = "%A", ["w"] = "%A", ["month"] = "%B", ["mm"] = "%B", ["m"] = "%B",
        };

        // py: sqlglot/parsers/tsql.py::DATE_DELTA_INTERVAL
        internal static readonly PyDict<string, string> DATE_DELTA_INTERVAL = new()
        {
            ["year"] = "year", ["yyyy"] = "year", ["yy"] = "year",
            ["quarter"] = "quarter", ["qq"] = "quarter", ["q"] = "quarter",
            ["month"] = "month", ["mm"] = "month", ["m"] = "month",
            ["week"] = "week", ["ww"] = "week", ["wk"] = "week",
            ["day"] = "day", ["dd"] = "day", ["d"] = "day",
        };

        private static readonly Regex DATE_FMT_RE = new("([dD]{1,2})|([mM]{1,2})|([yY]{1,4})|([hH]{1,2})|([sS]{1,2})|([fF]{2,7})");

        // N = Numeric, C=Currency
        private static readonly HashSet<string> TRANSPILE_SAFE_NUMBER_FMT = ["N", "C"];

        private static readonly DateTime DEFAULT_START_DATE = new(1900, 1, 1);

        private static readonly List<object?> NoArgs = new();

        // Unsupported options:
        // - OPTIMIZE FOR ( @variable_name { UNKNOWN | = <literal_constant> } [ , ...n ] )
        // - TABLE HINT
        // py: sqlglot/parsers/tsql.py::OPTIONS
        internal static readonly PyDict<string, List<object?>> OPTIONS = BuildOptions();

        private static PyDict<string, List<object?>> BuildOptions()
        {
            var options = new PyDict<string, List<object?>>();
            foreach (var key in new[] { "DISABLE_OPTIMIZED_PLAN_FORCING", "FAST", "IGNORE_NONCLUSTERED_COLUMNSTORE_INDEX",
                "LABEL", "MAXDOP", "MAXRECURSION", "MAX_GRANT_PERCENT", "MIN_GRANT_PERCENT", "NO_PERFORMANCE_SPOOL",
                "QUERYTRACEON", "RECOMPILE" })
                options[key] = new List<object?>();
            options["CONCAT"] = ["UNION"];
            options["DISABLE"] = ["EXTERNALPUSHDOWN", "SCALEOUTEXECUTION"];
            options["EXPAND"] = ["VIEWS"];
            options["FORCE"] = ["EXTERNALPUSHDOWN", "ORDER", "SCALEOUTEXECUTION"];
            options["HASH"] = ["GROUP", "JOIN", "UNION"];
            options["KEEP"] = ["PLAN"];
            options["KEEPFIXED"] = ["PLAN"];
            options["LOOP"] = ["JOIN"];
            options["MERGE"] = ["JOIN", "UNION"];
            options["OPTIMIZE"] = [new List<string> { "FOR", "UNKNOWN" }];
            options["ORDER"] = ["GROUP"];
            options["PARAMETERIZATION"] = ["FORCED", "SIMPLE"];
            options["ROBUST"] = ["PLAN"];
            options["USE"] = ["PLAN"];
            return options;
        }

        // py: sqlglot/parsers/tsql.py::FOR_XML_OPTIONS
        internal static readonly PyDict<string, List<string>> FOR_XML_OPTIONS = new()
        {
            ["AUTO"] = [], ["EXPLICIT"] = [], ["TYPE"] = [],
            ["ELEMENTS"] = ["XSINIL", "ABSENT"],
            ["BINARY"] = ["BASE64"],
        };

        // FOR JSON { AUTO | PATH } [, ROOT [ ( 'name' ) ] ] [, INCLUDE_NULL_VALUES ] [, WITHOUT_ARRAY_WRAPPER ]
        // py: sqlglot/parsers/tsql.py::FOR_JSON_OPTIONS
        internal static readonly PyDict<string, List<string>> FOR_JSON_OPTIONS = new()
        {
            ["AUTO"] = [], ["PATH"] = [], ["INCLUDE_NULL_VALUES"] = [], ["WITHOUT_ARRAY_WRAPPER"] = [],
        };

        // ------------------------------------------------------------------ builders

        private static object? Arg(IReadOnlyList<object?> args, int index) => index < args.Count ? args[index] : null;

        // py: sqlglot/expressions/builders.py::cast
        private static exp.Expr CastTo(object? value, exp.DType to) =>
            generators.dialect_helpers.cast(value, exp.DataType.build(to));

        // py: sqlglot/parsers/tsql.py::_build_formatted_time
        private static Func<IReadOnlyList<object?>, exp.Expr> _build_formatted_time<T>(bool full_format_mapping = false)
            where T : exp.Expr => args =>
        {
            var fmt = Arg(args, 0);
            if (fmt is exp.Expr format)
            {
                var mapping = new PyDict<string, string>();
                foreach (var (k, v) in dialects.tsql.TSQL.TIME_MAPPING_) mapping[k] = v;
                if (full_format_mapping) foreach (var (k, v) in FULL_FORMAT_TIME_MAPPING) mapping[k] = v;
                fmt = exp.Literal.@string(time.format_time(py.lower(format.name), mapping));
            }

            var @this = Arg(args, 1);
            if (@this is exp.Expr) @this = CastTo(@this, exp.DType.DATETIME2);

            return Mk<T>(("this", @this), ("format", fmt));
        };

        // py: sqlglot/parsers/tsql.py::_build_format
        private static exp.Expr _build_format(IReadOnlyList<object?> args)
        {
            var @this = Arg(args, 0);
            var fmt = Arg(args, 1) as exp.Expr;
            var culture = Arg(args, 2);

            var number_fmt = fmt is not null && (TRANSPILE_SAFE_NUMBER_FMT.Contains(fmt.name) || !DATE_FMT_RE.IsMatch(fmt.name));

            if (number_fmt)
                return Mk<exp.NumberToStr>(("this", @this), ("format", fmt), ("culture", culture));

            object? format = fmt;
            if (fmt is not null)
                format = exp.Literal.@string(fmt.name.Length == 1
                    ? time.format_time(fmt.name, dialects.tsql.TSQL.FORMAT_TIME_MAPPING_)
                    : time.format_time(fmt.name, dialects.tsql.TSQL.TIME_MAPPING_));

            return Mk<exp.TimeToStr>(("this", @this), ("format", format), ("culture", culture));
        }

        // py: sqlglot/parsers/tsql.py::_build_eomonth
        private static exp.Expr _build_eomonth(IReadOnlyList<object?> args)
        {
            var date = Mk<exp.TsOrDsToDate>(("this", Arg(args, 0)));
            var month_lag = Arg(args, 1);

            exp.Expr @this;
            if (month_lag is null) @this = date;
            else
            {
                var unit = DATE_DELTA_INTERVAL.get("month");
                @this = Mk<exp.DateAdd>(("this", date), ("expression", month_lag),
                    ("unit", unit is not null ? new exp.Var(@this: unit) : null));
            }

            return Mk<exp.LastDay>(("this", @this));
        }

        // py: sqlglot/parsers/tsql.py::_build_hashbytes
        private static exp.Expr _build_hashbytes(IReadOnlyList<object?> argsIn)
        {
            // upstream pops the algorithm from the caller's list so the argument-count check sees one argument
            var args = argsIn as List<object?> ?? argsIn.ToList();
            var kindExpr = (exp.Expr)args[0]!;
            var data = args[1];
            var kind = kindExpr.is_string ? py.upper(kindExpr.name) : "";

            if (kind == "MD5") { args.RemoveAt(0); return Mk<exp.MD5>(("this", data)); }
            if (kind is "SHA" or "SHA1") { args.RemoveAt(0); return Mk<exp.SHA>(("this", data)); }
            if (kind == "SHA2_256") return Mk<exp.SHA2>(("this", data), ("length", exp.Literal.number(256)));
            if (kind == "SHA2_512") return Mk<exp.SHA2>(("this", data), ("length", exp.Literal.number(512)));

            return Mk<exp.Anonymous>(("this", "HASHBYTES"), ("expressions", args));
        }

        // py: sqlglot/parsers/tsql.py::_build_date_delta
        private static Func<IReadOnlyList<object?>, exp.Expr> _build_date_delta<T>(
            PyDict<string, string>? unit_mapping = null, bool big_int = false) where T : exp.Expr => args =>
        {
            var unit = Arg(args, 0) as exp.Expr;
            if (unit is not null && unit_mapping is not null)
                unit = new exp.Var(@this: unit_mapping.get(py.lower(unit.name)) ?? unit.name);

            var start_date = Arg(args, 1) as exp.Expr;
            if (start_date is not null && start_date.is_number)
            {
                // Numeric types are valid DATETIME values
                if (start_date.is_int)
                {
                    var adds = DEFAULT_START_DATE.AddDays(Convert.ToDouble(start_date.to_py()));
                    start_date = exp.Literal.@string(adds.ToString("yyyy-MM-dd"));
                }
                else
                {
                    // We currently don't handle float values, i.e. they're not converted to equivalent DATETIMEs.
                    // This is not a problem when generating T-SQL code, it is when transpiling to other dialects.
                    return Mk<T>(("this", Arg(args, 2)), ("expression", start_date), ("unit", unit), ("big_int", big_int));
                }
            }

            return Mk<T>(("this", Mk<exp.TimeStrToTime>(("this", Arg(args, 2)))),
                ("expression", Mk<exp.TimeStrToTime>(("this", start_date))), ("unit", unit), ("big_int", big_int));
        };

        // py: sqlglot/parsers/tsql.py::_build_datetimefromparts
        private static exp.Expr _build_datetimefromparts(IReadOnlyList<object?> args) =>
            Mk<exp.TimestampFromParts>(("year", Arg(args, 0)), ("month", Arg(args, 1)), ("day", Arg(args, 2)),
                ("hour", Arg(args, 3)), ("min", Arg(args, 4)), ("sec", Arg(args, 5)), ("milli", Arg(args, 6)));

        // py: sqlglot/parsers/tsql.py::_build_timefromparts
        private static exp.Expr _build_timefromparts(IReadOnlyList<object?> args) =>
            Mk<exp.TimeFromParts>(("hour", Arg(args, 0)), ("min", Arg(args, 1)), ("sec", Arg(args, 2)),
                ("fractions", Arg(args, 3)), ("precision", Arg(args, 4)));

        // py: sqlglot/parsers/tsql.py::_build_with_arg_as_text
        private static Func<IReadOnlyList<object?>, exp.Expr> _build_with_arg_as_text<T>() where T : exp.Expr => args =>
        {
            var @this = Arg(args, 0);
            if (@this is exp.Expr e && !e.is_string) @this = CastTo(@this, exp.DType.TEXT);

            var expression = Arg(args, 1);
            return expression is not null
                ? Mk<T>(("this", @this), ("expression", expression))
                : Mk<T>(("this", @this));
        };

        // py: sqlglot/parsers/tsql.py::_build_parsename
        private static exp.Expr _build_parsename(IReadOnlyList<object?> args)
        {
            // PARSENAME(...) will be stored into exp.SplitPart if:
            // - All args are literals
            // - The part index (2nd arg) is <= 4 (max valid value, otherwise TSQL returns NULL)
            if (args.Count == 2 && args.All(a => a is exp.Literal))
            {
                var @this = (exp.Literal)args[0]!;
                var part_index = (exp.Literal)args[1]!;
                var split_count = @this.name.Split('.').Length;
                if (split_count <= 4)
                    return Mk<exp.SplitPart>(("this", @this), ("delimiter", exp.Literal.@string(".")),
                        ("part_index", exp.Literal.number(split_count + 1 - Convert.ToInt64(part_index.to_py()))));
            }

            return Mk<exp.Anonymous>(("this", "PARSENAME"), ("expressions", args.ToList()));
        }

        // py: sqlglot/parsers/tsql.py::_build_json_query
        private static exp.Expr _build_json_query(IReadOnlyList<object?> argsIn)
        {
            var args = argsIn.ToList();
            if (args.Count == 1)
                // The default value for path is '$'. As a result, if you don't provide a
                // value for path, JSON_QUERY returns the input expression.
                args.Add(exp.Literal.@string("$"));

            var json_extract = Parser.build_extract_json_with_path<exp.JSONExtract>()(args);
            json_extract.set("json_query", true);
            return json_extract;
        }

        // py: sqlglot/parsers/tsql.py::_build_datetrunc
        private static exp.Expr _build_datetrunc(IReadOnlyList<object?> args)
        {
            var unit = Arg(args, 0);
            var @this = Arg(args, 1);

            if (@this is exp.Expr e && e.is_string) @this = CastTo(@this, exp.DType.DATETIME2);

            return Mk<exp.TimestampTrunc>(("this", @this), ("unit", unit));
        }

        private static exp.Expr TsOrDsDefault(IReadOnlyList<object?> args) =>
            Mk<exp.TsOrDsToDate>(("this", Arg(args, 0)), ("default_date", exp.Literal.@string("1900-01-01")));

        // py: sqlglot/parsers/tsql.py::TSQLParser.FUNCTIONS
        public override PyDict<string, Func<IReadOnlyList<object?>, exp.Expr>> FUNCTIONS { get; } = Functions();
        private static PyDict<string, Func<IReadOnlyList<object?>, exp.Expr>> Functions()
        {
            var table = new PyDict<string, Func<IReadOnlyList<object?>, exp.Expr>>();
            foreach (var (key, value) in Parser.FUNCTIONS_) table[key] = value;
            table["ATN2"] = new exp.Atan2().from_arg_list;
            table["CHARINDEX"] = args => Mk<exp.StrPosition>(("this", Arg(args, 1)), ("substr", Arg(args, 0)), ("position", Arg(args, 2)));
            table["COUNT"] = args => Mk<exp.Count>(("this", Arg(args, 0)), ("expressions", args.Skip(1).ToList()), ("big_int", false));
            table["COUNT_BIG"] = args => Mk<exp.Count>(("this", Arg(args, 0)), ("expressions", args.Skip(1).ToList()), ("big_int", true));
            table["DATEADD"] = dialects.dialect.build_date_delta(typeof(exp.DateAdd), unit_mapping: DATE_DELTA_INTERVAL);
            table["DATEDIFF"] = _build_date_delta<exp.DateDiff>(DATE_DELTA_INTERVAL);
            table["DATEDIFF_BIG"] = _build_date_delta<exp.DateDiff>(DATE_DELTA_INTERVAL, big_int: true);
            table["DATENAME"] = _build_formatted_time<exp.TimeToStr>(full_format_mapping: true);
            table["DATETIMEFROMPARTS"] = _build_datetimefromparts;
            table["DAY"] = args => Mk<exp.Day>(("this", TsOrDsDefault(args)));
            table["EOMONTH"] = _build_eomonth;
            table["FORMAT"] = _build_format;
            table["GETDATE"] = new exp.CurrentTimestamp().from_arg_list;
            table["HASHBYTES"] = _build_hashbytes;
            table["ISNULL"] = args => Parser.build_coalesce(args, is_null: true);
            table["JSON_QUERY"] = _build_json_query;
            table["JSON_VALUE"] = Parser.build_extract_json_with_path<exp.JSONExtractScalar>();
            table["LEN"] = _build_with_arg_as_text<exp.Length>();
            table["LEFT"] = _build_with_arg_as_text<exp.Left>();
            table["MONTH"] = args => Mk<exp.Month>(("this", TsOrDsDefault(args)));
            table["NEWID"] = new exp.Uuid().from_arg_list;
            table["RIGHT"] = _build_with_arg_as_text<exp.Right>();
            table["PARSENAME"] = _build_parsename;
            table["REPLICATE"] = new exp.Repeat().from_arg_list;
            table["SCHEMA_NAME"] = new exp.CurrentSchema().from_arg_list;
            table["SQUARE"] = args => Mk<exp.Pow>(("this", Arg(args, 0)), ("expression", exp.Literal.number(2)));
            table["SYSDATETIME"] = new exp.CurrentTimestamp().from_arg_list;
            table["SUSER_NAME"] = new exp.CurrentUser().from_arg_list;
            table["SUSER_SNAME"] = new exp.CurrentUser().from_arg_list;
            table["SYSDATETIMEOFFSET"] = new exp.CurrentTimestampLTZ().from_arg_list;
            table["SYSTEM_USER"] = new exp.CurrentUser().from_arg_list;
            table["TIMEFROMPARTS"] = _build_timefromparts;
            table["DATETRUNC"] = _build_datetrunc;
            table["YEAR"] = args => Mk<exp.Year>(("this", TsOrDsDefault(args)));
            return table;
        }

        // py: sqlglot/parsers/tsql.py::TSQLParser.QUERY_MODIFIER_PARSERS
        public override PyDict<TokenType, Func<Parser, (string, object?)>> QUERY_MODIFIER_PARSERS { get; } = QueryModifiers();
        private static PyDict<TokenType, Func<Parser, (string, object?)>> QueryModifiers()
        {
            var table = new PyDict<TokenType, Func<Parser, (string, object?)>>();
            foreach (var (key, value) in Parser.QUERY_MODIFIER_PARSERS_) table[key] = value;
            table[TokenType.OPTION] = self => ("options", ((TSQLParser)self)._parse_options());
            table[TokenType.FOR] = self => ("for_", ((TSQLParser)self)._parse_for());
            return table;
        }

        // py: sqlglot/parsers/tsql.py::TSQLParser.STATEMENT_PARSERS
        public override PyDict<TokenType, Func<Parser, exp.Expr>> STATEMENT_PARSERS { get; } = Statements();
        private static PyDict<TokenType, Func<Parser, exp.Expr>> Statements()
        {
            var table = new PyDict<TokenType, Func<Parser, exp.Expr>>();
            foreach (var (key, value) in Parser.STATEMENT_PARSERS_) table[key] = value;
            table[TokenType.EXECUTE] = self => ((TSQLParser)self)._parse_execute();
            return table;
        }

        // py: sqlglot/parsers/tsql.py::TSQLParser.RANGE_PARSERS
        public override PyDict<TokenType, Func<Parser, exp.Expr?, exp.Expr?>> RANGE_PARSERS { get; } = Ranges();
        private static PyDict<TokenType, Func<Parser, exp.Expr?, exp.Expr?>> Ranges()
        {
            var table = new PyDict<TokenType, Func<Parser, exp.Expr?, exp.Expr?>>();
            foreach (var (key, value) in Parser.RANGE_PARSERS_) table[key] = value;
            table[TokenType.DCOLON] = (self, @this) => self.expression(Mk<exp.ScopeResolution>(("this", @this),
                ("expression", self._parse_function() ?? self._parse_var(any_token: true))));
            return table;
        }

        // py: sqlglot/parsers/tsql.py::TSQLParser.NO_PAREN_FUNCTION_PARSERS
        public override PyDict<string, Func<Parser, exp.Expr?>> NO_PAREN_FUNCTION_PARSERS { get; } = NoParen();
        private static PyDict<string, Func<Parser, exp.Expr?>> NoParen()
        {
            var table = new PyDict<string, Func<Parser, exp.Expr?>>();
            foreach (var (key, value) in Parser.NO_PAREN_FUNCTION_PARSERS_) table[key] = value;
            table["NEXT"] = self => self._parse_next_value_for();
            return table;
        }

        // py: sqlglot/parsers/tsql.py::TSQLParser.FUNCTION_PARSERS
        public override PyDict<string, Func<Parser, exp.Expr?>> FUNCTION_PARSERS { get; } = FunctionParsers();
        private static PyDict<string, Func<Parser, exp.Expr?>> FunctionParsers()
        {
            var table = new PyDict<string, Func<Parser, exp.Expr?>>();
            foreach (var (key, value) in Parser.FUNCTION_PARSERS_) table[key] = value;
            table["JSON_ARRAYAGG"] = self => self.expression(Mk<exp.JSONArrayAgg>(("this", self._parse_bitwise()),
                ("order", self._parse_order()), ("null_handling", self._parse_on_handling("NULL", "NULL", "ABSENT"))));
            table["DATEPART"] = self => ((TSQLParser)self)._parse_datepart();
            return table;
        }

        // The DCOLON (::) operator serves as a scope resolution (exp.ScopeResolution) operator in T-SQL
        // py: sqlglot/parsers/tsql.py::TSQLParser.COLUMN_OPERATORS
        public override PyDict<TokenType, Func<Parser, exp.Expr?, exp.Expr?, exp.Expr?>?> COLUMN_OPERATORS { get; } = ColumnOperators();
        private static PyDict<TokenType, Func<Parser, exp.Expr?, exp.Expr?, exp.Expr?>?> ColumnOperators()
        {
            var table = new PyDict<TokenType, Func<Parser, exp.Expr?, exp.Expr?, exp.Expr?>?>();
            foreach (var (key, value) in Parser.COLUMN_OPERATORS_) table[key] = value;
            table[TokenType.DCOLON] = (self, @this, to) =>
                to is exp.DataType dt && !Equals(dt.@this, exp.DType.USERDEFINED)
                    ? self.expression(Mk<exp.Cast>(("this", @this), ("to", to)))
                    : self.expression(Mk<exp.ScopeResolution>(("this", @this), ("expression", to)));
            return table;
        }

        // ------------------------------------------------------------------ methods

        // py: sqlglot/parsers/tsql.py::TSQLParser._parse_execute
        public exp.Expr _parse_execute()
        {
            object? return_status = null;
            var index = _index;
            if (_match(TokenType.PARAMETER))
            {
                var param = _parse_parameter();
                if (_match(TokenType.EQ)) return_status = param;
                else _retreat(index);
            }

            var execute = expression(Mk<exp.Execute>(("this", _parse_table(schema: true)),
                ("expressions", _parse_csv(() => _parse_expression()).Cast<object?>().ToList()),
                ("return_status", return_status)));

            if (py.lower(execute.name) == "sp_executesql")
            {
                var pairs = execute.args.Select(kv => (kv.Key, kv.Value)).ToArray();
                execute = expression(Mk<exp.ExecuteSql>(pairs));
            }

            return execute;
        }

        // py: sqlglot/parsers/tsql.py::TSQLParser._parse_datepart
        public exp.Expr _parse_datepart()
        {
            var @this = _parse_var(tokens: [TokenType.IDENTIFIER]);
            var expression_ = _match(TokenType.COMMA) ? _parse_bitwise() : null;
            var name = dialects.dialect.map_date_part(@this, dialect);

            return expression(Mk<exp.Extract>(("this", name), ("expression", expression_)));
        }

        // py: sqlglot/parsers/tsql.py::TSQLParser._parse_alter_table_set
        public override exp.AlterSet _parse_alter_table_set() => _parse_wrapped(() => base._parse_alter_table_set());

        // py: sqlglot/parsers/tsql.py::TSQLParser._parse_wrapped_select
        public override exp.Expr? _parse_wrapped_select(bool table = false)
        {
            if (_match(TokenType.MERGE))
            {
                var comments = _prev_comments;
                var merge = _parse_merge();
                merge.add_comments(comments, prepend: true);
                return merge;
            }

            return base._parse_wrapped_select(table);
        }

        // py: sqlglot/parsers/tsql.py::TSQLParser._parse_dcolon
        public override exp.Expr? _parse_dcolon()
        {
            // We want to use _parse_types() if the first token after :: is a known type,
            // otherwise we could parse something like x::varchar(max) into a function
            if (_match_set(TYPE_TOKENS, advance: false)) return _parse_types();

            return _parse_function() ?? _parse_types();
        }

        // py: sqlglot/parsers/tsql.py::TSQLParser._parse_options
        public List<object?>? _parse_options()
        {
            if (!_match(TokenType.OPTION)) return null;

            exp.Expr? parse_option()
            {
                var option = _parse_var_from_options(OPTIONS);
                if (option is null) return null;

                _match(TokenType.EQ);
                return expression(Mk<exp.QueryOption>(("this", option), ("expression", _parse_primary_or_var())));
            }

            return _parse_wrapped_csv(parse_option).Cast<object?>().ToList();
        }

        // py: sqlglot/parsers/tsql.py::TSQLParser._parse_key_value_option
        public exp.XMLKeyValueOption _parse_key_value_option()
        {
            var @this = _parse_primary_or_var();
            exp.Expr? expression_ = _match(TokenType.L_PAREN, advance: false)
                ? _parse_wrapped(() => _parse_string())
                : null;

            return Mk<exp.XMLKeyValueOption>(("this", @this), ("expression", expression_));
        }

        // py: sqlglot/parsers/tsql.py::TSQLParser._parse_for_clause_option
        public exp.Expr? _parse_for_clause_option(PyDict<string, List<string>> options) =>
            expression(Mk<exp.QueryOption>(("this",
                (object?)_parse_var_from_options(options, raise_unmatched: false) ?? _parse_key_value_option())));

        // py: sqlglot/parsers/tsql.py::TSQLParser._parse_for
        public exp.Expr? _parse_for()
        {
            if (_match_pair(TokenType.FOR, TokenType.XML))
                return expression(Mk<exp.ForClause>(("kind", "XML"),
                    ("expressions", _parse_csv(() => _parse_for_clause_option(FOR_XML_OPTIONS)).Cast<object?>().ToList())));

            if (_match_pair(TokenType.FOR, TokenType.JSON))
                return expression(Mk<exp.ForClause>(("kind", "JSON"),
                    ("expressions", _parse_csv(() => _parse_for_clause_option(FOR_JSON_OPTIONS)).Cast<object?>().ToList())));

            // FOR BROWSE — bare keyword, no options. BROWSE has no dedicated TokenType.
            if (_match_text_seq(["FOR", "BROWSE"]))
                return expression(Mk<exp.ForClause>(("kind", "BROWSE")));

            return null;
        }

        /// <summary>
        /// T-SQL supports the syntax alias = expression in the SELECT's projection list,
        /// so we transform all parsed Selects to convert their EQ projections into Aliases.
        /// </summary>
        // py: sqlglot/parsers/tsql.py::TSQLParser._parse_projections
        public override (List<exp.Expr>, List<exp.Expr>?) _parse_projections()
        {
            var (projections, _) = base._parse_projections();
            return (projections.Select(projection =>
                projection is exp.EQ eq && eq.@this is exp.Column column
                    ? alias_(eq.expression, column.@this)
                    : projection).ToList(), null);
        }

        private static exp.Expr alias_(object? expression_, object? alias) =>
            generator.Generator.alias_((exp.Expr)expression_!, alias);

        // py: sqlglot/parsers/tsql.py::TSQLParser._parse_commit_or_rollback
        public override exp.Expr _parse_commit_or_rollback()
        {
            var rollback = _prev.token_type == TokenType.ROLLBACK;

            _match_texts(["TRAN", "TRANSACTION"]);
            var @this = _parse_id_var();

            if (rollback) return expression(Mk<exp.Rollback>(("this", @this)));

            object? durability = null;
            if (_match_pair(TokenType.WITH, TokenType.L_PAREN))
            {
                _match_text_seq(["DELAYED_DURABILITY"]);
                _match(TokenType.EQ);

                if (_match_text_seq(["OFF"])) durability = false;
                else
                {
                    _match(TokenType.ON);
                    durability = true;
                }

                _match_r_paren();
            }

            return expression(Mk<exp.Commit>(("this", @this), ("durability", durability)));
        }

        // py: sqlglot/parsers/tsql.py::TSQLParser._parse_transaction
        public override exp.Expr _parse_transaction()
        {
            if (_match_texts(["TRAN", "TRANSACTION"]))
            {
                var transaction = expression(Mk<exp.Transaction>(("this", _parse_id_var())));
                if (_match_text_seq(["WITH", "MARK"]))
                    transaction.set("mark", _parse_string());

                return transaction;
            }

            return _parse_as_command(_prev);
        }

        // py: sqlglot/parsers/tsql.py::TSQLParser._parse_returns
        public override exp.Expr _parse_returns()
        {
            var table = _parse_id_var(any_token: false, tokens: RETURNS_TABLE_TOKENS);
            var returns = base._parse_returns();
            returns.set("table", table);
            return returns;
        }

        // py: sqlglot/parsers/tsql.py::TSQLParser._parse_convert
        public override exp.Expr? _parse_convert(bool strict, bool? safe = null)
        {
            var @this = _parse_types();
            _match(TokenType.COMMA);
            var args = new List<object?> { @this };
            args.AddRange(_parse_csv(() => _parse_assignment()));
            var convert = new exp.Convert().from_arg_list(args);
            convert.set("safe", safe);
            return convert;
        }

        // py: sqlglot/parsers/tsql.py::TSQLParser._parse_column_def
        public override exp.Expr? _parse_column_def(exp.Expr? @this, bool computed_column = true)
        {
            @this = base._parse_column_def(@this, computed_column);
            if (@this is null) return null;
            if (_match(TokenType.EQ)) @this.set("default", _parse_disjunction());
            if (_match_texts(COLUMN_DEFINITION_MODES)) @this.set("output", _prev.text);
            return @this;
        }

        // py: sqlglot/parsers/tsql.py::TSQLParser._parse_user_defined_function
        public override exp.Expr? _parse_user_defined_function(TokenType? kind = null)
        {
            var @this = base._parse_user_defined_function(kind);

            if (kind == TokenType.FUNCTION || @this is exp.UserDefinedFunction) return @this;

            if (kind == TokenType.PROCEDURE && @this is not null)
            {
                var expressions = @this.expressions.Cast<object?>().ToList();
                if (expressions.Count == 0 && !_match_set([TokenType.ALIAS, TokenType.WITH], advance: false))
                    expressions = _parse_csv(() => _parse_function_parameter()).Cast<object?>().ToList();

                return expression(Mk<exp.StoredProcedure>(
                    ("this", @this is exp.Table ? @this : @this.@this),
                    ("expressions", expressions),
                    ("wrapped", @this.args.get("wrapped"))));
            }

            return expression(Mk<exp.UserDefinedFunction>(("this", @this)));
        }

        // py: sqlglot/parsers/tsql.py::TSQLParser._parse_into
        public override exp.Expr? _parse_into()
        {
            var into = base._parse_into();

            var table = into is exp.Into ? into.find(typeof(exp.Table)) : null;
            if (table is exp.Table t && t.@this is exp.Identifier table_identifier
                && py.truthy(table_identifier.args.get("temporary")))
                // Promote the temporary property from the Identifier to the Into expression
                into!.set("temporary", true);

            return into;
        }

        // py: sqlglot/parsers/tsql.py::TSQLParser._parse_id_var
        public override exp.Expr? _parse_id_var(bool any_token = true, IReadOnlyCollection<TokenType>? tokens = null)
        {
            var is_temporary = _match(TokenType.HASH);
            var is_global = is_temporary && _match(TokenType.HASH);

            var @this = base._parse_id_var(any_token, tokens);
            if (@this is not null)
            {
                if (is_global) @this.set("global_", true);
                else if (is_temporary) @this.set("temporary", true);
            }

            return @this;
        }

        // py: sqlglot/parsers/tsql.py::TSQLParser._parse_table_part
        public override exp.Expr? _parse_table_part(bool schema = false)
        {
            // Schema mode already skips function parsing; otherwise, '(' may start a bare table hint.
            if (!schema && _next.token_type == TokenType.L_PAREN)
            {
                var index = _index;
                _advance(2);
                var is_hint = _match_texts(BARE_TABLE_HINTS) && _match(TokenType.R_PAREN);
                _retreat(index);

                if (is_hint && _parse_id_var(any_token: false) is { } @this) return @this;
            }

            return base._parse_table_part(schema);
        }

        // py: sqlglot/expressions/query.py::Table.parts
        private static List<exp.Expr> TableParts(exp.Table table)
        {
            var parts = new List<exp.Expr>();
            foreach (var arg in new[] { "catalog", "db", "this" })
            {
                var part = table.args.get(arg);
                if (part is exp.Dot dot) parts.AddRange(dot.flatten());
                else if (part is exp.Expr e) parts.Add(e);
            }
            return parts;
        }

        // py: sqlglot/parsers/tsql.py::TSQLParser._parse_table
        public override exp.Expr? _parse_table(bool schema = false, bool joins = false,
            IReadOnlyCollection<TokenType>? alias_tokens = null, bool parse_bracket = false,
            bool is_db_reference = false, bool parse_partition = false, bool consume_pipe = false)
        {
            var table = base._parse_table(schema, joins, alias_tokens, parse_bracket, is_db_reference, parse_partition, consume_pipe);
            if (table is exp.Table t
                && !py.truthy(t.args.get("hints"))
                && t.args.get("alias") is exp.TableAlias alias
                && alias.args.get("columns") is List<object?> { Count: 1 } alias_columns
                && alias_columns[0] is exp.Identifier hint
                && !py.truthy(hint.args.get("quoted"))
                && BARE_TABLE_HINTS.Contains(py.upper(hint.name))
                && TableParts(t).All(part => part is exp.Identifier))
            {
                var var = new exp.Var(@this: hint.name);
                var.add_comments(hint.pop_comments());
                alias.set("columns", null);
                if (alias.@this is null)
                {
                    t.add_comments(alias.pop_comments());
                    t.set("alias", _parse_table_alias(alias_tokens));
                }
                t.set("hints", new List<object?> { new exp.WithTableHint(expressions: new List<object?> { var }) });
            }

            return table;
        }

        // py: sqlglot/parsers/tsql.py::TSQLParser._parse_table_parts
        public override exp.Expr? _parse_table_parts(bool schema = false, bool is_db_reference = false,
            bool wildcard = false, bool fast = false)
        {
            var table = base._parse_table_parts(schema, is_db_reference, wildcard, fast);
            if (table is exp.Table t && t.@this is exp.Identifier id && t.name.StartsWith('#'))
            {
                var table_name = t.name;
                if (table_name.StartsWith("##"))
                {
                    id.set("this", table_name[2..]);
                    id.set("global_", true);
                }
                else
                {
                    id.set("this", table_name[1..]);
                    id.set("temporary", true);
                }
            }

            return table;
        }

        // py: sqlglot/parsers/tsql.py::TSQLParser._parse_create
        public override exp.Expr _parse_create()
        {
            var create = base._parse_create();

            if (create is exp.Create)
            {
                var target = create.@this is exp.Schema ? ((exp.Expr)create.@this).@this : create.@this;
                if (target is exp.Table table && table.@this is exp.Expr tid && py.truthy(tid.args.get("temporary")))
                {
                    if (!py.truthy(create.args.get("properties")))
                        create.set("properties", new exp.Properties(expressions: new List<object?>()));

                    ((exp.Expr)create.args["properties"]!).append("expressions", new exp.TemporaryProperty());
                }
            }

            return create;
        }

        // py: sqlglot/parsers/tsql.py::TSQLParser._parse_if
        public override exp.Expr? _parse_if()
        {
            var @this = _parse_condition();
            var @true = _parse_block();

            object? @false = _match(TokenType.ELSE) ? _parse_block() : false;

            return expression(Mk<exp.IfBlock>(("this", @this), ("true", @true), ("false", @false)));
        }

        // py: sqlglot/parsers/tsql.py::TSQLParser._parse_unique
        public override exp.UniqueColumnConstraint _parse_unique()
        {
            exp.Expr? @this;
            if (_match_texts(["CLUSTERED", "NONCLUSTERED"]))
                @this = _parse_constraint_callback(py.upper(_prev.text));
            else
                @this = _parse_schema(_parse_id_var(any_token: false));

            return expression(Mk<exp.UniqueColumnConstraint>(("this", @this)));
        }

        // py: sqlglot/parsers/tsql.py::TSQLParser._parse_update
        public override exp.Update _parse_update()
        {
            var expression_ = base._parse_update();
            expression_.set("options", _parse_options());
            return expression_;
        }

        // py: sqlglot/parsers/tsql.py::TSQLParser._parse_partition
        public override exp.Expr? _parse_partition()
        {
            if (!_match_text_seq(["WITH", "(", "PARTITIONS"])) return null;

            exp.Expr? parse_range()
            {
                var low = _parse_bitwise();
                var high = _match_text_seq(["TO"]) ? _parse_bitwise() : null;

                return high is not null ? expression(Mk<exp.PartitionRange>(("this", low), ("expression", high))) : low;
            }

            var partition = expression(Mk<exp.Partition>(("expressions", _parse_wrapped_csv(parse_range).Cast<object?>().ToList())));

            _match_r_paren();

            return partition;
        }

        // py: sqlglot/parsers/tsql.py::TSQLParser._parse_alter_table_alter
        public override exp.Expr? _parse_alter_table_alter()
        {
            var expression_ = base._parse_alter_table_alter();

            if (expression_ is not null)
            {
                if (expression_.args.get("collate") is exp.Column collation && collation.@this is exp.Identifier identifier)
                    collation.set("this", new exp.Var(@this: identifier.name));

                if (py.truthy(expression_.args.get("dtype")))
                {
                    if (_match_pair(TokenType.NOT, TokenType.NULL)) expression_.set("allow_null", false);
                    else if (_match(TokenType.NULL)) expression_.set("allow_null", true);
                }
            }

            return expression_;
        }

        // py: sqlglot/parsers/tsql.py::TSQLParser._parse_primary_key_part
        public override exp.Expr? _parse_primary_key_part() => _parse_ordered();
    }
}
