using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Parser = SqlGlotSharp.parser.Parser;
using Token = SqlGlotSharp.tokenizer_core.Token;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp.Tests;

public class ParserFunctionTests
{
    private sealed record Case(string sql, string method = "_parse_function", string level = "IMMEDIATE",
        int max_nodes = -1, bool anonymous = false, bool optional_parens = true, bool any_token = false,
        bool original_name = false, bool custom_registry = false, bool empty_registry = false, bool after_dot = false);
    private sealed class NamesDialect : dialects.dialect.Dialect
    {
        public override object? ORIGINAL_NAME_META_KEY => "original_name";
    }
    private static Parser Start(Case c, List<Token> input)
    {
        var p = new Parser(error_level: Enum.Parse<errors.ErrorLevel>(c.level), max_nodes: c.max_nodes,
            dialect: c.original_name ? new NamesDialect() : null);
        p.sql = c.sql; p._tokens = input; p._tokens_size = input.Count; p._index = -1; p._advance();
        if (c.after_dot) p._advance();
        return p;
    }
    private static object Snapshot(Token t) => new
    {
        type = t.token_type.ToString(), t.text, t.line, t.col, t.start, t.end, comments = t.comments.ToArray()
    };
    private static exp.Expr? Invoke(Parser p, Case c)
    {
        PyDict<string, Func<IReadOnlyList<object?>, exp.Expr>>? functions = c.custom_registry
            ? new() { ["CUSTOM"] = new exp.Abs().from_arg_list } : c.empty_registry ? new() : null;
        return c.method switch
        {
            "_parse_function" => p._parse_function(functions, c.anonymous, c.optional_parens, c.any_token),
            "_parse_function_call" => p._parse_function_call(functions, c.anonymous, c.optional_parens, c.any_token),
            "_parse_function_args" => new exp.Tuple(expressions: p._parse_function_args(alias: c.anonymous).Cast<object?>().ToList()),
            "_parse_expression" => p._parse_expression(),
            _ => throw new ArgumentException(c.method)
        };
    }

    [Fact]
    public async Task Python_cursor_oracle_matches_nested_ordinary_functions_validation_metadata_and_comments()
    {
        List<Case> cases = [];
        foreach (var sql in new[] {
            "", "1", "unknown", "mystery()", "mystery(1)", "mystery(1,2,)", "mystery(,)", "mystery(,,)",
            "mystery(1,,2)", "mystery(mystery(1), other(2, third()))", "mystery((1+2)*3,(a,b),(other(3)))",
            "mystery(a, b.c, \"x\", 'text', NULL, TRUE, FALSE, ?, :name, @value)",
            "mystery(ALL a + 2)", "mystery(1 AS value, a AS other)", "mystery(1 implicit)",
            "mystery() ; next", "mystery(1 +)", "mystery(1 +, 2)", "mystery(", "mystery(1", "mystery(1;",
            "mystery(mystery(1)", "mystery(1 AS)", "mystery(1) + ABS(2)",
            "ABS(1)", "ABS(mystery(1))", "ABS(ABS(-2)+3)", "ABS()", "ABS(1,2)",
            "ROUND(mystery(1),2)", "ROUND(1,2,3,4)", "SUM(a)", "SUM()", "NULLIF(1,mystery(2))",
            "STRPOS('abc','b')", "INSTR('abc','b')", "POWER(2,3)", "LENGTH('a')", "PI()",
            "\"MiXeD\"(1, mystery(2))", "{fn mystery(1)}", "{fn ABS(1)}", "{fn mystery(1)",
            "mystery(/* open */ 1 /* value */, /* comma */ other(2 /* inner */) /* close */) /* after */ ;",
            "/* leading */ unknown /* name */ (1 /* arg */) /* closing */ ;",
            "ABS(1) /* sqlglot.anonymous */", "ABS() /* sqlglot.anonymous */",
            "STRUCT(1 AS x)", "STRUCT(1 AS x, 2)", "STRUCT(x, x AS y)", "STRUCT(MAP(ARRAY(1), ARRAY(x)) AS a)",
            "ABS(1,2) /* sqlglot.anonymous extra */", "ABS(1) /* SQLGLOT.ANONYMOUS */",
            "ABS(1) /* \u001csqlglot.anonymous */", "mystery(\n other(\n 1\n ),\n 2\n)"
        })
            foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
                cases.Add(new(sql, level: level));
        foreach (var sql in new[] { "mystery(ABS(1)+other(2))*3", "ABS(mystery(2))", "mystery(1) AS value" })
            cases.Add(new(sql, "_parse_expression"));
        foreach (var sql in new[] { "ABS(1)", "CAST(1)", "COUNT(1)", "COALESCE(1,2)", "\"ABS\"(1)" })
            cases.Add(new(sql, anonymous: true));
        foreach (var sql in new[] { "ABS(1)", "abs(mystery(2))", "mystery(1)", "ABS(1) /* sqlglot.anonymous */" })
            cases.Add(new(sql, original_name: true));
        foreach (var limit in new[] { 0, 1, 2, 3, 4, 5, 10 })
            foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
                cases.Add(new("mystery(ABS(1), other((2+3)))", level: level, max_nodes: limit));
        cases.Add(new("CUSTOM(1)", custom_registry: true));
        cases.Add(new("CUSTOM()", custom_registry: true, level: "RAISE"));
        cases.Add(new("ABS(1)", empty_registry: true));
        cases.Add(new("mystery(1)", "_parse_function_call"));
        cases.Add(new("ABS(1)", optional_parens: false));
        cases.Add(new(". ABS(1)", after_dot: true));
        cases.Add(new(". CURRENT_DATE", after_dot: true));
        foreach (var sql in new[] { "1,mystery(2),(3),", "1 AS x, other(2)", ")" })
            foreach (var alias in new[] { false, true })
                cases.Add(new(sql, "_parse_function_args", level: "RAISE", anonymous: alias));
        await Compare(cases);
    }

