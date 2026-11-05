using System.Globalization;
using System.Numerics;
using System.Text;

namespace SqlGlotSharp;

public static partial class parser
{
    // py: sqlglot/parser.py::build_json_extract
    public static exp.JSONExtract build_json_extract(Parser self, exp.Expr? @this, exp.Expr? path) =>
        self.expression(new exp.JSONExtract(@this: @this, expression: self.dialect.to_json_path(path),
            only_json_types: self.JSON_ARROWS_REQUIRE_JSON_TYPE));

    // py: sqlglot/parser.py::build_json_extract_scalar
    public static exp.JSONExtractScalar build_json_extract_scalar(Parser self, exp.Expr? @this, exp.Expr? path) =>
        self.expression(new exp.JSONExtractScalar(@this: @this, expression: self.dialect.to_json_path(path),
            only_json_types: self.JSON_ARROWS_REQUIRE_JSON_TYPE,
            scalar_only: self.dialect.JSON_EXTRACT_SCALAR_SCALAR_ONLY));

    // py: sqlglot/parser.py::build_jsonb_extract
    public static exp.JSONBExtract build_jsonb_extract(Parser self, exp.Expr? @this, exp.Expr? path) =>
        self.expression(new exp.JSONBExtract(@this: @this, expression: path));

    // py: sqlglot/parser.py::build_jsonb_extract_scalar
    public static exp.JSONBExtractScalar build_jsonb_extract_scalar(Parser self, exp.Expr? @this, exp.Expr? path) =>
        self.expression(new exp.JSONBExtractScalar(@this: @this, expression: path));

    public partial class Parser
    {
        // Bounded call-site port of dialect.py::build_json_extract_path for Postgres's
        // two-argument arrow operators (zero_based_indexing=True, json_type=None).
        // Function/variadic JSON builders are NOT routed here and remain NotPorted.
        // Unlike to_json_path, each literal is ONE segment: '$.a', 'a.b', '[0]',
        // '{a,b}' and empty strings are keys, not JSONPath syntax. Non-literals,
        // including Neg(Literal), retain the fallback AST and omit JSON flags.
        // validate_expression rather than expression is intentional: upstream does
        // not consume _prev_comments or attach literal positions to new segments.
        public exp.Expr _build_postgres_json_extract(exp.Expr? @this, exp.Expr? path, bool scalar)
        {
            var args = new PyDict<string, object?> { ["this"] = @this, ["expression"] = path };
            if (path is exp.Literal literal)
            {
                var text = literal.name;
                exp.Expr segment = new exp.JSONPathKey(@this: text);
                if ((!JSON_ARROWS_REQUIRE_JSON_TYPE || !literal.is_string) && JsonInteger(text) is { } index)
                    segment = new exp.JSONPathSubscript(@this: index);
                args["expression"] = new exp.JSONPath(expressions: new List<object?>
                    { new exp.JSONPathRoot(), segment });
                args["only_json_types"] = JSON_ARROWS_REQUIRE_JSON_TYPE;
            }
            else args["expressions"] = null;
            return validate_expression(scalar ? (exp.Expr)new exp.JSONExtractScalar(args) : new exp.JSONExtract(args));
        }

        // Use the existing Python integer grammar. Overflow is valid integer syntax,
        // not a key: retain arbitrary precision just as jsonpath.parse already does.
        private static object? JsonInteger(string text)
        {
            try { return py.@int(text); }
            catch (FormatException) { return null; }
            catch (OverflowException)
            {
                var normalized = new StringBuilder();
                foreach (var rune in text.Trim().EnumerateRunes())
                {
                    if (rune.Value == '_') continue;
                    normalized.Append(Rune.GetUnicodeCategory(rune) == UnicodeCategory.DecimalDigitNumber
                        ? ((char)('0' + (int)Rune.GetNumericValue(rune))).ToString() : rune.ToString());
                }
                return BigInteger.Parse(normalized.ToString(), CultureInfo.InvariantCulture);
            }
        }
    }
}
