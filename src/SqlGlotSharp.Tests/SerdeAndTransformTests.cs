using System.Text.Json;
using SqlGlotSharp.Corpus;

namespace SqlGlotSharp.Tests;

public class SerdeAndTransformTests
{
    [Fact]
    public void Transform_copies_by_default_and_does_not_visit_replacement_subtrees()
    {
        var original = new exp.Select(expressions: new List<object?> { new exp.Var(@this: "x"), new exp.Var(@this: "y") });
        var visited = new List<string>();
        var transformed = (exp.Select)original.transform(node =>
        {
            visited.Add(node.GetType().Name);
            return node is exp.Var ? new exp.Literal(@this: node.name) : node;
        });
        Assert.Equal(new[] { "Select", "Var", "Var" }, visited);
        Assert.IsType<exp.Var>(original.expressions[0]);
        Assert.All(transformed.expressions, node => Assert.IsType<exp.Literal>(node));
        original.transform(node => node is exp.Var && node.name == "x" ? null : node, copy: false);
        Assert.Single(original.expressions);
    }

    [Fact]
    public void Serde_preserves_types_comments_metadata_and_parent_links()
    {
        var variable = new exp.Var(@this: "x") { comments = ["note"], _type = new exp.DataType(@this: exp.DType.INT) };
        variable.meta["nested"] = new List<object?> { "value", 1L };
        variable.meta["t"] = new List<object?> { 1L }; // Metadata must not be mistaken for type payloads.
        var root = new exp.Select(expressions: new List<object?> { variable });
        var json = serde.dump_json(root);
        var loaded = Assert.IsType<exp.Select>(serde.load(json));
        Assert.True(JsonElement.DeepEquals(json, serde.dump_json(loaded)));
        Assert.Same(loaded, ((exp.Expr)loaded.expressions[0]!).parent);
        Assert.Equal(exp.DType.INT, ((exp.Expr)loaded.expressions[0]!)._type!.args["this"]);
        Assert.Null(serde.load((IReadOnlyList<PyDict<string, object?>>?)null));
    }

    [Fact]
    public void L2_oracle_inputs_roundtrip_and_report_local_class_limitations()
    {
        var directory = Path.Combine(global::SqlGlotSharp.Corpus.CorpusReader.RootDirectory, "v30.27.0");
        var failures = new List<string>();
        var unsupported = new List<string>();
        var count = 0;
        foreach (var item in CorpusReader.Read(directory).Where(c => c.Level == "L2"))
        {
            try
            {
                var ast = item.Payload.GetProperty("ast");
                var node = serde.load(ast);
                var actual = serde.dump_json(node);
                if (!JsonElement.DeepEquals(ast, actual)) failures.Add($"{item.Id}: roundtrip differs");
            }
            catch (NotPortedException error) { unsupported.Add($"{item.Id}: {error.Anchor}"); }
            catch (Exception error) { failures.Add($"{item.Id}: {error.Message}"); }
            count++;
        }
        Assert.True(count > 0);
        // These are explicitly unsupported, not accepted SQL failures or skipped tests.
        // Test-local Python classes cannot be imported even by Python serde.load.
        Assert.Equal(2, unsupported.Count);
        Assert.All(unsupported, detail => Assert.Contains(".<locals>.SpecialUdf", detail));
        Assert.True(failures.Count == 0, $"{failures.Count}/{count} failures: " + string.Join("\n", failures.Take(10)));
    }

    private static string SourceDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