    [Fact]
    public async Task Type_named_nested_function_matches_Python_after_type_lookahead_retreat()
    {
        await Compare([new Case("ABS(UNKNOWN(1))")]);
    }

    private static async Task Compare(List<Case> cases)
    {
        var prepared = cases.Select(c => tokens.Tokenizer.ForDialect("").tokenize(c.sql)).ToArray();
        var input = cases.Select((c, i) => new { c.sql, c.method, c.level, c.max_nodes, c.anonymous,
            c.optional_parens, c.any_token, c.original_name, c.custom_registry, c.empty_registry, c.after_dot,
            tokens = prepared[i].Select(Snapshot).ToArray() }).ToArray();
        var actual = cases.Select((c, i) =>
        {
            var p = Start(c, prepared[i]); exp.Expr? node = null; List<PyDict<string, object?>> failure = [];
            try { node = Invoke(p, c); }
            catch (errors.ParseError e) { failure = e.errors; }
            catch (NotPortedException e) { throw new InvalidOperationException($"Oracle fixture {i}: {JsonSerializer.Serialize(c)}", e); }
            return new { ast = serde.dump(node), failure, errors = p.errors.Select(e => e.errors).ToArray(),
                index = p._index, curr = Snapshot(p._curr), next = Snapshot(p._next), prev = Snapshot(p._prev),
                prev_comments = p._prev_comments.ToArray(), token_comments = prepared[i].Select(t => t.comments.ToArray()).ToArray(),
                consumed = prepared[i].Take((int)p._index).Select(Snapshot).ToArray(), node_count = p._node_count };
        }).ToArray();
        const string script = """
            import sys, json
            sys.path.insert(0, 'python')
            from sqlglot import exp
            from sqlglot.parser import Parser
            from sqlglot.dialects.dialect import Dialect
            from sqlglot.tokens import Token, TokenType
            from sqlglot.errors import ParseError, ErrorLevel
            from sqlglot.serde import dump
            class NamesDialect(Dialect): ORIGINAL_NAME_META_KEY = 'original_name'
            def snapshot(t):
                return dict(type=t.token_type.name, text=t.text, line=t.line, col=t.col,
                    start=t.start, end=t.end, comments=t.comments)
            results = []
            for c in json.load(sys.stdin):
                tokens = [Token(TokenType[t['type']], t['text'], line=t['line'], col=t['col'],
                    start=t['start'], end=t['end'], comments=t['comments']) for t in c['tokens']]
                p = Parser(error_level=ErrorLevel[c['level']], max_nodes=c['max_nodes'],
                    dialect=NamesDialect() if c['original_name'] else None)
                p.sql = c['sql']; p._tokens, p._tokens_size, p._index = tokens, len(tokens), -1
                p._advance()
                if c['after_dot']: p._advance()
                node, failure = None, []
                functions = {'CUSTOM': exp.Abs.from_arg_list} if c['custom_registry'] else {} if c['empty_registry'] else None
                try:
                    if c['method'] == '_parse_function_args':
                        node = exp.Tuple(expressions=p._parse_function_args(alias=c['anonymous']))
                    elif c['method'] == '_parse_expression': node = p._parse_expression()
                    else:
                        node = getattr(p, c['method'])(functions=functions, anonymous=c['anonymous'],
                            optional_parens=c['optional_parens'], any_token=c['any_token'])
                except ParseError as e: failure = e.errors
                results.append(dict(ast=dump(node), failure=failure, errors=[e.errors for e in p.errors],
                    index=p._index, curr=snapshot(p._curr), next=snapshot(p._next), prev=snapshot(p._prev),
                    prev_comments=p._prev_comments[:], token_comments=[t.comments[:] for t in tokens],
                    consumed=[snapshot(t) for t in tokens[:p._index]], node_count=p._node_count))
            print(json.dumps(results))
            """;
        var expected = JsonNode.Parse(await Python(script, JsonSerializer.Serialize(input)))!.AsArray();
        var observed = JsonSerializer.SerializeToNode(actual, serde.JsonOptions)!.AsArray();
        for (var i = 0; i < cases.Count; i++)
            Assert.True(JsonNode.DeepEquals(expected[i], observed[i]),
                $"Case {i}: {JsonSerializer.Serialize(input[i])}\nPython: {expected[i]}\nC#: {observed[i]}");
    }

