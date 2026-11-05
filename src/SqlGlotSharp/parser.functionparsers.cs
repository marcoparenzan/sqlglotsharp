using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

// FUNCTION_PARSERS entries with special grammar (parser.py): SUBSTRING, TRIM, POSITION, STRING_AGG,
// JSON_OBJECT, CHR/CHAR, OVERLAY, INITCAP, NORMALIZE, CEIL/FLOOR, ARG_MAX/ARG_MIN, JSON_VALUE.
public static partial class parser
{
    public partial class Parser
    {
        private static exp.Expr FromArgList(Type type, IReadOnlyList<object?> args) =>
            ((exp.Expr)Activator.CreateInstance(type, [new PyDict<string, object?>()])!).from_arg_list(args);

        // py: sqlglot/parser.py::Parser._match_l_paren #0000000000
        public void _match_l_paren(exp.Expr? expression = null)
        {
            if (!_match(TokenType.L_PAREN, expression: expression)) raise_error("Expecting (");
        }

        // py: sqlglot/parser.py::Parser._parse_substring #0000000000
        public exp.Expr _parse_substring()
        {
            // Postgres supports the form: substring(string [from int] [for int])
            // (despite being undocumented, the reverse order also works)
            // https://www.postgresql.org/docs/9.1/functions-string.html @ Table 9-6
            var args = _parse_csv<exp.Expr>(() => _parse_bitwise()).Cast<object?>().ToList();

            exp.Expr? start = null, length = null;

            while (py.truthy(_curr))
            {
                if (_match(TokenType.FROM)) start = _parse_bitwise();
                else if (_match(TokenType.FOR))
                {
                    start ??= exp.Literal.number(1);
                    length = _parse_bitwise();
                }
                else break;
            }

            if (py.truthy(start)) args.Add(start);
            if (py.truthy(length)) args.Add(length);

            return validate_expression(FromArgList(typeof(exp.Substring), args), args);
        }

        // py: sqlglot/parser.py::Parser._parse_trim #0000000000
        public exp.Expr _parse_trim()
        {
            // https://www.w3resource.com/sql/character-functions/trim.php
            // https://docs.oracle.com/javadb/10.8.3.0/ref/rreftrimfunc.html
            string? position = null;
            exp.Expr? collation = null, expression_ = null;

            if (_match_texts(TRIM_TYPES)) position = py.upper(_prev.text);

            var @this = _parse_bitwise();
            if (_match_set([TokenType.FROM, TokenType.COMMA]))
            {
                var invert_order = _prev.token_type == TokenType.FROM || TRIM_PATTERN_FIRST;
                expression_ = _parse_bitwise();

                if (invert_order) (@this, expression_) = (expression_, @this);
            }

            if (_match(TokenType.COLLATE)) collation = _parse_bitwise();

            return expression(Mk<exp.Trim>(("this", @this), ("position", position), ("expression", expression_), ("collation", collation)));
        }

        // py: sqlglot/parser.py::Parser._parse_position #0000000000
        public exp.Expr _parse_position(bool haystack_first = false)
        {
            var args = _parse_csv<exp.Expr>(() => _parse_bitwise()).Cast<object?>().ToList();

            if (_match(TokenType.IN))
                return expression(Mk<exp.StrPosition>(("this", _parse_bitwise()), ("substr", SeqGet(args, 0))));

            var (haystack, needle) = haystack_first ? (SeqGet(args, 0), SeqGet(args, 1)) : (SeqGet(args, 1), SeqGet(args, 0));

            return expression(Mk<exp.StrPosition>(("this", haystack), ("substr", needle), ("position", SeqGet(args, 2))));
        }

        // py: sqlglot/parser.py::Parser._parse_charset_name #0000000000
        /// <summary>Parse a charset name after USING or CHARACTER SET.</summary>
        public virtual exp.Expr? _parse_charset_name() => _parse_var(tokens: [TokenType.BINARY, TokenType.IDENTIFIER]);

        // py: sqlglot/parser.py::Parser._parse_char #0000000000
        public exp.Expr _parse_char() =>
            expression(Mk<exp.Chr>(("expressions", _parse_csv<exp.Expr>(() => _parse_assignment()).Cast<object?>().ToList()),
                ("charset", _match(TokenType.USING) ? _parse_charset_name() : false)));

        // py: sqlglot/parser.py::Parser._parse_overlay #0000000000
        public exp.Expr _parse_overlay()
        {
            exp.Expr? overlay_arg(string text) =>
                _match(TokenType.COMMA) || _match_text_seq([text]) ? _parse_bitwise() : null;

            return expression(Mk<exp.Overlay>(("this", _parse_bitwise()), ("expression", overlay_arg("PLACING")),
                ("from_", overlay_arg("FROM")), ("for_", overlay_arg("FOR"))));
        }

