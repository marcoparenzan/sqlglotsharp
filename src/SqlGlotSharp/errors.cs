namespace SqlGlotSharp;

public static partial class errors
{
    // py: sqlglot/errors.py::ANSI_UNDERLINE #251edf29f9
    public const string ANSI_UNDERLINE = "\u001b[4m";
    // py: sqlglot/errors.py::ANSI_RESET #a04f8bc66a
    public const string ANSI_RESET = "\u001b[0m";
    // py: sqlglot/errors.py::ERROR_MESSAGE_CONTEXT_DEFAULT #e60841e8cc
    public const int ERROR_MESSAGE_CONTEXT_DEFAULT = 100;

    // py: sqlglot/errors.py::SqlglotError #234fa771b9
    public class SqlglotError(string message, Exception? inner = null) : Exception(message, inner)
    {
        public override string ToString() => Message;
    }
    // py: sqlglot/errors.py::UnsupportedError #cfa7f0ca7a
    public class UnsupportedError(string message) : SqlglotError(message);
    // py: sqlglot/errors.py::OptimizeError #62738a6691
    public class OptimizeError(string message) : SqlglotError(message);
    // py: sqlglot/errors.py::SchemaError #4d8a763079
    public class SchemaError(string message) : SqlglotError(message);
    // py: sqlglot/errors.py::ExecuteError #6b8e8e7477
    public class ExecuteError(string message) : SqlglotError(message);

    // py: sqlglot/errors.py::ParseError #247ddf9c10
    public class ParseError : SqlglotError
    {
        public List<PyDict<string, object?>> errors { get; }
        // py: sqlglot/errors.py::ParseError.__init__ #237fc45b8f
        public ParseError(string message, List<PyDict<string, object?>>? errors = null) : base(message)
        {
            this.errors = errors is { Count: > 0 } ? errors : [];
        }
        // py: sqlglot/errors.py::ParseError.new #24bf169bda
        public static ParseError @new(string message, string? description = null, int? line = null, int? col = null,
            string? start_context = null, string? highlight = null, string? end_context = null, string? into_expression = null) =>
            new(message, [new PyDict<string, object?>
            {
                ["description"] = description, ["line"] = line, ["col"] = col,
                ["start_context"] = start_context, ["highlight"] = highlight,
                ["end_context"] = end_context, ["into_expression"] = into_expression
            }]);
    }
    // py: sqlglot/errors.py::TokenError #bd848025f8
    public class TokenError : SqlglotError
    {
        public int? start { get; }
        public int? end { get; }
        // py: sqlglot/errors.py::TokenError.__init__ #b87b749849
        public TokenError(string message, int? start = null, int? end = null, Exception? inner = null) : base(message, inner)
        {
            this.start = start;
            this.end = end;
        }
    }

    // py: sqlglot/errors.py::highlight_sql #a5cfa39052
    public static (string formatted_sql, string start_context, string highlight, string end_context) highlight_sql(
        string sql, IReadOnlyList<(int start, int end)> positions, int context_length = ERROR_MESSAGE_CONTEXT_DEFAULT)
    {
        if (positions.Count == 0) throw new ArgumentException("positions must contain at least one (start, end) tuple");
        var start_context = "";
        var end_context = "";
        var first_highlight_start = 0;
        List<string> formatted_parts = [];
        var previous_part_end = 0;
        var sorted_positions = positions.OrderBy(p => p.start).ToArray();
        if (sorted_positions[0].start > 0)
        {
            first_highlight_start = sorted_positions[0].start;
            start_context = py.slice(sql, Math.Max(0, first_highlight_start - context_length), first_highlight_start);
            formatted_parts.Add(start_context);
            previous_part_end = first_highlight_start;
        }
        foreach (var (start, end) in sorted_positions)
        {
            var highlight_start = Math.Max(start, previous_part_end);
            var highlight_end = end + 1;
            if (highlight_start >= highlight_end) continue;
            if (highlight_start > previous_part_end) formatted_parts.Add(py.slice(sql, previous_part_end, highlight_start));
            formatted_parts.Add(ANSI_UNDERLINE + py.slice(sql, highlight_start, highlight_end) + ANSI_RESET);
            previous_part_end = highlight_end;
        }
        if (previous_part_end < py.len(sql))
        {
            end_context = py.slice(sql, previous_part_end, previous_part_end + context_length);
            formatted_parts.Add(end_context);
        }
        return (string.Concat(formatted_parts), start_context, py.slice(sql, first_highlight_start, previous_part_end), end_context);
    }

    // py: sqlglot/errors.py::concat_messages #73a4caed0b
    public static string concat_messages(IReadOnlyList<object?> errors, int maximum)
    {
        var msg = py.slice(errors, stop: maximum).Select(e => e is Exception error ? error.Message : py.str(e)).ToList();
        var remaining = errors.Count - maximum;
        if (remaining > 0) msg.Add($"... and {remaining} more");
        return string.Join("\n\n", msg);
    }
    // py: sqlglot/errors.py::merge_errors #30ba0d6b4b
    public static List<PyDict<string, object?>> merge_errors(IEnumerable<ParseError> errors) =>
        errors.SelectMany(error => error.errors).ToList();
}
