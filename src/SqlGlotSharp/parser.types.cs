using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

public static partial class parser
{
    public partial class Parser
    {
        // _parse_types, _parse_struct_types, _parse_json_type_arg and _parse_user_defined_type: see parser.typesfull.cs

        // py: sqlglot/parser.py::Parser._parse_type_size #0000000000
        public exp.DataTypeParam? _parse_type_size()
        {
            var value = _parse_type();
            if (value is null) return null;
            if (value is exp.Column && !py.truthy(value.args.get("table"))) value = new exp.Var(@this: py.upper(value.name));
            return expression(new exp.DataTypeParam(@this: value, expression: _parse_var(any_token: true)));
        }

        // py: sqlglot/parser.py::Parser.build_cast #0000000000
        public exp.Expr build_cast(bool strict, PyDict<string, object?> kwargs)
        {
            if (strict) return expression(new exp.Cast(kwargs));
            kwargs["requires_string"] = dialect.TRY_CAST_REQUIRES_STRING;
            return expression(new exp.TryCast(kwargs));
        }

        // py: sqlglot/parser.py::Parser._parse_cast #0000000000
        public exp.Expr _parse_cast(bool strict, bool? safe = null)
        {
            var @this = _parse_assignment();

            if (!_match(TokenType.ALIAS))
            {
                if (_match(TokenType.COMMA)) return expression(new exp.CastToStrType(@this: @this, to: _parse_string()));

                raise_error("Expected AS after CAST");
            }

            exp.Expr? fmt = null;
            var to = _parse_types(with_collation: true);

            exp.Expr? @default = null;
            if (_match(TokenType.DEFAULT))
            {
                @default = _parse_bitwise();
                _match_text_seq(["ON", "CONVERSION", "ERROR"]);
            }

            if (_match_set([TokenType.FORMAT, TokenType.COMMA]))
            {
                var fmt_string = _parse_wrapped(() => _parse_string(), optional: true);
                fmt = _parse_at_time_zone(fmt_string);

                to ??= exp.DataType.build(exp.DType.UNKNOWN);
                if (to.@this is exp.DType to_type && exp.DataType.TEMPORAL_TYPES_.Contains(to_type))
                {
                    var format_mapping = dialect.FORMAT_MAPPING.Count > 0
                        ? dialect.FORMAT_MAPPING.ToDictionary(pair => pair.Key, pair => py.str(pair.Value))
                        : (IReadOnlyDictionary<string, string>)dialect.TIME_MAPPING;
                    var format_trie = dialect.FORMAT_TRIE.Count > 0 ? dialect.FORMAT_TRIE : dialect.TIME_TRIE;
                    var format_text = global::SqlGlotSharp.time.format_time(
                        fmt_string is not null ? (string)fmt_string.@this! : "", format_mapping, format_trie);
                    var format_literal = exp.Literal.@string(format_text!);
                    @this = to_type == exp.DType.DATE
                        ? expression(new exp.StrToDate(@this: @this, format: format_literal, safe: safe))
                        : expression(new exp.StrToTime(@this: @this, format: format_literal, safe: safe));

                    if (fmt is exp.AtTimeZone at_time_zone && @this is exp.StrToTime) @this.set("zone", at_time_zone.args["zone"]);
                    return @this!;
                }
            }
            else if (to is null) raise_error("Expected TYPE after CAST");
            else if (to is exp.Identifier) to = exp.DataType.from_str(to.name, dialect: dialect, udt: true);
            else to = _parse_char_charset(to);

            return build_cast(strict, new PyDict<string, object?>
            {
                ["this"] = @this, ["to"] = to, ["format"] = fmt, ["safe"] = safe,
                ["action"] = _parse_var_from_options_core(key => CAST_ACTIONS.TryGetValue(key, out var v)
                    ? v.Select(item => new[] { item }).ToList() : null, raise_unmatched: false),
                ["default"] = @default
            });
        }

        // py: sqlglot/parser.py::Parser._parse_char_charset #0000000000
        public exp.Expr? _parse_char_charset(exp.Expr? to)
        {
            // MySQL: CHAR[(N)] CHARACTER SET charset_name, the length is kept
            if (to is exp.DataType && Equals(to.@this, exp.DType.CHAR)
                && (_match(TokenType.CHARACTER_SET) || _match_text_seq(["CHARACTER", "SET"])))
            {
                to.set("this", exp.DType.CHARACTER_SET);
                to.set("kind", _parse_var_or_string());
            }
            return to;
        }

        // py: sqlglot/parser.py::Parser._parse_convert #0000000000
        public virtual exp.Expr? _parse_convert(bool strict, bool? safe = null)
        {
            var @this = _parse_bitwise();

            exp.Expr? to;
            if (_match(TokenType.USING))
                to = new exp.DataType(@this: exp.DType.CHARACTER_SET, kind: _parse_charset_name());
            else if (_match(TokenType.COMMA)) to = _parse_char_charset(_parse_types());
            else to = null;

            return build_cast(strict, new PyDict<string, object?> { ["this"] = @this, ["to"] = to, ["safe"] = safe });
        }

