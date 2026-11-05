using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SqlGlotSharp.Tests;

public class QuerySubqueryTests
{
    private static exp.Expr? Alias(string kind) => kind switch
    {
        "table" => new exp.TableAlias(@this: new exp.Identifier(@this: "node", quoted: true),
            columns: new List<object?> { new exp.Identifier(@this: "c", quoted: false) }),
        "identifier" => new exp.Identifier(@this: "raw", quoted: false),
        "literal" => new exp.Literal(@this: "1", is_string: false),
        _ => null
    };

    [Fact]
    public async Task Aliases_copy_and_parent_linkage_match_python()
    {
        var cases = (from copy in new[] { true, false }
            from kind in new[] { "null", "string", "table", "identifier", "literal" }
            from text in kind == "string" ? new[] { "", "s", "select", "two words", "1x", "é", "aé", "a²", "a\u0301", "a\n", "a\n\n", "a😀" } : new[] { "" }
            select new { copy, kind, text }).ToArray();
        var actual = cases.Select(c =>
        {
            var query = new exp.Select(expressions: new List<object?> { new exp.Column(@this: new exp.Identifier(@this: "x", quoted: false)) });
            query.add_comments(["query"]); query.meta["tag"] = "original";
            var owner = new exp.Subquery(@this: query);
            object? alias = c.kind == "string" ? c.text : Alias(c.kind);
            var result = query.subquery(alias, c.copy);
            var wrapped = (exp.Expr)result.args["this"]!;
            var projection = (exp.Expr)((List<object?>)wrapped.args["expressions"]!)[0]!;
            var alias_node = result.args["alias"] as exp.Expr;
            return new { ast = serde.dump(result), same_query = ReferenceEquals(wrapped, query),
                same_projection = ReferenceEquals(projection, ((List<object?>)query.args["expressions"]!)[0]),
                same_alias = alias is exp.Expr && ReferenceEquals(alias_node, alias),
                query_parent_owner = ReferenceEquals(query.parent, owner), query_parent_result = ReferenceEquals(query.parent, result),
                wrapped_parent = ReferenceEquals(wrapped.parent, result), wrapped.arg_key,
                projection_parent = ReferenceEquals(projection.parent, wrapped),
                alias_parent = alias_node is null || ReferenceEquals(alias_node.parent, result),
                alias_key = alias_node?.arg_key, explicit_alias = result.args.ContainsKey("alias") };
        }).ToArray();
        const string script = """
            import sys,json
            sys.path.insert(0,'python')
            from sqlglot import exp
            from sqlglot.serde import dump
            output=[]
            for c in json.load(sys.stdin):
                query=exp.Select(expressions=[exp.Column(this=exp.Identifier(this='x',quoted=False))])
                query.add_comments(['query']); query.meta['tag']='original'
                owner=exp.Subquery(this=query)
                alias={'null':None,'table':exp.TableAlias(this=exp.Identifier(this='node',quoted=True),columns=[exp.Identifier(this='c',quoted=False)]),
                    'identifier':exp.Identifier(this='raw',quoted=False),'literal':exp.Literal(this='1',is_string=False)}.get(c['kind'])
                if c['kind']=='string': alias=c['text']
                result=query.subquery(alias,c['copy'])
                wrapped=result.this; projection=wrapped.expressions[0]; alias_node=result.args['alias']
                output.append(dict(ast=dump(result),same_query=wrapped is query,same_projection=projection is query.expressions[0],
                    same_alias=isinstance(alias,exp.Expr) and alias_node is alias,
                    query_parent_owner=query.parent is owner,query_parent_result=query.parent is result,
                    wrapped_parent=wrapped.parent is result,arg_key=wrapped.arg_key,projection_parent=projection.parent is wrapped,
                    alias_parent=alias_node is None or alias_node.parent is result,alias_key=alias_node.arg_key if alias_node is not None else None,
                    explicit_alias='alias' in result.args))
            print(json.dumps(output,ensure_ascii=True))
            """;
        var expected = JsonNode.Parse(await Python(script, JsonSerializer.Serialize(cases)));
        Assert.True(JsonNode.DeepEquals(expected, JsonSerializer.SerializeToNode(actual)), $"Expected: {expected}\nActual: {JsonSerializer.Serialize(actual)}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Copy_preserves_or_reuses_tree_and_does_not_copy_alias(bool copy)
    {
        var query = new exp.Select(expressions: new List<object?> { new exp.Literal(@this: "1", is_string: false) });
        var alias = new exp.TableAlias(@this: new exp.Identifier(@this: "a", quoted: false));
        var old_alias_owner = new exp.Subquery(@this: new exp.Select(), alias: alias);
        var result = query.subquery(alias, copy);
        var wrapped = (exp.Expr)result.args["this"]!;
        wrapped.set("distinct", new exp.Distinct());
        Assert.Equal(!copy, query.args.ContainsKey("distinct"));
        Assert.Same(alias, result.args["alias"]);
        Assert.Same(result, alias.parent);
        // Like Python, adoption does not remove the old owner's reference.
        Assert.Same(alias, old_alias_owner.args["alias"]);
        Assert.Null(result.parent);
        Assert.Equal("alias", alias.arg_key);
        Assert.Null(alias.index);
    }

    [Fact]
    public void Query_trait_supports_set_operations_but_rejects_non_query()
    {
        var union = new exp.Union(@this: new exp.Select(), expression: new exp.Select());
        Assert.Same(union, union.subquery(copy: false).args["this"]);
        Assert.Throws<InvalidOperationException>(() => new exp.Literal(@this: "1", is_string: false).subquery());
    }

    [Fact]
    public async Task Minimal_identifier_builder_matches_python()
    {
        var cases = (from name in new[] { "", "s", "two words", "é", "aé", "a²", "a\u0301", "a\n" }
            from quoted in new bool?[] { null, false, true }
            select new { name, quoted }).ToArray();
        var actual = cases.Select(c => serde.dump(exp.to_identifier(c.name, c.quoted))).ToArray();
        const string script = """
            import sys,json
            sys.path.insert(0,'python')
            from sqlglot import exp
            from sqlglot.serde import dump
            print(json.dumps([dump(exp.to_identifier(c['name'],quoted=c['quoted'])) for c in json.load(sys.stdin)],ensure_ascii=True))
            """;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(await Python(script, JsonSerializer.Serialize(cases))), JsonSerializer.SerializeToNode(actual)));
        Assert.Null(exp.to_identifier(null)); Assert.Null(exp.maybe_copy(null));
        var node = new exp.Identifier(@this: "x", quoted: true);
        Assert.Same(node, exp.to_identifier(node, quoted: false, copy: false));
        var clone = exp.to_identifier(node, quoted: false)!;
        Assert.NotSame(node, clone); Assert.Equal(true, clone.args["quoted"]);
        Assert.Throws<ArgumentException>(() => exp.to_identifier(1L));
    }

    [Fact]
    public async Task Anchors_match_port_status_and_are_unique()
    {
        const string script = """
            import sys,json
            from pathlib import Path
            sys.path.insert(0,'src/tools')
            import port_status
            members=port_status.inventory(Path('python'),json.loads(Path('src/tools/scope.json').read_text()))
            report=port_status.report(members,port_status.scan_anchors(Path('src/SqlGlotSharp')))
            rows=[r for r in report['members'] if any(c['file']=='expressions/query.subquery.cs' for c in r.get('csharp',[]))]
            assert len(rows)==3,rows
            for r in rows:
                assert len(r['csharp'])==1 and r['status']=='ported',r
            print('verified')
            """;
        Assert.Equal("verified", (await Python(script)).Trim());
    }

    private static string Source([CallerFilePath] string source = "") => source;
    private static async Task<string> Python(string script, string? input = null)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SQLGLOT_PYTHON") ?? "python")
        {
            WorkingDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Source())!, "..", "..")),
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-B"); start.ArgumentList.Add("-c"); start.ArgumentList.Add(script);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            if (input is not null) await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
            process.StandardInput.Close(); await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) { process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await stderr); return await stdout;
    }
}
