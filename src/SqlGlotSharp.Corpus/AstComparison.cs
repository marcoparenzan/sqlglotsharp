using System.Text.Json;
using System.Text.Json.Nodes;

namespace SqlGlotSharp.Corpus;

/// <summary>
/// Compares serde dump trees, not their traversal order. Only argument dictionaries and
/// the indexes identifying their parents are unordered; all JSON arrays remain ordered.
/// Invalid dumps are never equivalent, including to themselves.
/// </summary>
public static class AstComparison
{
    public static bool AreEqual(JsonElement expected, JsonElement actual)
    {
        return TryDecode(expected, out var left) && TryDecode(actual, out var right)
            && JsonNode.DeepEquals(left, right);
    }

    /// <summary>Only successful result dumps use structural comparison. Error envelopes remain exact.</summary>
    public static bool ResultsEqual(JsonElement expected, JsonElement actual)
    {
        if (expected.ValueKind != JsonValueKind.Object || actual.ValueKind != JsonValueKind.Object
            || expected.TryGetProperty("error", out _) || actual.TryGetProperty("error", out _)
            || !expected.TryGetProperty("result", out var left) || !actual.TryGetProperty("result", out var right))
            return JsonElement.DeepEquals(expected, actual);
        if (!UniqueProperties(expected) || !UniqueProperties(actual)) return false;
        var properties = expected.EnumerateObject().ToArray();
        if (properties.Length != actual.EnumerateObject().Count()) return false;
        foreach (var property in properties)
            if (!actual.TryGetProperty(property.Name, out var other)
                || (property.Name != "result" && !JsonElement.DeepEquals(property.Value, other))) return false;
        if (left.ValueKind != JsonValueKind.Array || right.ValueKind != JsonValueKind.Array
            || left.GetArrayLength() != right.GetArrayLength()) return false;
        for (var i = 0; i < left.GetArrayLength(); i++)
        {
            if (left[i].ValueKind == JsonValueKind.Null || right[i].ValueKind == JsonValueKind.Null)
            {
                if (left[i].ValueKind != right[i].ValueKind) return false;
            }
            else if (!AreEqual(left[i], right[i])) return false;
        }
        return true;
    }

    private sealed class Node(JsonObject attributes, bool expression)
    {
        public JsonObject Attributes { get; } = attributes;
        public bool Expression { get; } = expression;
        public Dictionary<string, Argument> Arguments { get; } = new(StringComparer.Ordinal);
    }

    private sealed class Argument(bool list)
    {
        public bool List { get; } = list;
        public List<Node> Items { get; } = [];
    }

    private static bool TryDecode(JsonElement dump, out JsonObject? tree)
    {
        tree = null;
        if (dump.ValueKind != JsonValueKind.Array || dump.GetArrayLength() == 0) return false;
        var nodes = new List<Node>();
        foreach (var record in dump.EnumerateArray())
        {
            if (record.ValueKind != JsonValueKind.Object || !UniqueProperties(record)) return false;
            var hasClass = record.TryGetProperty("c", out var className);
            var hasValue = record.TryGetProperty("v", out _);
            if (hasClass && (className.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(className.GetString()))) return false;
            var enumValue = hasClass && className.GetString() == "DataType.Type";
            if ((!hasClass && !hasValue) || (hasClass && hasValue != enumValue)) return false;
            var attributes = new JsonObject();
            foreach (var field in record.EnumerateObject())
            {
                if (field.Name is "i" or "k" or "a") continue;
                if (field.Name == "t" && field.Value.ValueKind != JsonValueKind.Null)
                {
                    if (!TryDecode(field.Value, out var type)) return false;
                    attributes[field.Name] = type;
                }
                else attributes[field.Name] = JsonNode.Parse(field.Value.GetRawText());
            }
            var node = new Node(attributes, hasClass && !enumValue);
            if (nodes.Count == 0)
            {
                if (!hasClass || record.TryGetProperty("i", out _) || record.TryGetProperty("k", out _)
                    || record.TryGetProperty("a", out _)) return false;
            }
            else
            {
                // serde emits a rooted tree with parents preceding children. This rejects
                // negative/out-of-range/forward/self indexes, cycles and disconnected roots.
                if (!record.TryGetProperty("i", out var index) || index.ValueKind != JsonValueKind.Number
                    || !index.TryGetInt32(out var parentIndex) || parentIndex < 0 || parentIndex >= nodes.Count
                    || !record.TryGetProperty("k", out var key) || key.ValueKind != JsonValueKind.String) return false;
                var parent = nodes[parentIndex];
                if (!parent.Expression) return false;
                var list = false;
                if (record.TryGetProperty("a", out var array))
                {
                    if (array.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
                    list = array.GetBoolean();
                }
                var name = key.GetString()!;
                if (!parent.Arguments.TryGetValue(name, out var argument))
                    parent.Arguments.Add(name, argument = new Argument(list));
                else if (!list || !argument.List) return false; // No overwrite or list/scalar mixing.
                argument.Items.Add(node); // Encounter order is the serialized list order.
            }
            nodes.Add(node);
        }
        // Bottom-up construction avoids dependence on traversal order and recursion depth.
        var decoded = new Dictionary<Node, JsonObject>();
        for (var i = nodes.Count - 1; i >= 0; i--)
        {
            var node = nodes[i];
            var arguments = new JsonObject();
            foreach (var (name, argument) in node.Arguments)
            {
                var values = new JsonArray();
                foreach (var child in argument.Items) values.Add(decoded[child]);
                arguments[name] = new JsonObject { ["list"] = argument.List, ["items"] = values };
            }
            decoded.Add(node, new JsonObject { ["attributes"] = node.Attributes, ["arguments"] = arguments });
        }
        tree = decoded[nodes[0]];
        return true;
    }

    private static bool UniqueProperties(JsonElement value)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        return value.EnumerateObject().All(p => names.Add(p.Name));
    }
}