        // py: sqlglot/parser.py::Parser._parse_initcap #0000000000
        public exp.Expr _parse_initcap()
        {
            var expr = FromArgList(typeof(exp.Initcap), _parse_function_args().Cast<object?>().ToList());

            // attach dialect's default delimiters
            if (expr.args.get("expression") is null)
                expr.set("expression", exp.Literal.@string(dialect.INITCAP_DEFAULT_DELIMITER_CHARS));

            return expr;
        }

        // py: sqlglot/parser.py::Parser._parse_normalize #0000000000
        public exp.Expr _parse_normalize() =>
            expression(Mk<exp.Normalize>(("this", _parse_bitwise()), ("form", _match(TokenType.COMMA) ? _parse_var() : false)));

        // py: sqlglot/parser.py::Parser._parse_ceil_floor #0000000000
        public exp.Expr _parse_ceil_floor(Type expr_type)
        {
            var args = _parse_csv<exp.Expr>(() => _parse_lambda()).Cast<object?>().ToList();

            var @this = SeqGet(args, 0);
            var decimals = SeqGet(args, 1);

            return (exp.Expr)Activator.CreateInstance(expr_type, [new PyDict<string, object?>
            {
                ["this"] = @this, ["decimals"] = decimals, ["to"] = _match_text_seq(["TO"]) ? _parse_var() : null
            }])!;
        }

        // py: sqlglot/parser.py::Parser._parse_distinct_arg_function #0000000000
        public exp.Expr _parse_distinct_arg_function(Type func, int distinct_index = 0)
        {
            var is_distinct = _match(TokenType.DISTINCT);
            if (!is_distinct) _match(TokenType.ALL);

            List<object?> args = [_parse_lambda()];
            if (_match(TokenType.COMMA)) args.AddRange(_parse_function_args());

            var target = SeqGet(args, distinct_index);
            if (is_distinct && py.truthy(target))
                args[distinct_index] = expression(Mk<exp.Distinct>(("expressions", new List<object?> { target })));

            return FromArgList(func, args);
        }

        // py: sqlglot/parser.py::Parser._parse_format_json #0000000000
        public exp.Expr? _parse_format_json(exp.Expr? @this)
        {
            if (!py.truthy(@this) || !_match_text_seq(["FORMAT", "JSON"])) return @this;

            return expression(Mk<exp.FormatJson>(("this", @this)));
        }

        // py: sqlglot/parser.py::Parser._parse_json_key_value #0000000000
        public exp.Expr? _parse_json_key_value()
        {
            _match_text_seq(["KEY"]);
            var key = _parse_column();
            _match_set(JSON_KEY_VALUE_SEPARATOR_TOKENS);
            _match_text_seq(["VALUE"]);
            var value = _parse_bitwise();

            if (!py.truthy(key) && !py.truthy(value)) return null;
            return expression(Mk<exp.JSONKeyValue>(("this", key), ("expression", value)));
        }

        // py: sqlglot/parser.py::Parser._parse_on_handling #0000000000
        /// <summary>Parses the "X ON Y" or "DEFAULT &lt;expr&gt; ON Y" syntax, e.g. NULL ON NULL (Oracle, T-SQL, MySQL).</summary>
        public object? _parse_on_handling(string on, params string[] values)
        {
            foreach (var value in values)
                if (_match_text_seq([value, "ON", on])) return $"{value} ON {on}";

            var index = _index;
            if (_match(TokenType.DEFAULT))
            {
                var default_value = _parse_bitwise();
                if (_match_text_seq(["ON", on])) return default_value;

                _retreat(index);
            }

            return null;
        }

        // py: sqlglot/parser.py::Parser._parse_json_object #0000000000
        public exp.Expr _parse_json_object(bool agg = false)
        {
            var star = _parse_star();
            var expressions = py.truthy(star)
                ? new List<object?> { star }
                : _parse_csv<exp.Expr>(() => _parse_format_json(_parse_json_key_value())).Cast<object?>().ToList();
            var null_handling = _parse_on_handling("NULL", "NULL", "ABSENT");

            bool? unique_keys = null;
            if (_match_text_seq(["WITH", "UNIQUE"])) unique_keys = true;
            else if (_match_text_seq(["WITHOUT", "UNIQUE"])) unique_keys = false;

            _match_text_seq(["KEYS"]);

            object? return_type = _match_text_seq(["RETURNING"]) ? _parse_format_json(_parse_type()) : false;
            object? encoding = _match_text_seq(["ENCODING"]) ? _parse_var() : false;

            var arguments = new (string, object?)[]
            {
                ("expressions", expressions), ("null_handling", null_handling), ("unique_keys", unique_keys),
                ("return_type", return_type), ("encoding", encoding)
            };
            return agg ? expression(Mk<exp.JSONObjectAgg>(arguments)) : expression(Mk<exp.JSONObject>(arguments));
        }