    [Theory]



    [InlineData("mystery(SELECT 1)", "_parse_select", 3)]






    public void Unsupported_paths_are_explicit_not_reinterpreted(string sql, string dependency, long index)
    {
        var input = tokens.Tokenizer.ForDialect("").tokenize(sql);
        var p = Start(new(sql), input);
        if (dependency == "_parse_select")
        {
            var call = Assert.IsType<exp.Anonymous>(p._parse_function());
            Assert.IsType<exp.Select>(call.expressions[0]);
            Assert.Equal(input.Count, p._index);
        }
        else
        {
            var e = Assert.Throws<NotPortedException>(() => p._parse_function());
            Assert.Contains(dependency, e.Message); Assert.Equal(index, p._index);
        }
    }

    [Fact]
    public async Task Registry_keys_and_partial_anchor_status_match_upstream()
    {
        const string script = """
            import sys, json
            from pathlib import Path
            sys.path.insert(0, 'python'); sys.path.insert(0, 'src/tools')
            from sqlglot.parser import Parser
            import port_status
            members = port_status.inventory(Path('python'), json.loads(Path('src/tools/scope.json').read_text()))
            report = port_status.report(members, port_status.scan_anchors(Path('src/SqlGlotSharp')))
            rows = [r for r in report['members'] if any(c['file'] == 'parser.functions.cs' for c in r.get('csharp', []))]
            assert len(rows) == 5, rows
            for r in rows:
                expected = 'ported' if r['anchor'].endswith('::Parser._parse_function') else 'stub'
                assert r['status'] == expected and len(r['csharp']) == 1, (r['anchor'], r['hash'], r['status'])
            print(json.dumps(dict(functions=sorted(Parser.FUNCTIONS), parsers=sorted(Parser.FUNCTION_PARSERS))))
            """;
        var expected = JsonNode.Parse(await Python(script));
        var p = new Parser();
        var actual = JsonSerializer.SerializeToNode(new {
            functions = p.FUNCTIONS.Keys.Order(StringComparer.Ordinal).ToArray(),
            parsers = p.FUNCTION_PARSERS.Keys.Order(StringComparer.Ordinal).ToArray() });
        Assert.True(JsonNode.DeepEquals(expected, actual), $"Python: {expected}\nC#: {actual}");
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
