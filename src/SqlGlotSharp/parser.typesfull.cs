using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

// Faithful port of Parser._parse_types and its helpers. Dialect-specific TYPE_CONVERTERS
// (DuckDB, Snowflake) are not ported and stay explicit.
public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser._parse_user_defined_type #1d5c8e0af1
        public virtual exp.Expr? _parse_user_defined_type(exp.Identifier identifier)
        {
            var type_name = identifier.name;

            while (_match(TokenType.DOT))
            {
                var advanced = _advance_any();
                type_name = $"{type_name}.{(py.truthy(advanced) ? _prev.text : "None")}";
            }

            return exp.DataType.from_str(type_name, dialect: dialect, udt: true);
        }

        // py: sqlglot/parser.py::Parser._parse_types #092e9cfe23
        public exp.Expr? _parse_types(bool check_func = false, bool schema = false,
            bool allow_identifiers = true, bool with_collation = false)
        {
            var index = _index;
            exp.Expr? @this = null;
            TokenType? type_token;

            if (_match_set(TYPE_TOKENS)) type_token = _prev.token_type;
            else
            {
                type_token = null;
                var identifier = allow_identifiers ? _parse_id_var(any_token: false, tokens: [TokenType.VAR]) : null;
                if (identifier is exp.Identifier id)
                {
                    if (id.quoted && QUOTED_TYPES_TO_PRESERVE.Contains(id.name))
                        @this = exp.DataType.build(id, udt: true);
                    else
                    {
                        List<tokenizer_core.Token>? tokens;
                        try { tokens = dialect.tokenize(id.name); }
                        catch (errors.TokenError) { tokens = null; }

                        if (tokens is { Count: > 0 })
                        {
                            type_token = tokens[0].token_type;
                            if (TYPE_TOKENS.Contains(type_token.Value))
                            {
                                if (tokens.Count > 1) return exp.DataType.from_str(id.name, dialect: dialect);
                                goto typed;
                            }
                        }
                        if (dialect.SUPPORTS_USER_DEFINED_TYPES)
                            @this = _parse_user_defined_type(id);
                        else
                        {
                            _retreat(_index - 1);
                            return null;
                        }
                    }
                }
                else return null;
            }
        typed:

            if (type_token == TokenType.PSEUDO_TYPE)
                return expression(new exp.PseudoType(@this: py.upper(_prev.text)));

            if (type_token == TokenType.OBJECT_IDENTIFIER)
                return expression(new exp.ObjectIdentifier(@this: py.upper(_prev.text)));

            // https://materialize.com/docs/sql/types/map/
            if (type_token == TokenType.MAP && _match(TokenType.L_BRACKET))
            {
                var key_type = _parse_types(check_func, schema, allow_identifiers);
                if (!_match(TokenType.FARROW))
                {
                    _retreat(index);
                    return null;
                }

                var value_type = _parse_types(check_func, schema, allow_identifiers);
                if (!_match(TokenType.R_BRACKET))
                {
                    _retreat(index);
                    return null;
                }

                return new exp.DataType(@this: exp.DType.MAP, expressions: new List<object?> { key_type, value_type }, nested: true);
            }

            bool In(HashSet<TokenType> set) => type_token is not null && set.Contains(type_token.Value);
            var nested = In(NESTED_TYPE_TOKENS);
            var is_struct = In(STRUCT_TYPE_TOKENS);
            var is_aggregate = In(AGGREGATE_TYPE_TOKENS);
            List<object?>? expressions = null;
            var maybe_func = false;

            if (_match(TokenType.L_PAREN))
            {
                if (is_struct)
                    expressions = _parse_csv(() => _parse_struct_types(type_required: true)).Cast<object?>().ToList();
                else if (nested)
                {
                    expressions = _parse_csv(() => _parse_types(check_func, schema, allow_identifiers)).Cast<object?>().ToList();
                    if (type_token == TokenType.NULLABLE && expressions.Count == 1)
                    {
                        @this = (exp.Expr)expressions[0]!;
                        @this.set("nullable", true);
                        _match_r_paren();
                        return @this;
                    }
                }
                else if (In(ENUM_TYPE_TOKENS))
                    expressions = _parse_csv(() => _parse_equality()).Cast<object?>().ToList();
                else if (type_token == TokenType.REF)
                {
                    // REF(schema.customer) names a target; REF(1) can still be a function call.
                    var target = _try_parse(() => _parse_table_parts(schema: true));
                    if (target is null)
                    {
                        _retreat(index);
                        return null;
                    }
                    expressions = [target];
                }
                else if (type_token == TokenType.JSON)
                    // ClickHouse JSON type supports arguments: JSON(col Type, SKIP col, param=value)
                    expressions = _parse_csv(_parse_json_type_arg).Cast<object?>().ToList();
                else if (is_aggregate)
                {
                    var func_or_ident = _parse_function(anonymous: true)
                        ?? _parse_id_var(any_token: false, tokens: [TokenType.VAR, TokenType.ANY]);
                    if (!py.truthy(func_or_ident)) return null;
                    expressions = [func_or_ident];
                    if (_match(TokenType.COMMA))
                        expressions.AddRange(_parse_csv(() => _parse_types(check_func, schema, allow_identifiers)));
                }
                else
                {
                    expressions = _parse_csv(_parse_type_size).Cast<object?>().ToList();

                    // https://docs.snowflake.com/en/sql-reference/data-types-vector
                    if (type_token == TokenType.VECTOR && expressions.Count == 2)
                        expressions = _parse_vector_expressions(expressions);
                }

                if (!_match(TokenType.R_PAREN))
                {
                    _retreat(index);
                    return null;
                }

                maybe_func = true;
            }

            List<object?>? values = null;

            if (nested && _match(TokenType.LT))
            {
                if (is_struct)
                    expressions = _parse_csv(() => _parse_struct_types(type_required: true)).Cast<object?>().ToList();
                else
                    expressions = _parse_csv(() => _parse_types(check_func, schema, allow_identifiers, with_collation: true))
                        .Cast<object?>().ToList();

                if (!_match(TokenType.GT)) raise_error("Expecting >");

                if (_match_set([TokenType.L_BRACKET, TokenType.L_PAREN]))
                {
                    values = _parse_csv(_parse_disjunction).Cast<object?>().ToList();
                    if (values.Count == 0 && is_struct)
                    {
                        values = null;
                        _retreat(_index - 1);
                    }
                    else _match_set([TokenType.R_BRACKET, TokenType.R_PAREN]);
                }
            }

            if (In(TIMESTAMPS))
            {
                if (_match_text_seq(["WITH", "TIME", "ZONE"]))
                {
                    maybe_func = false;
                    var tz_type = TIMES.Contains(type_token!.Value) ? exp.DType.TIMETZ : exp.DType.TIMESTAMPTZ;
                    @this = new exp.DataType(@this: tz_type, expressions: expressions);
                }
                else if (_match_text_seq(["WITH", "LOCAL", "TIME", "ZONE"]))
                {
                    maybe_func = false;
                    @this = new exp.DataType(@this: exp.DType.TIMESTAMPLTZ, expressions: expressions);
                }
                else if (_match_text_seq(["WITHOUT", "TIME", "ZONE"])) maybe_func = false;
            }
            else if (type_token == TokenType.INTERVAL)
            {
                if (dialect.VALID_INTERVAL_UNITS.Contains(py.upper(_curr.text)))
                {
                    exp.Expr? unit = _parse_var(upper: true);
                    if (_match_text_seq(["TO"]))
                        unit = new exp.IntervalSpan(@this: unit, expression: _parse_var(upper: true));

                    @this = expression(new exp.DataType(@this: expression(new exp.Interval(unit: unit))));
                }
                else @this = expression(new exp.DataType(@this: exp.DType.INTERVAL));
            }
            else if (type_token == TokenType.VOID) @this = new exp.DataType(@this: exp.DType.NULL);

            if (maybe_func && check_func)
            {
                var index2 = _index;
                var peek = _parse_string();

                if (!py.truthy(peek))
                {
                    _retreat(index);
                    return null;
                }

                _retreat(index2);
            }

            if (!py.truthy(@this))
            {
                if (_match_text_seq(["UNSIGNED"]))
                {
                    if (!SIGNED_TO_UNSIGNED_TYPE_TOKEN.TryGetValue(type_token!.Value, out var unsigned_type_token))
                        raise_error($"Cannot convert {type_token} to unsigned.");
                    else type_token = unsigned_type_token;
                }

                // NULLABLE without parentheses can be a column (Presto/Trino)
                if (type_token == TokenType.NULLABLE && !py.truthy(expressions))
                {
                    _retreat(index);
                    return null;
                }

                @this = new exp.DataType(@this: Enum.Parse<exp.DType>(type_token!.Value.ToString()),
                    expressions: expressions, nested: nested);

                // Empty arrays/structs are allowed
                if (values is not null)
                {
                    exp.Expr container = is_struct ? new exp.Struct(expressions: values) : new exp.Array(expressions: values);
                    // exp.cast(container, this, copy=False): the container is never a Cast, so this is a plain Cast.
                    @this = new exp.Cast(@this: container, to: @this);
                }
            }
            else if (py.truthy(expressions)) @this!.set("expressions", expressions);

            // https://materialize.com/docs/sql/types/list/#type-name
            while (_match(TokenType.LIST))
                @this = new exp.DataType(@this: exp.DType.LIST, expressions: new List<object?> { @this }, nested: true);

            index = _index;

            // Postgres supports the INT ARRAY[3] syntax as a synonym for INT[3]
            var matched_array = _match(TokenType.ARRAY);

            while (py.truthy(_curr))
            {
                var datatype_token = _prev.token_type;
                var matched_l_bracket = _match(TokenType.L_BRACKET);

                if ((!matched_l_bracket && !matched_array)
                    || (datatype_token == TokenType.ARRAY && _match(TokenType.R_BRACKET)))
                    // Postgres allows casting empty arrays such as ARRAY[]::INT[],
                    // not to be confused with the fixed size array parsing
                    break;

                matched_array = false;
                var csv = _parse_csv(_parse_disjunction).Cast<object?>().ToList();
                values = csv.Count > 0 ? csv : null;
                if (values is not null && !schema
                    && (!dialect.SUPPORTS_FIXED_SIZE_ARRAYS || datatype_token == TokenType.ARRAY
                        || !_match(TokenType.R_BRACKET, advance: false)))
                {
                    // Retreating here means that we should not parse the following values as part of the data type, e.g. in DuckDB
                    // ARRAY[1] should retreat and instead be parsed into exp.Array in contrast to INT[x][y] which denotes a fixed-size array data type
                    _retreat(index);
                    break;
                }

                @this = new exp.DataType(new PyDict<string, object?>
                {
                    ["this"] = exp.DType.ARRAY, ["expressions"] = new List<object?> { @this },
                    ["values"] = values, ["nested"] = true
                });
                _match(TokenType.R_BRACKET);
            }

            if (TYPE_CONVERTERS.Count != 0 && @this is exp.DataType { @this: exp.DType })
                throw new NotPortedException("sqlglot/parser.py::Parser.TYPE_CONVERTERS (DuckDB/Snowflake type converters)");

            if (with_collation && @this is exp.DataType && _match(TokenType.COLLATE))
                @this.set("collate", _parse_identifier() ?? _parse_column());

            return @this;
        }

        // py: sqlglot/parser.py::Parser._parse_json_type_arg #3a1a55a9ad
        /// <summary>Parse a single argument to ClickHouse's JSON type.</summary>
        public exp.Expr? _parse_json_type_arg()
        {
            // SKIP col or SKIP REGEXP 'pattern'
            if (_match_text_seq(["SKIP"]))
            {
                var regexp = _match(TokenType.RLIKE);
                var arg = _parse_column();
                if (arg is exp.Column skipped) arg = skipped.to_dot();
                return expression(new exp.SkipJSONColumn(regexp: regexp, expression: arg));
            }

            var param_or_col = _parse_column();
            if (param_or_col is not exp.Column column) return null;

            // Parameter: name=value (e.g., max_dynamic_paths=2)
            if (column.parts.Count == 1 && _match(TokenType.EQ))
            {
                var param = column.name;
                var value = _parse_primary();
                return expression(new exp.EQ(@this: new exp.Var(@this: param), expression: value));
            }

            // Column type hint: col_name Type
            var col = column.to_dot();
            var kind = _parse_types(check_func: false, allow_identifiers: false);
            return expression(new exp.ColumnDef(@this: col, kind: kind));
        }

        // py: sqlglot/parser.py::Parser._parse_vector_expressions #fe1db13674
        public List<object?> _parse_vector_expressions(List<object?> expressions) =>
            [exp.DataType.from_str(((exp.Expr)expressions[0]!).name, dialect: dialect), .. expressions.Skip(1)];

        // py: sqlglot/parser.py::Parser._parse_struct_types #8eba3d7a2d
        public exp.Expr? _parse_struct_types(bool type_required = false)
        {
            var index = _index;
            exp.Expr? @this;

            if (py.truthy(_curr) && py.truthy(_next) && TYPE_TOKENS.Contains(_curr.token_type) && TYPE_TOKENS.Contains(_next.token_type))
                // Takes care of special cases like `STRUCT<list ARRAY<...>>` where the identifier is also a
                // type token. Without this, the list will be parsed as a type and we'll eventually crash
                @this = _parse_id_var();
            else
                @this = _parse_type(parse_interval: false, fallback_to_identifier: true) ?? _parse_id_var();

            _match(TokenType.COLON);

            if (type_required && @this is not exp.DataType && !_match_set(TYPE_TOKENS, advance: false))
            {
                _retreat(index);
                return _parse_types();
            }

            return _parse_column_def(@this);
        }
    }
}
