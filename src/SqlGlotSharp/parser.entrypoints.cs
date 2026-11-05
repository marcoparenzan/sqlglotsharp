using Token = SqlGlotSharp.tokenizer_core.Token;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

// Entrypoint/chunkification algorithms from parser.py. Statement dispatch is bounded:
// unavailable special statements and command/while parsing never become approximate ASTs.
public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser.STATEMENT_PARSERS #0000000000
        public static readonly PyDict<TokenType, Func<Parser, exp.Expr>> STATEMENT_PARSERS_ = new()
        {
            [TokenType.ALTER] = p => p._parse_alter(),
            [TokenType.ANALYZE] = p => p._parse_analyze(),
            [TokenType.BEGIN] = p => p._parse_transaction(),
            [TokenType.CACHE] = p => p._parse_cache(),
            [TokenType.COMMENT] = p => p._parse_comment(),
            [TokenType.COMMIT] = p => p._parse_commit_or_rollback(),
            [TokenType.COPY] = p => p._parse_copy(),
            [TokenType.CREATE] = p => p._parse_create(),
            [TokenType.DECLARE] = p => p._parse_declare(),
            [TokenType.DELETE] = p => p._parse_delete(),
            [TokenType.DESC] = p => p._parse_describe(),
            [TokenType.DESCRIBE] = p => p._parse_describe(),
            [TokenType.DROP] = p => p._parse_drop(),
            [TokenType.GRANT] = p => p._parse_grant(),
            [TokenType.REVOKE] = p => p._parse_revoke(),
            [TokenType.INSERT] = p => p._parse_insert(),
            [TokenType.KILL] = p => p._parse_kill(),
            [TokenType.LOAD] = p => p._parse_load(),
            [TokenType.MERGE] = p => p._parse_merge(),
            [TokenType.PIVOT] = p => p._parse_simplified_pivot(),
            [TokenType.PRAGMA] = p => p.expression(new exp.Pragma(@this: p._parse_expression())),
            [TokenType.REFRESH] = _ => throw new NotPortedException("sqlglot/parser.py::Parser._parse_refresh"),
            [TokenType.ROLLBACK] = p => p._parse_commit_or_rollback(),
            [TokenType.SET] = p => p._parse_set(),
            [TokenType.TRUNCATE] = p => p._parse_truncate_table()!,
            [TokenType.UNCACHE] = p => p._parse_uncache(),
            [TokenType.UNPIVOT] = p => p._parse_simplified_pivot(is_unpivot: true),
            [TokenType.UPDATE] = p => p._parse_update(),
            [TokenType.USE] = p => p._parse_use(),
            [TokenType.SEMICOLON] = _ => new exp.Semicolon(),
        };
        public virtual PyDict<TokenType, Func<Parser, exp.Expr>> STATEMENT_PARSERS => STATEMENT_PARSERS_;

        // py: sqlglot/parser.py::Parser.EXPRESSION_PARSERS #0000000000
        public static readonly PyDict<Type, Func<Parser, exp.Expr?>> EXPRESSION_PARSERS_ = new()
        {
            [typeof(exp.Cluster)] = p => p._parse_sort<exp.Cluster>(TokenType.CLUSTER_BY),
            [typeof(exp.Column)] = p => p._parse_column(),
            [typeof(exp.ColumnDef)] = p => p._parse_column_def(p._parse_column()),
            [typeof(exp.Condition)] = p => p._parse_disjunction(),
            [typeof(exp.DataType)] = p => p._parse_types(allow_identifiers: false, schema: true),
            [typeof(exp.Expr)] = p => p._parse_expression(),
            [typeof(exp.From)] = p => p._parse_from(joins: true),
            [typeof(exp.GrantPrincipal)] = p => p._parse_grant_principal(),
            [typeof(exp.GrantPrivilege)] = p => p._parse_grant_privilege(),
            [typeof(exp.Group)] = p => p._parse_group(),
            [typeof(exp.Having)] = p => p._parse_having(),
            [typeof(exp.Hint)] = p => p._parse_hint_body(),
            [typeof(exp.Identifier)] = p => p._parse_id_var(),
            [typeof(exp.Join)] = p => p._parse_join(),
            [typeof(exp.Lambda)] = p => p._parse_lambda(),
            [typeof(exp.Lateral)] = p => p._parse_lateral(),
            [typeof(exp.Limit)] = p => p._parse_limit(),
            [typeof(exp.Offset)] = p => p._parse_offset(),
            [typeof(exp.Order)] = p => p._parse_order(),
            [typeof(exp.Ordered)] = p => p._parse_ordered(),
            [typeof(exp.Properties)] = p => p._parse_properties(),
            [typeof(exp.PartitionedByProperty)] = p => p._parse_partitioned_by(),
            [typeof(exp.Qualify)] = p => p._parse_qualify(),
            [typeof(exp.Returning)] = p => p._parse_returning(),
            [typeof(exp.Select)] = p => p._parse_select(),
            [typeof(exp.Sort)] = p => p._parse_sort<exp.Sort>(TokenType.SORT_BY),
            [typeof(exp.Table)] = p => p._parse_table_parts(),
            [typeof(exp.TableAlias)] = p => p._parse_table_alias(),
            [typeof(exp.Tuple)] = p => p._parse_value(values: false),
            [typeof(exp.Whens)] = p => p._parse_when_matched(),
            [typeof(exp.Where)] = p => p._parse_where(),
            [typeof(exp.Window)] = p => p._parse_named_window(),
            [typeof(exp.With)] = p => p._parse_with(),
        };
        public virtual PyDict<Type, Func<Parser, exp.Expr?>> EXPRESSION_PARSERS => EXPRESSION_PARSERS_;

        // py: sqlglot/parser.py::Parser.parse_into #4212c6e688
        /// <summary>
        /// Parses a list of tokens into a given Expr type. If a collection of Expr
        /// types is given instead, this method will try to parse the token list into each one
        /// of them, stopping at the first for which the parsing succeeds.
        /// </summary>
        /// <param name="expression_types">A <see cref="Type"/> or a collection of them.</param>
        public List<exp.Expr?> parse_into(object expression_types, List<Token> raw_tokens, string? sql = null)
        {
            // Local renamed from Python's "errors": that name is the parser's own error list here.
            List<SqlGlotSharp.errors.ParseError> parse_errors = [];
            var types = expression_types is Type single ? [single] : ((System.Collections.IEnumerable)expression_types).Cast<Type>().ToList();
            foreach (var expression_type in types)
            {
                // Python raises TypeError here; it is a caller error, not a ParseError.
                if (!EXPRESSION_PARSERS.TryGetValue(expression_type, out var parser))
                    throw new ArgumentException($"No parser registered for {PythonTypeRepr(expression_type)}");
                try
                {
                    return _parse(parser, raw_tokens, sql);
                }
                catch (SqlGlotSharp.errors.ParseError e)
                {
                    e.errors[0]["into_expression"] = expression_type;
                    parse_errors.Add(e);
                }
            }
            var described = expression_types is Type t
                ? PythonTypeRepr(t) : "[" + string.Join(", ", types.Select(PythonTypeRepr)) + "]";
            throw new SqlGlotSharp.errors.ParseError($"Failed to parse '{(py.truthy(sql) ? sql : string.Join(", ", raw_tokens))}' into {described}",
                errors: SqlGlotSharp.errors.merge_errors(parse_errors));
        }

        // Python str(type): "<class 'sqlglot.expressions.query.Table'>".
        private static string PythonTypeRepr(Type type) => $"<class '{serde.PythonClassName(type)}'>";

        // py: sqlglot/parser.py::Parser.parse #335a04ebc4
        /// <summary>
        /// Parses a list of tokens and returns a list of syntax trees, one tree
        /// per parsed SQL statement.
        /// </summary>
        /// <param name="raw_tokens">The list of tokens.</param>
        /// <param name="sql">The original SQL string.</param>
        /// <returns>The list of the produced syntax trees.</returns>
        public List<exp.Expr?> parse(List<Token> raw_tokens, string sql) =>
            _parse(p => p._parse_statement(), raw_tokens, sql);

        // py: sqlglot/parser.py::Parser._parse_batch_statements #0b945e1553
        public List<exp.Expr?> _parse_batch_statements(Func<Parser, exp.Expr?> parse_method,
            bool sep_first_statement = true)
        {
            List<exp.Expr?> expressions = [];
            // Chunkification binds if/while statements with the first statement of the body
            if (sep_first_statement)
            {
                _match(TokenType.BEGIN);
                expressions.Add(parse_method(this));
            }
            var chunks_length = _chunks.Count;
            while (_chunk_index < chunks_length)
            {
                _advance_chunk();
                if (_match(TokenType.ELSE, advance: false)) return expressions;
                if (expressions.Count != 0 && !py.truthy(_next) && _match(TokenType.END))
                {
                    expressions.Add(new exp.EndStatement());
                    continue;
                }
                expressions.Add(parse_method(this));
                if (_index < _tokens_size) raise_error("Invalid expression / Unexpected token");
                check_errors();
            }
            return expressions;
        }

        // py: sqlglot/parser.py::Parser._parse #c95288ecb6
        public List<exp.Expr?> _parse(Func<Parser, exp.Expr?> parse_method, List<Token> raw_tokens,
            string? sql = null)
        {
            reset();
            this.sql = sql ?? "";
            var total = raw_tokens.Count;
            List<List<Token>> chunks = [[]];
            for (var i = 0; i < total; i++)
            {
                var token = raw_tokens[i];
                if (token.token_type == TokenType.SEMICOLON)
                {
                    if (token.comments.Count != 0) chunks.Add([token]);
                    if (i < total - 1) chunks.Add([]);
                }
                else chunks[^1].Add(token);
            }
            _chunks = chunks;
            return _parse_batch_statements(parse_method, sep_first_statement: false);
        }

        // py: sqlglot/parser.py::Parser._parse_statement #0000000000
        public virtual exp.Expr? _parse_statement()
        {
            if (!py.truthy(_curr)) return null;
            if (_match_set(STATEMENT_PARSERS.Keys.ToArray()))
            {
                var comments = _prev_comments;
                var stmt = STATEMENT_PARSERS[_prev.token_type](this);
                stmt.add_comments(comments, prepend: true);
                return stmt;
            }
            // The host exposes a tokenizer factory rather than tokenizer_class.
            if (_match_set(dialect.tokenizer().COMMANDS))
                return _parse_command();
            if (_match_text_seq(["WHILE"]))
                return _parse_whileblock();
            var expression = _parse_expression();
            expression = expression is not null ? _parse_set_operations(expression) : _parse_select();
            if (expression is exp.Subquery && _match(TokenType.PIPE_GT, advance: false))
                expression = _parse_pipe_syntax_query(expression);
            return _parse_query_modifiers(expression);
        }
    }
}
