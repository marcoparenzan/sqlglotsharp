using System.Text.Json;

namespace SqlGlotSharp;

public static partial class serde
{
    // py: sqlglot/serde.py::StackVal #1d1e334706
    private readonly record struct StackVal(object? Node, int? Index, string? ArgKey, bool IsArray);
    // py: sqlglot/serde.py::INDEX #49773514fb
    public const string INDEX = "i";
    // py: sqlglot/serde.py::ARG_KEY #a553e45e52
    public const string ARG_KEY = "k";
    // py: sqlglot/serde.py::IS_ARRAY #347d8c4ae5
    public const string IS_ARRAY = "a";
    // py: sqlglot/serde.py::CLASS #09fa0f1c77
    public const string CLASS = "c";
    // py: sqlglot/serde.py::TYPE #d53b08df5d
    public const string TYPE = "t";
    // py: sqlglot/serde.py::COMMENTS #9b3c332c4e
    public const string COMMENTS = "o";
    // py: sqlglot/serde.py::META #4f957ff94a
    public const string META = "m";
    // py: sqlglot/serde.py::VALUE #0e2b809b27
    public const string VALUE = "v";
    // py: sqlglot/serde.py::DATA_TYPE #539681b4b1
    public const string DATA_TYPE = "DataType.Type";

    // py: sqlglot/serde.py::dump #50d2aa1105
    public static List<PyDict<string, object?>> dump(object? expression)
    {
        var i = 0;
        List<PyDict<string, object?>> payloads = [];
        var stack = new Stack<StackVal>(); stack.Push(new(expression, null, null, false));
        while (stack.TryPop(out var item))
        {
            var payload = new PyDict<string, object?>();
            if (item.Index is not null) payload[INDEX] = item.Index;
            if (item.ArgKey is not null) payload[ARG_KEY] = item.ArgKey;
            if (item.IsArray) payload[IS_ARRAY] = true;
            payloads.Add(payload);
            if (item.Node is exp.Expr node)
            {
                if (!TryPythonClassName(node.GetType(), out var name)) throw new NotPortedException($"serde.dump:{node.GetType().Name}");
                payload[CLASS] = name;
                var type = node.type;
                if (type is not null && !ReferenceEquals(type, node)) payload[TYPE] = dump(type);
                if (node.comments is { Count: > 0 }) payload[COMMENTS] = node.comments;
                if (node._meta is not null) payload[META] = node._meta;
                foreach (var (key, values) in node.args.Reverse())
                {
                    if (values is List<object?> list)
                        for (var j = list.Count - 1; j >= 0; j--) stack.Push(new(list[j], i, key, true));
                    else if (values is not null) stack.Push(new(values, i, key, false));
                }
            }
            else if (item.Node is exp.DType dtype) { payload[CLASS] = DATA_TYPE; payload[VALUE] = exp.ToValue(dtype); }
            else payload[VALUE] = item.Node;
            i++;
        }
        return payloads;
    }

    // py: sqlglot/serde.py::load #6e8f9c0426
    public static object? load(IReadOnlyList<PyDict<string, object?>>? payloads)
    {
        if (payloads is not { Count: > 0 }) return null;
        var root = _load(payloads[0]);
        var nodes = new List<object?> { root };
        foreach (var payload in payloads.Skip(1))
        {
            var node = payload.ContainsKey(CLASS) ? _load(payload) : payload[VALUE];
            nodes.Add(node);
            var parent = (exp.Expr)nodes[Convert.ToInt32(payload[INDEX])]!;
            var key = (string)payload[ARG_KEY]!;
            if (py.truthy(payload.get(IS_ARRAY))) parent.append(key, node);
            else parent.set(key, node);
        }
        return root;
    }

    // py: sqlglot/serde.py::_load #e1d5e96e5c
    public static object _load(PyDict<string, object?> payload)
    {
        var name = (string)payload[CLASS]!;
        if (name == DATA_TYPE) return exp.DTypeFromValue((string)payload[VALUE]!);
        var type = name == TimeBucketClassName ? typeof(exp.TimeBucket)
            : ClassNames.FirstOrDefault(pair => pair.Value == name || pair.Key.Name == name).Key;
        if (type is null || type.IsAbstract || type.IsInterface) throw new NotPortedException($"serde.load:{name}");
        var expression = (exp.Expr)Activator.CreateInstance(type, [new PyDict<string, object?>()])!;
        expression._type = load(PayloadList(payload.get(TYPE))) as exp.DataType;
        expression.comments = payload.get(COMMENTS) as List<string>;
        if (payload.get(COMMENTS) is List<object?> comments) expression.comments = comments.Cast<string>().ToList();
        expression._meta = payload.get(META) as PyDict<string, object?>;
        return expression;
    }

    // Extension identities live outside the generated upstream class registry.
    private const string TimeBucketClassName = "oracle_plugins.timebucket_contract.TimeBucket";
    private static bool TryPythonClassName(Type type, out string name)
    {
        if (type == typeof(exp.TimeBucket)) { name = TimeBucketClassName; return true; }
        return ClassNames.TryGetValue(type, out name!);
    }

    internal static string PythonClassName(Type type) => TryPythonClassName(type, out var name) ? name : type.FullName ?? type.Name;

    /// <summary>Reverse of <see cref="PythonClassName"/> for dotted Python names, e.g. "sqlglot.expressions.query.Table".</summary>
    public static Type? TypeFromPythonClassName(string name) => name == TimeBucketClassName ? typeof(exp.TimeBucket)
        : ClassNames.FirstOrDefault(pair => pair.Value == name).Key;

    public static object? load(JsonElement json) => load(ReadPayloads(json));
    private static List<PyDict<string, object?>>? ReadPayloads(JsonElement json) => json.ValueKind == JsonValueKind.Null ? null :
        json.EnumerateArray().Select(item => (PyDict<string, object?>)FromJson(item)!).ToList();
    private static IReadOnlyList<PyDict<string, object?>>? PayloadList(object? value) => value switch
    {
        null => null,
        IReadOnlyList<PyDict<string, object?>> payloads => payloads,
        List<object?> items => items.Cast<PyDict<string, object?>>().ToList(),
        _ => throw new InvalidDataException("Invalid nested type payload")
    };
    private static object? FromJson(JsonElement json) => json.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => json.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => ReadNumber(json),
        JsonValueKind.Array => json.EnumerateArray().Select(FromJson).ToList(),
        JsonValueKind.Object => JsonMapping(json),
        _ => throw new InvalidDataException("Unsupported JSON value")
    };
    private static PyDict<string, object?> JsonMapping(JsonElement json)
    {
        var mapping = new PyDict<string, object?>();
        foreach (var property in json.EnumerateObject()) mapping[property.Name] = FromJson(property.Value);
        return mapping;
    }
}
