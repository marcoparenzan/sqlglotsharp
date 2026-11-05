using Token = SqlGlotSharp.tokenizer_core.Token;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser._warn_unsupported #0000000000
        public void _warn_unsupported()
        {
            if (_tokens_size <= 1) return;

            // We use _find_sql because self.sql may comprise multiple chunks, and we're only
            // interested in emitting a warning for the one being currently processed.
            var text = py.slice(_find_sql(_tokens[0], _tokens[^1]), 0, error_message_context);

            logger.warning($"'{text}' contains unsupported syntax. Falling back to parsing as a 'Command'.");
        }

        // py: sqlglot/parser.py::Parser._parse_command #0000000000
        public exp.Expr _parse_command()
        {
            _warn_unsupported();
            var comments = _prev_comments;
            return expression(Mk<exp.Command>(("this", py.upper(_prev.text)), ("expression", _parse_string())), comments: comments);
        }

        // py: sqlglot/parser.py::Parser._parse_as_command #0000000000
        public exp.Command _parse_as_command(Token start)
        {
            while (py.truthy(_curr)) _advance();
            var text = _find_sql(start, _prev);
            var size = start.text.Length;
            _warn_unsupported();
            return Mk<exp.Command>(("this", py.slice(text, 0, size)), ("expression", text[Math.Min(size, text.Length)..]));
        }

        // py: sqlglot/parser.py::Parser._parse_create #0000000000
        public virtual exp.Expr _parse_create()
        {
            // Note: this can't be None because we've matched a statement parser
            var start = _prev;

            var replace = start.token_type == TokenType.REPLACE
                || _match_pair(TokenType.OR, TokenType.REPLACE)
                || _match_pair(TokenType.OR, TokenType.ALTER);
            var refresh = _match_pair(TokenType.OR, TokenType.REFRESH);

            var unique = _match(TokenType.UNIQUE);

            bool? clustered;
            if (_match_text_seq(["CLUSTERED", "COLUMNSTORE"])) clustered = true;
            else if (_match_text_seq(["NONCLUSTERED", "COLUMNSTORE"]) || _match_text_seq(["COLUMNSTORE"])) clustered = false;
            else clustered = null;

            if (_match_pair(TokenType.TABLE, TokenType.FUNCTION, advance: false)) _advance();

            exp.Properties? properties = null;
            var create_token = _match_set(CREATABLES) ? _prev : null;

            if (create_token is null)
            {
                // exp.Properties.Location.POST_CREATE
                properties = _parse_properties();
                create_token = _match_set(CREATABLES) ? _prev : null;

                if (properties is null || create_token is null) return _parse_as_command(start);
            }

            var create_token_type = create_token.token_type;

            var concurrently = create_token_type == TokenType.INDEX && _match_text_seq(["CONCURRENTLY"]);
            var exists = _parse_exists(not_: true);
            exp.Expr? @this = null;
            exp.Expr? expression_ = null;
            List<object?>? indexes = null;
            bool? no_schema_binding = null;
            object? begin = null;
            exp.Expr? clone = null;

            void extend_props(exp.Properties? temp_props)
            {
                if (properties is not null && temp_props is not null) properties.expressions.AddRange(temp_props.expressions);
                else if (temp_props is not null) properties = temp_props;
            }

            if (create_token_type is TokenType.FUNCTION or TokenType.PROCEDURE)
            {
                @this = _parse_user_defined_function(create_token_type);

                // exp.Properties.Location.POST_SCHEMA (the UDF's type signature)
                extend_props(_parse_properties());

                expression_ = _match(TokenType.ALIAS) ? _parse_heredoc() : null;

                bool is_table, overload_mode;
                if (!py.truthy(expression_) && create_token_type == TokenType.FUNCTION
                    && @this is exp.UserDefinedFunction udf && py.truthy(udf.args.get("wrapped")))
                {
                    var pre_table_index = _index;
                    is_table = _match(TokenType.TABLE);

                    expression_ = _parse_expression();
                    overload_mode = py.truthy(expression_) && _curr.token_type == TokenType.COMMA
                        && _next.token_type == TokenType.L_PAREN;
                    if (!overload_mode)
                    {
                        _retreat(pre_table_index);
                        is_table = false;
                        expression_ = null;
                    }
                }
                else
                {
                    is_table = false;
                    overload_mode = false;
                }

                extend_props(_parse_function_properties());

                if (!py.truthy(expression_))
                {
                    if (_match(TokenType.COMMAND)) expression_ = _parse_as_command(_prev);
                    else
                    {
                        begin = _match(TokenType.BEGIN);
                        var return_ = _match_text_seq(["RETURN"]);

                        if (_match(TokenType.STRING, advance: false))
                        {
                            // Takes care of BigQuery's JavaScript UDF definitions that end in an OPTIONS property
                            expression_ = _parse_string();
                            extend_props(_parse_properties());
                        }
                        else
                            expression_ = create_token_type == TokenType.FUNCTION
                                ? _parse_user_defined_function_expression() : _parse_block();

                        if (return_) expression_ = expression(Mk<exp.Return>(("this", expression_)));
                    }
                }

                if (overload_mode && py.truthy(expression_))
                    expression_ = _parse_macro_overloads((exp.UserDefinedFunction)@this!, expression_!, is_table);
            }
            else if (create_token_type == TokenType.INDEX)
            {
                // Postgres allows anonymous indexes, eg. CREATE INDEX IF NOT EXISTS ON t(c)
                exp.Expr? index;
                bool anonymous;
                if (!_match(TokenType.ON))
                {
                    index = _parse_id_var();
                    anonymous = false;
                }
                else
                {
                    index = null;
                    anonymous = true;
                }

                @this = _parse_index(index: index, anonymous: anonymous);
            }
            else if (create_token_type == TokenType.TRIGGER || create_token_type == TokenType.CONSTRAINT && _match(TokenType.TRIGGER))
            {
                var is_constraint = create_token_type == TokenType.CONSTRAINT;
                if (is_constraint) create_token = _prev;

                var trigger_name = _parse_id_var();
                if (trigger_name is null) return _parse_as_command(start);

                var timing_var = _parse_var_from_options(TRIGGER_TIMING, raise_unmatched: false);
                var timing = timing_var?.@this;
                if (!py.truthy(timing)) return _parse_as_command(start);

                var events = _parse_trigger_events();
                if (!_match(TokenType.ON)) raise_error("Expected ON in trigger definition");

                var table = _parse_table_parts();
                var referenced_table = _match(TokenType.FROM) ? _parse_table_parts() : null;
                var (deferrable, initially) = _parse_trigger_deferrable();
                var referencing = _parse_trigger_referencing();
                var for_each = _parse_trigger_for_each();
                object? when = _match_text_seq(["WHEN"]) ? _parse_wrapped(() => _parse_disjunction(), optional: true) : false;
                var execute = _parse_trigger_execute();

                if (execute is null) return _parse_as_command(start);

                var trigger_props = expression(Mk<exp.TriggerProperties>(("table", table), ("timing", timing),
                    ("events", events.Cast<object?>().ToList()), ("execute", execute), ("constraint", is_constraint),
                    ("referenced_table", referenced_table), ("deferrable", deferrable), ("initially", initially),
                    ("referencing", referencing), ("for_each", for_each), ("when", when)));

                @this = trigger_name;
                extend_props(Mk<exp.Properties>(("expressions", new List<object?> { trigger_props })));
            }
            else if (create_token_type == TokenType.TYPE)
            {
                @this = _parse_table_parts(schema: true);
                if (!py.truthy(@this) || !_match(TokenType.ALIAS)) return _parse_as_command(start);

                if (_match(TokenType.ENUM))
                    expression_ = new exp.DataType(@this: exp.DType.ENUM,
                        expressions: _parse_wrapped_csv(() => _parse_string()).Cast<object?>().ToList());
                else if (_match(TokenType.L_PAREN, advance: false)) expression_ = _parse_schema();
                else return _parse_as_command(start);
            }
            else if (DB_CREATABLES.Contains(create_token_type))
            {
                var table_parts = _parse_table_parts(schema: true, is_db_reference: create_token_type == TokenType.SCHEMA);

                // exp.Properties.Location.POST_NAME
                _match(TokenType.COMMA);
                extend_props(_parse_properties(before: true));

                @this = _parse_schema(table_parts);

                // exp.Properties.Location.POST_SCHEMA and POST_WITH
                extend_props(_parse_properties());

                var has_alias = _match(TokenType.ALIAS);
                if (!_match_set(DDL_SELECT_TOKENS, advance: false))
                    // exp.Properties.Location.POST_ALIAS
                    extend_props(_parse_properties());

                if (create_token_type == TokenType.SEQUENCE)
                {
                    expression_ = _parse_types();
                    var props = _parse_properties();
                    if (props is not null)
                    {
                        var sequence_props = new exp.SequenceProperties();
                        List<object?> options = [];
                        foreach (var prop in props.expressions.ToList())
                        {
                            if (prop is not exp.SequenceProperties seq) continue;
                            foreach (var (arg, value) in seq.args.ToList())
                            {
                                if (arg == "options") options.AddRange((IEnumerable<object?>)value!);
                                else sequence_props.set(arg, value);
                            }
                            seq.pop();
                        }

                        if (options.Count > 0) sequence_props.set("options", options);

                        props.append("expressions", sequence_props);
                        extend_props(props);
                    }
                }
                else
                {
                    expression_ = _parse_ddl_select();

                    // Some dialects also support using a table as an alias instead of a SELECT.
                    // Here we fallback to this as an alternative.
                    if (!py.truthy(expression_) && has_alias) expression_ = _try_parse(() => _parse_table_parts()!);
                }

                if (create_token_type == TokenType.TABLE)
                {
                    // exp.Properties.Location.POST_EXPRESSION
                    extend_props(_parse_properties());

                    indexes = [];
                    while (true)
                    {
                        var index = _parse_index();

                        // exp.Properties.Location.POST_INDEX
                        extend_props(_parse_properties());
                        if (index is null) break;
                        _match(TokenType.COMMA);
                        indexes.Add(index);
                    }
                }
                else if (create_token_type == TokenType.VIEW)
                {
                    if (_match_text_seq(["WITH", "NO", "SCHEMA", "BINDING"])) no_schema_binding = true;
                }
                else if (create_token_type is TokenType.SINK or TokenType.SOURCE)
                    extend_props(_parse_properties());

                var shallow = _match_text_seq(["SHALLOW"]);

                if (_match_texts(CLONE_KEYWORDS))
                {
                    var copy = py.lower(_prev.text) == "copy";
                    clone = expression(Mk<exp.Clone>(("this", _parse_table(schema: true)), ("shallow", shallow), ("copy", copy)));
                }
            }

            if (py.truthy(_curr) && !_match_set([TokenType.R_PAREN, TokenType.COMMA], advance: false))
                return _parse_as_command(start);

            var create_kind_text = py.upper(create_token.text);
            return expression(new exp.Create(new PyDict<string, object?>
            {
                ["this"] = @this, ["kind"] = dialect.CREATABLE_KIND_MAPPING.get(create_kind_text) ?? create_kind_text,
                ["replace"] = replace, ["refresh"] = refresh, ["unique"] = unique,
                ["expression"] = expression_, ["exists"] = exists, ["properties"] = properties,
                ["indexes"] = indexes, ["no_schema_binding"] = no_schema_binding,
                ["begin"] = begin, ["clone"] = clone, ["concurrently"] = concurrently, ["clustered"] = clustered
            }));
        }

        // py: sqlglot/parser.py::Parser._parse_user_defined_function #0000000000
        public virtual exp.Expr? _parse_user_defined_function(TokenType? kind = null)
        {
            var @this = _parse_table_parts(schema: true);

            if (!_match(TokenType.L_PAREN)) return @this;

            var expressions = _parse_csv<exp.Expr>(_parse_function_parameter);
            _match_r_paren();
            return expression(Mk<exp.UserDefinedFunction>(("this", @this),
                ("expressions", expressions.Cast<object?>().ToList()), ("wrapped", true)));
        }

        // py: sqlglot/parser.py::Parser._parse_function_properties #0000000000
        public exp.Properties? _parse_function_properties()
        {
            // Skip the generic `key = value` fallback in _parse_property since this
            // runs post-AS where a function body like `name = expr` can be misread
            // as a property.
            List<object?> properties = [];
            while (true)
            {
                string keyword;
                object? prop;
                if (_match_texts(PROPERTY_PARSERS.Keys))
                {
                    keyword = py.upper(_prev.text);
                    prop = _call_property_parser(PROPERTY_PARSERS[keyword], []);
                }
                else if (_match(TokenType.DEFAULT) && _match_texts(PROPERTY_PARSERS.Keys))
                {
                    keyword = py.upper(_prev.text);
                    prop = _call_property_parser(PROPERTY_PARSERS[keyword], new PyDict<string, object?> { ["default"] = true });
                }
                else break;

                if (!py.truthy(prop))
                {
                    raise_error($"Failed to parse property '{keyword}'");
                    break;
                }
                if (prop is System.Collections.IEnumerable list and not exp.Expr and not string)
                    foreach (var p in list) properties.Add(p);
                else properties.Add(prop);
            }

            return properties.Count > 0 ? (exp.Properties)expression(new exp.Properties(expressions: properties)) : null;
        }

        // py: sqlglot/parser.py::Parser._parse_heredoc #0000000000
        public exp.Expr? _parse_heredoc()
        {
            if (_match(TokenType.HEREDOC_STRING))
                return expression(Mk<exp.Heredoc>(("this", _prev.text)));

            if (!_match_text_seq(["$"])) return null;

            List<string> tags = ["$"];
            string? tag_text = null;

            if (_is_connected())
            {
                _advance();
                tags.Add(py.upper(_prev.text));
            }
            else raise_error("No closing $ found");

            if (tags[^1] != "$")
            {
                if (_is_connected() && _match_text_seq(["$"]))
                {
                    tag_text = tags[^1];
                    tags.Add("$");
                }
                else raise_error("No closing $ found");
            }

            var heredoc_start = _curr;

            while (py.truthy(_curr))
            {
                if (_match_text_seq(tags.ToArray(), advance: false))
                {
                    var @this = _find_sql(heredoc_start, _prev);
                    _advance(tags.Count);
                    return expression(Mk<exp.Heredoc>(("this", @this), ("tag", tag_text)));
                }

                _advance();
            }

            raise_error($"No closing {string.Concat(tags)} found");
            return null;
        }

        // py: sqlglot/parser.py::Parser._parse_user_defined_function_expression #0000000000
        public virtual exp.Expr? _parse_user_defined_function_expression() => _parse_statement();

        // py: sqlglot/parser.py::Parser._parse_block #0000000000
        public exp.Expr _parse_block() =>
            expression(Mk<exp.Block>(("expressions",
                _parse_batch_statements(self => self._parse_statement()).Cast<object?>().ToList())));

        // py: sqlglot/parser.py::Parser._parse_macro_overloads #0000000000
        public exp.Expr _parse_macro_overloads(exp.UserDefinedFunction @this, exp.Expr first_body, bool first_is_table = false)
        {
            List<object?> overloads =
            [
                expression(Mk<exp.MacroOverload>(("this", first_body),
                    ("expressions", @this.expressions.Count > 0 ? @this.expressions : null), ("is_table", first_is_table)))
            ];
            @this.set("expressions", null);
            @this.set("wrapped", false);

            while (_match(TokenType.COMMA))
            {
                if (!_match(TokenType.L_PAREN)) break;

                var @params = _parse_csv<exp.Expr>(_parse_function_parameter);
                _match_r_paren();

                if (!_match(TokenType.ALIAS)) break;

                var is_table = _match(TokenType.TABLE);
                var body = _parse_expression();
                overloads.Add(expression(Mk<exp.MacroOverload>(("this", body),
                    ("expressions", @params.Cast<object?>().ToList()), ("is_table", is_table))));
            }

            return expression(Mk<exp.MacroOverloads>(("expressions", overloads)));
        }

        // py: sqlglot/parser.py::Parser._parse_trigger_events #0000000000
        public List<exp.Expr> _parse_trigger_events()
        {
            List<exp.Expr> events = [];

            while (true)
            {
                var event_type = _match_set(TRIGGER_EVENTS) ? py.upper(_prev.text) : null;

                if (event_type is null) raise_error("Expected trigger event (INSERT, UPDATE, DELETE, TRUNCATE)");

                var columns = event_type == "UPDATE" && _match_text_seq(["OF"])
                    ? _parse_csv<exp.Expr>(() => _parse_column()).Cast<object?>().ToList() : null;

                events.Add(expression(Mk<exp.TriggerEvent>(("this", event_type), ("columns", columns))));

                if (!_match(TokenType.OR)) break;
            }

            return events;
        }

        // py: sqlglot/parser.py::Parser._parse_trigger_deferrable #0000000000
        public (object? deferrable, object? initially) _parse_trigger_deferrable()
        {
            var deferrable_var = _parse_var_from_options(TRIGGER_DEFERRABLE, raise_unmatched: false);
            var deferrable = deferrable_var?.@this;

            object? initially = null;
            if (py.truthy(deferrable) && _match_text_seq(["INITIALLY"]))
                initially = _match_texts(["IMMEDIATE", "DEFERRED"]) ? py.upper(_prev.text) : null;

            return (deferrable, initially);
        }

        // py: sqlglot/parser.py::Parser._parse_trigger_referencing_clause #0000000000
        public exp.Expr? _parse_trigger_referencing_clause(string keyword)
        {
            if (!_match_text_seq([keyword])) return null;
            if (!_match_text_seq(["TABLE"])) raise_error($"Expected TABLE after {keyword} in REFERENCING clause");
            _match_text_seq(["AS"]);
            return _parse_id_var();
        }

        // py: sqlglot/parser.py::Parser._parse_trigger_referencing #0000000000
        public exp.Expr? _parse_trigger_referencing()
        {
            if (!_match_text_seq(["REFERENCING"])) return null;

            exp.Expr? old_alias = null, new_alias = null;

            while (true)
            {
                var alias = _parse_trigger_referencing_clause("OLD");
                if (alias is not null)
                {
                    if (old_alias is not null) raise_error("Duplicate OLD clause in REFERENCING");
                    old_alias = alias;
                    continue;
                }
                alias = _parse_trigger_referencing_clause("NEW");
                if (alias is not null)
                {
                    if (new_alias is not null) raise_error("Duplicate NEW clause in REFERENCING");
                    new_alias = alias;
                }
                else break;
            }

            if (old_alias is null && new_alias is null)
                raise_error("REFERENCING clause requires at least OLD TABLE or NEW TABLE");

            return expression(Mk<exp.TriggerReferencing>(("old", old_alias), ("new", new_alias)));
        }

        // py: sqlglot/parser.py::Parser._parse_trigger_for_each #0000000000
        public string? _parse_trigger_for_each()
        {
            if (!_match_text_seq(["FOR", "EACH"])) return null;

            return _match_texts(["ROW", "STATEMENT"]) ? py.upper(_prev.text) : null;
        }

        // py: sqlglot/parser.py::Parser._parse_trigger_execute #0000000000
        public exp.Expr? _parse_trigger_execute()
        {
            if (!_match(TokenType.EXECUTE)) return null;

            if (!_match_set([TokenType.FUNCTION, TokenType.PROCEDURE]))
                raise_error("Expected FUNCTION or PROCEDURE after EXECUTE");

            var func_call = _parse_column();
            return expression(Mk<exp.TriggerExecute>(("this", func_call)));
        }

        // py: sqlglot/parser.py::Parser._parse_index #0000000000
        public exp.Expr? _parse_index(exp.Expr? index = null, bool anonymous = false)
        {
            object? unique, primary, amp;
            exp.Expr? table;
            if (py.truthy(index) || anonymous)
            {
                unique = null;
                primary = null;
                amp = null;

                _match(TokenType.ON);
                _match(TokenType.TABLE); // hive
                table = _parse_table_parts(schema: true);
            }
            else
            {
                unique = _match(TokenType.UNIQUE);
                primary = _match_text_seq(["PRIMARY"]);
                amp = _match_text_seq(["AMP"]);

                if (!_match(TokenType.INDEX)) return null;

                index = _parse_id_var();
                table = null;
            }

            var @params = _parse_index_params();

            return expression(Mk<exp.Index>(("this", index), ("table", table), ("unique", unique),
                ("primary", primary), ("amp", amp), ("params", @params)));
        }

        // py: sqlglot/parser.py::Parser._parse_index_params #0000000000
        public exp.Expr _parse_index_params()
        {
            exp.Expr? @using = !_match_text_seq(["USING", "INDEX", "TABLESPACE"], advance: false) && _match(TokenType.USING)
                ? _parse_var(any_token: true) : null;

            List<object?>? columns = _match(TokenType.L_PAREN, advance: false)
                ? _parse_wrapped_csv(() => _parse_with_operator()).Cast<object?>().ToList() : null;

            var include = _match_text_seq(["INCLUDE"]) ? _parse_wrapped_id_vars().Cast<object?>().ToList() : null;
            var partition_by = _parse_partition_by();
            object? with_storage = _match(TokenType.WITH) ? _parse_wrapped_properties() : false;
            var tablespace = _match_text_seq(["USING", "INDEX", "TABLESPACE"]) || _match_text_seq(["TABLESPACE"])
                ? _parse_id_var() : null;
            var where = _parse_where();

            var on = _match(TokenType.ON) ? _parse_field() : null;

            return expression(Mk<exp.IndexParameters>(("using", @using), ("columns", columns), ("include", include),
                ("partition_by", partition_by.Cast<object?>().ToList()), ("where", where),
                ("with_storage", with_storage), ("tablespace", tablespace), ("on", on)));
        }
    }
}
