using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace SqlGlotSharp.Tests;

public class GeneratorLeafTests
{
    // Oracle invokes exactly the Python handlers, not generate/sql dispatch.
    private const string OracleScript = """
        import sys, json
        sys.path.insert(0, 'python')
        from sqlglot import exp
        from sqlglot.generator import Generator
        results = []
        for case in json.load(sys.stdin):
            g = Generator(**case['options'])
            handler, text, args = case['handler'], case['text'], case['args']
            if handler == 'identifier_sql':
                node = exp.Identifier(this=text, quoted=args.get('quoted', False))
                if args.get('function_parent', False):
                    parent = exp.Anonymous(this=node)
                result = g.identifier_sql(node)
            elif handler == 'literal_sql':
                result = g.literal_sql(exp.Literal(this=text, is_string=args['is_string']))
            elif handler == 'boolean_sql':
                result = g.boolean_sql(exp.Boolean(this=text))
            elif handler == 'null_sql':
                result = g.null_sql(exp.Null())
            else:
                result = getattr(g, handler)(text, **args)
            results.append(result)
        print(json.dumps(results))
        """;

    [Theory]
    [InlineData("")]
    [InlineData("postgres")]
    public async Task Identifiers_match_python_normalization_quoting_and_unicode(string dialect)
    {
        string[] names = ["", "foo", "Foo", "select", "a b", "a\"b", "a\nb", "1name", "²name", "①name", "Ⅳname",
            "é", "İ", "ΟΣ", "𐐀", "\U0001F100name", "\U0001D7CEname", "\u0085name"];
        object[] modes = [false, true, "safe", "unsafe"];
        var cases = new List<LeafCase>();
        var actual = new List<string>();
        foreach (var identify in modes)
        foreach (var normalize in new[] { false, true })
        foreach (var pretty in new[] { false, true })
        foreach (var quoted in new[] { false, true })
        foreach (var name in names)
        {
            var g = new generator.Generator(dialect: dialect, identify: identify, normalize: normalize, pretty: pretty);
            var node = new exp.Identifier(@this: name, quoted: quoted);
            cases.Add(new("identifier_sql", name,
                new() { ["dialect"] = dialect, ["identify"] = identify, ["normalize"] = normalize, ["pretty"] = pretty },
                new() { ["quoted"] = quoted }));
            actual.Add(g.identifier_sql(node));
            Assert.Equal(name, node.name); // Leaf rendering does not mutate normalization/quote flags.
            Assert.Equal(quoted, node.quoted);
        }
        var functionName = new exp.Identifier(@this: "Foo");
        _ = new exp.Anonymous(@this: functionName);
        cases.Add(new("identifier_sql", "Foo", new() { ["dialect"] = dialect, ["identify"] = true },
            new() { ["function_parent"] = true }));
        actual.Add(new generator.Generator(dialect: dialect, identify: true).identifier_sql(functionName));
        await CompareOracle(cases, actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("postgres")]
    public async Task Literals_match_python_without_reformatting_numeric_text(string dialect)
    {
        string?[] strings = [null, "", "O'Reilly", "''", "a\\b", "line\nbreak", "\r\n\t\b\f\v", "\0", "café", "😀", "__SQLGLOT__LB__"];
        string?[] numbers = [null, "", "0", "-0", "001.2300", "1e-09", "-123456789012345678901234567890", "NaN", "Infinity"];
        var cases = new List<LeafCase>();
        var actual = new List<string>();
        foreach (var pretty in new[] { false, true })
        foreach (var isString in new[] { false, true })
        foreach (var text in isString ? strings : numbers)
        {
            var g = new generator.Generator(dialect: dialect, pretty: pretty);
            cases.Add(new("literal_sql", text, new() { ["dialect"] = dialect, ["pretty"] = pretty }, new() { ["is_string"] = isString }));
            actual.Add(g.literal_sql(new exp.Literal(@this: text, is_string: isString)));
        }
        await CompareOracle(cases, actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("postgres")]
    public async Task Escaping_matches_python_byte_flags_delimiters_and_line_sentinels(string dialect)
    {
        string[] strings = ["", "'\\\n\r\t\b\f\v\0", "\u007f\u0080éÿĀ😀", "<>x<>''", "__SQLGLOT__LB__\n"];
        var cases = new List<LeafCase>();
        var actual = new List<string>();
        foreach (var pretty in new[] { false, true })
        foreach (var byteString in new[] { false, true })
        foreach (var bytes in new[] { false, true })
        foreach (var backslash in new[] { false, true })
        foreach (var delimiter in new string?[] { null, "", "<>", "'" })
        foreach (var text in strings)
        {
            var escapedDelimiter = delimiter == "<>" ? "<escaped>" : "";
            cases.Add(new("escape_str", text, new() { ["dialect"] = dialect, ["pretty"] = pretty },
                new() { ["is_byte_string"] = byteString, ["is_bytes"] = bytes, ["escape_backslash"] = backslash,
                    ["delimiter"] = delimiter, ["escaped_delimiter"] = escapedDelimiter }));
            actual.Add(new generator.Generator(dialect: dialect, pretty: pretty).escape_str(text,
                escape_backslash: backslash, delimiter: delimiter, escaped_delimiter: escapedDelimiter,
                is_byte_string: byteString, is_bytes: bytes));
        }
        await CompareOracle(cases, actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("postgres")]
    public async Task Null_and_boolean_match_python_truthiness(string dialect)
    {
        object?[] values = [null, false, true, 0, 1, "", "false", Array.Empty<object>(), new object[] { 0 }];
        var g = new generator.Generator(dialect: dialect);
        var cases = new List<LeafCase> { new("null_sql", null, new() { ["dialect"] = dialect }, new()) };
        var actual = new List<string> { g.null_sql(new exp.Null()) };
        Assert.Equal("NULL", g.null_sql());
        foreach (var value in values)
        {
            cases.Add(new("boolean_sql", value, new() { ["dialect"] = dialect }, new()));
            actual.Add(g.boolean_sql(new exp.Boolean(@this: value)));
        }
        await CompareOracle(cases, actual);
    }

    [Fact]
    public void Leaves_support_dispatch_but_not_preprocessing()
    {
        var g = new generator.Generator();
        var node = new exp.Identifier(@this: "x");
        Assert.Equal("x", g.identifier_sql(node));
        Assert.Equal("x", g.sql(node));
        Assert.Same(node, g.preprocess(node));
        Assert.Equal("x", g.generate(node));
    }

    [Fact]
    public async Task Anchors_are_current_according_to_port_status()
    {
        const string script = """
            import sys, json
            from pathlib import Path
            sys.path.insert(0, 'src/tools')
            import port_status as p
            members = p.inventory(Path('python'), json.loads(Path('src/tools/scope.json').read_text()))
            anchors = p.scan_anchors(Path('src/SqlGlotSharp'))
            names = {'identifier_sql','literal_sql','escape_str','null_sql','boolean_sql','_replace_line_breaks'}
            entries = [x for x in p.report(members, anchors)['members'] if x['anchor'].startswith('sqlglot/generator.py::Generator.') and x['anchor'].split('.')[-1] in names]
            assert len(entries) == 6, entries
            assert all(x['status'] == 'ported' for x in entries), entries
            print('OK')
            """;
        Assert.Equal("OK", (await Python(script)).Trim());
    }

    private sealed record LeafCase(string handler, object? text,
        Dictionary<string, object?> options, Dictionary<string, object?> args);

    private static async Task CompareOracle(List<LeafCase> cases, List<string> actual)
    {
        var expected = JsonSerializer.Deserialize<string[]>(await Python(OracleScript, JsonSerializer.Serialize(cases)))!;
        Assert.Equal(cases.Count, expected.Length);
        for (var i = 0; i < cases.Count; i++)
            Assert.True(expected[i] == actual[i], $"Case {i}: {JsonSerializer.Serialize(cases[i])}\nExpected: {JsonSerializer.Serialize(expected[i])}\nActual: {JsonSerializer.Serialize(actual[i])}");
    }

    private static string RepositoryRoot => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath())!, "..", ".."));
    private static string SourcePath([CallerFilePath] string source = "") => source;

    private static async Task<string> Python(string script, string? input = null)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SQLGLOT_PYTHON") ?? "python")
        {
            WorkingDirectory = RepositoryRoot, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        start.ArgumentList.Add("-B");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(script);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            if (input is not null) await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, await stderr);
        return await stdout;
    }
}
