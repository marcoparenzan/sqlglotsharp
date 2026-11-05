namespace SqlGlotSharp.dialects;

public static partial class dialect
{
    public partial class Dialect
    {
        // py: sqlglot/dialects/dialect.py::Dialect.format_time #be67925110
        public exp.Expr? format_time(object? expression)
        {
            if (expression is string text) return exp.Literal.@string(global::SqlGlotSharp.time.format_time(py.slice(text, 1, -1), TIME_MAPPING, TIME_TRIE));
            if (expression is exp.Expr node && node.is_string)
                return exp.Literal.@string(global::SqlGlotSharp.time.format_time((string)node.@this!, TIME_MAPPING, TIME_TRIE));
            return expression as exp.Expr;
        }

        // py: sqlglot/dialects/dialect.py::Dialect.to_json_path #588eb136fd
        public exp.Expr? to_json_path(exp.Expr? path)
        {
            if (path is exp.Literal literal)
            {
                var text = literal.name;
                if (literal.is_number) text = "[" + text + "]";
                try { return jsonpath.parse(text, this); }
                catch (Exception error) when (error is errors.ParseError or errors.TokenError)
                {
                    var trimmed = text.TrimStart();
                    if (STRICT_JSON_PATH_SYNTAX && !trimmed.StartsWith("lax", StringComparison.Ordinal) && !trimmed.StartsWith("strict", StringComparison.Ordinal))
                        logger.warning($"Invalid JSON path syntax. {error.Message}");
                }
            }
            return path;
        }
    }
}
