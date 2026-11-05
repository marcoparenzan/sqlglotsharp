using System.Text.Json;
using System.Numerics;

namespace SqlGlotSharp.Tests;

public class TimeHelperTests
{
    [Fact]
    public void Time_helpers_and_timezone_inventory_match_python_oracle()
    {
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(SourceDirectory(), "time-cases.json")));
        foreach (var item in data.RootElement.GetProperty("formats").EnumerateArray())
        {
            var mapping = new PyDict<string, string>();
            foreach (var property in item.GetProperty("mapping").EnumerateObject()) mapping[property.Name] = property.Value.GetString()!;
            Assert.Equal(item.GetProperty("expected").GetString(), time.format_time(item.GetProperty("input").GetString()!, mapping));
        }
        foreach (var item in data.RootElement.GetProperty("precisions").EnumerateArray())
            Assert.Equal(item.GetProperty("expected").GetInt32(), time.subsecond_precision(item.GetProperty("input").GetString()!));
        Assert.Equal(data.RootElement.GetProperty("timezone_count").GetInt32(), time.TIMEZONES.Count);
        Assert.Contains("europe/rome", time.TIMEZONES);
    }

    [Fact]
    public void Generated_trie_terminals_and_big_integer_json_are_preserved()
    {
        var root = new PyDict<object, object?> { ["a"] = new PyDict<object, object?> { [0L] = true } };
        Assert.Equal(trie.TrieResult.EXISTS, trie.in_trie(root, "a").result);
        var huge = BigInteger.Parse("231584178474632390847141970017375815706539969331281128078915168015826259279872");
        var expression = new exp.JSONPathSubscript(@this: huge);
        var json = serde.dump_json(expression);
        var restored = (exp.Expr)serde.load(json)!;
        Assert.Equal(huge, restored.@this);
        Assert.True(JsonElement.DeepEquals(json, serde.dump_json(restored)));
        Assert.False(py.truthy(new PyDecimal("-0.00")));
        Assert.True(py.truthy(new PyDecimal("NaN")));
    }

    private static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
