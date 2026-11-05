using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SqlGlotSharp.Extensions.Layer3.Tests;

/// <summary>
/// Layer3 (base + MEMBER OF). Expected values live in Layer3/expected.json, generated from the Python oracle
/// (src/tools/gen_layer3_expected.py, plugin src/tools/oracle_plugins/layer3.py) over the texts of the host corpus.
/// </summary>
public class Layer3Tests
{
    private static JsonNode Expected([CallerFilePath] string source = "") =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(source)!, "Layer3", "expected.json")))!;

    [Fact]
    public void Corpus_parses_to_the_same_ast_sql_and_errors_as_the_oracle()
    {
        var entries = Expected()["parses"]!.AsArray();
        Assert.True(entries.Count >= 110);
        var failures = new List<string>();
        foreach (var entry in entries)
        {
            var text = entry!["text"]!.GetValue<string>();
            var layer3 = dialects.dialect.Dialect.get_or_raise("layer3");
            if (entry["error"] is { } error)
            {
                var name = error.GetValue<string>();
                var actual = Record.Exception(() => SqlGlot.parse_one(text, read: "layer3"));
                if (actual is null || actual.GetType().Name != name) failures.Add($"{text}: expected {name}, got {actual?.GetType().Name ?? "no error"}");
                continue;
            }

            try
            {
                var node = SqlGlot.parse_one(text, read: "layer3");
                if (node.GetType().Name != entry["class"]!.GetValue<string>())
                    failures.Add($"{text}: class {node.GetType().Name} != {entry["class"]}");
                var sql = layer3.generator().generate(node);
                if (sql != entry["sql"]!.GetValue<string>()) failures.Add($"{text}: sql '{sql}' != '{entry["sql"]}'");
                var ast = JsonSerializer.SerializeToNode(serde.dump(node), serde.JsonOptions)!;
                if (!JsonNode.DeepEquals(Normalize(ast), Normalize(entry["ast"]!)))
                    failures.Add($"{text}: AST differs");
            }
            catch (Exception ex) { failures.Add($"{text}: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    // Python's dump omits nothing; C# serialization may reorder properties/skip nothing: compare as key-sorted trees.
    private static JsonNode? Normalize(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, Normalize(p.Value?.DeepClone())))),
        JsonArray a => new JsonArray(a.Select(x => Normalize(x?.DeepClone())).ToArray()),
        _ => node?.DeepClone()
    };

    [Theory]
    [InlineData("x MEMBER OF y", "JSON_ARRAY_CONTAINS(x, y)")]
    [InlineData("'a' MEMBER OF t.col AND b = 1", "JSON_ARRAY_CONTAINS('a', t.col) AND b = 1")]
    public void Member_of_is_json_array_contains(string text, string expected)
    {
        var dialect = dialects.dialect.Dialect.get_or_raise("layer3");
        Assert.Equal(expected, dialect.generator().generate(SqlGlot.parse_one(text, read: "layer3")));
        Assert.Contains(SqlGlot.parse_one(text, read: "layer3").find_all<exp.JSONArrayContains>(), _ => true);
    }

    [Fact]
    public void Member_of_is_not_a_base_dialect_keyword()
    {
        Assert.Throws<errors.ParseError>(() => SqlGlot.parse_one("x MEMBER OF y"));
    }

    [Fact]
    public void Unknown_functions_are_anonymous_and_known_ones_typed()
    {
        Assert.IsType<exp.Anonymous>(SqlGlot.parse_one("MY_UNKNOWN(x)", read: "layer3"));
        Assert.IsType<exp.Upper>(SqlGlot.parse_one("UPPER(x)", read: "layer3"));
    }

    [Fact]
    public void Tree_api_and_builders_cover_the_host_surface()
    {
        Assert.Equal("t.a", exp.column("a", table: "t").sql("layer3"));
        Assert.Equal("a", exp.column("a").sql());
        var ast = SqlGlot.parse_one("COUNT(DISTINCT t.x) > 1 AND y IS NULL", read: "layer3");
        Assert.Equal(new[] { "y", "t.x" }, ast.find_all<exp.Column>().Select(c => c.sql()).ToArray()); // breadth-first, like Python
        Assert.NotNull(ast.find<exp.Count>());
        Assert.IsType<exp.And>(ast.find<exp.Count>()!.find_ancestor(typeof(exp.And)));
        Assert.Equal("t", ast.find_all<exp.Column>().Last().text("table"));
        Assert.True(ast.find<exp.Is>()!.parent is exp.And);
        var copy = ast.copy();
        copy.find<exp.Is>()!.replace(new exp.Boolean(@this: true));
        Assert.Equal("COUNT(DISTINCT t.x) > 1 AND TRUE", copy.sql());
        Assert.Equal("COUNT(DISTINCT t.x) > 1 AND y IS NULL", ast.sql());
    }

    // --- extension from outside the library (requirement A): a dialect defined in this assembly ---

    private sealed class Layer3Like : dialects.dialect.Dialect
    {
        public override parser.Parser CreateParser(errors.ErrorLevel? error_level = null) => new LikeParser(error_level, this);
        public override generator.Generator CreateGenerator(bool pretty = false) => new generator.Generator(dialect: this, pretty: pretty);
        public override tokens.Tokenizer CreateTokenizer() => new Layer3Tokenizer(this);
    }

    private sealed class LikeParser(errors.ErrorLevel? level, object dialect) : parsers.@base.BaseParser(level, 100, 3, -1, dialect)
    {
        public override PyDict<tokenizer_core.TokenType, Func<parser.Parser, exp.Expr?, exp.Expr?>> RANGE_PARSERS { get; } = Build();

        private static PyDict<tokenizer_core.TokenType, Func<parser.Parser, exp.Expr?, exp.Expr?>> Build()
        {
            var table = new PyDict<tokenizer_core.TokenType, Func<parser.Parser, exp.Expr?, exp.Expr?>>();
            foreach (var (key, value) in parser.Parser.RANGE_PARSERS_) table[key] = value;
            table[tokenizer_core.TokenType.MEMBER_OF] = (self, @this) =>
                self.expression(new exp.JSONArrayContains(@this: @this, expression: self._parse_column()));
            return table;
        }
    }

    [Fact]
    public void A_dialect_defined_outside_the_library_can_be_registered_and_used()
    {
        dialects.dialect.Dialect.register("layer3_external", typeof(Layer3Like));
        try
        {
            Assert.Equal("JSON_ARRAY_CONTAINS(x, t.c)",
                Assert.Single(SqlGlot.transpile("x MEMBER OF t.c", read: "layer3_external", write: "layer3_external")));
        }
        finally
        {
            dialects.dialect.Dialect.unregister("layer3_external");
        }
    }
}
