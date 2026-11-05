using System.Text.Json;

namespace SqlGlotSharp.Tests;

public class JsonPathParserTests
{
    [Fact]
    public void Parser_matches_all_recorded_python_jsonpath_outputs_and_errors()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(SourceDirectory(), "jsonpath-parse-cases.json")));
        var failures = new List<string>();
        var count = 0;
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var path = item.GetProperty("path").GetString()!;
            var expected = item.GetProperty("expected");
            JsonElement actual;
            try
            {
                var expression = jsonpath.parse(path, item.GetProperty("read").GetString());
                actual = JsonSerializer.SerializeToElement(new { ast = serde.dump_json(expression) });
            }
            catch (Exception error)
            {
                var type = error switch
                {
                    errors.ParseError => "ParseError", errors.TokenError => "TokenError",
                    FormatException => "ValueError", IndexOutOfRangeException or ArgumentOutOfRangeException => "IndexError",
                    _ => error.GetType().Name
                };
                actual = JsonSerializer.SerializeToElement(new { error = new { @class = type, message = error.Message } });
            }
            if (!JsonElement.DeepEquals(expected, actual)) failures.Add($"{path}: expected {expected.GetRawText()}, actual {actual.GetRawText()}");
            count++;
        }
        Assert.Equal(531, count);
        Assert.True(failures.Count == 0, $"{failures.Count} JSONPath differences: " + string.Join("\n", failures.Take(10)));
    }

    private static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