        // py: sqlglot/parser.py::Parser._parse_on_condition #0000000000
        public exp.Expr? _parse_on_condition()
        {
            // MySQL uses "X ON EMPTY Y ON ERROR" (e.g. JSON_VALUE) while Oracle uses the opposite (e.g. JSON_EXISTS)
            object? empty, error;
            var tokens = ON_CONDITION_TOKENS.Cast<string>().ToArray();
            if (dialect.ON_CONDITION_EMPTY_BEFORE_ERROR)
            {
                empty = _parse_on_handling("EMPTY", tokens);
                error = _parse_on_handling("ERROR", tokens);
            }
            else
            {
                error = _parse_on_handling("ERROR", tokens);
                empty = _parse_on_handling("EMPTY", tokens);
            }

            var @null = _parse_on_handling("NULL", tokens);

            if (!py.truthy(empty) && !py.truthy(error) && !py.truthy(@null)) return null;

            return expression(Mk<exp.OnCondition>(("empty", empty), ("error", error), ("null", @null)));
        }

        // py: sqlglot/parser.py::Parser._parse_json_value #0000000000
        public exp.Expr _parse_json_value(bool json_path = true)
        {
            var @this = _parse_bitwise();
            _match(TokenType.COMMA);
            var path = _parse_bitwise();

            object? returning = _match(TokenType.RETURNING) ? _parse_types() : false;

            return expression(Mk<exp.JSONValue>(("this", @this), ("path", json_path ? dialect.to_json_path(path) : path),
                ("returning", returning), ("on_condition", _parse_on_condition())));
        }

        // py: sqlglot/parser.py::Parser._parse_string_agg #0000000000
        public exp.Expr _parse_string_agg()
        {
            List<object?> args;
            if (_match(TokenType.DISTINCT))
            {
                args = [expression(Mk<exp.Distinct>(("expressions", new List<object?> { _parse_disjunction() })))];
                if (_match(TokenType.COMMA)) args.AddRange(_parse_csv<exp.Expr>(() => _parse_disjunction()));
            }
            else
            {
                _match(TokenType.ALL);
                args = _parse_csv<exp.Expr>(() => _parse_disjunction()).Cast<object?>().ToList();
            }

            exp.Expr? on_overflow;
            if (_match_text_seq(["ON", "OVERFLOW"]))
            {
                // trino: LISTAGG(expression [, separator] [ON OVERFLOW overflow_behavior])
                if (_match_text_seq(["ERROR"])) on_overflow = new exp.Var(@this: "ERROR");
                else
                {
                    _match_text_seq(["TRUNCATE"]);
                    on_overflow = expression(Mk<exp.OverflowTruncateBehavior>(("this", _parse_string()),
                        ("with_count", _match_text_seq(["WITH", "COUNT"]) || !_match_text_seq(["WITHOUT", "COUNT"]))));
                }
            }
            else on_overflow = null;

            var index = _index;
            if (!_match(TokenType.R_PAREN) && args.Count > 0)
            {
                // postgres: STRING_AGG([DISTINCT] expression, separator [ORDER BY expression1 {ASC | DESC} [, ...]])
                // bigquery: STRING_AGG([DISTINCT] expression [, separator] [ORDER BY key [{ASC | DESC}] [, ... ]] [LIMIT n])
                // The order is parsed through `this` as a canonicalization for WITHIN GROUPs
                args[0] = _parse_limit(@this: _parse_order(@this: (exp.Expr?)args[0]));
                return expression(Mk<exp.GroupConcat>(("this", args[0]), ("separator", SeqGet(args, 1))));
            }

            // Checks if we can parse an order clause: WITHIN GROUP (ORDER BY <order_by_expression_list> [ASC | DESC]).
            // This is done "manually", instead of letting _parse_window parse it into an exp.WithinGroup node, so that
            // the STRING_AGG call is parsed like in MySQL / SQLite and can thus be transpiled more easily to them.
            if (!_match_text_seq(["WITHIN", "GROUP"]))
            {
                _retreat(index);
                return validate_expression(FromArgList(typeof(exp.GroupConcat), args), args);
            }

            // The corresponding match_r_paren will be called in parse_function (caller)
            _match_l_paren();

            return expression(Mk<exp.GroupConcat>(("this", _parse_order(@this: (exp.Expr?)SeqGet(args, 0))),
                ("separator", SeqGet(args, 1)), ("on_overflow", on_overflow)));
        }
    }
}
