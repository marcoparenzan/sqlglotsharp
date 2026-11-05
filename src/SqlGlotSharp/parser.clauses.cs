using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

// Query clauses and statements that completed the bounded INSERT/CTE closures (parser.py).
public static partial class parser
{
    public partial class Parser
    {
        // Set while parsing the CONNECT BY condition: PRIOR is then a no-paren function (upstream mutates the
        // class-level NO_PAREN_FUNCTION_PARSERS temporarily; a per-instance flag is thread safe).
        private bool _connect_prior;

        // py: sqlglot/parser.py::Parser._parse_sort #0000000000
        public T? _parse_sort<T>(TokenType token) where T : exp.Expr
        {
            if (!_match(token)) return null;
            return (T)expression((exp.Expr)Activator.CreateInstance(typeof(T),
                [new PyDict<string, object?> { ["expressions"] = _parse_csv(() => _parse_ordered()).Cast<object?>().ToList() }])!);
        }

        // py: sqlglot/parser.py::Parser._parse_cluster #0000000000
        public exp.Expr _parse_cluster()
        {
            _match(TokenType.CLUSTER_BY);
            return expression(new exp.Cluster(expressions: _parse_csv(() => _parse_column()).Cast<object?>().ToList()));
        }

        // py: sqlglot/parser.py::Parser._parse_locks #0000000000
        public List<exp.Expr> _parse_locks()
        {
            List<exp.Expr> locks = [];
            while (true)
            {
                bool? update, key = null;
                if (_match_text_seq(["FOR", "UPDATE"])) update = true;
                else if (_match_text_seq(["FOR", "SHARE"]) || _match_text_seq(["LOCK", "IN", "SHARE", "MODE"])) update = false;
                else if (_match_text_seq(["FOR", "KEY", "SHARE"])) (update, key) = (false, true);
                else if (_match_text_seq(["FOR", "NO", "KEY", "UPDATE"])) (update, key) = (true, true);
                else break;

                List<object?>? expressions = null;
                if (_match_text_seq(["OF"])) expressions = _parse_csv(() => _parse_table(schema: true)).Cast<object?>().ToList();

                object? wait = null;
                if (_match_text_seq(["NOWAIT"])) wait = true;
                else if (_match_text_seq(["WAIT"])) wait = _parse_primary();
                else if (_match_text_seq(["SKIP", "LOCKED"])) wait = false;

                locks.Add(expression(new exp.Lock(update: update, expressions: expressions, wait: wait, key: key)));
            }

            return locks;
        }

        // py: sqlglot/parser.py::Parser._parse_connect_with_prior #0000000000
        public exp.Expr? _parse_connect_with_prior()
        {
            var previous = _connect_prior;
            _connect_prior = true;
            try { return _parse_disjunction(); }
            finally { _connect_prior = previous; }
        }

        // py: sqlglot/parser.py::Parser._parse_connect #0000000000
        public exp.Expr? _parse_connect(bool skip_start_token = false)
        {
            exp.Expr? start;
            if (skip_start_token) start = null;
            else if (_match_text_seq(["START", "WITH"])) start = _parse_disjunction();
            else return null;

            _match(TokenType.CONNECT_BY);
            var nocycle = _match_text_seq(["NOCYCLE"]);
            var connect = _parse_connect_with_prior();

            if (start is null && _match_text_seq(["START", "WITH"])) start = _parse_disjunction();

            return expression(new exp.Connect(start: start, connect: connect, nocycle: nocycle));
        }

        // py: sqlglot/parser.py::Parser._parse_hint #0000000000
        public exp.Expr? _parse_hint()
        {
            if (_match(TokenType.HINT) && _prev_comments.Count > 0)
                return dialect.parse_into(typeof(exp.Hint), _prev_comments[0]).FirstOrDefault();

            return null;
        }

        // py: sqlglot/parser.py::Parser._parse_into #0000000000
        public virtual exp.Expr? _parse_into()
        {
            if (!_match(TokenType.INTO)) return null;

            var temp = _match(TokenType.TEMPORARY);
            var unlogged = _match_text_seq(["UNLOGGED"]);
            _match(TokenType.TABLE);

            return expression(new exp.Into(@this: _parse_table(schema: true), temporary: temp, unlogged: unlogged));
        }

