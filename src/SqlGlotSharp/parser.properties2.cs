using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

// Small PROPERTY_PARSERS helpers (parser.py).
public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser._parse_called_on_null_input_property #0000000000
        public exp.Expr? _parse_called_on_null_input_property()
        {
            if (!_match_text_seq(["ON", "NULL", "INPUT"]))
            {
                _retreat(_index - 1);
                return null;
            }

            return expression(new exp.CalledOnNullInputProperty());
        }

        // py: sqlglot/parser.py::Parser._parse_contains_property / _parse_modifies_property / _parse_reads_property
        public exp.Expr? _parse_sql_read_write_property(string keywords, string value)
        {
            if (_match_text_seq(keywords.Split(' '))) return expression(new exp.SqlReadWriteProperty(@this: value));
            return null;
        }

        // py: sqlglot/parser.py::Parser._parse_copy_property #0000000000
        public exp.Expr? _parse_copy_property()
        {
            if (!_match_text_seq(["GRANTS"]))
            {
                _retreat(_index - 1);
                return null;
            }

            return expression(new exp.CopyGrantsProperty());
        }

        // py: sqlglot/parser.py::Parser._parse_definer #0000000000
        public exp.Expr? _parse_definer()
        {
            _match(TokenType.EQ);

            var user = _parse_id_var();
            _match(TokenType.PARAMETER);
            object? host = _parse_id_var();
            if (host is null && _match(TokenType.MOD)) host = _prev.text;

            if (user is null || host is null) return null;

            var g = new generator.Generator();
            return new exp.DefinerProperty(@this: $"{g.sql(user)}@{(host is exp.Expr he ? g.sql(he) : host)}");
        }

        // py: sqlglot/parser.py::Parser._parse_locking #0000000000
        public exp.Expr _parse_locking()
        {
            string? kind;
            if (_match(TokenType.TABLE)) kind = "TABLE";
            else if (_match(TokenType.VIEW)) kind = "VIEW";
            else if (_match(TokenType.ROW)) kind = "ROW";
            else if (_match_text_seq(["DATABASE"])) kind = "DATABASE";
            else kind = null;

            exp.Expr? @this = kind is "DATABASE" or "TABLE" or "VIEW" ? _parse_table_parts() : null;

            string? for_or_in;
            if (_match(TokenType.FOR)) for_or_in = "FOR";
            else if (_match(TokenType.IN)) for_or_in = "IN";
            else for_or_in = null;

            string? lock_type;
            if (_match_text_seq(["ACCESS"])) lock_type = "ACCESS";
            else if (_match_texts(["EXCL", "EXCLUSIVE"])) lock_type = "EXCLUSIVE";
            else if (_match_text_seq(["SHARE"])) lock_type = "SHARE";
            else if (_match_text_seq(["READ"])) lock_type = "READ";
            else if (_match_text_seq(["WRITE"])) lock_type = "WRITE";
            else if (_match_text_seq(["CHECKSUM"])) lock_type = "CHECKSUM";
            else lock_type = null;

            var @override = _match_text_seq(["OVERRIDE"]);

            return expression(new exp.LockingProperty(@this: @this, kind: kind, for_or_in: for_or_in, lock_type: lock_type, @override: @override));
        }
    }
}
