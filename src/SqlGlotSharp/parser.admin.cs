using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

// CACHE, UNCACHE, LOAD DATA, DESCRIBE and ANALYZE statements (parser.py).
public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser.ANALYZE_EXPRESSION_PARSERS #0000000000
        public static readonly PyDict<string, Func<Parser, exp.Expr?>> ANALYZE_EXPRESSION_PARSERS_ = new()
        {
            ["ALL"] = self => self._parse_analyze_columns(),
            ["COMPUTE"] = self => self._parse_analyze_statistics(),
            ["DELETE"] = self => self._parse_analyze_delete(),
            ["DROP"] = self => self._parse_analyze_histogram(),
            ["ESTIMATE"] = self => self._parse_analyze_statistics(),
            ["LIST"] = self => self._parse_analyze_list(),
            ["PREDICATE"] = self => self._parse_analyze_columns(),
            ["UPDATE"] = self => self._parse_analyze_histogram(),
            ["VALIDATE"] = self => self._parse_analyze_validate(),
        };
        public virtual PyDict<string, Func<Parser, exp.Expr?>> ANALYZE_EXPRESSION_PARSERS => ANALYZE_EXPRESSION_PARSERS_;

        // py: sqlglot/parser.py::Parser._parse_uncache #0000000000
        public exp.Expr _parse_uncache()
        {
            if (!_match(TokenType.TABLE)) raise_error("Expecting TABLE after UNCACHE");

            return expression(new exp.Uncache(exists: _parse_exists(), @this: _parse_table(schema: true)));
        }

        // py: sqlglot/parser.py::Parser._parse_cache #0000000000
        public exp.Expr _parse_cache()
        {
            var lazy = _match_text_seq(["LAZY"]);
            _match(TokenType.TABLE);
            var table = _parse_table(schema: true);

            List<object?> options = [];
            if (_match_text_seq(["OPTIONS"]))
            {
                _match_l_paren();
                var k = _parse_string();
                _match(TokenType.EQ);
                var v = _parse_string();
                options = [k, v];
                _match_r_paren();
            }

            _match(TokenType.ALIAS);
            return expression(new exp.Cache(@this: table, lazy: lazy, options: options, expression: _parse_select(nested: true)));
        }

        // py: sqlglot/parser.py::Parser._parse_load #0000000000
        public exp.Expr _parse_load()
        {
            if (_match_text_seq(["DATA"]))
            {
                var local = _match_text_seq(["LOCAL"]);
                _match_text_seq(["INPATH"]);
                var inpath = _parse_string();
                var overwrite = _match(TokenType.OVERWRITE);
                bool? temp = null;
                if (_match(TokenType.INTO))
                {
                    temp = _match(TokenType.TEMPORARY);
                    _match(TokenType.TABLE);
                }

                return expression(new exp.LoadData(
                    @this: _parse_table(schema: true),
                    local: local,
                    overwrite: overwrite,
                    temp: temp,
                    inpath: inpath,
                    files: _match_text_seq(["FROM", "FILES"]) ? new exp.Properties(expressions: _parse_wrapped_properties()) : false,
                    partition: _parse_partition(),
                    input_format: _match_text_seq(["INPUTFORMAT"]) ? _parse_string() : false,
                    serde: _match_text_seq(["SERDE"]) ? _parse_string() : false));
            }
            return _parse_as_command(_prev);
        }

        // py: sqlglot/parser.py::Parser._parse_describe #0000000000
        public exp.Expr _parse_describe()
        {
            var kind = _match_set(CREATABLES) ? _prev.text : null;
            var style = _match_texts(DESCRIBE_STYLES) ? py.upper(_prev.text) : null;
            if (_match(TokenType.DOT))
            {
                style = null;
                _retreat(_index - 2);
            }

            var format = _match(TokenType.FORMAT, advance: false) ? _parse_property() : null;

            exp.Expr? @this;
            if (_match_set(STATEMENT_PARSERS.Keys, advance: false)) @this = _parse_statement();
            else @this = _parse_table(schema: true);

            var properties = _parse_properties();
            var expressions = properties?.args.get("expressions");
            var partition = _parse_partition();
            return expression(new exp.Describe(
                @this: @this,
                style: style,
                kind: kind,
                expressions: expressions,
                partition: partition,
                format: format,
                as_json: _match_text_seq(["AS", "JSON"])));
        }

        // py: sqlglot/parser.py::Parser._parse_analyze #0000000000
        public exp.Expr _parse_analyze()
        {
            var start = _prev;
            // https://duckdb.org/docs/sql/statements/analyze
            if (!py.truthy(_curr)) return expression(new exp.Analyze());

            List<object?> options = [];
            while (_match_texts(ANALYZE_STYLES))
            {
                if (py.upper(_prev.text) == "BUFFER_USAGE_LIMIT") options.Add($"BUFFER_USAGE_LIMIT {new generator.Generator().sql(_parse_number())}");
                else options.Add(py.upper(_prev.text));
            }

            List<exp.Expr>? tables = null;
            exp.Expr? inner_expression = null;

            var kind = py.truthy(_curr) ? py.upper(_curr.text) : null;

            if (_match(TokenType.TABLE)) tables = _parse_csv<exp.Expr>(() => _parse_table_parts());
            else if (_match(TokenType.INDEX)) tables = EnsureExprList(_parse_table_parts());
            else if (_match_text_seq(["TABLES"]))
            {
                if (_match_set([TokenType.FROM, TokenType.IN]))
                {
                    kind = $"{kind} {py.upper(_prev.text)}";
                    tables = EnsureExprList(_parse_table(schema: true, is_db_reference: true));
                }
            }
            else if (_match_text_seq(["DATABASE"])) tables = EnsureExprList(_parse_table(schema: true, is_db_reference: true));
            else if (_match_text_seq(["CLUSTER"])) tables = EnsureExprList(_parse_table());
            // Try matching inner expr keywords before fallback to parse table.
            else if (_match_texts(ANALYZE_EXPRESSION_PARSERS.Keys))
            {
                kind = null;
                inner_expression = ANALYZE_EXPRESSION_PARSERS[py.upper(_prev.text)](this);
            }
            else
            {
                // Empty kind  https://prestodb.io/docs/current/sql/analyze.html
                kind = null;
                tables = _parse_csv<exp.Expr>(() => _parse_table_parts());
            }

            var partition = _try_parse(() => _parse_partition()!);
            if (partition is null && _match_texts(PARTITION_KEYWORDS)) return _parse_as_command(start);

            // https://docs.starrocks.io/docs/sql-reference/sql-statements/cbo_stats/ANALYZE_TABLE/
            string? mode = null;
            if (_match_text_seq(["WITH", "SYNC", "MODE"]) || _match_text_seq(["WITH", "ASYNC", "MODE"]))
                mode = $"WITH {py.upper(_tokens[(int)_index - 2].text)} MODE";

            if (_match_texts(ANALYZE_EXPRESSION_PARSERS.Keys))
                inner_expression = ANALYZE_EXPRESSION_PARSERS[py.upper(_prev.text)](this);

            var properties = _parse_properties();
            return expression(new exp.Analyze(
                kind: kind,
                tables: (tables ?? []).Cast<object?>().ToList(),
                mode: mode,
                partition: partition,
                properties: properties,
                expression: inner_expression,
                options: options));
        }

        // py: sqlglot/parser.py::Parser._parse_analyze_statistics #0000000000
        public exp.Expr _parse_analyze_statistics()
        {
            string? @this = null;
            var kind = py.upper(_prev.text);
            var option = _match_text_seq(["DELTA"]) ? py.upper(_prev.text) : null;
            List<object?> expressions = [];

            if (!_match_text_seq(["STATISTICS"])) raise_error("Expecting token STATISTICS");

            if (_match_text_seq(["NOSCAN"])) @this = "NOSCAN";
            else if (_match(TokenType.FOR))
            {
                if (_match_text_seq(["ALL", "COLUMNS"])) @this = "FOR ALL COLUMNS";
                if (_match_text_seq(["COLUMNS"]))
                {
                    @this = "FOR COLUMNS";
                    expressions = _parse_csv<exp.Expr>(() => _parse_column_reference()).Cast<object?>().ToList();
                }
            }
            else if (_match_text_seq(["SAMPLE"]))
            {
                var sample = _parse_number();
                expressions = [expression(new exp.AnalyzeSample(sample: sample, kind: _match(TokenType.PERCENT) ? py.upper(_prev.text) : null))];
            }

            return expression(new exp.AnalyzeStatistics(kind: kind, option: option, @this: @this, expressions: expressions));
        }

        // py: sqlglot/parser.py::Parser._parse_analyze_validate #0000000000
        public exp.Expr _parse_analyze_validate()
        {
            string? kind = null;
            string? @this = null;
            exp.Expr? expression_ = null;
            if (_match_text_seq(["REF", "UPDATE"]))
            {
                kind = "REF";
                @this = "UPDATE";
                if (_match_text_seq(["SET", "DANGLING", "TO", "NULL"])) @this = "UPDATE SET DANGLING TO NULL";
            }
            else if (_match_text_seq(["STRUCTURE"]))
            {
                kind = "STRUCTURE";
                if (_match_text_seq(["CASCADE", "FAST"])) @this = "CASCADE FAST";
                else if (_match_text_seq(["CASCADE", "COMPLETE"]) && _match_texts(["ONLINE", "OFFLINE"]))
                {
                    @this = $"CASCADE COMPLETE {py.upper(_prev.text)}";
                    expression_ = _parse_into();
                }
            }

            return expression(new exp.AnalyzeValidate(kind: kind, @this: @this, expression: expression_));
        }

        // py: sqlglot/parser.py::Parser._parse_analyze_columns #0000000000
        public exp.Expr? _parse_analyze_columns()
        {
            var @this = py.upper(_prev.text);
            if (_match_text_seq(["COLUMNS"])) return expression(new exp.AnalyzeColumns(@this: $"{@this} {py.upper(_prev.text)}"));
            return null;
        }

        // py: sqlglot/parser.py::Parser._parse_analyze_delete #0000000000
        public exp.Expr? _parse_analyze_delete()
        {
            var kind = _match_text_seq(["SYSTEM"]) ? py.upper(_prev.text) : null;
            if (_match_text_seq(["STATISTICS"])) return expression(new exp.AnalyzeDelete(kind: kind));
            return null;
        }

        // py: sqlglot/parser.py::Parser._parse_analyze_list #0000000000
        public exp.Expr? _parse_analyze_list()
        {
            if (_match_text_seq(["CHAINED", "ROWS"])) return expression(new exp.AnalyzeListChainedRows(expression: _parse_into()));
            return null;
        }

        // py: sqlglot/parser.py::Parser._parse_analyze_histogram #0000000000
        public exp.Expr _parse_analyze_histogram()
        {
            var @this = py.upper(_prev.text);
            exp.Expr? expression_ = null;
            List<object?> expressions = [];
            string? update_options = null;

            if (_match_text_seq(["HISTOGRAM", "ON"]))
            {
                expressions = _parse_csv<exp.Expr>(() => _parse_column_reference()).Cast<object?>().ToList();
                List<object?> with_expressions = [];
                while (_match(TokenType.WITH))
                {
                    // https://docs.starrocks.io/docs/sql-reference/sql-statements/cbo_stats/ANALYZE_TABLE/
                    if (_match_texts(["SYNC", "ASYNC"]))
                    {
                        if (_match_text_seq(["MODE"], advance: false))
                        {
                            with_expressions.Add($"{py.upper(_prev.text)} MODE");
                            _advance();
                        }
                    }
                    else
                    {
                        var buckets = _parse_number();
                        if (_match_text_seq(["BUCKETS"])) with_expressions.Add($"{new generator.Generator().sql(buckets)} BUCKETS");
                    }
                }
                if (with_expressions.Count > 0) expression_ = expression(new exp.AnalyzeWith(expressions: with_expressions));

                if (_match_texts(["MANUAL", "AUTO"]) && _match(TokenType.UPDATE, advance: false))
                {
                    update_options = py.upper(_prev.text);
                    _advance();
                }
                else if (_match_text_seq(["USING", "DATA"])) expression_ = expression(new exp.UsingData(@this: _parse_string()));
            }

            return expression(new exp.AnalyzeHistogram(@this: @this, expression: expression_, expressions: expressions, update_options: update_options));
        }
    }
}
