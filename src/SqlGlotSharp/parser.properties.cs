using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

// Property parsing for CREATE/ALTER (parser.py: PROPERTY_PARSERS, _parse_properties, _parse_property).
// Entries whose upstream helper is not ported yet throw NotPorted with the property key; they never
// fall back to a different interpretation.
public static partial class parser
{
    public sealed record PropertyParser(Func<Parser, PyDict<string, object?>, object?> Parse, bool AcceptsKwargs = false);

    public partial class Parser
    {
        private static PropertyParser Prop(Func<Parser, object?> parse) => new((self, _) => parse(self));
        private static PropertyParser Flag<T>() where T : exp.Expr => Prop(self => self.expression(Mk<T>()));
        private static PropertyParser Assign<T>() where T : exp.Expr => Prop(self => self._parse_property_assignment<T>());
        private static PropertyParser Stability(string value) =>
            Prop(self => self.expression(Mk<exp.StabilityProperty>(("this", exp.Literal.@string(value)))));
        private static PropertyParser Unported(string key) =>
            new((_, _) => throw new NotPortedException($"sqlglot/parser.py::Parser.PROPERTY_PARSERS[{key}]"), true);

        // py: sqlglot/parser.py::Parser.PROPERTY_PARSERS #0000000000
        public static readonly PyDict<string, PropertyParser> PROPERTY_PARSERS_ = BuildPropertyParsers();
        public virtual PyDict<string, PropertyParser> PROPERTY_PARSERS => PROPERTY_PARSERS_;

