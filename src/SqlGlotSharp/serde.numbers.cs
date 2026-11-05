using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SqlGlotSharp;

public static partial class serde
{
    public static JsonSerializerOptions JsonOptions { get; } = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new BigIntegerConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
    public static JsonElement dump_json(object? expression) => JsonSerializer.SerializeToElement(dump(expression), JsonOptions);
    private static object ReadNumber(JsonElement json)
    {
        if (json.TryGetInt64(out var number)) return number;
        var raw = json.GetRawText();
        if (!raw.Contains('.') && !raw.Contains('e') && !raw.Contains('E')) return BigInteger.Parse(raw, CultureInfo.InvariantCulture);
        return json.GetDouble();
    }
    private sealed class BigIntegerConverter : JsonConverter<BigInteger>
    {
        public override BigInteger Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return BigInteger.Parse(document.RootElement.GetRawText(), CultureInfo.InvariantCulture);
        }
        public override void Write(Utf8JsonWriter writer, BigInteger value, JsonSerializerOptions options) =>
            writer.WriteRawValue(value.ToString(CultureInfo.InvariantCulture));
    }
}