        // py: sqlglot/parser.py::Parser._parse_decode #0000000000
        public exp.Expr? _parse_decode()
        {
            var args = _parse_csv(() => _parse_disjunction());

            if (args.Count < 3)
                return expression(new exp.Decode(@this: helper.seq_get(args, 0), charset: helper.seq_get(args, 1)));

            return expression(new exp.DecodeCase(expressions: args.Cast<object?>().ToList()));
        }

        // py: sqlglot/parser.py::Parser._parse_xml_element #0000000000
        public exp.Expr _parse_xml_element()
        {
            exp.Expr? @this;
            bool? evalname;
            if (_match_text_seq(["EVALNAME"]))
            {
                evalname = true;
                @this = _parse_bitwise();
            }
            else
            {
                evalname = null;
                _match_text_seq(["NAME"]);
                @this = _parse_id_var();
            }

            return expression(new exp.XMLElement(@this: @this,
                expressions: _match(TokenType.COMMA) ? _parse_csv(() => _parse_bitwise()).Cast<object?>().ToList() : false,
                evalname: evalname));
        }

        // py: sqlglot/parser.py::Parser._parse_xml_namespace #0000000000
        public List<exp.Expr> _parse_xml_namespace()
        {
            List<exp.Expr> namespaces = [];

            while (true)
            {
                var uri = _match(TokenType.DEFAULT) ? _parse_string() : _parse_alias(_parse_string());
                namespaces.Add(expression(new exp.XMLNamespace(@this: uri)));
                if (!_match(TokenType.COMMA)) break;
            }

            return namespaces;
        }

        // py: sqlglot/parser.py::Parser._parse_xml_table #0000000000
        public exp.Expr _parse_xml_table()
        {
            List<exp.Expr>? namespaces = null;
            List<exp.Expr>? passing = null;
            List<exp.Expr>? columns = null;

            if (_match_text_seq(["XMLNAMESPACES", "("]))
            {
                namespaces = _parse_xml_namespace();
                _match_text_seq([")", ","]);
            }

            var @this = _parse_string();

            if (_match_text_seq(["PASSING"]))
            {
                // The BY VALUE keywords are optional and are provided for semantic clarity
                _match_text_seq(["BY", "VALUE"]);
                passing = _parse_csv<exp.Expr>(() => _parse_column());
            }

            var by_ref = _match_text_seq(["RETURNING", "SEQUENCE", "BY", "REF"]);

            if (_match_text_seq(["COLUMNS"])) columns = _parse_csv<exp.Expr>(() => _parse_field_def());

            return expression(new exp.XMLTable(@this: @this, namespaces: namespaces?.Cast<object?>().ToList(),
                passing: passing?.Cast<object?>().ToList(), columns: columns?.Cast<object?>().ToList(), by_ref: by_ref));
        }

        // py: sqlglot/parser.py::Parser._parse_match_against #0000000000
        public exp.Expr _parse_match_against()
        {
            List<exp.Expr> expressions;
            if (_match_text_seq(["TABLE"]))
            {
                // parse SingleStore MATCH(TABLE ...) syntax
                // https://docs.singlestore.com/cloud/reference/sql-reference/full-text-search-functions/match/
                expressions = [];
                var table = _parse_table();
                if (table is not null) expressions = [table];
            }
            else expressions = _parse_csv<exp.Expr>(() => _parse_column());

            _match_text_seq([")", "AGAINST", "("]);

            var @this = _parse_string();

            string? modifier;
            if (_match_text_seq(["IN", "NATURAL", "LANGUAGE", "MODE"]))
            {
                modifier = "IN NATURAL LANGUAGE MODE";
                if (_match_text_seq(["WITH", "QUERY", "EXPANSION"])) modifier = $"{modifier} WITH QUERY EXPANSION";
            }
            else if (_match_text_seq(["IN", "BOOLEAN", "MODE"])) modifier = "IN BOOLEAN MODE";
            else if (_match_text_seq(["WITH", "QUERY", "EXPANSION"])) modifier = "WITH QUERY EXPANSION";
            else modifier = null;

            return expression(new exp.MatchAgainst(@this: @this, expressions: expressions.Cast<object?>().ToList(), modifier: modifier));
        }

        // py: sqlglot/parser.py::Parser._parse_gap_fill #0000000000
        public exp.Expr _parse_gap_fill()
        {
            _match(TokenType.TABLE);
            var @this = _parse_table();

            _match(TokenType.COMMA);
            List<object?> args = [@this];
            args.AddRange(_parse_lambda_list());

            var gap_fill = new exp.GapFill().from_arg_list(args);
            return validate_expression(gap_fill, args);
        }

        private IEnumerable<object?> _parse_lambda_list() => _parse_csv<exp.Expr>(() => _parse_lambda()).Cast<object?>();

        // py: sqlglot/parser.py::Parser._parse_weight_string #0000000000
        public exp.Expr _parse_weight_string()
        {
            var name = _tokens[(int)_index - 2].text;
            var args = _parse_csv<exp.Expr>(() => _parse_assignment());

            // MySQL also accepts an undocumented WEIGHT_STRING(str, n, n, n) form
            if (args.Count != 1)
                return expression(new exp.Anonymous(@this: name, expressions: args.Cast<object?>().ToList()));

            return expression(new exp.WeightString(@this: args[0], to: _match(TokenType.ALIAS) ? _parse_types() : false));
        }
    }
}
