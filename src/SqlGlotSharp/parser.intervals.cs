using System.Text.RegularExpressions;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

public static partial class parser
{
    public partial class Parser
    {
        // Parser-local copies of the upstream builders.py constants: expressions are read-only.
        // py: sqlglot/expressions/builders.py::INTERVAL_STRING_RE #0000000000
        private static readonly Regex _parse_interval__string_re = new(
            @"\s*(-?[0-9]+(?:\.[0-9]+)?)\s*([a-zA-Z]+)\s*".Replace(@"\s", @"[\s\x1c-\x1f]"));
        // py: sqlglot/expressions/builders.py::INTERVAL_DAY_TIME_RE #0000000000
        private static readonly Regex _parse_interval__day_time_re = new(
            @"\A\s*-?\s*\d+(?:\.\d+)?\s+(?:-?(?:\d+:)?\d+:\d+(?:\.\d+)?|-?(?:\d+:){1,2}|:)\s*".Replace(@"\s", @"[\s\x1c-\x1f]"));

        // Python re's Unicode whitespace includes U+001C..U+001F; its decimal-digit
        // class also handles supplementary scalars, unlike .NET Regex's UTF-16 class.
        // No captures are used from the day-time match, so mapping Nd scalars to an
        // ASCII digit preserves this regex's language (not the interval value).
        private static string _parse_interval__day_time_input(string value) => string.Concat(
            value.EnumerateRunes().Select(r => System.Text.Rune.GetUnicodeCategory(r)
                == System.Globalization.UnicodeCategory.DecimalDigitNumber ? "0" : r.ToString()));

        // Partial closure: unit functions and term parsing retain their explicit NotPorted dependencies.
        // py: sqlglot/parser.py::Parser._parse_interval_span #0000000000
        public exp.Interval _parse_interval_span(exp.Expr? @this, bool parse_function_unit = true)
        {
            bool? interval_span_units_omitted = null;
            var fraction_in_string = false;
            if (@this is not null && @this.is_string && SUPPORTS_OMITTED_INTERVAL_SPAN_UNIT
                && _parse_interval__day_time_re.IsMatch(_parse_interval__day_time_input(@this.name)))
            {
                var index = _index;
                var first_unit = _parse_var(any_token: true, upper: true);
                exp.Expr? second_unit = null;
                if (first_unit is not null && _match_text_seq(["TO"]))
                    second_unit = _parse_var(any_token: true, upper: true);
                interval_span_units_omitted = !(first_unit is not null && second_unit is not null);
                _retreat(index);
            }

            var unit_index = _index;
            exp.Expr? unit = null;
            if (interval_span_units_omitted != true)
            {
                var is_unit = py.truthy(_curr) && (_curr.token_type == TokenType.VAR
                    || dialect.VALID_INTERVAL_UNITS.Contains(py.upper(_curr.text)));
                unit = parse_function_unit && is_unit ? _parse_function() : null;
                if (unit is null && is_unit) unit = _parse_var(any_token: true, upper: true);
            }

            if (@this is not null && @this.is_number)
            {
                try { @this = exp.Literal.@string(@this.to_py()); }
                catch (FormatException) { raise_error($"Invalid numeric interval literal: {py.repr(@this.name)}"); }
            }
            else if (@this is not null && @this.is_string)
            {
                var parts = _parse_interval__string_re.Matches(@this.name);
                if (parts.Count > 0 && unit is not null)
                {
                    unit = null;
                    _retreat(unit_index);
                }
                if (parts.Count == 1)
                {
                    @this = exp.Literal.@string(parts[0].Groups[1].Value);
                    unit = expression(new exp.Var(@this: py.upper(parts[0].Groups[2].Value)));
                    fraction_in_string = parts[0].Groups[1].Value.Contains('.');
                }
            }

            if (INTERVAL_SPANS && _match_text_seq(["TO"]))
                unit = expression(new exp.IntervalSpan(@this: unit,
                    expression: _parse_function() ?? _parse_var(any_token: true, upper: true)));
            return (exp.Interval)expression(new exp.Interval(@this: @this, unit: unit,
                fraction_in_string: fraction_in_string ? true : null));
        }

        // py: sqlglot/parser.py::Parser._parse_interval #0000000000
        public exp.Expr? _parse_interval(bool require_interval = true, bool parse_function_unit = true)
        {
            var index = _index;
            if (!_match(TokenType.INTERVAL) && require_interval) return null;
            var @this = _match(TokenType.STRING, advance: false) ? _parse_primary() : _parse_term();
            if (@this is null || (@this is exp.Column column && !py.truthy(column.table)
                && !((exp.Identifier)column.@this!).quoted && py.truthy(_curr)
                && !dialect.VALID_INTERVAL_UNITS.Contains(py.upper(_curr.text))))
            {
                _retreat(index);
                return null;
            }
            var interval = _parse_interval_span(@this, parse_function_unit);
            index = _index;
            _match(TokenType.PLUS);
            if (_match_set([TokenType.STRING, TokenType.NUMBER], advance: false))
                return expression(new exp.Add(@this: interval,
                    expression: _parse_interval(false, parse_function_unit)));
            _retreat(index);
            return interval;
        }
    }
}
