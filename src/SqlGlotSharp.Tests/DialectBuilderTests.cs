using System.Text.Json;
using D = SqlGlotSharp.dialects.dialect;

namespace SqlGlotSharp.Tests;

public class DialectBuilderTests
{
    [Fact]
    public void Builders_match_all_recorded_python_outputs()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(SourceDirectory(), "dialect-builder-cases.json")));
        var count = 0;
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var args = item.GetProperty("args").EnumerateArray().Select(ReadArgument).ToArray();
            var settings = item.GetProperty("settings");
            Type Class() => typeof(exp).GetNestedType(settings.GetProperty("cls").GetString()!)!;
            var mapping = new PyDict<string, string>();
            if (settings.TryGetProperty("mapping", out var mapped))
                foreach (var property in mapped.EnumerateObject()) mapping[property.Name] = property.Value.GetString()!;
            object? actualValue = item.GetProperty("kind").GetString() switch
            {
                "binary" => D.binary_from_function(typeof(exp.Add))(args),
                "timestamp_trunc" => D.build_timestamp_trunc(args),
                "isnull" => D.isnull_to_is_null(args),
                "date_delta" => D.build_date_delta(Class(), mapping)(args),
                "formatted_time" => D.build_formatted_time(Class(), @default: settings.TryGetProperty("default", out var def) ? def.GetBoolean() : null)(args, D.Dialect.get_or_raise(settings.GetProperty("dialect").GetString())),
                "unit_to_str" => D.unit_to_str((exp.Expr)args[0]!),
                "unit_to_var" => D.unit_to_var((exp.Expr)args[0]!),
                "week_unit" => D.week_unit_to_dow(args[0] as exp.Expr),
                "week_offset" => D.week_offset_to_dow((long)args[0]!),
                "map_date_part" => D.map_date_part((exp.Expr)args[0]!),
                _ => throw new InvalidOperationException("Unknown test kind")
            };
            var actual = actualValue is exp.Expr expression ? JsonSerializer.SerializeToElement(new { ast = serde.dump_json(expression) }) : JsonSerializer.SerializeToElement(new { value = actualValue });
            var expected = item.GetProperty("expected");
            Assert.True(JsonElement.DeepEquals(expected, actual), $"{item.GetProperty("kind")}: {expected.GetRawText()} != {actual.GetRawText()}");
            count++;
        }
        Assert.Equal(34, count);
    }
    private static object? ReadArgument(JsonElement item) => item.ValueKind switch
    {
        JsonValueKind.Array => serde.load(item),
        JsonValueKind.Number => item.GetInt64(),
        JsonValueKind.String => item.GetString(),
        JsonValueKind.Null => null,
        _ => throw new InvalidOperationException("Unsupported test input")
    };
    private static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