        private static PyDict<string, PropertyParser> BuildPropertyParsers()
        {
            var table = new PyDict<string, PropertyParser>
            {
                ["ALLOWED_VALUES"] = Prop(self => self.expression(Mk<exp.AllowedValuesProperty>(("expressions", self._parse_csv<exp.Expr>(self._parse_primary).Cast<object?>().ToList())))),
                ["ALGORITHM"] = Assign<exp.AlgorithmProperty>(),
                ["AUTO_INCREMENT"] = Assign<exp.AutoIncrementProperty>(),
                ["BACKUP"] = Prop(self => self.expression(Mk<exp.BackupProperty>(("this", self._parse_var(any_token: true))))),
                ["CHARSET"] = new((self, kwargs) => self._parse_character_set(kwargs.ContainsKey("default") && py.truthy(kwargs["default"])), true),
                ["COLLATE"] = new((self, kwargs) => self._parse_property_assignment<exp.CollateProperty>(kwargs), true),
                ["COMMENT"] = Assign<exp.SchemaCommentProperty>(),
                ["DETERMINISTIC"] = Stability("IMMUTABLE"),
                ["DYNAMIC"] = Flag<exp.DynamicProperty>(),
                ["DISTSTYLE"] = Assign<exp.DistStyleProperty>(),
                ["EMPTY"] = Flag<exp.EmptyProperty>(),
                ["ENGINE"] = Assign<exp.EngineProperty>(),
                ["HANDLER"] = Assign<exp.HandlerProperty>(),
                ["EXECUTE"] = Assign<exp.ExecuteAsProperty>(),
                ["EXTERNAL"] = Flag<exp.ExternalProperty>(),
                ["FORMAT"] = Assign<exp.FileFormatProperty>(),
                ["GLOBAL"] = Flag<exp.GlobalProperty>(),
                ["HEAP"] = Flag<exp.HeapProperty>(),
                ["ICEBERG"] = Flag<exp.IcebergProperty>(),
                ["IMMUTABLE"] = Stability("IMMUTABLE"),
                ["INHERITS"] = Prop(self => self.expression(Mk<exp.InheritsProperty>(("expressions",
                    self._parse_wrapped_csv(() => self._parse_table()).Cast<object?>().ToList())))),
                ["LANGUAGE"] = Assign<exp.LanguageProperty>(),
                ["LIKE"] = Prop(self => self._parse_create_like()),
                ["LOCATION"] = Assign<exp.LocationProperty>(),
                ["MATERIALIZED"] = Flag<exp.MaterializedProperty>(),
                ["MULTISET"] = Prop(self => self.expression(Mk<exp.SetProperty>(("multi", true)))),
                ["NO"] = Prop(self => self._parse_no_property()),
                ["ON"] = Prop(self => self._parse_on_property()),
                ["PARTITION"] = Prop(self => self._parse_partitioned_of()),
                ["PARTITION BY"] = Prop(self => self._parse_partitioned_by()),
                ["PARTITIONED BY"] = Prop(self => self._parse_partitioned_by()),
                ["PARTITIONED_BY"] = Prop(self => self._parse_partitioned_by()),
                ["RETURNS"] = Prop(self => self._parse_returns()),
                ["STRICT"] = Flag<exp.StrictProperty>(),
                ["STREAMING"] = Flag<exp.StreamingTableProperty>(),
                ["ROW_FORMAT"] = Assign<exp.RowFormatProperty>(),
                ["SAMPLE"] = Prop(self => self.expression(Mk<exp.SampleProperty>(("this",
                    self._match_text_seq(["BY"]) ? self._parse_bitwise() : false)))),
                ["SECURE"] = Flag<exp.SecureProperty>(),
                ["SECURITY"] = Prop(self => self._parse_sql_security()),
                ["SQL SECURITY"] = Prop(self => self._parse_sql_security()),
                ["SET"] = Prop(self => self.expression(Mk<exp.SetProperty>(("multi", false)))),
                ["SHARING"] = Assign<exp.SharingProperty>(),
                ["STABLE"] = Stability("STABLE"),
                ["STORED"] = Prop(self => self._parse_stored()),
                ["TBLPROPERTIES"] = Prop(self => self._parse_wrapped_properties()),
                ["TEMP"] = Flag<exp.TemporaryProperty>(),
                ["TEMPORARY"] = Flag<exp.TemporaryProperty>(),
                ["TRANSIENT"] = Flag<exp.TransientProperty>(),
                ["TRANSFORM"] = Prop(self => self.expression(Mk<exp.TransformModelProperty>(("expressions",
                    self._parse_wrapped_csv(() => self._parse_expression()).Cast<object?>().ToList())))),
                ["CALLED"] = Prop(self => self._parse_called_on_null_input_property()),
                ["CONTAINS"] = Prop(self => self._parse_sql_read_write_property("SQL", "CONTAINS SQL")),
                ["MODIFIES"] = Prop(self => self._parse_sql_read_write_property("SQL DATA", "MODIFIES SQL DATA")),
                ["READS"] = Prop(self => self._parse_sql_read_write_property("SQL DATA", "READS SQL DATA")),
                ["COPY"] = Prop(self => self._parse_copy_property()),
                ["DEFINER"] = Prop(self => self._parse_definer()),
                ["LOCKING"] = Prop(self => self._parse_locking()),
                ["USING"] = Assign<exp.FileFormatProperty>(),
                ["UNLOGGED"] = Flag<exp.UnloggedProperty>(),
                ["VOLATILE"] = Prop(self => self._parse_volatile_property()),
                ["WITH"] = Prop(self => self._parse_with_property()),
                ["DATA_DELETION"] = Prop(self => self._parse_data_deletion_property()),
                ["SYSTEM_VERSIONING"] = Prop(self => self._parse_system_versioning_property()),
            };
            // Keys of the upstream table whose helper is still missing: explicit, never approximated.
            foreach (var key in new[] { "AUTO", "BLOCKCOMPRESSION", "CHECKSUM", "CLUSTER BY", "CLUSTERED",
                "DATABLOCKSIZE", "DISTRIBUTED", "DUPLICATE", "DISTKEY",
                "ENVIRONMENT", "FALLBACK", "FREESPACE", "INPUT", "JOURNAL", "LAYOUT", "LIFETIME", "LOCK",
                "LOG", "MERGEBLOCKRATIO", "ORDER BY", "OUTPUT", "PRIMARY KEY", "RANGE", "REMOTE",
                "ROW", "SETTINGS", "SORTKEY", "SOURCE", "TO", "TTL" })
                table[key] = Unported(key);
            return table;
        }

        private object? _call_property_parser(PropertyParser parser, PyDict<string, object?> kwargs)
        {
            if (kwargs.Count != 0 && !parser.AcceptsKwargs)
            {
                raise_error($"Cannot parse property '{_prev.text}'");
                return null;
            }
            return parser.Parse(this, kwargs);
        }

        // py: sqlglot/parser.py::Parser._parse_properties #0000000000
        public exp.Properties? _parse_properties(bool before = false)
        {
            List<object?> properties = [];
            while (true)
            {
                var prop = before ? _parse_property_before() : _parse_property();
                if (!py.truthy(prop)) break;
                if (prop is System.Collections.IEnumerable list and not exp.Expr and not string)
                    foreach (var p in list) properties.Add(p);
                else properties.Add(prop);

                _match(TokenType.COMMA, expression: (exp.Expr)properties[^1]!);
            }

            return properties.Count > 0 ? (exp.Properties)expression(new exp.Properties(expressions: properties)) : null;
        }

