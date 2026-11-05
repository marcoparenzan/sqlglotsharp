using System.Runtime.CompilerServices;

namespace SqlGlotSharp;

public static partial class py
{
    /// <summary>
    /// Minimal runtime equivalent of Python's <c>logging</c> module, limited to what sqlglot uses:
    /// named loggers (<c>getLogger("sqlglot")</c> always returns the same object), levels, <c>disabled</c>,
    /// handlers, and Python's <c>lastResort</c> behaviour (no handler configured: WARNING and above go to stderr).
    /// Not a ported sqlglot member; no dependency outside the .NET base library.
    /// </summary>
    public static class logging
    {
        public const int NOTSET = 0;
        public const int DEBUG = 10;
        public const int INFO = 20;
        public const int WARNING = 30;
        public const int ERROR = 40;
        public const int CRITICAL = 50;

        private static readonly Dictionary<string, Logger> loggers = new(StringComparer.Ordinal);

        /// <summary>Returns the logger with this name, creating it on first use (same object on every call).</summary>
        public static Logger getLogger(string name)
        {
            lock (loggers)
            {
                if (!loggers.TryGetValue(name, out var logger))
                    loggers[name] = logger = new Logger(name);
                return logger;
            }
        }

        public static string getLevelName(int level) => level switch
        {
            CRITICAL => "CRITICAL",
            ERROR => "ERROR",
            WARNING => "WARNING",
            INFO => "INFO",
            DEBUG => "DEBUG",
            NOTSET => "NOTSET",
            _ => $"Level {level}",
        };

        /// <summary>One logging event, as Python's <c>LogRecord</c>. <c>pathname</c> is the C# source file of the caller.</summary>
        public sealed class LogRecord(string name, int levelno, string msg, Exception? exc_info,
            string pathname, string funcName, int lineno)
        {
            public string name { get; } = name;
            public int levelno { get; } = levelno;
            public string levelname { get; } = getLevelName(levelno);
            public string msg { get; } = msg;
            public Exception? exc_info { get; } = exc_info;
            public string pathname { get; } = pathname;
            public string module { get; } = Path.GetFileNameWithoutExtension(pathname);
            public string funcName { get; } = funcName;
            public int lineno { get; } = lineno;
            public string getMessage() => msg;
        }

        /// <summary>Base class for handlers, as Python's <c>logging.Handler</c>: override <c>emit</c>.</summary>
        public abstract class Handler
        {
            public int level { get; private set; } = NOTSET;
            public void setLevel(int level) => this.level = level;
            public void handle(LogRecord record)
            {
                if (record.levelno >= level) emit(record);
            }
            public abstract void emit(LogRecord record);
        }

        /// <summary>A handler that calls a delegate for each record (Python code would subclass Handler).</summary>
        public sealed class CallbackHandler(Action<LogRecord> callback) : Handler
        {
            public override void emit(LogRecord record) => callback(record);
        }

        public sealed class Logger
        {
            private readonly object sync = new();
            private Handler[] _handlers = [];

            internal Logger(string name) => this.name = name;

            public string name { get; }
            public int level { get; private set; } = NOTSET;
            public bool disabled { get; set; }
            public IReadOnlyList<Handler> handlers => _handlers;

            public void setLevel(int level) => this.level = level;

            public void addHandler(Handler handler)
            {
                lock (sync)
                    if (!_handlers.Contains(handler)) _handlers = [.. _handlers, handler];
            }

            public void removeHandler(Handler handler)
            {
                lock (sync) _handlers = _handlers.Where(h => h != handler).ToArray();
            }

            /// <summary>Own level if set, otherwise the root logger's default (WARNING).</summary>
            public int getEffectiveLevel() => level != NOTSET ? level : WARNING;

            public bool isEnabledFor(int level) => !disabled && level >= getEffectiveLevel();

            public void debug(string msg, [CallerFilePath] string pathname = "", [CallerMemberName] string funcName = "", [CallerLineNumber] int lineno = 0)
                => _log(DEBUG, msg, null, pathname, funcName, lineno);
            public void info(string msg, [CallerFilePath] string pathname = "", [CallerMemberName] string funcName = "", [CallerLineNumber] int lineno = 0)
                => _log(INFO, msg, null, pathname, funcName, lineno);
            public void warning(string msg, [CallerFilePath] string pathname = "", [CallerMemberName] string funcName = "", [CallerLineNumber] int lineno = 0)
                => _log(WARNING, msg, null, pathname, funcName, lineno);
            public void error(string msg, [CallerFilePath] string pathname = "", [CallerMemberName] string funcName = "", [CallerLineNumber] int lineno = 0)
                => _log(ERROR, msg, null, pathname, funcName, lineno);
            public void critical(string msg, [CallerFilePath] string pathname = "", [CallerMemberName] string funcName = "", [CallerLineNumber] int lineno = 0)
                => _log(CRITICAL, msg, null, pathname, funcName, lineno);
            public void exception(string msg, Exception exc_info, [CallerFilePath] string pathname = "", [CallerMemberName] string funcName = "", [CallerLineNumber] int lineno = 0)
                => _log(ERROR, msg, exc_info, pathname, funcName, lineno);

            private void _log(int level, string msg, Exception? exc_info, string pathname, string funcName, int lineno)
            {
                if (!isEnabledFor(level)) return;
                var record = new LogRecord(name, level, msg, exc_info, pathname, funcName, lineno);
                var current = _handlers;
                if (current.Length == 0)
                {
                    // Python's logging.lastResort: stderr, WARNING and above, message only.
                    if (level >= WARNING)
                        Console.Error.WriteLine(exc_info is null ? msg : msg + Environment.NewLine + exc_info);
                    return;
                }
                foreach (var handler in current) handler.handle(record);
            }
        }
    }
}
