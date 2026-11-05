using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser.ALTER_PARSERS #0000000000
        public static readonly PyDict<string, Func<Parser, object?>> ALTER_PARSERS_ = new()
        {
            ["ADD"] = self => self._parse_alter_table_add(),
            ["AS"] = self => self._parse_select(),
            ["ALTER"] = self => self._parse_alter_table_alter(),
            ["CLUSTER BY"] = self => self._parse_cluster_property(),
            ["DELETE"] = self => self.expression(new exp.Delete(where: self._parse_where())),
            ["DROP"] = self => self._parse_alter_table_drop(),
            ["RENAME"] = self => self._parse_alter_table_rename(),
            ["SET"] = self => self._parse_alter_table_set(),
            ["SWAP"] = self => self.expression(new exp.SwapTable(@this: self._match(TokenType.WITH) ? self._parse_table(schema: true) : false)),
        };
        public virtual PyDict<string, Func<Parser, object?>> ALTER_PARSERS => ALTER_PARSERS_;

        // py: sqlglot/parser.py::Parser.ALTER_ALTER_PARSERS #0000000000
        public static readonly PyDict<string, Func<Parser, exp.Expr?>> ALTER_ALTER_PARSERS_ = new()
        {
            ["DISTKEY"] = self => self._parse_alter_diststyle(),
            ["DISTSTYLE"] = self => self._parse_alter_diststyle(),
            ["SORTKEY"] = self => self._parse_alter_sortkey(),
            ["COMPOUND"] = self => self._parse_alter_sortkey(compound: true),
        };
        public virtual PyDict<string, Func<Parser, exp.Expr?>> ALTER_ALTER_PARSERS => ALTER_ALTER_PARSERS_;

        private static List<exp.Expr> EnsureExprList(object? value) =>
            helper.ensure_list(value).Cast<object?>().Where(item => item is not null).Cast<exp.Expr>().ToList();

        private static List<exp.Expr> AlterList(exp.Expr? node) => node is null ? [] : [node];

        // py: sqlglot/parser.py::Parser._parse_column_def_with_exists
        public exp.ColumnDef? _parse_column_def_with_exists()
        {
            var start = _index;
            _match(TokenType.COLUMN);
            var exists = _parse_exists(not_: true);
            var node = _parse_field_def();
            if (node is not exp.ColumnDef column) { _retreat(start); return null; }
            column.set("exists", exists);
            return column;
        }

        public exp.ColumnDef? _parse_add_column() => py.upper(_prev.text) == "ADD"
            ? _parse_column_def_with_exists() : null;

        // py: sqlglot/parser.py::Parser._parse_alter_table_add
        public List<exp.Expr> _parse_alter_table_add()
        {
            exp.Expr? ParseAddition()
            {
                _match_text_seq(["ADD"]);
                if (_match_set(ADD_CONSTRAINT_TOKENS, advance: false)
                    || _match_texts(ADD_CONSTRAINT_KEYWORDS.Cast<string>(), advance: false))
                    return expression(new exp.AddConstraint(new PyDict<string, object?>
                    { ["expressions"] = _parse_csv(_parse_constraint).Cast<object?>().ToList() }));
                var column = _parse_add_column();
                if (column is not null) return column;
                var exists = _parse_exists(not_: true);
                if (_match_pair(TokenType.PARTITION, TokenType.L_PAREN, advance: false))
                    return expression(new exp.AddPartition(exists: exists, @this: _parse_field(any_token: true),
                        location: _match_text_seq(["LOCATION"], advance: false) ? _parse_property() : false));
                return null;
            }
            if (!_match_set(ADD_CONSTRAINT_TOKENS, advance: false)
                && (!dialect.ALTER_TABLE_ADD_REQUIRED_FOR_EACH_COLUMN || _match_text_seq(["COLUMNS"])))
            {
                var schema = _parse_schema();
                return schema is not null ? [schema] : _parse_csv<exp.Expr>(_parse_column_def_with_exists);
            }
            return _parse_csv(ParseAddition);
        }

        // py: sqlglot/parser.py::Parser._parse_alter_table_alter
        public virtual exp.Expr? _parse_alter_table_alter()
        {
            if (_match_texts(ALTER_ALTER_PARSERS.Keys)) return ALTER_ALTER_PARSERS[py.upper(_prev.text)](this);

            // Many dialects support the ALTER [COLUMN] syntax, so if there is no
            // keyword after ALTER we default to parsing this statement
            _match(TokenType.COLUMN);
            var exists = _parse_exists();
            var column = _parse_field(any_token: true);
            object? optionalExists = exists ? true : null;
            if (_match_pair(TokenType.DROP, TokenType.DEFAULT))
                return expression(new exp.AlterColumn(new PyDict<string, object?>
                { ["this"] = column, ["drop"] = true, ["exists"] = optionalExists }));
            if (_match_pair(TokenType.SET, TokenType.DEFAULT))
                return expression(new exp.AlterColumn(new PyDict<string, object?>
                { ["this"] = column, ["default"] = _parse_disjunction(), ["exists"] = optionalExists }));
            if (_match(TokenType.COMMENT))
                return expression(new exp.AlterColumn(new PyDict<string, object?>
                { ["this"] = column, ["comment"] = _parse_string(), ["exists"] = optionalExists }));
            if (_match_text_seq(["DROP", "NOT", "NULL"]))
                return expression(new exp.AlterColumn(new PyDict<string, object?>
                { ["this"] = column, ["drop"] = true, ["allow_null"] = true, ["exists"] = optionalExists }));
            if (_match_text_seq(["SET", "NOT", "NULL"]))
                return expression(new exp.AlterColumn(new PyDict<string, object?>
                { ["this"] = column, ["allow_null"] = false, ["exists"] = optionalExists }));
            if (_match_text_seq(["SET", "VISIBLE"]))
                return expression(new exp.AlterColumn(new PyDict<string, object?>
                { ["this"] = column, ["visible"] = "VISIBLE", ["exists"] = optionalExists }));
            if (_match_text_seq(["SET", "INVISIBLE"]))
                return expression(new exp.AlterColumn(new PyDict<string, object?>
                { ["this"] = column, ["visible"] = "INVISIBLE", ["exists"] = optionalExists }));
            _match_text_seq(["SET", "DATA"]);
            var dtype = _match_text_seq(["TYPE"]) || !ALTER_COLUMN_TYPE_REQUIRES_KEYWORD ? _parse_types() : null;
            if (dtype is null) return null;
            return expression(new exp.AlterColumn(new PyDict<string, object?>
            {
                ["this"] = column, ["dtype"] = dtype,
                ["collate"] = _match(TokenType.COLLATE) ? (object?)_parse_term() : false,
                ["using"] = _match(TokenType.USING) ? (object?)_parse_disjunction() : false,
                ["exists"] = optionalExists
            }));
        }

        // py: sqlglot/parser.py::Parser._parse_drop_column #0000000000
        public exp.Expr? _parse_drop_column()
        {
            if (!_match(TokenType.DROP)) return null;

            string? kind = null;
            if (!ALTER_DROP_REQUIRES_COLUMN)
            {
                // A bare `DROP [IF EXISTS] c [CASCADE | RESTRICT]` drops a column
                var index = _index;
                _parse_exists();
                if (_curr.token_type is not (TokenType.COLUMN or TokenType.CONSTRAINT)
                    && _match_set(ID_VAR_TOKENS)
                    && (!py.truthy(_curr) || _match(TokenType.COMMA, advance: false)
                        || _match_texts(["CASCADE", "RESTRICT"], advance: false))) kind = "COLUMN";
                _retreat(index);
            }

            return _parse_drop(kind: kind);
        }

        public exp.Expr? _parse_alter_drop_action()
        {
            var node = _parse_drop_column();
            if (node is exp.Drop drop && drop.args.get("kind") is "COLUMN"
                && !dialect.ALTER_TABLE_DROP_REQUIRED_FOR_EACH_COLUMN && _match(TokenType.COMMA))
            {
                var tables = ((List<object?>)drop.args["tables"]!).ToList();
                tables.AddRange(_parse_csv(_parse_column).Cast<object?>());
                drop.set("tables", tables);
            }
            return node;
        }

        public List<exp.Expr> _parse_alter_table_drop()
        {
            var index = _index - 1;
            var partition_exists = _parse_exists();
            if (_match(TokenType.PARTITION, advance: false))
                return _parse_csv<exp.Expr>(() => _parse_drop_partition(exists: partition_exists));
            _retreat(index);
            return _parse_csv(_parse_alter_drop_action);
        }

        public exp.Expr? _parse_alter_table_rename()
        {
            if (_match(TokenType.COLUMN)
                || (!ALTER_RENAME_REQUIRES_COLUMN && !_match_text_seq(["TO"], advance: false)))
            {
                var exists = _parse_exists();
                var oldColumn = _parse_column();
                var to = _match_text_seq(["TO"]);
                var newColumn = _parse_column();
                if (oldColumn is null || !to || newColumn is null) return null;
                return expression(new exp.RenameColumn(new PyDict<string, object?>
                { ["this"] = oldColumn, ["to"] = newColumn, ["exists"] = exists }));
            }
            _match_text_seq(["TO"]);
            return expression(new exp.AlterRename(new PyDict<string, object?> { ["this"] = _parse_table(schema: true) }));
        }

        // py: sqlglot/parser.py::Parser._parse_alter_table_set #0000000000
        public virtual exp.AlterSet _parse_alter_table_set()
        {
            var alter_set = expression(new exp.AlterSet());

            if (_match(TokenType.L_PAREN, advance: false) || _match_text_seq(["TABLE", "PROPERTIES"]))
                alter_set.set("expressions", _parse_wrapped_csv(_parse_assignment).Cast<object?>().ToList());
            else if (_match_text_seq(["FILESTREAM_ON"], advance: false))
                alter_set.set("expressions", new List<object?> { _parse_assignment() });
            else if (_match_texts(["LOGGED", "UNLOGGED"]))
                alter_set.set("option", new exp.Var(@this: py.upper(_prev.text)));
            else if (_match_text_seq(["WITHOUT"]) && _match_texts(["CLUSTER", "OIDS"]))
                alter_set.set("option", new exp.Var(@this: $"WITHOUT {py.upper(_prev.text)}"));
            else if (_match_text_seq(["LOCATION"]))
                alter_set.set("location", _parse_field());
            else if (_match_text_seq(["ACCESS", "METHOD"]))
                alter_set.set("access_method", _parse_field());
            else if (_match_text_seq(["TABLESPACE"]))
                alter_set.set("tablespace", _parse_field());
            else if (_match_text_seq(["FILE", "FORMAT"]) || _match_text_seq(["FILEFORMAT"]))
                alter_set.set("file_format", new List<object?> { _parse_field() });
            else if (_match_text_seq(["STAGE_FILE_FORMAT"]))
                alter_set.set("file_format", _parse_wrapped_options().Cast<object?>().ToList());
            else if (_match_text_seq(["STAGE_COPY_OPTIONS"]))
                alter_set.set("copy_options", _parse_wrapped_options().Cast<object?>().ToList());
            else if (_match_text_seq(["TAG"]) || _match_text_seq(["TAGS"]))
                alter_set.set("tag", _parse_csv(_parse_assignment).Cast<object?>().ToList());
            else
            {
                if (_match_text_seq(["SERDE"])) alter_set.set("serde", _parse_field());

                var properties = _parse_wrapped(() => _parse_properties(), optional: true);
                alter_set.set("expressions", new List<object?> { properties });
            }

            return alter_set;
        }

        // py: sqlglot/parser.py::Parser._parse_drop_partition #0000000000
        public exp.Expr _parse_drop_partition(bool? exists = null) =>
            expression(new exp.DropPartition(expressions: _parse_csv<exp.Expr>(_parse_partition).Cast<object?>().ToList(), exists: exists));

        // py: sqlglot/parser.py::Parser._parse_partition #0000000000
        public virtual exp.Expr? _parse_partition()
        {
            if (!_match_texts(PARTITION_KEYWORDS)) return null;

            return expression(new exp.Partition(subpartition: py.upper(_prev.text) == "SUBPARTITION",
                expressions: _parse_wrapped_csv(() => _parse_disjunction()).Cast<object?>().ToList()));
        }

        // py: sqlglot/parser.py::Parser._parse_cluster_property #0000000000
        public exp.Expr _parse_cluster_property() =>
            expression(new exp.ClusterProperty(expressions: _parse_wrapped_csv(() => _parse_column()).Cast<object?>().ToList()));

        // py: sqlglot/parser.py::Parser._parse_alter_diststyle #0000000000
        public exp.Expr _parse_alter_diststyle()
        {
            if (_match_texts(["ALL", "EVEN", "AUTO"])) return expression(new exp.AlterDistStyle(@this: new exp.Var(@this: py.upper(_prev.text))));

            _match_text_seq(["KEY", "DISTKEY"]);
            return expression(new exp.AlterDistStyle(@this: _parse_column()));
        }

        // py: sqlglot/parser.py::Parser._parse_alter_sortkey #0000000000
        public exp.Expr _parse_alter_sortkey(bool? compound = null)
        {
            if (compound == true) _match_text_seq(["SORTKEY"]);

            if (_match(TokenType.L_PAREN, advance: false))
                return expression(new exp.AlterSortKey(expressions: _parse_wrapped_id_vars().Cast<object?>().ToList(), compound: compound));

            _match_texts(["AUTO", "NONE"]);
            return expression(new exp.AlterSortKey(@this: new exp.Var(@this: py.upper(_prev.text)), compound: compound));
        }

        // py: sqlglot/parser.py::Parser._parse_alter #0000000000
        public exp.Expr _parse_alter()
        {
            var start = _prev;

            var iceberg = _match_text_seq(["ICEBERG"]);

            var alter_token = _match_set(ALTERABLES) ? _prev : null;
            if (alter_token is null) return _parse_as_command(start);
            if (iceberg && alter_token.token_type != TokenType.TABLE) return _parse_as_command(start);

            var exists = _parse_exists();
            var only = _match_text_seq(["ONLY"]);

            exp.Expr? @this;
            object? check;
            exp.Expr? cluster;
            Func<Parser, object?>? parser;
            if (alter_token.token_type == TokenType.SESSION)
            {
                @this = null;
                check = null;
                cluster = null;
                parser = ALTER_PARSERS.get(py.upper(alter_token.text));
            }
            else
            {
                @this = _parse_table(schema: true, parse_partition: ALTER_TABLE_PARTITIONS);
                check = _match_text_seq(["WITH", "CHECK"]);
                cluster = _match(TokenType.ON) ? _parse_on_property() : null;
                parser = _match_texts(ALTER_PARSERS.Keys) ? ALTER_PARSERS[py.upper(_prev.text)] : null;
            }

            List<object?> options = [];
            if (parser is null && alter_token.token_type == TokenType.TABLE && !ALTER_TABLE_REQUIRES_ACTION)
            {
                // Table options may precede the actions (MySQL ENGINE=InnoDB, ADD COLUMN a INT).
                // A clause the property parser rejects, like PARTITION BY RANGE ... MAXVALUE, stays a Command
                var properties = _try_parse(() => _parse_properties()!);
                if (properties is not null) options.AddRange(properties.expressions);
            }

            var actions = EnsureExprList(parser?.Invoke(this));
            if (actions.Count > 0 || options.Count > 0)
            {
                // Actions of different kinds and table options may alternate (MySQL ADD c INT, AUTO_INCREMENT=5, ADD d INT).
                // The comma before an item may have already been consumed by the previous parser
                while (ALTER_TABLE_MIXED_ACTIONS && (_match(TokenType.COMMA) || _prev.token_type == TokenType.COMMA))
                {
                    if (!_match_texts(PROPERTY_PARSERS.Keys, advance: false) && _match_texts(ALTER_PARSERS.Keys))
                    {
                        var next_parser = ALTER_PARSERS[py.upper(_prev.text)];
                        var parsed = EnsureExprList(_try_parse(() => next_parser(this) ?? new List<object?>()));
                        if (parsed.Count == 0 || parsed.Any(action => action is exp.Command)) return _parse_as_command(start);

                        actions.AddRange(parsed);
                    }
                    else
                    {
                        var properties = _try_parse(() => _parse_properties()!);
                        if (properties is null) break;
                        options.AddRange(properties.expressions);
                    }
                }

                var not_valid = _match_text_seq(["NOT", "VALID"]);
                var trailing = _try_parse(() => _parse_properties()!);
                if (trailing is not null) options.AddRange(trailing.expressions);
                var cascade = dialect.ALTER_TABLE_SUPPORTS_CASCADE && _match_text_seq(["CASCADE"]);

                if (!py.truthy(_curr))
                    return expression(new exp.Alter(new PyDict<string, object?>
                    {
                        ["this"] = @this, ["kind"] = py.upper(alter_token.text), ["exists"] = exists,
                        ["actions"] = actions.Cast<object?>().ToList(), ["only"] = only, ["options"] = options,
                        ["cluster"] = cluster, ["not_valid"] = not_valid, ["check"] = check,
                        ["cascade"] = cascade, ["iceberg"] = iceberg
                    }));
            }

            return _parse_as_command(start);
        }
    }
}
