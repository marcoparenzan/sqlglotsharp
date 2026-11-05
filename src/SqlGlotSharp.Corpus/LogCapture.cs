namespace SqlGlotSharp.Corpus;

/// <summary>
/// Replay-side equivalent of the recorder's handler on <c>logging.getLogger("sqlglot")</c>. One handler is
/// attached once; each case routes the records it produces to its own sink (AsyncLocal, so parallel cases
/// do not mix). <see cref="Owner"/> tells which module logged, for the per-module logger contexts the recorder
/// saw when a test patched <c>parser.logger</c> or <c>generator.logger</c>.
/// </summary>
internal static class LogCapture
{
    private static readonly AsyncLocal<Action<py.logging.LogRecord>?> current = new();
    private static readonly py.logging.CallbackHandler handler = new(record => current.Value?.Invoke(record));

    static LogCapture() => py.logging.getLogger("sqlglot").addHandler(handler);

    public static IDisposable Begin(Action<py.logging.LogRecord> sink)
    {
        var previous = current.Value;
        current.Value = sink;
        return new Scope(() => current.Value = previous);
    }

    /// <summary>"parser", "generator" or "other", from the C# source file that logged.</summary>
    public static string Owner(py.logging.LogRecord record)
    {
        var directory = Path.GetFileName(Path.GetDirectoryName(record.pathname) ?? "");
        var file = Path.GetFileName(record.pathname);
        if (directory == "parsers" || file.StartsWith("parser.", StringComparison.Ordinal)) return "parser";
        if (directory == "generators" || file.StartsWith("generator.", StringComparison.Ordinal)) return "generator";
        return "other";
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