        // py: sqlglot/parser.py::Parser._parse_recursive_with_search #0000000000
        public exp.Expr? _parse_recursive_with_search()
        {
            _match_text_seq(["SEARCH"]);

            var kind = _match_texts(RECURSIVE_CTE_SEARCH_KIND) ? py.upper(_prev.text) : null;

            if (kind is null) return null;

            _match_text_seq(["FIRST", "BY"]);

            return expression(new exp.RecursiveWithSearch(
                kind: kind,
                @this: _parse_id_var(),
                expression: _match_text_seq(["SET"]) ? _parse_id_var() : false,
                to: _match_text_seq(["TO"]) ? _parse_bitwise() : false,
                @default: _match(TokenType.DEFAULT) ? _parse_bitwise() : false,
                @using: _match_text_seq(["USING"]) ? _parse_id_var() : false));
        }

        // py: sqlglot/parser.py::Parser._parse_kill #0000000000
        public exp.Expr _parse_kill()
        {
            exp.Expr? kind = _match_texts(["CONNECTION", "QUERY"]) ? new exp.Var(@this: _prev.text) : null;

            return expression(new exp.Kill(@this: _parse_primary(), kind: kind));
        }

        // py: sqlglot/parser.py::Parser._parse_settings_property #0000000000
        public exp.Expr _parse_settings_property() =>
            expression(new exp.SettingsProperty(expressions: _parse_csv(() => _parse_assignment()).Cast<object?>().ToList()));

        // py: sqlglot/parser.py::Parser._parse_serde_properties #0000000000
        public exp.Expr? _parse_serde_properties(bool with_ = false)
        {
            var index = _index;
            with_ = with_ || _match_text_seq(["WITH"]);

            if (!_match(TokenType.SERDE_PROPERTIES))
            {
                _retreat(index);
                return null;
            }
            return expression(new exp.SerdeProperties(expressions: _parse_wrapped_properties(), with_: with_));
        }

        // py: sqlglot/parser.py::Parser._parse_row_format #0000000000
        public exp.Expr? _parse_row_format(bool match_row = false)
        {
            if (match_row && !_match_pair(TokenType.ROW, TokenType.FORMAT)) return null;

            if (_match_text_seq(["SERDE"]))
            {
                var @this = _parse_string();

                var serde_properties = _parse_serde_properties();

                return expression(new exp.RowFormatSerdeProperty(@this: @this, serde_properties: serde_properties));
            }

            _match_text_seq(["DELIMITED"]);

            var kwargs = new PyDict<string, object?>();

            if (_match_text_seq(["FIELDS", "TERMINATED", "BY"]))
            {
                kwargs["fields"] = _parse_string();
                if (_match_text_seq(["ESCAPED", "BY"])) kwargs["escaped"] = _parse_string();
            }
            if (_match_text_seq(["COLLECTION", "ITEMS", "TERMINATED", "BY"])) kwargs["collection_items"] = _parse_string();
            if (_match_text_seq(["MAP", "KEYS", "TERMINATED", "BY"])) kwargs["map_keys"] = _parse_string();
            if (_match_text_seq(["LINES", "TERMINATED", "BY"])) kwargs["lines"] = _parse_string();
            if (_match_text_seq(["NULL", "DEFINED", "AS"])) kwargs["null"] = _parse_string();

            return expression(new exp.RowFormatDelimitedProperty(kwargs));
        }

        // py: sqlglot/parser.py::Parser._parse_multitable_inserts #0000000000
        public exp.Expr _parse_multitable_inserts(List<string>? comments, string? kind = null)
        {
            kind ??= py.upper(_prev.text);
            List<object?> expressions = [];

            exp.Expr? ParseConditionalInsert()
            {
                exp.Expr? when_expression;
                if (_match(TokenType.WHEN))
                {
                    when_expression = _parse_disjunction();
                    _match(TokenType.THEN);
                }
                else when_expression = null;

                var else_ = _match(TokenType.ELSE);

                if (!_match(TokenType.INTO)) return null;

                return expression(new exp.ConditionalInsert(
                    @this: expression(new exp.Insert(@this: _parse_table(schema: true), expression: _parse_derived_table_values())),
                    expression: when_expression,
                    else_: else_));
            }

            var item = ParseConditionalInsert();
            while (item is not null)
            {
                expressions.Add(item);
                item = ParseConditionalInsert();
            }

            return expression(new exp.MultitableInserts(kind: kind, expressions: expressions, source: _parse_table()), comments: comments);
        }