        // py: sqlglot/parser.py::Parser._parse_property_before #0000000000
        public object? _parse_property_before()
        {
            // only used for teradata currently
            _match(TokenType.COMMA);

            var kwargs = new PyDict<string, object?>
            {
                ["no"] = _match_text_seq(["NO"]),
                ["dual"] = _match_text_seq(["DUAL"]),
                ["before"] = _match_text_seq(["BEFORE"]),
                ["default"] = _match_text_seq(["DEFAULT"]),
                ["local"] = _match_text_seq(["LOCAL"]) ? "LOCAL" : _match_text_seq(["NOT", "LOCAL"]) ? "NOT LOCAL" : (object)false,
                ["after"] = _match_text_seq(["AFTER"]),
                ["minimum"] = _match_texts(["MIN", "MINIMUM"]),
                ["maximum"] = _match_texts(["MAX", "MAXIMUM"]),
            };
            var truthy = new PyDict<string, object?>();
            foreach (var (key, value) in kwargs) if (py.truthy(value)) truthy[key] = value;

            if (_match_texts(PROPERTY_PARSERS.Keys))
                return _call_property_parser(PROPERTY_PARSERS[py.upper(_prev.text)], truthy);

            if (_match_pair(TokenType.CHAR, TokenType.SET))
                return _parse_character_set(py.truthy(kwargs["default"]));

            return null;
        }

        // py: sqlglot/parser.py::Parser._parse_wrapped_properties #0000000000
        public List<object?> _parse_wrapped_properties()
        {
            var wrapped = _match(TokenType.L_PAREN);
            if (!wrapped) raise_error("Expecting (");
            var result = _parse_csv<object>(() => _parse_property());
            if (wrapped) _match_r_paren();
            return result.Cast<object?>().ToList();
        }

        // py: sqlglot/parser.py::Parser._parse_property #0000000000
        public object? _parse_property()
        {
            if (_match_texts(PROPERTY_PARSERS.Keys))
                return _call_property_parser(PROPERTY_PARSERS[py.upper(_prev.text)], []);

            if (_match_pair(TokenType.CHAR, TokenType.SET)) return _parse_character_set();

            if (_match(TokenType.DEFAULT))
            {
                if (_match_texts(PROPERTY_PARSERS.Keys))
                    return _call_property_parser(PROPERTY_PARSERS[py.upper(_prev.text)], new PyDict<string, object?> { ["default"] = true });

                if (_match_pair(TokenType.CHAR, TokenType.SET)) return _parse_character_set(@default: true);
            }

            if (_match_text_seq(["COMPOUND", "SORTKEY"]))
                throw new NotPortedException("sqlglot/parser.py::Parser._parse_sortkey");

            if (_match_text_seq(["PARAMETER", "STYLE", "PANDAS"]))
                return expression(Mk<exp.ParameterStyleProperty>(("this", "PANDAS")));

            if (_match_text_seq(["NOT", "DETERMINISTIC"]))
                return expression(Mk<exp.StabilityProperty>(("this", exp.Literal.@string("VOLATILE"))));

            var index = _index;

            var seq_props = _parse_sequence_properties();
            if (seq_props is not null) return seq_props;

            _retreat(index);
            return _parse_key_value_property();
        }

        // py: sqlglot/parser.py::Parser._parse_property_assignment #0000000000
        public T _parse_property_assignment<T>(PyDict<string, object?>? kwargs = null) where T : exp.Expr
        {
            _match(TokenType.EQ);
            _match(TokenType.ALIAS);

            var arguments = new List<(string, object?)> { ("this", _parse_unquoted_field()) };
            if (kwargs is not null) foreach (var (key, value) in kwargs) arguments.Add((key, value));
            return (T)expression(Mk<T>(arguments.ToArray()));
        }

        // py: sqlglot/parser.py::Parser._parse_unquoted_field #0000000000
        public exp.Expr? _parse_unquoted_field()
        {
            var field = _parse_field();
            if (field is exp.Identifier { quoted: false } id) field = new exp.Var(@this: id.name);
            return field;
        }

