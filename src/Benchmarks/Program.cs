using System.Diagnostics;
using System.Text.Json;
using SqlGlotSharp;

// Same measurement as python/benchmarks/parse.py (_bench): best of 5 runs, stop early after a run longer than 1 s.
// One untimed warm-up run per query first, so JIT compilation is not counted.
// Usage: dotnet run -c Release --project src/Benchmarks -- <queries.json> [parse|transpile]
// queries.json is written by src/tools/bench_compare.py (the query definitions of python/benchmarks/parse.py).
var queries = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(args[0]))!;
var mode = args.Length > 1 ? args[1] : "parse";
var results = new Dictionary<string, double?>();
foreach (var (name, sql) in queries)
{
    Action run = mode == "transpile"
        ? () => _ = SqlGlot.transpile(sql, error_level: errors.ErrorLevel.IGNORE)[0]
        : () => SqlGlot.parse_one(sql, error_level: errors.ErrorLevel.IGNORE);
    try
    {
        run();
        var best = double.PositiveInfinity;
        for (var i = 0; i < 5; i++)
        {
            var watch = Stopwatch.StartNew();
            run();
            var elapsed = watch.Elapsed.TotalSeconds;
            best = Math.Min(best, elapsed);
            if (elapsed > 1) break;
        }
        results[name] = best;
    }
    catch (Exception e)
    {
        Console.Error.WriteLine($"{name}: {e.GetType().Name}: {e.Message.Split('\n')[0]}");
        results[name] = null;
    }
}
Console.WriteLine(JsonSerializer.Serialize(results));
