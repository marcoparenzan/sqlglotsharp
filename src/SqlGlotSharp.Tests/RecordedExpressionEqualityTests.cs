using System.Text.Json;

namespace SqlGlotSharp.Tests;

public class RecordedExpressionEqualityTests
{
    [Fact]
    public void Recorded_upstream_expression_comparisons_match_in_csharp()
    {
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(SourceDirectory(), "expression-equality-cases.json")));
        var count = 0;
        foreach (var item in data.RootElement.EnumerateArray())
        {
            var left = (exp.Expr)serde.load(item.GetProperty("left"))!;
            var same = item.GetProperty("same_instance").GetBoolean();
            var right = same ? left : (exp.Expr)serde.load(item.GetProperty("right"))!;
            var expected = item.GetProperty("expected").GetBoolean();
            Assert.True(left.Equals(right) == expected, $"Comparison differs: {item.GetProperty("id").GetString()}");
            if (same) Assert.Null(left._hash);
            if (expected && !same) Assert.Equal(left.GetHashCode(), right.GetHashCode());
            count++;
        }
        Assert.Equal(44, count);
    }
    private static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
