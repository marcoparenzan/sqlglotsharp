using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser._parse_insert_table #0000000000
        public exp.Expr? _parse_insert_table()
        {
            var target = _parse_table(schema: true, parse_partition: true);
            if (target is exp.Table && _match(TokenType.ALIAS))
            {
                var alias = _parse_id_var();
                if (alias is not null)
                    target.set("alias", expression(new exp.TableAlias(@this: alias), comments: alias.pop_comments()));
                return _parse_schema(target);
            }
            return target;
        }

        // Schema-mode continuation of _parse_table: upstream returns immediately after _parse_schema.
        private exp.Expr? _parse_insert_schema_table(bool parse_bracket, bool is_db_reference, bool parse_partition)
        {
            var bracket = parse_bracket ? _parse_bracket(null) : null;
            var bracketTable = bracket is not null ? expression(new exp.Table(@this: bracket)) : null;
            if (_match_text_seq(["ROWS", "FROM"], advance: false))
                throw new NotPortedException("sqlglot/parser.py::Parser._parse_table / ROWS FROM schema target");
            var only = _match(TokenType.ONLY);
            var target = bracketTable ?? _parse_bracket(_parse_table_parts(schema: true, is_db_reference: is_db_reference));
            if (only) target!.set("only", true);
            _match(TokenType.STAR);
            if ((parse_partition || SUPPORTS_PARTITION_SELECTION) && _match(TokenType.PARTITION, advance: false))
                target!.set("partition", _parse_partition());
            return _parse_schema(target);
        }

        // Shared INSERT/DDL schema closure; unsupported table constraints remain explicit.
        // py: sqlglot/parser.py::Parser._parse_schema #0000000000
        public exp.Expr? _parse_schema(exp.Expr? @this = null)
        {
            var index = _index;
            if (!_match(TokenType.L_PAREN)) return @this;
            if (_match_set(SELECT_START_TOKENS))
            {
                _retreat(index);
                return @this;
            }
            var args = _parse_csv(() => _parse_constraint() ?? _parse_field_def());
            _match_r_paren();
            return expression(new exp.Schema(@this: @this, expressions: args.Cast<object?>().ToList()));
        }

        // Reuses the existing bounded _parse_column_def, not a simplified identifier grammar.
        // py: sqlglot/parser.py::Parser._parse_field_def #0000000000
        public exp.Expr? _parse_field_def() => _parse_column_def(_parse_field(any_token: true));

        // py: sqlglot/parser.py::Parser._parse_ddl_select #0000000000
        public exp.Expr? _parse_ddl_select() => _parse_query_modifiers(
            _parse_set_operations(_parse_select(nested: true, parse_subquery_alias: false)));

        // py: sqlglot/parser.py::Parser._parse_returning #0000000000
        public exp.Returning? _parse_returning()
        {
            if (!_match(TokenType.RETURNING)) return null;
            var expressions = _parse_csv(() => _parse_expression());
            object? into = _match(TokenType.INTO) ? _parse_table_part() : false;
            return expression(new exp.Returning(expressions: expressions.Cast<object?>().ToList(), into: into));
        }
    }
}
