namespace SqlGlotSharp.parsers;

public static class timescale
{
    /// <summary>PostgreSQL grammar with canonical TIME_BUCKET and anonymous FIRST/LAST calls.</summary>
    public class TimescaleParser : postgres.PostgresParser
    {
        public TimescaleParser(errors.ErrorLevel? error_level = null, int error_message_context = 100,
            int max_errors = 3, int max_nodes = -1, object? dialect = null)
            : base(error_level, error_message_context, max_errors, max_nodes,
                dialect ?? new dialects.timescale.Timescale())
        {
            // Copy, never mutate the PostgreSQL or generated shared registries.
            var functions = new PyDict<string, Func<IReadOnlyList<object?>, exp.Expr>>();
            foreach (var (name, builder) in base.FUNCTIONS) functions[name] = builder;
            functions.Remove("FIRST");
            functions.Remove("LAST");
            functions["TIME_BUCKET"] = new exp.TimeBucket().from_arg_list;
            FUNCTIONS = functions;
        }

        public override PyDict<string, Func<IReadOnlyList<object?>, exp.Expr>> FUNCTIONS { get; }
    }
}
