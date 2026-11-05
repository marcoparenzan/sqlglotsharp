namespace SqlGlotSharp.parsers;

public static partial class tsql
{
    // py: sqlglot/parsers/tsql.py::TSQLParser
    public partial class TSQLParser
    {
        public TSQLParser(errors.ErrorLevel? error_level = null, int error_message_context = 100,
            int max_errors = 3, int max_nodes = -1, object? dialect = null)
            : base(error_level, error_message_context, max_errors, max_nodes,
                dialect ?? new dialects.tsql.TSQL()) { }
    }
}