        // py: sqlglot/parser.py::Parser._parse_insert #0000000000
        public exp.Expr _parse_insert()
        {
            List<string> comments = [];
            var hint = _parse_hint();
            var overwrite = _match(TokenType.OVERWRITE);
            var ignore = _match(TokenType.IGNORE);
            var local = _match_text_seq(["LOCAL"]);
            object? alternative = null;
            bool? is_function = null;

            exp.Expr? @this;
            if (_match_text_seq(["DIRECTORY"]))
            {
                @this = expression(new exp.Directory(@this: _parse_var_or_string(), local: local, row_format: _parse_row_format(match_row: true)));
            }
            else
            {
                if (_match_set([TokenType.FIRST, TokenType.ALL]))
                {
                    comments.AddRange(_prev_comments);
                    return _parse_multitable_inserts(comments);
                }

                // Oracle's conditional form defaults to ALL when the keyword is omitted
                if (_match(TokenType.WHEN, advance: false)) return _parse_multitable_inserts(comments, kind: "ALL");

                if (_match(TokenType.OR)) alternative = _match_texts(INSERT_ALTERNATIVES) ? _prev.text : (object)false;

                _match(TokenType.INTO);
                comments.AddRange(_prev_comments);
                _match(TokenType.TABLE);
                is_function = _match(TokenType.FUNCTION);

                @this = is_function == true ? _parse_function() : _parse_insert_table();
            }

            // MySQL's INSERT ... SET is normalized into the INSERT ... (cols) VALUES (vals) variant
            exp.Expr? set_values = null;
            if (_match(TokenType.SET))
            {
                List<object?> columns = [];
                List<object?> values = [];

                exp.Expr? ParseSetAssignment()
                {
                    var target = _parse_column();
                    if (target is exp.Column && _match(TokenType.EQ))
                    {
                        exp.Expr? value;
                        if (dialect.SUPPORTS_VALUES_DEFAULT && _match(TokenType.DEFAULT)) value = new exp.Var(@this: py.upper(_prev.text));
                        else value = _parse_disjunction();

                        if (value is not null)
                        {
                            columns.Add(target.@this);
                            values.Add(value);
                            return value;
                        }
                    }

                    raise_error("Expected column assignment in INSERT ... SET");
                    return null;
                }

                _parse_csv(ParseSetAssignment);

                @this = expression(new exp.Schema(@this: @this, expressions: columns));
                set_values = expression(new exp.Values(expressions: new List<object?> { new exp.Tuple(expressions: values) },
                    alias: _parse_table_alias()));
            }

            var returning = _parse_returning(); // TSQL allows RETURNING before source

            object? stored = _match_text_seq(["STORED"]) ? _parse_stored() : false;
            var by_name = _match_text_seq(["BY", "NAME"]);
            var exists = _parse_exists();
            exp.Expr? replace_where = null;
            object? replace_using = null;

            if (_match(TokenType.REPLACE))
            {
                if (_match(TokenType.WHERE)) replace_where = _parse_disjunction();
                else if (_match(TokenType.USING)) replace_using = _parse_using_identifiers().Cast<object?>().ToList();
            }

            return expression(new exp.Insert(new PyDict<string, object?>
            {
                ["hint"] = hint,
                ["is_function"] = is_function,
                ["this"] = @this,
                ["stored"] = stored,
                ["by_name"] = by_name,
                ["exists"] = exists,
                ["where"] = replace_where,
                ["using"] = replace_using,
                ["partition"] = _match(TokenType.PARTITION_BY) ? _parse_partitioned_by() : false,
                ["settings"] = _match_text_seq(["SETTINGS"]) ? _parse_settings_property() : false,
                ["default"] = _match_text_seq(["DEFAULT", "VALUES"]),
                ["expression"] = set_values ?? (exp.Expr?)_parse_derived_table_values(allow_value_synonym: true) ?? _parse_ddl_select(),
                ["conflict"] = _parse_on_conflict(),
                ["returning"] = returning ?? _parse_returning(),
                ["overwrite"] = overwrite,
                ["alternative"] = alternative,
                ["ignore"] = ignore,
                ["source"] = _match(TokenType.TABLE) ? _parse_table() : false
            }), comments: comments);
        }
    }
}
