using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

// Bounded ordinary-function group from parser.py, in upstream member order.
// Zero anchors deliberately report partial dependency closures: special grammar,
// custom builders, lambda bodies, UDTFs, query and window grammar are not ported.
// Argument parsing routes through parser.arguments.cs; unsupported lambda
// bodies and function modifiers retain explicit NotPorted boundaries.
public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser.FUNCTIONS #0000000000
        public static readonly PyDict<string, Func<IReadOnlyList<object?>, exp.Expr>> FUNCTIONS_ = _parse_function_call__functions();
        public virtual PyDict<string, Func<IReadOnlyList<object?>, exp.Expr>> FUNCTIONS => FUNCTIONS_;

        private static PyDict<string, Func<IReadOnlyList<object?>, exp.Expr>> _parse_function_call__functions()
        {
            var functions = new PyDict<string, Func<IReadOnlyList<object?>, exp.Expr>>();
            foreach (var (name, type) in exp.FUNCTION_BY_NAME)
            {
                var function = (exp.Expr)Activator.CreateInstance(type, [new PyDict<string, object?>()])!;
                functions[name] = function.from_arg_list;
            }
            // These override the registry upstream. Never silently substitute the
            // registry constructor for a custom/dialect-dependent builder.
            functions["CONVERT_TIMEZONE"] = args => build_convert_timezone(args);
            functions["VAR_MAP"] = build_var_map;
            ApplyFunctionOverrides(functions);
            var str_position = new exp.StrPosition();
            // Upstream COUNT override: unlike the generated constructor this
            // always supplies big_int=True and keeps all arguments after this.
            functions["COUNT"] = args => new exp.Count(@this: args.Count > 0 ? args[0] : null,
                expressions: args.Skip(1).ToList(), big_int: true);
            functions["STRPOS"] = str_position.from_arg_list;
            functions["INSTR"] = str_position.from_arg_list;
            return functions;
        }

        // py: sqlglot/parser.py::Parser.FUNCTION_PARSERS #0000000000
        public static readonly PyDict<string, Func<Parser, exp.Expr?>> FUNCTION_PARSERS_ = _parse_function_call__parsers();
        public virtual PyDict<string, Func<Parser, exp.Expr?>> FUNCTION_PARSERS => FUNCTION_PARSERS_;

        private static PyDict<string, Func<Parser, exp.Expr?>> _parse_function_call__parsers()
        {
            var parsers = new PyDict<string, Func<Parser, exp.Expr?>>();
            foreach (var name in new exp.ArgMax().sql_names().Concat(new exp.ArgMin().sql_names()).Concat(new[] {
                "CAST", "CEIL", "CONVERT", "CHAR", "CHR", "DECODE", "EXTRACT", "FLOOR", "GAP_FILL", "INITCAP",
                "JSON_OBJECT", "JSON_OBJECTAGG", "JSON_TABLE", "MATCH", "NORMALIZE", "OVERLAY", "POSITION",
                "SAFE_CAST", "STRING_AGG", "SUBSTRING", "TRIM", "TRY_CAST", "TRY_CONVERT", "WEIGHT_STRING", "XMLELEMENT", "XMLTABLE" }))
                parsers[name] = _ => throw new NotPortedException($"sqlglot/parser.py::Parser.FUNCTION_PARSERS[{name}] (special grammar)");
            foreach (var name in new exp.ArgMax().sql_names()) parsers[name] = self => self._parse_distinct_arg_function(typeof(exp.ArgMax));
            foreach (var name in new exp.ArgMin().sql_names()) parsers[name] = self => self._parse_distinct_arg_function(typeof(exp.ArgMin));
            parsers["CEIL"] = self => self._parse_ceil_floor(typeof(exp.Ceil));
            parsers["FLOOR"] = self => self._parse_ceil_floor(typeof(exp.Floor));
            parsers["CHAR"] = self => self._parse_char();
            parsers["CHR"] = self => self._parse_char();
            parsers["INITCAP"] = self => self._parse_initcap();
            parsers["JSON_OBJECT"] = self => self._parse_json_object();
            parsers["JSON_OBJECTAGG"] = self => self._parse_json_object(agg: true);
            parsers["NORMALIZE"] = self => self._parse_normalize();
            parsers["OPENJSON"] = self => self._parse_open_json();
            parsers["OVERLAY"] = self => self._parse_overlay();
            parsers["POSITION"] = self => self._parse_position();
            parsers["STRING_AGG"] = self => self._parse_string_agg();
            parsers["SUBSTRING"] = self => self._parse_substring();
            parsers["TRIM"] = self => self._parse_trim();
            parsers["CAST"] = self => self._parse_cast(self.STRICT_CAST);
            parsers["SAFE_CAST"] = self => self._parse_cast(false, safe: true);
            parsers["TRY_CAST"] = self => self._parse_cast(false, safe: true);
            parsers["CONVERT"] = self => self._parse_convert(self.STRICT_CAST);
            parsers["TRY_CONVERT"] = self => self._parse_convert(false, safe: true);
            parsers["DECODE"] = self => self._parse_decode();
            parsers["GAP_FILL"] = self => self._parse_gap_fill();
            parsers["MATCH"] = self => self._parse_match_against();
            parsers["WEIGHT_STRING"] = self => self._parse_weight_string();
            parsers["XMLELEMENT"] = self => self._parse_xml_element();
            parsers["XMLTABLE"] = self => self._parse_xml_table();
            parsers["EXTRACT"] = self => self._parse_extract();
            return parsers;
        }

        // py: sqlglot/parser.py::Parser._parse_function #e13a244d71
        public exp.Expr? _parse_function(PyDict<string, Func<IReadOnlyList<object?>, exp.Expr>>? functions = null,
            bool anonymous = false, bool optional_parens = true, bool any_token = false)
        {
            // This allows us to also parse {fn <function>} syntax (Snowflake, MySQL support this)
            // See: https://community.snowflake.com/s/article/SQL-Escape-Sequences
            var fn_syntax = false;
            if (_match(TokenType.L_BRACE, advance: false) && py.truthy(_next) && py.upper(_next.text) == "FN")
            {
                _advance(2);
                fn_syntax = true;
            }
            var func = _parse_function_call(functions: functions, anonymous: anonymous,
                optional_parens: optional_parens, any_token: any_token);
            if (fn_syntax) _match(TokenType.R_BRACE);
            return func;
        }

        // py: sqlglot/parser.py::Parser._parse_function_args #0000000000
        public List<exp.Expr> _parse_function_args(bool alias = false) =>
            _parse_csv(() => _parse_lambda(alias));

        // py: sqlglot/parser.py::Parser._parse_function_call #0000000000
        public exp.Expr? _parse_function_call(PyDict<string, Func<IReadOnlyList<object?>, exp.Expr>>? functions = null,
            bool anonymous = false, bool optional_parens = true, bool any_token = false)
        {
            if (!py.truthy(_curr)) return null;
            var comments = _curr.comments;
            var prev = _prev;
            var token = _curr;
            var token_type = _curr.token_type;
            object @this = _curr.text;
            var upper = py.upper(_curr.text);
            var after_dot = prev.token_type == TokenType.DOT;
            NO_PAREN_FUNCTION_PARSERS.TryGetValue(upper, out var no_paren_parser);
            if (no_paren_parser is null && _connect_prior && upper == "PRIOR")
                no_paren_parser = self => self.expression(new exp.Prior(@this: self._parse_bitwise()));
            if (optional_parens && no_paren_parser is not null && !INVALID_FUNC_NAME_TOKENS.Contains(token_type) && !after_dot)
            {
                _advance();
                return _parse_window(no_paren_parser(this));
            }
            if (_next.token_type != TokenType.L_PAREN)
            {
                if (optional_parens && NO_PAREN_FUNCTIONS.ContainsKey(token_type) && !after_dot)
                {
                    _advance();
                    // Upstream constructs with no arguments, then expression() validates
                    // and consumes _prev_comments. No positions/window adapter here.
                    var function_type = NO_PAREN_FUNCTIONS[token_type];
                    return expression((exp.Expr)Activator.CreateInstance(function_type, [new PyDict<string, object?>()])!);
                }
                return null;
            }
            if (any_token)
            {
                if (RESERVED_TOKENS.Contains(token_type)) return null;
            }
            else if (!FUNC_TOKENS.Contains(token_type)) return null;
            _advance(2);
            FUNCTION_PARSERS.TryGetValue(upper, out var function_parser);
            exp.Expr? result;
            if (function_parser is not null && !anonymous)
                result = function_parser(this);
            else
            {
                if (SUBQUERY_PREDICATES.TryGetValue(token_type, out var subquery_predicate))
                {
                    exp.Expr? expr = null;
                    if (py.truthy(_curr) && SUBQUERY_TOKENS.Contains(_curr.token_type))
                    {
                        expr = _parse_select();
                        _match_r_paren();
                    }
                    else if (py.truthy(prev) && prev.token_type is TokenType.LIKE or TokenType.ILIKE)
                    {
                        // Backtrack one token since we've consumed the L_PAREN here. Instead, we'd like
                        // to parse "LIKE [ANY | ALL] (...)" as a whole into an exp.Tuple or exp.Paren
                        _advance(-1);
                        expr = _parse_bitwise();
                    }

                    if (py.truthy(expr))
                        return expression((exp.Expr)Activator.CreateInstance(subquery_predicate,
                            [new PyDict<string, object?> { ["this"] = expr }])!, comments: comments);
                }
                functions ??= FUNCTIONS;
                functions.TryGetValue(upper, out var function);
                var known_function = function is not null && !anonymous;
                var alias = !known_function || FUNCTIONS_WITH_ALIASED_ARGS.Contains(upper);
                var args = upper == "COUNT"
                    ? _parse_csv(() => _parse_function_args__ordinary_lambda(alias, allow_distinct: true))
                    : _parse_function_args(alias);
                var post_func_comments = py.truthy(_curr) ? _curr.comments : null;
                if (known_function && py.truthy(post_func_comments))
                {
                    // exp.SQLGLOT_ANONYMOUS is not exported in the generated C#
                    // constants. Exact upstream core.py constant, not a new AST member.
                    if (post_func_comments!.Any(comment => _parse_function_call__lstrip(comment).StartsWith("sqlglot.anonymous", StringComparison.Ordinal)))
                        known_function = false;
                }
                if (alias && known_function)
                    args = _kv_to_prop_eq(args.Cast<exp.Expr>().ToList());
                if (known_function)
                {
                    // Registry constructors have a typed one-argument signature;
                    // dialect/custom builders above are explicit boundaries.
                    // The builder may mutate its list (T-SQL HASHBYTES pops the algorithm); validation sees the result.
                    var builder_args = args.Cast<object?>().ToList();
                    var func = _call_function_builder(function!, builder_args);
                    func = validate_expression(func, builder_args);
                    if (py.truthy(dialect.ORIGINAL_NAME_META_KEY))
                        func.meta[py.str(dialect.ORIGINAL_NAME_META_KEY)] = @this;
                    result = func;
                }
                else
                {
                    if (token_type == TokenType.IDENTIFIER)
                        @this = new exp.Identifier(@this: @this, quoted: true).update_positions(token);
                    result = expression(new exp.Anonymous(@this: @this, expressions: args.Cast<object?>().ToList()));
                }
                result = result.update_positions(token);
            }
            if (result is not null) result.add_comments(comments);
            if (function_parser is not null) _match(TokenType.R_PAREN, expression: result);
            else _match_r_paren(result);
            return _parse_window(result);
        }

        private static string _parse_function_call__lstrip(string text)
        {
            var start = 0;
            while (start < text.Length && (char.IsWhiteSpace(text[start]) || text[start] is '\u001c' or '\u001d' or '\u001e' or '\u001f')) start++;
            return text[start..];
        }

        private bool _parse_function_call__nulls_ahead() => _curr.token_type == TokenType.VAR
            && (py.upper(_curr.text) is "IGNORE" or "RESPECT") && py.upper(_next.text) == "NULLS";

    }
}
