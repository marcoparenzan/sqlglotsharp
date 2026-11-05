using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Parser = SqlGlotSharp.parser.Parser;
using BaseParser = SqlGlotSharp.parsers.@base.BaseParser;
using Dialect = SqlGlotSharp.dialects.dialect.Dialect;
using Postgres = SqlGlotSharp.dialects.postgres.Postgres;

namespace SqlGlotSharp.Tests;

public class BaseParserTests
{
    private sealed record Case(string kind, string sql, string method = "parse", string level = "IMMEDIATE",
        bool optional_parens = true, bool after_dot = false, int max_nodes = -1);

    [Fact]
    public void Factory_uses_BaseParser_without_changing_generic_or_Postgres_data()
    {
        var dialect = new Dialect();
        var p = Assert.IsType<BaseParser>(dialect.parser());
        Assert.Same(dialect, p.dialect);
        Assert.NotSame(p, dialect.parser());
        Assert.Equal(errors.ErrorLevel.IGNORE, dialect.parser(errors.ErrorLevel.IGNORE).error_level);
        Assert.IsType<parsers.postgres.PostgresParser>(new Postgres().parser());
        var generic = new Parser();
        Assert.False(generic.NO_PAREN_FUNCTIONS.ContainsKey(tokenizer_core.TokenType.LOCALTIME));
        Assert.False(generic.NO_PAREN_FUNCTIONS.ContainsKey(tokenizer_core.TokenType.LOCALTIMESTAMP));
        Assert.Contains(tokenizer_core.TokenType.STRAIGHT_JOIN, generic.ID_VAR_TOKENS);
        Assert.Contains(tokenizer_core.TokenType.STRAIGHT_JOIN, generic.TABLE_ALIAS_TOKENS);
        Assert.DoesNotContain(tokenizer_core.TokenType.STRAIGHT_JOIN, p.ID_VAR_TOKENS);
        Assert.DoesNotContain(tokenizer_core.TokenType.STRAIGHT_JOIN, p.TABLE_ALIAS_TOKENS);
        Assert.NotSame(generic.NO_PAREN_FUNCTIONS, p.NO_PAREN_FUNCTIONS);
        Assert.IsType<exp.Localtime>(Assert.IsType<exp.Select>(Assert.Single(dialect.parse("SELECT LOCALTIME"))).expressions[0]);
        Assert.IsType<exp.Localtimestamp>(Assert.IsType<exp.Select>(Assert.Single(dialect.parse("SELECT LOCALTIMESTAMP"))).expressions[0]);
    }

