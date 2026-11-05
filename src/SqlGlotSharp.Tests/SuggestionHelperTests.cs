using System.Text.Json;

namespace SqlGlotSharp.Tests;

public class SuggestionHelperTests
{
    [Fact]
    public void Similarity_and_closest_match_agree_with_python_oracle()
    {
        var path = Path.Combine(SourceDirectory(), "similarity-cases.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var item in document.RootElement.GetProperty("ratios").EnumerateArray())
            Assert.Equal(item.GetProperty("expected").GetDouble(), py.difflib.sequence_ratio(
                item.GetProperty("first").GetString()!, item.GetProperty("second").GetString()!));
        foreach (var item in document.RootElement.GetProperty("closest").EnumerateArray())
            Assert.Equal(item.GetProperty("expected").GetString(), py.difflib.closest(
                item.GetProperty("word").GetString()!, item.GetProperty("candidates").EnumerateArray().Select(c => c.GetString()!)));
    }

    [Fact]
    public void Suggestion_error_message_matches_upstream()
    {
        Assert.Equal("Unknown dialect 'postgre'. Did you mean postgres?",
            Assert.Throws<ArgumentException>(() => helper.suggest_closest_match_and_fail("dialect", "postgre", ["postgres", "tsql"])).Message);
        Assert.Equal("Unknown dialect 'zzz'.",
            Assert.Throws<ArgumentException>(() => helper.suggest_closest_match_and_fail("dialect", "zzz", ["postgres", "tsql"])).Message);
    }

    private static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
