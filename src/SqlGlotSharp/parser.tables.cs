using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser._parse_table_part #0000000000
        public virtual exp.Expr? _parse_table_part(bool schema = false) =>
            (!schema ? _parse_function(optional_parens: false) : null)
            ?? _parse_id_var(any_token: false) ?? _parse_string_as_identifier() ?? _parse_placeholder();

        // Partial at historical/change/pivot and function dependencies.
        // py: sqlglot/parser.py::Parser._parse_table_parts #0000000000
        public virtual exp.Expr? _parse_table_parts(bool schema = false, bool is_db_reference = false, bool wildcard = false, bool fast = false)
        {
            if (fast) return _parse_table_parts_fast();
            object? catalog = null, db = null, table = _parse_table_part(schema);
            while (_match(TokenType.DOT))
            {
                if (py.truthy(catalog))
                    table = expression(new exp.Dot(@this: table, expression: _parse_table_part(schema)));
                else
                {
                    catalog = db; db = table;
                    table = (object?)_parse_table_part(schema) ?? "";
                }
            }
            if (wildcard && _is_connected() && (table is exp.Identifier || !py.truthy(table)) && _match(TokenType.STAR))
            {
                if (table is exp.Identifier identifier) identifier.args["this"] = py.str(identifier.args["this"]) + "*";
                else table = new exp.Identifier(@this: "*");
            }
            if (is_db_reference) { catalog = db; db = table; table = null; }
            if (!py.truthy(table) && !is_db_reference) raise_error($"Expected table name but got {_curr}");
            if (!py.truthy(db) && is_db_reference) raise_error($"Expected database name but got {_curr}");
            var result = expression(new exp.Table(@this: table, db: db, catalog: catalog));
            List<string> comments = [];
            foreach (var part in new[] { catalog, db, table })
            {
                if (part is exp.Dot dot)
                    foreach (var leaf in dot.flatten()) comments.AddRange(leaf.pop_comments() ?? []);
                else if (part is exp.Expr node) comments.AddRange(node.pop_comments() ?? []);
            }
            if (comments.Count > 0) result.add_comments(comments);
            if (_match_text_seq(["CHANGES", "(", "INFORMATION", "=>"], advance: false))
                throw new NotPortedException("Parser._parse_changes");
            var historical = _parse_historical_data();
            if (historical is not null) result.set("when", historical);
            var pivots = _parse_pivots();
            if (py.truthy(pivots)) result.set("pivots", pivots);
            return result;
        }

        // Separate continuation preserves the upstream fast path unchanged.
        private exp.Expr _parse_table_slow(bool schema, bool joins, IReadOnlyCollection<TokenType>? alias_tokens,
            bool parse_bracket, bool is_db_reference, bool parse_partition)
        {
            var bracket = parse_bracket ? _parse_bracket(null) : null;
            exp.Expr? bracketTable = bracket is not null ? expression(new exp.Table(@this: bracket)) : null;
            List<object?>? rows = _match_text_seq(["ROWS", "FROM"])
                ? _parse_wrapped_csv(() => _parse_table()).Cast<object?>().ToList() : null;
            exp.Expr? rowsTable = py.truthy(rows) ? expression(new exp.Table(rows_from: rows)) : null;
            var only = _match(TokenType.ONLY);
            var result = bracketTable ?? rowsTable ?? _parse_bracket(_parse_table_parts(schema, is_db_reference));
            if (result is null) throw new InvalidOperationException("Table construction returned null");
            if (only) result.set("only", true);
            _match(TokenType.STAR);
            if ((parse_partition || SUPPORTS_PARTITION_SELECTION) && _match(TokenType.PARTITION, advance: false))
                result.set("partition", _parse_partition());
            if (schema) return _parse_schema(result)!;
            if (dialect.ALIAS_POST_VERSION) result.set("version", _parse_version());
            if (dialect.ALIAS_POST_TABLESAMPLE)
            {
                result.set("sample", _parse_table_sample());
                if (py.truthy(result.args.get("sample")) && dialect.ALIAS_POST_VERSION && !py.truthy(result.args.get("version")))
                    result.set("version", _parse_version());
            }
            var alias = _parse_table_alias(alias_tokens is { Count: > 0 } ? alias_tokens : TABLE_ALIAS_TOKENS);
            if (alias is not null)
            {
                result.set("alias", alias);
                if (result is exp.Table && !py.truthy(result.args.get("when")))
                    result.set("when", _parse_historical_data());
            }
            if (_match(TokenType.INDEXED_BY)) result.set("indexed", _parse_table_parts());
            else if (_match_text_seq(["NOT", "INDEXED"])) result.set("indexed", false);
            if (result is exp.Table && _match_text_seq(["AT"]))
                throw new NotPortedException("Parser._parse_table / Table.to_column AtIndex");
            result.set("hints", _parse_table_hints());
            if (!py.truthy(result.args.get("pivots"))) result.set("pivots", _parse_pivots());
            if (!dialect.ALIAS_POST_TABLESAMPLE) result.set("sample", _parse_table_sample());
            if (!dialect.ALIAS_POST_VERSION) result.set("version", _parse_version());
            if (joins) foreach (var join in _parse_joins(alias_tokens)) result.append("joins", join);
            if (_match_pair(TokenType.WITH, TokenType.ORDINALITY))
            {
                result.set("ordinality", true);
                result.set("alias", _parse_table_alias());
            }
            if (result is exp.Table && result.@this is exp.TableFromRows tableFromRows)
            {
                foreach (var arg in tableFromRows.arg_types.Keys)
                    if (arg != "this") tableFromRows.set(arg, result.args.get(arg));
                result = tableFromRows;
            }
            return result;
        }

    }
}