        // py: sqlglot/parser.py::Parser._parse_character_set #0000000000
        public exp.Expr _parse_character_set(bool @default = false)
        {
            _match(TokenType.EQ);
            return expression(Mk<exp.CharacterSetProperty>(("this", _parse_var_or_string()), ("default", @default)));
        }

        // py: sqlglot/parser.py::Parser._parse_stored #0000000000
        public exp.Expr _parse_stored()
        {
            if (_match_text_seq(["BY"]))
                return expression(Mk<exp.StorageHandlerProperty>(("this", _parse_var_or_string())));

            _match(TokenType.ALIAS);
            var input_format = _match_text_seq(["INPUTFORMAT"]) ? _parse_string() : null;
            var output_format = _match_text_seq(["OUTPUTFORMAT"]) ? _parse_string() : null;

            exp.Expr? @this = py.truthy(input_format) || py.truthy(output_format)
                ? expression(Mk<exp.InputOutputFormat>(("input_format", input_format), ("output_format", output_format)))
                : _parse_var_or_string() ?? _parse_number() ?? _parse_id_var();
            return expression(Mk<exp.FileFormatProperty>(("this", @this), ("hive_format", true)));
        }

        // py: sqlglot/parser.py::Parser._parse_volatile_property #0000000000
        public exp.Expr _parse_volatile_property()
        {
            var pre_volatile_token = _index >= 2 ? _tokens[(int)_index - 2] : null;

            if (pre_volatile_token is not null && PRE_VOLATILE_TOKENS.Contains(pre_volatile_token.token_type))
                return Mk<exp.VolatileProperty>();

            return expression(Mk<exp.StabilityProperty>(("this", exp.Literal.@string("VOLATILE"))));
        }

        // py: sqlglot/parser.py::Parser._parse_returns #0000000000
        public virtual exp.Expr _parse_returns()
        {
            exp.Expr? value;
            bool? @null = null;
            var is_table = _match(TokenType.TABLE);

            if (is_table)
            {
                if (_match(TokenType.LT))
                {
                    value = expression(Mk<exp.Schema>(("this", "TABLE"),
                        ("expressions", _parse_csv<exp.Expr>(() => _parse_struct_types()).Cast<object?>().ToList())));
                    if (!_match(TokenType.GT)) raise_error("Expecting >");
                }
                else value = _parse_schema(new exp.Var(@this: "TABLE"));
            }
            else if (_match_text_seq(["NULL", "ON", "NULL", "INPUT"]))
            {
                @null = true;
                value = null;
            }
            else value = _parse_types();

            return expression(Mk<exp.ReturnsProperty>(("this", value), ("is_table", is_table), ("null", @null)));
        }

        // py: sqlglot/parser.py::Parser._parse_sql_security #0000000000
        public exp.Expr _parse_sql_security() =>
            expression(Mk<exp.SqlSecurityProperty>(("this",
                _match_texts(SECURITY_PROPERTY_KEYWORDS) ? py.upper(_prev.text) : (object)false)));

        // py: sqlglot/parser.py::Parser._parse_no_property #0000000000
        public exp.Expr? _parse_no_property()
        {
            if (_match_text_seq(["PRIMARY", "INDEX"])) return Mk<exp.NoPrimaryIndexProperty>();
            if (_match_text_seq(["SQL"])) return expression(Mk<exp.SqlReadWriteProperty>(("this", "NO SQL")));
            return null;
        }

        // py: sqlglot/parser.py::Parser._parse_on_property #0000000000
        public exp.Expr _parse_on_property()
        {
            if (_match_text_seq(["COMMIT", "PRESERVE", "ROWS"])) return Mk<exp.OnCommitProperty>();
            if (_match_text_seq(["COMMIT", "DELETE", "ROWS"])) return Mk<exp.OnCommitProperty>(("delete", true));
            return expression(Mk<exp.OnProperty>(("this", _parse_schema(_parse_id_var()))));
        }

        // py: sqlglot/parser.py::Parser._parse_partitioned_by #0000000000
        public exp.Expr _parse_partitioned_by()
        {
            _match(TokenType.EQ);
            return expression(Mk<exp.PartitionedByProperty>(("this", _parse_schema() ?? _parse_bracket(_parse_field()))));
        }

