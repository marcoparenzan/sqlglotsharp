using System.Text.Json;
using SqlGlotSharp.Corpus;

namespace SqlGlotSharp.Tests;

public class ScoreboardTests
{
    [Fact]
    public void Summary_separates_not_ported_from_failure_and_sorts_groups()
    {
        using var json = JsonDocument.Parse("{}");
        CaseResult Result(string level, CaseStatus status) => new(new("id", level, "base", [], json.RootElement.Clone()), status, null);
        var rows = ScoreboardReport.Summarize([Result("L1", CaseStatus.NotPorted), Result("L0", CaseStatus.Passed), Result("L0", CaseStatus.Failed)]);
        Assert.Equal("L0", rows[0].Level);
        Assert.Equal(50, rows[0].Percent);
        Assert.Equal(1, rows[1].NotPorted);
        Assert.Equal(0, rows[1].Passed);
    }

    [Fact]
    public void Markdown_shows_delta_and_explicit_baseline()
    {
        var markdown = ScoreboardReport.Markdown([new("L0", "base", 1, 1, 2)], [new("L0", "base", 2, 0, 2)]);
        Assert.Contains("NotPorted is not a pass", markdown);
        Assert.Contains("| 25.00 | -1 |", markdown);
        Assert.Contains("n/a", ScoreboardReport.Markdown([new("L1", "base", 0, 0, 1)]));
    }
}
