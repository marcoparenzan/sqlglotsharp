using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

// COPY statement (parser.py).
public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser._parse_format_name #0000000000
        public exp.Expr _parse_format_name() =>
            // Note: Although not specified in the docs, Snowflake does accept a string/identifier
            // for FILE_FORMAT = <format_name>
            expression(new exp.Property(@this: new exp.Var(@this: "FORMAT_NAME"), value: _parse_string() ?? _parse_table_parts()));

        // py: sqlglot/parser.py::Parser._parse_wrapped_options #0000000000
        public List<exp.Expr> _parse_wrapped_options()
        {
            _match(TokenType.EQ);
            _match(TokenType.L_PAREN);

            List<exp.Expr> opts = [];
            while (py.truthy(_curr) && !_match(TokenType.R_PAREN))
            {
                object? option;
                // The FORMAT_NAME can be set to an identifier for Snowflake and T-SQL
                if (_match_text_seq(["FORMAT_NAME", "="])) option = _parse_format_name();
                else option = _parse_property();

                if (option is null)
                {
                    raise_error("Unable to parse option");
                    break;
                }

                foreach (var item in helper.ensure_list(option)) opts.Add((exp.Expr)item!);
            }

            return opts;
        }

        // py: sqlglot/parser.py::Parser._parse_copy_parameters #0000000000
        public List<exp.CopyParameter> _parse_copy_parameters()
        {
            TokenType? sep = dialect.COPY_PARAMS_ARE_CSV ? TokenType.COMMA : null;

            List<exp.CopyParameter> options = [];
            while (py.truthy(_curr) && !_match(TokenType.R_PAREN, advance: false))
            {
                var option = _parse_var(any_token: true);
                if (option is null) break;

                var prev = py.upper(_prev.text);

                // Different dialects might separate options and values by white space, "=" and "AS"
                _match(TokenType.EQ);
                _match(TokenType.ALIAS);

                var param = expression(new exp.CopyParameter(@this: option));

                if (COPY_INTO_VARLEN_OPTIONS.Contains(prev) && _match(TokenType.L_PAREN, advance: false))
                    // Snowflake FILE_FORMAT case, Databricks COPY & FORMAT options
                    param.set("expressions", _parse_wrapped_options().Cast<object?>().ToList());
                else if (prev == "FILE_FORMAT")
                    // T-SQL's external file format case
                    param.set("expression", _parse_field());
                else if (prev == "FORMAT" && _prev.token_type == TokenType.ALIAS && _match_texts(["AVRO", "JSON"]))
                {
                    param.set("this", new exp.Var(@this: $"FORMAT AS {py.upper(_prev.text)}"));
                    param.set("expression", _parse_field());
                }
                else param.set("expression", _parse_unquoted_field() ?? _parse_bracket());

                options.Add(param);

                if (sep is not null) _match(sep.Value);
            }

            return options;
        }

        // py: sqlglot/parser.py::Parser._parse_credentials #0000000000
        public exp.Credentials? _parse_credentials()
        {
            var expr = expression(new exp.Credentials());

            if (_match_text_seq(["STORAGE_INTEGRATION", "="])) expr.set("storage", _parse_field());
            if (_match_text_seq(["CREDENTIALS"]))
            {
                // Snowflake case: CREDENTIALS = (...), Redshift case: CREDENTIALS <string>
                object? creds = _match(TokenType.EQ) ? _parse_wrapped_options().Cast<object?>().ToList() : _parse_field();
                expr.set("credentials", creds);
            }
            if (_match_text_seq(["ENCRYPTION"])) expr.set("encryption", _parse_wrapped_options().Cast<object?>().ToList());
            if (_match_text_seq(["IAM_ROLE"]))
                expr.set("iam_role", _match(TokenType.DEFAULT) ? new exp.Var(@this: _prev.text) : _parse_field());
            if (_match_text_seq(["REGION"])) expr.set("region", _parse_field());

            return expr;
        }

        // py: sqlglot/parser.py::Parser._parse_file_location #0000000000
        public virtual exp.Expr? _parse_file_location() => _parse_field();

        // py: sqlglot/parser.py::Parser._parse_copy #0000000000
        public exp.Expr _parse_copy()
        {
            var start = _prev;

            _match(TokenType.INTO);

            var @this = _match(TokenType.L_PAREN, advance: false)
                ? _parse_select(nested: true, parse_subquery_alias: false)
                : _parse_table(schema: true);

            var kind = _match(TokenType.FROM) || !_match_text_seq(["TO"]);

            var files = _parse_csv<exp.Expr>(_parse_file_location);
            if (_match(TokenType.EQ, advance: false))
            {
                // Backtrack one token since we've consumed the lhs of a parameter assignment here.
                // This can happen for Snowflake dialect. Instead, we'd like to parse the parameter
                // list via `_parse_wrapped(..)` below.
                _advance(-1);
                files = [];
            }

            var credentials = _parse_credentials();

            _match_text_seq(["WITH"]);

            var @params = _parse_wrapped(_parse_copy_parameters, optional: true);

            // Fallback case
            if (py.truthy(_curr)) return _parse_as_command(start);

            return expression(new exp.Copy(@this: @this, kind: kind, credentials: credentials,
                files: files.Cast<object?>().ToList(), @params: @params.Cast<object?>().ToList()));
        }
    }
}