        // py: sqlglot/parser.py::Parser._parse_partitioned_of #0000000000
        public exp.Expr? _parse_partitioned_of()
        {
            if (!_match_text_seq(["OF"]))
            {
                _retreat(_index - 1);
                return null;
            }

            var @this = _parse_table(schema: true);

            exp.Expr? expression_;
            if (_match(TokenType.DEFAULT)) expression_ = new exp.Var(@this: "DEFAULT");
            else if (_match_text_seq(["FOR", "VALUES"])) expression_ = _parse_partition_bound_spec();
            else
            {
                raise_error("Expecting either DEFAULT or FOR VALUES clause.");
                expression_ = null;
            }

            return expression(Mk<exp.PartitionedOfProperty>(("this", @this), ("expression", expression_)));
        }

        // py: sqlglot/parser.py::Parser._parse_partition_bound_spec #0000000000
        public exp.Expr _parse_partition_bound_spec()
        {
            exp.Expr? bound_expr()
            {
                if (_match_text_seq(["MINVALUE"])) return new exp.Var(@this: "MINVALUE");
                if (_match_text_seq(["MAXVALUE"])) return new exp.Var(@this: "MAXVALUE");
                return _parse_bitwise();
            }

            object? @this = null;
            exp.Expr? expression_ = null;
            List<object?>? from_expressions = null, to_expressions = null;

            if (_match(TokenType.IN)) @this = _parse_wrapped_csv(() => _parse_bitwise()).Cast<object?>().ToList();
            else if (_match(TokenType.FROM))
            {
                from_expressions = _parse_wrapped_csv(bound_expr).Cast<object?>().ToList();
                _match_text_seq(["TO"]);
                to_expressions = _parse_wrapped_csv(bound_expr).Cast<object?>().ToList();
            }
            else if (_match_text_seq(["WITH", "(", "MODULUS"]))
            {
                @this = _parse_number();
                _match_text_seq([",", "REMAINDER"]);
                expression_ = _parse_number();
                _match_r_paren();
            }
            else raise_error("Failed to parse partition bound spec.");

            return expression(Mk<exp.PartitionBoundSpec>(("this", @this), ("expression", expression_),
                ("from_expressions", from_expressions), ("to_expressions", to_expressions)));
        }

        // py: sqlglot/parser.py::Parser._parse_create_like #0000000000
        public exp.Expr? _parse_create_like()
        {
            var table = _parse_table(schema: true);

            List<object?> options = [];
            while (_match_texts(["INCLUDING", "EXCLUDING"]))
            {
                var @this = py.upper(_prev.text);

                var id_var = _parse_id_var();
                if (id_var is null) return null;

                options.Add(expression(Mk<exp.Property>(("this", @this),
                    ("value", new exp.Var(@this: py.upper((string)id_var.@this!))))));
            }

            return expression(Mk<exp.LikeProperty>(("this", table), ("expressions", options)));
        }

        // py: sqlglot/parser.py::Parser._parse_with_property #0000000000
        public object? _parse_with_property()
        {
            if (_match_text_seq(["(", "SYSTEM_VERSIONING"]))
            {
                var prop = _parse_system_versioning_property(with_: true);
                _match_r_paren();
                return prop;
            }

            if (_match(TokenType.L_PAREN, advance: false))
            {
                List<object?> result = [];
                foreach (var i in _parse_wrapped_properties())
                    if (i is List<object?> list) result.AddRange(list); else result.Add(i);
                return result;
            }

            if (_match_text_seq(["JOURNAL"]))
                throw new NotPortedException("sqlglot/parser.py::Parser._parse_withjournaltable");

            if (_match_texts(VIEW_ATTRIBUTES))
                return expression(Mk<exp.ViewAttributeProperty>(("this", py.upper(_prev.text))));

            if (_match_text_seq(["DATA"])) return _parse_withdata(no: false);
            if (_match_text_seq(["NO", "DATA"])) return _parse_withdata(no: true);

            if (_match(TokenType.SERDE_PROPERTIES, advance: false))
            {
                return _parse_serde_properties(with_: true);
            }

            if (_match(TokenType.SCHEMA))
                return expression(Mk<exp.WithSchemaBindingProperty>(("this", _parse_var_from_options(SCHEMA_BINDING_OPTIONS))));

            if (_match_texts(PROCEDURE_OPTIONS.Keys, advance: false))
                return expression(Mk<exp.WithProcedureOptions>(
                    ("expressions", _parse_csv(() => _parse_procedure_option()).Cast<object?>().ToList())));

            if (!py.truthy(_next)) return null;

            throw new NotPortedException("sqlglot/parser.py::Parser._parse_withisolatedloading");
        }

