using System.Text.Json;

namespace SqlGlotSharp.Tests;

public class UnicodeCaseTests
{
    [Fact]
    public void Unicode_casing_and_properties_match_python_for_all_changed_scalars_and_context_cases()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(SourceDirectory(), "unicode-case-cases.json")));
        var count = 0;
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var input = item.GetProperty("input").GetString()!;
            Assert.Equal(item.GetProperty("upper").GetString(), py.upper(input));
            Assert.Equal(item.GetProperty("lower").GetString(), py.lower(input));
            Assert.Equal(item.GetProperty("isupper").GetBoolean(), py.isupper(input));
            Assert.Equal(item.GetProperty("islower").GetBoolean(), py.islower(input));
            Assert.Equal(item.GetProperty("safe").GetBoolean(), py.safe_identifier(input));
            count++;
        }
        Assert.True(count > 2900);
    }

    [Fact]
    public void Identifier_normalization_expands_unicode_and_preserves_final_sigma_context()
    {
        var upper = dialects.dialect.Dialect.get_or_raise(", normalization_strategy=uppercase");
        var identifier = new exp.Identifier(@this: "straße");
        upper.normalize_identifier(identifier);
        Assert.Equal("STRASSE", identifier.name);
        var lower = dialects.dialect.Dialect.get_or_raise("");
        lower.normalize_identifier(identifier.set_kwargs(new PyKwargs { ["this"] = "ΟΣ" }));
        Assert.Equal("ος", identifier.name);
        Assert.True(lower.can_quote(new exp.Identifier(@this: "a𐐨"), "safe"));
        Assert.Equal("\ud800", py.lower("\ud800"));
        var postgres = dialects.dialect.Dialect.get_or_raise("postgres, normalization_strategy=uppercase");
        var asciiOnly = new exp.Identifier(@this: "straße");
        postgres.normalize_identifier(asciiOnly);
        Assert.Equal("STRAßE", asciiOnly.name);
    }

    private static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
