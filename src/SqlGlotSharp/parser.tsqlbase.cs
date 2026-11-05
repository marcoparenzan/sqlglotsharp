using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

// Base-parser statements first needed by T-SQL: table hints, NEXT VALUE FOR, DECLARE.
public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser._parse_condition
        public exp.Expr? _parse_condition() => _parse_wrapped(() => _parse_expression(), optional: true);

        // py: sqlglot/parser.py::Parser._parse_whileblock
        public exp.Expr _parse_whileblock() =>
            expression(Mk<exp.WhileBlock>(("this", _parse_condition()), ("body", _parse_block())));

        // py: sqlglot/parser.py::Parser._parse_procedure_option
        public exp.Expr? _parse_procedure_option()
        {
            if (_match_text_seq(["EXECUTE", "AS"]))
                return expression(Mk<exp.ExecuteAsProperty>(("this",
                    (object?)_parse_var_from_options(EXECUTE_AS_OPTIONS, raise_unmatched: false) ?? _parse_string())));

            return _parse_var_from_options(PROCEDURE_OPTIONS);
        }

        // py: sqlglot/parser.py::Parser._parse_retention_period
        public exp.Var _parse_retention_period()
        {
            // Parse TSQL's HISTORY_RETENTION_PERIOD: {INFINITE | <number> DAY | DAYS | MONTH ...}
            var number = _parse_number();
            var number_str = number is not null ? $"{number.name} " : "";
            var unit = _parse_var(any_token: true);
            return new exp.Var(@this: $"{number_str}{unit?.name}");
        }

        // py: sqlglot/parser.py::Parser._parse_system_versioning_property
        public exp.Expr _parse_system_versioning_property(bool with_ = false)
        {
            _match(TokenType.EQ);
            var prop = expression(Mk<exp.WithSystemVersioningProperty>(("on", true), ("with_", with_)));

            if (_match_text_seq(["OFF"]))
            {
                prop.set("on", false);
                return prop;
            }

            _match(TokenType.ON);
            if (_match(TokenType.L_PAREN))
            {
                while (py.truthy(_curr) && !_match(TokenType.R_PAREN))
                {
                    if (_match_text_seq(["HISTORY_TABLE", "="]))
                        prop.set("this", _parse_table_parts());
                    else if (_match_text_seq(["DATA_CONSISTENCY_CHECK", "="]))
                        prop.set("data_consistency", py.truthy(_advance_any()) ? _prev.text.ToUpperInvariant() : null);
                    else if (_match_text_seq(["HISTORY_RETENTION_PERIOD", "="]))
                        prop.set("retention_period", _parse_retention_period());

                    _match(TokenType.COMMA);
                }
            }

            return prop;
        }

        // py: sqlglot/parser.py::Parser._parse_data_deletion_property
        public exp.Expr _parse_data_deletion_property()
        {
            _match(TokenType.EQ);
            var on = _match_text_seq(["ON"]) || !_match_text_seq(["OFF"]);
            var prop = expression(Mk<exp.DataDeletionProperty>(("on", on)));

            if (_match(TokenType.L_PAREN))
            {
                while (py.truthy(_curr) && !_match(TokenType.R_PAREN))
                {
                    if (_match_text_seq(["FILTER_COLUMN", "="]))
                        prop.set("filter_column", _parse_column());
                    else if (_match_text_seq(["RETENTION_PERIOD", "="]))
                        prop.set("retention_period", _parse_retention_period());

                    _match(TokenType.COMMA);
                }
            }

            return prop;
        }

        // py: sqlglot/parser.py::Parser._parse_open_json
        public exp.Expr _parse_open_json()
        {
            var @this = _parse_bitwise();
            object? path = _match(TokenType.COMMA) ? _parse_string() : false;

            exp.Expr? _parse_open_json_column_def()
            {
                var column = _parse_field(any_token: true);
                var kind = _parse_types();
                var column_path = _parse_string();
                var as_json = _match_pair(TokenType.ALIAS, TokenType.JSON);

                return expression(Mk<exp.OpenJSONColumnDef>(("this", column), ("kind", kind), ("path", column_path), ("as_json", as_json)));
            }

            List<object?>? expressions = null;
            if (_match_pair(TokenType.R_PAREN, TokenType.WITH))
            {
                _match_l_paren();
                expressions = _parse_csv(_parse_open_json_column_def).Cast<object?>().ToList();
            }

            return expression(Mk<exp.OpenJSON>(("this", @this), ("path", path), ("expressions", expressions)));
        }

        // py: sqlglot/parser.py::Parser._parse_table_hints
        public List<object?>? _parse_table_hints()
        {
            var hints = new List<object?>();
            if (_match_pair(TokenType.WITH, TokenType.L_PAREN))
            {
                // https://learn.microsoft.com/en-us/sql/t-sql/queries/hints-transact-sql-table?view=sql-server-ver16
                hints.Add(expression(Mk<exp.WithTableHint>(("expressions",
                    _parse_csv(() => _parse_function() ?? _parse_var(any_token: true)).Cast<object?>().ToList()))));
                _match_r_paren();
            }
            else
            {
                // https://dev.mysql.com/doc/refman/8.0/en/index-hints.html
                while (_match_set(TABLE_INDEX_HINT_TOKENS))
                {
                    var hint = Mk<exp.IndexTableHint>(("this", _prev.text.ToUpperInvariant()));

                    _match_set([TokenType.INDEX, TokenType.KEY]);
                    if (_match(TokenType.FOR))
                        hint.set("target", py.truthy(_advance_any()) ? _prev.text.ToUpperInvariant() : null);

                    hint.set("expressions", _parse_wrapped_id_vars().Cast<object?>().ToList());
                    hints.Add(hint);
                }
            }

            return hints.Count > 0 ? hints : null;
        }

        // py: sqlglot/parser.py::Parser._parse_next_value_for
        public exp.Expr? _parse_next_value_for()
        {
            if (!_match_text_seq(["VALUE", "FOR"]))
            {
                _retreat(_index - 1);
                return null;
            }

            return expression(Mk<exp.NextValueFor>(("this", _parse_column()),
                ("order", _match(TokenType.OVER) ? _parse_wrapped(() => _parse_order()) : (object)false)));
        }

        // py: sqlglot/parser.py::Parser._parse_declareitem
        public exp.Expr? _parse_declareitem()
        {
            _match_texts(["VAR", "VARIABLE"]);

            var vars = _parse_csv(() => _parse_id_var());
            if (vars.Count == 0) return null;

            _match(TokenType.ALIAS);
            var kind = _match(TokenType.TABLE) ? _parse_schema() : _parse_types();
            object? @default = (_match(TokenType.DEFAULT) || _match(TokenType.EQ)) ? _parse_bitwise() : false;

            return expression(Mk<exp.DeclareItem>(("this", vars.Cast<object?>().ToList()), ("kind", kind), ("default", @default)));
        }

        // py: sqlglot/parser.py::Parser._parse_declare
        public exp.Expr _parse_declare()
        {
            var start = _prev;
            var replace = _match_text_seq(["OR", "REPLACE"]);
            var expressions = _try_parse(() => _parse_csv(() => _parse_declareitem()));

            if (expressions is not { Count: > 0 } || py.truthy(_curr))
                return _parse_as_command(start);

            return expression(Mk<exp.Declare>(("expressions", expressions.Cast<object?>().ToList()), ("replace", replace)));
        }

    }
}
