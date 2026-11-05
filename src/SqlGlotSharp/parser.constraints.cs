using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

public static partial class parser
{
    public partial class Parser
    {
        private static readonly HashSet<string> ConstraintCallbacks = [
            "AUTOINCREMENT", "AUTO_INCREMENT", "CASESPECIFIC", "CHECK", "COLLATE", "COMMENT",
            "COMPRESS", "CLUSTERED", "NONCLUSTERED", "DEFAULT", "ENCODE", "EPHEMERAL", "EXCLUDE",
            "FOREIGN KEY", "FORMAT", "GENERATED", "IDENTITY", "INLINE", "LIKE", "NOT", "NULL", "ON",
            "PATH", "PERIOD", "PRIMARY KEY", "REFERENCES", "TITLE", "TTL", "UNIQUE", "UPPERCASE",
            "WITH", "BUCKET", "TRUNCATE"
        ];

        // py: sqlglot/parser.py::Parser.CONSTRAINT_PARSERS #0000000000
        internal exp.Expr? _parse_constraint_callback(string key) => key switch
        {
            "AUTOINCREMENT" or "AUTO_INCREMENT" or "IDENTITY" => _parse_auto_increment(),
            "CASESPECIFIC" => expression(new exp.CaseSpecificColumnConstraint(not_: false)),
            "CHECK" => _parse_check_constraint(),
            "COLLATE" => expression(new exp.CollateColumnConstraint(@this: _parse_identifier() ?? _parse_column())),
            "COMMENT" => expression(new exp.CommentColumnConstraint(@this: _parse_string())),
            "COMPRESS" => _parse_compress(),
            "CLUSTERED" => expression(new exp.ClusteredColumnConstraint(@this: _parse_wrapped_csv(() => _parse_ordered()).Cast<object?>().ToList())),
            "NONCLUSTERED" => expression(new exp.NonClusteredColumnConstraint(@this: _parse_wrapped_csv(() => _parse_ordered()).Cast<object?>().ToList())),
            "DEFAULT" => expression(new exp.DefaultColumnConstraint(@this: _parse_bitwise())),
            "ENCODE" => expression(new exp.EncodeColumnConstraint(@this: _parse_var())),
            "EPHEMERAL" => expression(new exp.EphemeralColumnConstraint(@this: _parse_bitwise())),
            "EXCLUDE" => expression(new exp.ExcludeColumnConstraint(@this: _parse_index_params())),
            "FOREIGN KEY" => _parse_foreign_key(),
            "FORMAT" => expression(new exp.DateFormatColumnConstraint(@this: _parse_var_or_string())),
            "GENERATED" => _parse_generated_as_identity(),
            "INLINE" => _parse_inline(),
            "LIKE" => _parse_create_like(),
            "NOT" => _parse_not_constraint(),
            "NULL" => expression(new exp.NotNullColumnConstraint(allow_null: true)),
            "ON" => _match(TokenType.UPDATE)
                ? (exp.Expr)expression(new exp.OnUpdateColumnConstraint(@this: _parse_function()))
                : expression(new exp.OnProperty(@this: _parse_id_var())),
            "PATH" => expression(new exp.PathColumnConstraint(@this: _parse_string())),
            "PERIOD" => _parse_period_for_system_time(),
            "PRIMARY KEY" => _parse_primary_key(),
            "REFERENCES" => _parse_references(match: false),
            "TITLE" => expression(new exp.TitleColumnConstraint(@this: _parse_var_or_string())),
            "TTL" => expression(new exp.MergeTreeTTL(expressions: new List<object?> { _parse_bitwise() })),
            "UNIQUE" => _parse_unique(),
            "UPPERCASE" => expression(new exp.UppercaseColumnConstraint()),
            "WITH" => expression(new exp.Properties(expressions: _parse_wrapped_properties())),
            "BUCKET" or "TRUNCATE" => _parse_partitioned_by_bucket_or_truncate(),
            _ => throw new NotPortedException($"Parser._parse_column_constraint / {key} callback")
        };

