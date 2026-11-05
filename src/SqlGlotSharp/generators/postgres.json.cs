using System.Globalization;

namespace SqlGlotSharp.generators;

public static partial class postgres
{
    public partial class PostgresGenerator
    {
        // Bounded port: sqlglot/generators/postgres.py::_json_extract_sql.
        // Paths are AST segments, not reparsed strings: in particular numeric keys stay keys.
        internal string json_extract_sql(exp.Expr expression, string name, string op)
        {
            var path = expression.expression as exp.Expr;
            if (path is not (exp.JSONPath or exp.Variadic) && expression.expressions.Count == 0)
                return arrow_json_extract_sql(expression, op);

            if (path?.expressions is { Count: 1 } parts && parts[0] is exp.JSONPathRoot)
                expression.set("expression", new exp.Variadic(exp.Literal.@string("{}")));

            return json_extract_segments(expression, name,
                quoted_index: !py.truthy(expression.args.get("only_json_types")),
                op: py.truthy(expression.args.get("only_json_types")) ? op : null);
        }

        // Bounded port: sqlglot/dialects/dialect.py::arrow_json_extract_sql.
        internal string arrow_json_extract_sql(exp.Expr expression, string op)
        {
            if (JSON_TYPE_REQUIRED_FOR_EXTRACTION && expression.@this is exp.Literal { is_string: true } literal)
                literal.replace(new exp.Cast(literal.copy(), new exp.DataType(exp.DType.JSON)));

            if (expression.expression is exp.Binary or exp.Predicate or exp.Not or exp.Concat)
                expression.set("expression", new exp.Paren(expression.expression));
            return binary(expression, op);
        }

        // Bounded port: sqlglot/dialects/dialect.py::json_extract_segments.
        private string json_extract_segments(exp.Expr expression, string name, bool quoted_index, string? op)
        {
            if (expression.expression is not exp.JSONPath path)
                return func(name, new object?[] { expression.@this, expression.expression }
                    .Concat(expression.expressions).ToArray());

            var segments = new List<string>();
            foreach (var segment in path.expressions)
            {
                var text = sql(segment);
                if (text.Length == 0) continue;
                if (segment is exp.JSONPathPart && (quoted_index || segment is not exp.JSONPathSubscript))
                    text = $"{dialect.QUOTE_START}{escape_str(text)}{dialect.QUOTE_END}";
                segments.Add(text);
            }
            return op is not null ? string.Join($" {op} ", new[] { sql(expression.@this) }.Concat(segments)) :
                func(name, new object?[] { expression.@this }.Concat(segments.Cast<object?>()).ToArray());
        }

        // sqlglot/dialects/dialect.py::json_path_key_only_name.
        internal string json_path_key_only_name(exp.JSONPathKey expression)
        {
            if (expression.@this is exp.JSONPathWildcard) unsupported("Unsupported wildcard in JSONPathKey expression");

            return expression.name;
        }
    }
}
