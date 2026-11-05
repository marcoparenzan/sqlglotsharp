using System.Globalization;
using System.Text;

namespace SqlGlotSharp.Corpus;

public sealed record ScoreRow(string Level, string Dialect, int Passed, int Failed, int NotPorted)
{
    public int Total => Passed + Failed + NotPorted;
    public double Percent => Total == 0 ? 0 : 100.0 * Passed / Total;
}

public static class ScoreboardReport
{
    public static ScoreRow[] Summarize(IEnumerable<CaseResult> results) => results
        .GroupBy(r => (r.Case.Level, r.Case.Dialect))
        .OrderBy(g => g.Key.Level, StringComparer.Ordinal).ThenBy(g => g.Key.Dialect, StringComparer.Ordinal)
        .Select(g => new ScoreRow(g.Key.Level, g.Key.Dialect,
            g.Count(r => r.Status == CaseStatus.Passed), g.Count(r => r.Status == CaseStatus.Failed),
            g.Count(r => r.Status == CaseStatus.NotPorted))).ToArray();

    public static string Markdown(IEnumerable<ScoreRow> rows, IEnumerable<ScoreRow>? previous = null)
    {
        var old = (previous ?? []).ToDictionary(r => (r.Level, r.Dialect));
        var output = new StringBuilder("# SQLGlot port scoreboard\n\nNotPorted is not a pass. L0 runs the C# tokenizer; later SQL boundaries remain unimplemented.\n\n| Level | Dialect | Pass | Fail | Not ported | Total | Pass % | Pass delta |\n|---|---|---:|---:|---:|---:|---:|---:|\n");
        foreach (var row in rows)
        {
            var delta = old.TryGetValue((row.Level, row.Dialect), out var prior)
                ? (row.Passed - prior.Passed).ToString("+0;-0;0", CultureInfo.InvariantCulture) : "n/a";
            output.AppendLine($"| {row.Level} | {row.Dialect} | {row.Passed} | {row.Failed} | {row.NotPorted} | {row.Total} | {row.Percent.ToString("F2", CultureInfo.InvariantCulture)} | {delta} |");
        }
        return output.ToString();
    }
}
