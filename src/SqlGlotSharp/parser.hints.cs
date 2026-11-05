using TokenType = SqlGlotSharp.tokenizer_core.TokenType;
using Token = SqlGlotSharp.tokenizer_core.Token;

namespace SqlGlotSharp;

// Optimizer hints, versioned and historical table references, map builders (parser.py).
public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser._parse_hint_function_call #0000000000
        public virtual exp.Expr? _parse_hint_function_call() => _parse_function_call();

        // py: sqlglot/parser.py::Parser._parse_hint_fallback_to_string #0000000000
        public exp.Expr _parse_hint_fallback_to_string()
        {
            var start = _curr;
            while (py.truthy(_curr)) _advance();

            var end = _tokens[(int)_index - 1];
            return new exp.Hint(expressions: new List<object?> { _find_sql(start, end) });
        }

        // py: sqlglot/parser.py::Parser._parse_hint_body #0000000000
        public exp.Expr? _parse_hint_body()
        {
            var start_index = _index;
            var should_fallback_to_string = false;

            List<object?> hints = [];
            try
            {
                while (true)
                {
                    var hint = _parse_csv<exp.Expr>(() => _parse_hint_function_call() ?? _parse_var(upper: true));
                    if (hint.Count == 0) break;
                    hints.AddRange(hint);
                }
            }
            catch (errors.ParseError)
            {
                should_fallback_to_string = true;
            }

            if (should_fallback_to_string || py.truthy(_curr))
            {
                _retreat(start_index);
                return _parse_hint_fallback_to_string();
            }

            return expression(new exp.Hint(expressions: hints));
        }

        // py: sqlglot/parser.py::Parser._parse_historical_data #0000000000
        public exp.Expr? _parse_historical_data()
        {
            // https://docs.snowflake.com/en/sql-reference/constructs/at-before
            var index = _index;
            exp.Expr? historical_data = null;
            if (_match_texts(HISTORICAL_DATA_PREFIX))
            {
                var @this = py.upper(_prev.text);
                object? kind = _match(TokenType.L_PAREN) && _match_texts(HISTORICAL_DATA_KIND) ? py.upper(_prev.text) : false;
                var expression_ = _match(TokenType.FARROW) ? _parse_bitwise() : null;

                if (expression_ is not null)
                {
                    _match_r_paren();
                    historical_data = expression(new exp.HistoricalData(@this: @this, kind: kind, expression: expression_));
                }
                else _retreat(index);
            }

            return historical_data;
        }

        // py: sqlglot/parser.py::Parser._parse_version #0000000000
        public exp.Expr? _parse_version()
        {
            string? @this = null;
            var found = false;
            foreach (var (phrase, value) in VERSION_PHRASES)
            {
                if (_match_text_seq(phrase.ToArray()))
                {
                    @this = value;
                    found = true;
                    break;
                }
            }
            if (!found) return null;

            string kind;
            exp.Expr? expression_;
            if (_match_set([TokenType.FROM, TokenType.BETWEEN]))
            {
                kind = py.upper(_prev.text);
                var start = _parse_bitwise();
                _match_texts(["TO", "AND"]);
                var end = _parse_bitwise();
                expression_ = expression(new exp.Tuple(expressions: new List<object?> { start, end }));
            }
            else if (_match_text_seq(["CONTAINED", "IN"]))
            {
                kind = "CONTAINED IN";
                expression_ = expression(new exp.Tuple(expressions: _parse_wrapped_csv(() => _parse_bitwise()).Cast<object?>().ToList()));
            }
            else if (_match(TokenType.ALL))
            {
                kind = "ALL";
                expression_ = null;
            }
            else
            {
                _match_text_seq(["AS", "OF"]);
                kind = "AS OF";
                expression_ = _parse_bitwise();
            }

            return expression(new exp.Version(@this: @this, expression: expression_, kind: kind));
        }

        // py: sqlglot/parser.py::build_var_map
        internal static exp.Expr build_var_map(IReadOnlyList<object?> args)
        {
            if (args.Count == 1 && args[0] is exp.Expr star && star.is_star) return new exp.StarMap(@this: star);

            List<object?> keys = [], values = [];
            for (var i = 0; i < args.Count; i += 2)
            {
                keys.Add(args[i]);
                values.Add(args[i + 1]);
            }

            return new exp.VarMap(keys: new exp.Array(expressions: keys), values: new exp.Array(expressions: values));
        }

        // py: sqlglot/parser.py::build_convert_timezone
        internal static exp.Expr build_convert_timezone(IReadOnlyList<object?> args, string? default_source_tz = null)
        {
            if (args.Count == 2)
            {
                exp.Expr? source_tz = default_source_tz is not null && default_source_tz.Length != 0 ? exp.Literal.@string(default_source_tz) : null;
                return new exp.ConvertTimezone(source_tz: source_tz, target_tz: helper.seq_get(args, 0), timestamp: helper.seq_get(args, 1));
            }

            return new exp.ConvertTimezone().from_arg_list(args);
        }
    }
}