        // py: sqlglot/parser.py::Parser._parse_withdata #0000000000
        public exp.Expr _parse_withdata(bool no = false)
        {
            bool? statistics;
            if (_match_text_seq(["AND", "STATISTICS"])) statistics = true;
            else if (_match_text_seq(["AND", "NO", "STATISTICS"])) statistics = false;
            else statistics = null;

            return expression(Mk<exp.WithDataProperty>(("no", no), ("statistics", statistics)));
        }

        // py: sqlglot/parser.py::Parser._parse_sequence_properties #0000000000
        public exp.SequenceProperties? _parse_sequence_properties()
        {
            var seq = new exp.SequenceProperties();

            List<object?> options = [];
            var index = _index;

            while (py.truthy(_curr))
            {
                _match(TokenType.COMMA);
                if (_match_text_seq(["INCREMENT"]))
                {
                    _match_text_seq(["BY"]);
                    _match_text_seq(["="]);
                    seq.set("increment", _parse_term());
                }
                else if (_match_text_seq(["MINVALUE"])) seq.set("minvalue", _parse_term());
                else if (_match_text_seq(["MAXVALUE"])) seq.set("maxvalue", _parse_term());
                else if (_match_text_seq(["START"]))
                {
                    _match_text_seq(["WITH"]);
                    _match_text_seq(["="]);
                    seq.set("start", _parse_term());
                }
                else if (_match_text_seq(["CACHE"]))
                    // T-SQL allows empty CACHE which is initialized dynamically
                    seq.set("cache", _parse_number() ?? (object)true);
                else if (_match_text_seq(["OWNED", "BY"]))
                    // "OWNED BY NONE" is the default
                    seq.set("owned", _match_text_seq(["NONE"]) ? null : _parse_column());
                else
                {
                    var opt = _parse_var_from_options(CREATE_SEQUENCE, raise_unmatched: false);
                    if (opt is not null) options.Add(opt);
                    else break;
                }
            }

            seq.set("options", options.Count > 0 ? options : null);
            return _index == index ? null : seq;
        }

        // py: sqlglot/parser.py::Parser._parse_var_from_options #0000000000
        public exp.Var? _parse_var_from_options(PyDict<string, List<string>> options, bool raise_unmatched = true) =>
            _parse_var_from_options_core(key => options.TryGetValue(key, out var v) ? v.Select(s => new[] { s }).ToList() : null, raise_unmatched);

        public exp.Var? _parse_var_from_options(PyDict<string, List<List<string>>> options, bool raise_unmatched = true) =>
            _parse_var_from_options_core(key => options.TryGetValue(key, out var v) ? v.Select(s => s.ToArray()).ToList() : null, raise_unmatched);

        private exp.Var? _parse_var_from_options_core(Func<string, List<string[]>?> lookup, bool raise_unmatched)
        {
            var start = _curr;
            if (!py.truthy(start)) return null;

            var option = py.upper(start.text);
            var continuations = TEXT_MATCH_EXCLUDED_TOKENS.Contains(start.token_type) ? null : lookup(option);

            var index = _index;
            _advance();
            var matched = false;
            foreach (var keywords in continuations ?? [])
            {
                if (_match_text_seq(keywords))
                {
                    option = $"{option} {string.Join(" ", keywords)}";
                    matched = true;
                    break;
                }
            }

            if (!matched && (continuations is null || continuations.Count > 0))
            {
                if (raise_unmatched) raise_error($"Unknown option {option}");

                _retreat(index);
                return null;
            }

            return new exp.Var(@this: option);
        }

        // py: sqlglot/parser.py::Parser._parse_key_value_property #0000000000
        public exp.Property? _parse_key_value_property(Func<exp.Expr?>? parse_value = null)
        {
            var index = _index;
            var key = _parse_column();

            if (!_match(TokenType.EQ))
            {
                _retreat(index);
                return null;
            }

            // Transform the key to exp.Dot if it's dotted identifiers wrapped in exp.Column or to exp.Var otherwise
            if (key is exp.Column column)
                key = column.parts.Count > 1 ? column.to_dot() : new exp.Var(@this: column.name);

            var value = parse_value is not null ? parse_value() : _parse_bitwise() ?? _parse_var(any_token: true);

            // Transform the value to exp.Var if it was parsed as exp.Column(exp.Identifier())
            if (value is exp.Column valueColumn) value = new exp.Var(@this: valueColumn.name);

            return (exp.Property)expression(new exp.Property(@this: key, value: value));
        }
    }
}
