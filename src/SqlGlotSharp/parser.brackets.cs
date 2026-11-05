using Token = SqlGlotSharp.tokenizer_core.Token;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

// Bracket, slice and array-constructor parsing. Struct literals ({...}) need _kv_to_prop_eq and
// remain explicit NotPorted; index offsets need the optimizer (see generator.Generator.apply_index_offset).
public static partial class parser
{
    // py: sqlglot/parser.py::build_array_constructor #9da21ea7b2
    public static exp.Expr build_array_constructor(Type exp_class, List<exp.Expr> args, TokenType bracket_kind,
        dialects.dialect.Dialect dialect)
    {
        var array_exp = (exp.Expr)Activator.CreateInstance(exp_class,
            [new PyDict<string, object?> { ["expressions"] = args.Cast<object?>().ToList() }])!;

        if (exp_class == typeof(exp.Array))
        {
            array_exp.set("value_constructor", args.Count == 1 && args[0] is exp.Query);
            if (dialect.HAS_DISTINCT_ARRAY_CONSTRUCTORS)
                array_exp.set("bracket_notation", bracket_kind == TokenType.L_BRACKET);
        }

        return array_exp;
    }

    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser._parse_bracket_key_value #a17df393ee
        public exp.Expr? _parse_bracket_key_value(bool is_map = false) =>
            _parse_slice(_parse_alias(_parse_disjunction(), @explicit: true));

        // py: sqlglot/parser.py::Parser._parse_odbc_datetime_literal #ebe76d22c7
        /// <summary>
        /// Parses a datetime column in ODBC format. We parse the column into the corresponding
        /// types, for example <c>{d'yyyy-mm-dd'}</c> will be parsed as a <c>Date</c> column.
        /// </summary>
        public exp.Expr _parse_odbc_datetime_literal()
        {
            _match(TokenType.VAR);
            var exp_class = ODBC_DATETIME_LITERALS[_prev.text.ToLowerInvariant()];
            var expression = this.expression((exp.Expr)Activator.CreateInstance(exp_class,
                [new PyDict<string, object?> { ["this"] = _parse_string() }])!);
            if (!_match(TokenType.R_BRACE)) raise_error("Expected }");
            return expression;
        }

        // py: sqlglot/parser.py::Parser._parse_bracket #f29903e57c
        public exp.Expr? _parse_bracket(exp.Expr? @this = null)
        {
            if (!_match_set(BRACKETS)) return @this;

            if (MAP_KEYS_ARE_ARBITRARY_EXPRESSIONS)
                throw new NotPortedException("sqlglot/parser.py::Parser._parse_bracket (MAP keys as arbitrary expressions)");
            var parse_map = false;

            var bracket_kind = _prev.token_type;
            if (bracket_kind == TokenType.L_BRACE && py.truthy(_curr) && _curr.token_type == TokenType.VAR
                && ODBC_DATETIME_LITERALS.ContainsKey(_curr.text.ToLowerInvariant()))
                return _parse_odbc_datetime_literal();

            var expressions = _parse_csv(() => _parse_bracket_key_value(is_map: bracket_kind == TokenType.L_BRACE));

            if (bracket_kind == TokenType.L_BRACKET && !_match(TokenType.R_BRACKET)) raise_error("Expected ]");
            else if (bracket_kind == TokenType.L_BRACE && !_match(TokenType.R_BRACE)) raise_error("Expected }");

            // https://duckdb.org/docs/sql/data_types/struct.html#creating-structs
            if (bracket_kind == TokenType.L_BRACE)
                @this = expression(new exp.Struct(expressions: _kv_to_prop_eq(expressions, parse_map: parse_map).Cast<object?>().ToList()));
            else if (!py.truthy(@this))
                @this = build_array_constructor(typeof(exp.Array), expressions, bracket_kind, dialect);
            else
            {
                if (ARRAY_CONSTRUCTORS.TryGetValue(py.upper(@this.name), out var constructor_type))
                    return build_array_constructor(constructor_type, expressions, bracket_kind, dialect);

                var offset_expressions = generator.Generator.apply_index_offset(
                    @this, expressions.Cast<object?>().ToList(), -dialect.INDEX_OFFSET, dialect);
                @this = expression(new exp.Bracket(@this: @this, expressions: offset_expressions),
                    comments: @this.pop_comments());
            }

            _add_comments(@this);
            return _parse_bracket(@this);
        }

        // py: sqlglot/parser.py::Parser._parse_slice #dd44f0b2c4
        public exp.Expr? _parse_slice(exp.Expr? @this)
        {
            if (!_match(TokenType.COLON)) return @this;

            // DuckDB's `-` in `[a:-:step]` is the default end, which depends on the sign of step
            exp.Expr? end;
            if (_match_pair(TokenType.DASH, TokenType.COLON, advance: false))
            {
                _advance();
                end = null;
            }
            else end = _parse_assignment();
            var step = _match(TokenType.COLON) ? _parse_unary() : null;
            return expression(new exp.Slice(@this: @this, expression: end, step: step));
        }
    }
}