        // py: sqlglot/parser.py::Parser._parse_auto_increment #0000000000
        public exp.Expr _parse_auto_increment()
        {
            exp.Expr? start = null;
            exp.Expr? increment = null;
            bool? order = null;

            if (_match(TokenType.L_PAREN, advance: false))
            {
                var args = _parse_wrapped_csv(() => _parse_bitwise());
                start = helper.seq_get(args, 0);
                increment = helper.seq_get(args, 1);
            }

            // The remaining parts form an unordered bag and any of them can be omitted, in which
            // case the engine falls back to its own default, so they are parsed independently.
            while (true)
            {
                if (_match_text_seq(["START"])) start = _parse_bitwise();
                else if (_match_text_seq(["INCREMENT"])) increment = _parse_bitwise();
                else if (_match_text_seq(["ORDER"])) order = true;
                else if (_match_text_seq(["NOORDER"])) order = false;
                else break;
            }

            if (py.truthy(start) || py.truthy(increment) || order is not null)
                return new exp.GeneratedAsIdentityColumnConstraint(start: start, increment: increment, @this: false, order: order);

            return new exp.AutoIncrementColumnConstraint();
        }

        // py: sqlglot/parser.py::Parser._parse_compress #0000000000
        public exp.Expr _parse_compress()
        {
            if (_match(TokenType.L_PAREN, advance: false))
                return expression(new exp.CompressColumnConstraint(@this: _parse_wrapped_csv(() => _parse_bitwise()).Cast<object?>().ToList()));

            return expression(new exp.CompressColumnConstraint(@this: _parse_bitwise()));
        }

        // py: sqlglot/parser.py::Parser._parse_inline #0000000000
        public exp.Expr _parse_inline()
        {
            _match_text_seq(["LENGTH"]);
            return expression(new exp.InlineLengthColumnConstraint(@this: _parse_bitwise()));
        }

        // py: sqlglot/parser.py::Parser._parse_not_constraint #0000000000
        public exp.Expr? _parse_not_constraint()
        {
            if (_match_text_seq(["NULL"])) return expression(new exp.NotNullColumnConstraint());
            if (_match_text_seq(["CASESPECIFIC"])) return expression(new exp.CaseSpecificColumnConstraint(not_: true));
            if (_match_text_seq(["FOR", "REPLICATION"])) return expression(new exp.NotForReplicationColumnConstraint());

            // Unconsume the `NOT` token
            _retreat(_index - 1);
            return null;
        }

        // py: sqlglot/parser.py::Parser._parse_period_for_system_time #0000000000
        public exp.Expr? _parse_period_for_system_time()
        {
            if (!_match_text_seq(["FOR", "SYSTEM_TIME"]))
            {
                _retreat(_index - 1);
                return null;
            }

            var id_vars = _parse_wrapped_id_vars();
            return expression(new exp.PeriodForSystemTimeConstraint(@this: helper.seq_get(id_vars, 0), expression: helper.seq_get(id_vars, 1)));
        }

        // py: sqlglot/parser.py::Parser._parse_partitioned_by_bucket_or_truncate #0000000000
        public exp.Expr? _parse_partitioned_by_bucket_or_truncate()
        {
            if (!_match(TokenType.L_PAREN, advance: false))
            {
                // Partitioning by bucket or truncate follows the syntax:
                // PARTITION BY (BUCKET(..) | TRUNCATE(..))
                // If we do not have parenthesis after each keyword, we should instead parse this as an identifier
                _retreat(_index - 1);
                return null;
            }

            var is_bucket = py.upper(_prev.text) == "BUCKET";

            var args = _parse_wrapped_csv(() => _parse_primary() ?? _parse_column());
            var @this = helper.seq_get(args, 0);
            var expression_ = helper.seq_get(args, 1);

            if (@this is exp.Literal)
            {
                // Iceberg partition transforms: Hive writes bucket(<num buckets>, <col name>), Trino the reverse;
                // both are canonicalized to bucket(<col name>, <num buckets>)
                (@this, expression_) = (expression_, @this);
            }

            return is_bucket
                ? expression(new exp.PartitionedByBucket(@this: @this, expression: expression_))
                : expression(new exp.PartitionByTruncate(@this: @this, expression: expression_));
        }

