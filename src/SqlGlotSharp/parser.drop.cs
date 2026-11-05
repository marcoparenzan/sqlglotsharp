using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser._parse_drop #0000000000
        public exp.Expr _parse_drop(bool exists = false, string? kind = null)
        {
            var start = _prev;

            bool temporary, materialized, iceberg;
            if (!py.truthy(kind))
            {
                temporary = _match(TokenType.TEMPORARY);
                materialized = _match_text_seq(["MATERIALIZED"]);
                iceberg = _match_text_seq(["ICEBERG"]);
                kind = _match_set(CREATABLES) ? py.upper(_prev.text) : null;
            }
            else
            {
                temporary = false;
                materialized = false;
                iceberg = false;
            }

            if (!py.truthy(kind) || (iceberg && kind != "TABLE")) return _parse_as_command(start);

            var concurrently = kind == "INDEX"
                && ID_VAR_TOKENS.Contains(_next.token_type)
                && _match_text_seq(["CONCURRENTLY"]);

            var if_exists = exists || _parse_exists();

            List<exp.Expr> tables;
            if (kind == "COLUMN")
            {
                // A dropped column named after a keyword, like `any` or `charset`, is only a name
                var name = (NO_PAREN_FUNCTION_PARSERS.ContainsKey(py.upper(_curr.text)) ? null : _parse_column()) ?? _parse_id_var();
                var column = name is exp.Identifier ? expression(new exp.Column(@this: name)) : name;
                tables = column is null ? [] : [column];
            }
            else if (kind is "TABLE" or "VIEW")
                tables = _parse_csv<exp.Expr>(() => _parse_table_parts(schema: true));
            else
            {
                var table = _parse_table_parts(schema: true, is_db_reference: kind == "SCHEMA");
                tables = table is null ? [] : [table];
            }

            exp.Expr? cluster = null;
            if (_match(TokenType.ON))
            {
                if (kind is "INDEX" or "TRIGGER") cluster = expression(new exp.OnProperty(@this: _parse_table_parts()));
                else cluster = _parse_on_property();
            }

            List<exp.Expr>? expressions = null;
            if (_match(TokenType.L_PAREN, advance: false))
                expressions = _parse_wrapped_csv<exp.Expr>(() => _parse_types());

            var cascade_or_restrict = _match_texts(["CASCADE", "RESTRICT"]) ? py.upper(_prev.text) : "";

            return expression(new exp.Drop(new PyDict<string, object?>
            {
                ["exists"] = if_exists,
                ["tables"] = tables.Cast<object?>().ToList(),
                ["expressions"] = expressions?.Cast<object?>().ToList(),
                ["kind"] = dialect.CREATABLE_KIND_MAPPING.get(kind!) ?? kind,
                ["temporary"] = temporary,
                ["materialized"] = materialized,
                ["cascade"] = cascade_or_restrict == "CASCADE",
                ["restrict"] = cascade_or_restrict == "RESTRICT",
                ["constraints"] = _match_text_seq(["CONSTRAINTS"]),
                ["purge"] = _match_text_seq(["PURGE"]),
                ["cluster"] = cluster,
                ["concurrently"] = concurrently,
                ["sync"] = _match_text_seq(["SYNC"]),
                ["iceberg"] = iceberg,
                ["force"] = _match_text_seq(["FORCE"])
            }));
        }

        // py: sqlglot/parser.py::Parser._parse_exists
        public bool _parse_exists(bool not_ = false) => not_
            ? _match_text_seq(["IF", "NOT", "EXISTS"]) : _match_text_seq(["IF", "EXISTS"]);

        // py: sqlglot/parser.py::Parser._parse_truncate_table
        public exp.Expr? _parse_truncate_table()
        {
            // Preserve the ordinary TRUNCATE(number, decimals) function ambiguity.
            if (_match(TokenType.L_PAREN))
            {
                _retreat(_index - 2);
                return _parse_function();
            }
            var start = _prev;
            // Clickhouse supports TRUNCATE DATABASE as well
            var is_database = _match(TokenType.DATABASE);
            _match(TokenType.TABLE);
            var exists = _parse_exists(not_: false);
            var expressions = _parse_csv(() => _parse_table(schema: true, is_db_reference: is_database));
            var cluster = _match(TokenType.ON) ? _parse_on_property() : null;
            string? identity = null;
            if (_match_text_seq(["RESTART", "IDENTITY"])) identity = "RESTART";
            else if (_match_text_seq(["CONTINUE", "IDENTITY"])) identity = "CONTINUE";
            string? option = null;
            if (_match_text_seq(["CASCADE"]) || _match_text_seq(["RESTRICT"])) option = _prev.text;
            var partition = _parse_partition();
            // Fallback case
            if (py.truthy(_curr)) return _parse_as_command(start);
            return expression(new exp.TruncateTable(new PyDict<string, object?>
            {
                ["expressions"] = expressions.Cast<object?>().ToList(), ["is_database"] = is_database,
                ["exists"] = exists, ["cluster"] = cluster, ["identity"] = identity,
                ["option"] = option, ["partition"] = partition
            }));
        }
    }
}