    [Fact]
    public void Both_original_LOCALTIME_corpus_mismatches_pass_without_exclusions()
    {
        string[] initialIds = [
            "0327ff7091dc3642b22f240382069eb393f5fc9cd20ab997651308a0f96426df",
            "144e8150240a5447e89c3f5a858a4a0b2c030952f3c6aa9487c954a4ee829a5e",
            "16461e5565eb8c2555109873adf712c903f029601e50f61f90bd922609ba34b1",
            "178ebb4d3f2e93c2107da22dea639e94f5bf10913deb61a09470ffc45db55402",
            "3e04139ddd457182b3d56f047d4c6997d9d75fa1b7cb1080687b2390e8fe5f3d",
            "4d10c57af166493a72c79ba9b8a98c4376d21b98247be89cb4fc7d16b7f980c6",
            "63b333d1667557c1139ef47a2dfdf95590da8e6e8f27466196e0e49dbd559672",
            "7069d1c31896963f062003d1bb9cba1c502d79bc7a567311ebd9dfd416ed0dd5",
            "7a49e1ae7899302a9f1aa52fc18e2658ce1e4349771c0daaea49d4bf726e0bc7",
            "917d4deaf1a3728d509205598152ec56eafd12dadc3d795d4d592bbeb8d525bf",
            "950b736f8261be5cb19dbc2d6c7ab08ad266b170144e99f65716e4cb84643ff0",
            "b1ffcf986ef73bca582a08dee7ddf71c7878ea0047d7eb4d24186cd0bb49140e",
            "b71e6dc08995ca163afd61363e64da31a2e84e429c5a90667288d31cd59c0f44",
            "b7a0318a92168597fd5b14df0f2a2359329d77a1bc17ebfe279c6c73de623a2b",
            "b7d7e3fe7f5dc03bb8a996db1987c86b897bbd0f7c4084683ded3a72a03a8c46",
            "fc6b513867d2a9a3415e223f08147f684e1b8847aa59f3a0d9e1ded19f092777"
        ];
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Source())!, "..", ".."));
        var cases = Corpus.CorpusReader.Read(Path.Combine(global::SqlGlotSharp.Corpus.CorpusReader.RootDirectory, "v30.27.0"))
            .Where(c => initialIds.Contains(c.Id) && c.Payload.GetProperty("sql").GetString()!
                .Contains("LOCALTIME", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.Equal(2, cases.Length);
        foreach (var c in cases) Corpus.CorpusVerifier.Verify(c);
    }

    [Fact]
    public async Task Python_base_generic_and_Postgres_match_functions_identifiers_comments_errors_and_cursor()
    {
        List<Case> cases = [];
        foreach (var kind in new[] { "base", "generic", "postgres" })
        {
            foreach (var name in new[] { "CURRENT_DATE", "CURRENT_DATETIME", "CURRENT_TIME", "CURRENT_TIMESTAMP",
                "CURRENT_USER", "CURRENT_ROLE", "LOCALTIME", "LOCALTIMESTAMP", "CURRENT_CATALOG", "SESSION_USER" })
            {
                foreach (var sql in new[] { name, $"/* before */ {name} /* name */ ;", $"{name}()", $"{name}(3)" })
                    cases.Add(new(kind, sql, "_parse_function_call"));
                cases.Add(new(kind, name, "_parse_function_call", optional_parens: false));
                cases.Add(new(kind, ". " + name, "_parse_function_call", after_dot: true));
                foreach (var level in new[] { "IMMEDIATE", "RAISE", "WARN", "IGNORE" })
                    foreach (var sql in new[] { $"SELECT {name}", $"SELECT /* before */ {name} /* name */ AS x",
                        $"SELECT {name} +", $"SELECT {name}; SELECT 2" })
                        cases.Add(new(kind, sql, level: level));
            }
            foreach (var name in new[] { "STRAIGHT_JOIN", "LOCALTIME", "LOCALTIMESTAMP", "CURRENT_CATALOG", "SESSION_USER",
                "CURRENT_DATE", "WINDOW", "foo", "\"LOCALTIME\"" })
                foreach (var method in new[] { "_parse_id_var", "_parse_table_alias" })
                    cases.Add(new(kind, $"/* before */ {name} /* after */", method));
            foreach (var sql in new[] { "SELECT t.LOCALTIME, \"LOCALTIMESTAMP\"", "SELECT a AS STRAIGHT_JOIN",
                "SELECT a FROM tbl AS STRAIGHT_JOIN", "SELECT LOCALTIME LOCALTIMESTAMP", "SELECT SESSION_USER, CURRENT_CATALOG" })
                foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
                    cases.Add(new(kind, sql, level: level));
            foreach (var limit in new[] { 0, 1, 2 })
                foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
                    cases.Add(new(kind, "/* lead */ CURRENT_DATE /* tail */", "_parse_function_call", level, max_nodes: limit));
        }
        var actual = cases.Select(Snapshot).ToArray();
        const string script = """
            import sys, json
            sys.path.insert(0, 'python')
            from sqlglot.parser import Parser
            from sqlglot.parsers.base import BaseParser
            from sqlglot.parsers.postgres import PostgresParser
            from sqlglot.dialects.dialect import Dialect
            from sqlglot.dialects.postgres import Postgres
            from sqlglot.errors import ErrorLevel, ParseError
            from sqlglot.serde import dump
            def token(t):
                return dict(type=t.token_type.name, text=t.text, line=t.line, col=t.col,
                            start=t.start, end=t.end, comments=t.comments)
            results = []
            for c in json.load(sys.stdin):
                d = Postgres() if c['kind'] == 'postgres' else Dialect()
                cls = dict(base=BaseParser, generic=Parser, postgres=PostgresParser)[c['kind']]
                p = cls(error_level=ErrorLevel[c['level']], max_nodes=c['max_nodes'], dialect=d)
                tokens = d.tokenize(c['sql'])
                ast, failure, message = None, [], None
                try:
                    if c['method'] == 'parse':
                        ast = [dump(n) for n in p.parse(tokens, c['sql'])]
                    else:
                        p.sql = c['sql']; p._tokens = tokens; p._tokens_size = len(tokens); p._index = -1; p._advance()
                        if c['after_dot']: p._advance()
                        n = p._parse_function_call(optional_parens=c['optional_parens']) if c['method'] == '_parse_function_call' else getattr(p,c['method'])()
                        ast = [dump(n)]
                except ParseError as e:
                    failure, message = e.errors, str(e)
                results.append(dict(ast=ast, failure=failure, message=message, errors=[e.errors for e in p.errors],
                    index=p._index, nodes=p._node_count, curr=token(p._curr), prev=token(p._prev),
                    next=token(p._next), prev_comments=p._prev_comments))
            print(json.dumps(results))
            """;
        var expected = JsonNode.Parse(await Python(script, JsonSerializer.Serialize(cases)))!.AsArray();
        Assert.Equal(cases.Count, expected.Count);
        for (var i = 0; i < cases.Count; i++)
        {
            var node = JsonSerializer.SerializeToNode(actual[i]);
            Assert.True(JsonNode.DeepEquals(expected[i], node), $"{cases[i]}\nPython: {expected[i]}\nC#: {node}");
        }
    }

    private static object Token(tokenizer_core.Token t) => new
    {
        type = t.token_type.ToString(), t.text, t.line, t.col, t.start, t.end, comments = t.comments.ToArray()
    };
    private static object Snapshot(Case c)
    {
        Dialect d = c.kind == "postgres" ? new Postgres() : new Dialect();
        var level = Enum.Parse<errors.ErrorLevel>(c.level);
        Parser p = c.kind switch
        {
            "base" => new BaseParser(error_level: level, max_nodes: c.max_nodes, dialect: d),
            "postgres" => new parsers.postgres.PostgresParser(error_level: level, max_nodes: c.max_nodes, dialect: d),
            _ => new Parser(error_level: level, max_nodes: c.max_nodes, dialect: d)
        };
        var input = d.tokenize(c.sql);
        object? ast = null; List<PyDict<string, object?>> failure = []; string? message = null;
        try
        {
            if (c.method == "parse") ast = p.parse(input, c.sql).Select(serde.dump).ToArray();
            else
            {
                p.sql = c.sql; p._tokens = input; p._tokens_size = input.Count; p._index = -1; p._advance();
                if (c.after_dot) p._advance();
                var n = c.method switch
                {
                    "_parse_function_call" => p._parse_function_call(optional_parens: c.optional_parens),
                    "_parse_id_var" => p._parse_id_var(),
                    "_parse_table_alias" => p._parse_table_alias(),
                    _ => throw new ArgumentException(c.method)
                };
                ast = new[] { serde.dump(n) };
            }
        }
        catch (errors.ParseError e) { failure = e.errors; message = e.ToString(); }
        return new { ast, failure, message, errors = p.errors.Select(e => e.errors).ToArray(),
            index = p._index, nodes = p._node_count, curr = Token(p._curr), prev = Token(p._prev),
            next = Token(p._next), prev_comments = p._prev_comments.ToArray() };
    }

    private static string Source([CallerFilePath] string path = "") => path;
    private static async Task<string> Python(string script, string input)
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
            await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
            process.StandardInput.Close(); await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) { process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await stderr);
        return await stdout;
    }
}