        // py: sqlglot/parser.py::Parser._parse_check_constraint
        public exp.CheckColumnConstraint? _parse_check_constraint()
        {
            if (!_match(TokenType.L_PAREN, advance: false)) return null;
            var node = expression(new exp.CheckColumnConstraint(new PyDict<string, object?>
            {
                ["this"] = _parse_wrapped(_parse_assignment), ["enforced"] = _match_text_seq(["ENFORCED"])
            }));
            _constraint_modifier_boundary();
            return node;
        }

        // py: sqlglot/parser.py::Parser._parse_constraint
        public exp.Expr? _parse_constraint()
        {
            if (!_match(TokenType.CONSTRAINT) || _match_texts(RESERVED_CONSTRAINT_KINDS.Cast<string>(), advance: false))
                return _parse_unnamed_constraint(SCHEMA_UNNAMED_CONSTRAINTS);
            return expression(new exp.Constraint(@this: _parse_id_var(), expressions: _parse_unnamed_constraints()));
        }

        public List<object?> _parse_unnamed_constraints()
        {
            List<object?> result = [];
            while (true)
            {
                var node = _parse_unnamed_constraint() ?? _parse_function();
                if (node is null) break;
                result.Add(node);
            }
            return result;
        }

        public exp.Expr? _parse_unnamed_constraint(IEnumerable<object>? constraints = null)
        {
            var index = _index;
            if (_match(TokenType.IDENTIFIER, advance: false)
                || !_match_texts(constraints?.Cast<string>() ?? ConstraintCallbacks)) return null;
            var key = _prev.text.ToUpperInvariant();
            if (!ConstraintCallbacks.Contains(key))
            {
                raise_error($"No parser found for schema constraint {key}.");
                throw new NotPortedException($"Parser._parse_constraint / {key} callback");
            }
            var result = _parse_constraint_callback(key);
            if (result is null) _retreat(index);
            return result;
        }

        public exp.Expr? _parse_unique_key()
        {
            if (py.truthy(_curr) && _curr.token_type != TokenType.IDENTIFIER
                && ConstraintCallbacks.Contains(_curr.text.ToUpperInvariant())) return null;
            return _parse_id_var(any_token: false);
        }

        public virtual exp.UniqueColumnConstraint _parse_unique()
        {
            if (dialect is dialects.postgres.Postgres)
                return expression(new exp.UniqueColumnConstraint(new PyDict<string, object?>
                {
                    ["nulls"] = _match_text_seq(["NULLS", "NOT", "DISTINCT"]), ["this"] = _parse_schema(),
                    ["include"] = _parse_constraint_index_params(), ["options"] = _parse_key_constraint_options().Cast<object?>().ToList().Cast<object?>().ToList()
                }));
            _match_texts(["KEY", "INDEX"]);
            return expression(new exp.UniqueColumnConstraint(new PyDict<string, object?>
            {
                ["nulls"] = _match_text_seq(["NULLS", "NOT", "DISTINCT"]),
                ["this"] = _parse_schema(_parse_unique_key()),
                ["index_type"] = _match(TokenType.USING) && py.truthy(_advance_any()) ? _prev.text : (object)false,
                ["on_conflict"] = _parse_on_conflict(), ["options"] = _parse_key_constraint_options().Cast<object?>().ToList()
            }));
        }

        // Includes upstream string options and actions, including node-free Var creation.
        public List<string> _parse_key_constraint_options()
        {
            List<string> options = [];
            while (py.truthy(_curr))
            {
                if (_match(TokenType.ON))
                {
                    string? action = null;
                    object? on = py.truthy(_advance_any()) ? _prev.text : null;
                    if (_match_text_seq(["NO", "ACTION"])) action = "NO ACTION";
                    else if (_match_text_seq(["CASCADE"])) action = "CASCADE";
                    else if (_match_text_seq(["RESTRICT"])) action = "RESTRICT";
                    else if (_match_pair(TokenType.SET, TokenType.NULL)) action = "SET NULL";
                    else if (_match_pair(TokenType.SET, TokenType.DEFAULT)) action = "SET DEFAULT";
                    else raise_error("Invalid key constraint");
                    options.Add($"ON {py.str(on)} {action ?? "None"}");
                }
                else
                {
                    var start = _curr;
                    var option = start.text.ToUpperInvariant();
                    var continuations = TEXT_MATCH_EXCLUDED_TOKENS.Contains(start.token_type)
                        ? null : KEY_CONSTRAINT_OPTIONS.GetValueOrDefault(option);
                    var index = _index;
                    _advance();
                    var matched = false;
                    foreach (var keyword in continuations ?? [])
                    {
                        if (!_match_text_seq([keyword])) continue;
                        option = $"{option} {keyword}"; matched = true; break;
                    }
                    if (!matched && (continuations is null || continuations.Count > 0))
                    { _retreat(index); break; }
                    options.Add(option);
                }
            }
            _constraint_modifier_boundary();
            return options;
        }

