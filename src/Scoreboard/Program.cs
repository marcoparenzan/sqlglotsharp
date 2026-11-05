using System.Text.Json;
using SqlGlotSharp.Corpus;

if (args.Length is < 1 or > 3)
{
    Console.Error.WriteLine("Usage: dotnet run --project src/Scoreboard -- <corpus-directory> [report-directory] [results.trx]");
    return 2;
}
List<(string Id, string Level, string Dialect, double Ms)> timings = [];
try
{
    var output = args.Length >= 2 ? args[1] : Path.Combine(FindRepoRoot(), "docs", "status");
    Directory.CreateDirectory(output);
    var snapshot = Path.Combine(output, "scoreboard.json");
    var previous = File.Exists(snapshot) ? JsonSerializer.Deserialize<ScoreRow[]>(File.ReadAllText(snapshot)) : null;
    List<CorpusCase> cases = CorpusReader.Read(args[0]).ToList();
    // Recorder artifacts proven not replayable (each with a reason); never counted as pass or fail.
    var exclusionFile = Path.Combine(AppContext.BaseDirectory, "known-exclusions.json");
    if (File.Exists(exclusionFile))
    {
        var excluded = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(File.ReadAllText(exclusionFile))!
            .Select(e => e["id"]).ToHashSet(StringComparer.Ordinal);
        var before = cases.Count;
        cases = cases.Where(c => !excluded.Contains(c.Id)).ToList();
        if (before != cases.Count) Console.WriteLine($"excluded {before - cases.Count} known recorder artifacts (known-exclusions.json)");
    }
    // Optional quick loop, e.g. SCOREBOARD_ONLY=L1.base,L1.postgres. Use a separate report directory.
    if (Environment.GetEnvironmentVariable("SCOREBOARD_ONLY") is { Length: > 0 } only)
    {
        var keep = only.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        cases = cases.Where(c => keep.Contains($"{c.Level}.{c.Dialect}")).ToList();
    }
    // Quick loop only: SCOREBOARD_MAXSQL=20000 skips stress-test inputs (slow quadratic parse). Checkpoints run without it.
    if (int.TryParse(Environment.GetEnvironmentVariable("SCOREBOARD_MAXSQL"), out var maxSql))
        cases = cases.Where(c => !c.Payload.TryGetProperty("sql", out var s) || s.ValueKind != System.Text.Json.JsonValueKind.String || s.GetString()!.Length <= maxSql).ToList();
    var results = args.Length == 3 ? TrxResults.Read(args[2], cases) :
        CorpusRunner.Run(cases, (Action<CorpusCase>)(item =>
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try { CorpusVerifier.Verify(item); }
            finally { lock (timings) timings.Add((item.Id, item.Level, item.Dialect, watch.Elapsed.TotalMilliseconds)); }
        }),
            int.TryParse(Environment.GetEnvironmentVariable("SCOREBOARD_THREADS"), out var threads) ? threads : 1);
    var rows = ScoreboardReport.Summarize(results);
    if (timings.Count != 0)
    {
        var total = timings.Sum(t => t.Ms);
        Console.WriteLine($"verify time {total / 1000:F1}s; slowest: " + string.Join(", ", timings.OrderByDescending(t => t.Ms).Take(8).Select(t => $"{t.Level}.{t.Dialect}:{t.Id[..10]}={t.Ms:F0}ms")));
    }
    File.WriteAllText(Path.Combine(output, "failures.json"), JsonSerializer.Serialize(results.Where(r => r.Status == CaseStatus.Failed).Select(r => new { r.Case.Id, r.Case.Level, r.Case.Dialect, r.Detail }), new JsonSerializerOptions { WriteIndented = true }));
    // NotPorted causes grouped by capability: the finite work list for the roadmap.
    File.WriteAllText(Path.Combine(output, "notported.json"), JsonSerializer.Serialize(results.Where(r => r.Status == CaseStatus.NotPorted)
        .GroupBy(r => (r.Detail ?? "").Split('\n')[0]).OrderByDescending(g => g.Count())
        .Select(g => new { Cause = g.Key, Count = g.Count(), ByLevelDialect = g.GroupBy(r => $"{r.Case.Level}.{r.Case.Dialect}").ToDictionary(x => x.Key, x => x.Count()), Example = g.First().Case.Id, Sqls = g.Where(r => r.Case.Payload.TryGetProperty("sql", out var q) && q.ValueKind == System.Text.Json.JsonValueKind.String).Select(r => r.Case.Payload.GetProperty("sql").GetString()!).Distinct().Take(60).ToArray() }),
        new JsonSerializerOptions { WriteIndented = true }));
    File.WriteAllText(Path.Combine(output, "scoreboard.md"), ScoreboardReport.Markdown(rows, previous));
    File.WriteAllText(snapshot, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"{results.Count} cases: pass={rows.Sum(r => r.Passed)}, fail={rows.Sum(r => r.Failed)}, not-ported={rows.Sum(r => r.NotPorted)}");
    Console.WriteLine(Path.GetFullPath(Path.Combine(output, "scoreboard.md")));
    // Successful report generation does not imply SQL parity.
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

static string FindRepoRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "UPSTREAM.json"))) return dir.FullName;
    return Directory.GetCurrentDirectory();
}
