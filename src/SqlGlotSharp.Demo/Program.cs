using SqlGlotSharp;

// Native preview only: no Python backend and no raw-SQL fallback.
try
{
    var pretty = false;
    var dialectName = "postgres";
    string? read = null, write = null;
    var sqlArgs = new List<string>();
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--pretty": pretty = true; break;
            case "--help":
                Console.WriteLine("SqlGlotSharp native preview: [--dialect postgres|timescale|tsql|fabric] [--read base|postgres|timescale|tsql|fabric] [--write base|postgres|timescale|tsql|fabric] [--pretty] [SQL]");
                Console.WriteLine("Incomplete grammar/transforms fail explicitly. Timescale is identity-only; no DateBin mapping or cross-dialect equivalence is promised.");
                return 0;
            case "--dialect": dialectName = Value(args, ref i); break;
            case "--read": read = Value(args, ref i); break;
            case "--write": write = Value(args, ref i); break;
            default:
                if (args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"Unknown option: {args[i]}");
                sqlArgs.Add(args[i]); break;
        }
    }
    if (dialectName is not ("postgres" or "timescale" or "tsql" or "fabric"))
        throw new ArgumentException("--dialect must be postgres, timescale, tsql or fabric");
    var source = DialectName(read ?? dialectName);
    var target = write is null ? null : DialectName(write);
    var sql = string.Join(" ", sqlArgs);
    if (string.IsNullOrWhiteSpace(sql))
        sql = "SELECT a, COUNT(*) FROM measurements WHERE a > 2 GROUP BY a HAVING COUNT(*) > 1 LIMIT 3";
    Console.WriteLine($"SqlGlotSharp — {read ?? dialectName} → {write ?? read ?? dialectName} native port");
    foreach (var output in SqlGlot.Transpile(sql, read: source, write: target, pretty: pretty))
        Console.WriteLine(output);
    return 0;
}
catch (NotPortedException error)
{
    Console.Error.WriteLine($"Not yet supported (no fallback): {error.Message}");
    return 2;
}
catch (Exception error) when (error is errors.SqlglotError or ArgumentException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

static string Value(string[] args, ref int i)
{
    var option = args[i];
    if (++i == args.Length) throw new ArgumentException($"{option} requires a dialect name");
    return args[i];
}

static string DialectName(string name) => name switch
{
    "base" or "" => "",
    "postgres" or "timescale" or "tsql" or "fabric" => name,
    _ => throw new ArgumentException("Read/write dialect must be base, postgres, timescale, tsql or fabric")
};