        // Upstream has no such boundary: modifiers are handled by _parse_key_constraint_options and the callers.
        private void _constraint_modifier_boundary() { }

        public exp.Reference? _parse_references(bool match = true)
        {
            if (match && !_match(TokenType.REFERENCES)) return null;
            var @this = _parse_table(schema: true);
            var options = _parse_key_constraint_options();
            return expression(new exp.Reference(new PyDict<string, object?>
            { ["this"] = @this, ["expressions"] = null, ["options"] = options.Cast<object?>().ToList() }));
        }

        public List<exp.Expr> _parse_wrapped_id_vars(bool optional = false) =>
            _parse_wrapped_csv(() => _parse_id_var(), optional: optional);

        public exp.ForeignKey _parse_foreign_key()
        {
            object? expressions = !_match(TokenType.REFERENCES, advance: false) ? _parse_wrapped_id_vars().Cast<object?>().ToList() : null;
            var reference = _parse_references();
            PyDict<string, object?> onOptions = new();
            while (_match(TokenType.ON))
            {
                if (!_match_set([TokenType.DELETE, TokenType.UPDATE])) raise_error("Expected DELETE or UPDATE");
                var kind = _prev.text.ToLowerInvariant();
                string action;
                if (_match_text_seq(["NO", "ACTION"])) action = "NO ACTION";
                else if (_match(TokenType.SET))
                { _match_set([TokenType.NULL, TokenType.DEFAULT]); action = "SET " + _prev.text.ToUpperInvariant(); }
                else { _advance(); action = _prev.text.ToUpperInvariant(); }
                onOptions[kind] = action;
            }
            PyDict<string, object?> args = new()
            { ["expressions"] = expressions, ["reference"] = reference, ["options"] = _parse_key_constraint_options().Cast<object?>().ToList() };
            foreach (var pair in onOptions) args[pair.Key] = pair.Value;
            return expression(new exp.ForeignKey(args));
        }

        public virtual exp.Expr? _parse_primary_key_part() => _parse_field();

        public exp.Expr _parse_primary_key(bool wrapped_optional = false, bool in_props = false, bool named_primary_key = false)
        {
            object? desc = _match_set([TokenType.ASC, TokenType.DESC]) ? _prev.token_type == TokenType.DESC : null;
            exp.Expr? @this = null;
            if (named_primary_key && !ConstraintCallbacks.Contains(_curr.text.ToUpperInvariant())
                && py.truthy(_next) && _next.token_type == TokenType.L_PAREN) @this = _parse_id_var();
            if (!in_props && !_match(TokenType.L_PAREN, advance: false))
            {
                var node = expression(new exp.PrimaryKeyColumnConstraint(new PyDict<string, object?>
                { ["desc"] = desc, ["options"] = _parse_key_constraint_options().Cast<object?>().ToList() }));
                // py: sqlglot/parsers/postgres.py::PostgresParser._parse_primary_key
                if (dialect is dialects.postgres.Postgres)
                {
                    node.set("include", _parse_constraint_index_params());
                    node.set("options", ((List<object?>)node.args["options"]!).Concat(_parse_key_constraint_options().Cast<object?>()).ToList());
                }
                return node;
            }
            var expressions = _parse_wrapped_csv(_parse_primary_key_part, optional: wrapped_optional);
            return expression(new exp.PrimaryKey(new PyDict<string, object?>
            {
                ["this"] = @this, ["expressions"] = expressions.Cast<object?>().ToList(),
                ["include"] = _parse_constraint_index_params(), ["options"] = _parse_key_constraint_options().Cast<object?>().ToList()
            }));
        }

        private exp.IndexParameters _parse_constraint_index_params() => (exp.IndexParameters)_parse_index_params();
    }
}
