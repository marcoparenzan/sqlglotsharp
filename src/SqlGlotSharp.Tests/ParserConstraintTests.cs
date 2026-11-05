using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dialect = SqlGlotSharp.dialects.dialect.Dialect;
using Token = SqlGlotSharp.tokenizer_core.Token;

namespace SqlGlotSharp.Tests;

public class ParserConstraintTests
{
    private sealed record Case(string sql, string mode = "statement", string dialect = "postgres", string level = "IMMEDIATE", int max_nodes = -1, bool optional = false, bool props = false, bool named = false);
    private static object Snapshot(Token t) => new
    { type = t.token_type.ToString(), t.text, t.line, t.col, t.start, t.end, comments = t.comments.ToArray() };

    private static readonly string[] Tables = [
        "CREATE TABLE t (id INT PRIMARY KEY, name TEXT UNIQUE, qty INT CHECK (qty > 0), parent INT REFERENCES p(id))",
        "CREATE TABLE t (a INT, b INT, PRIMARY KEY (a,b), UNIQUE(a,b), CHECK (a > 0), FOREIGN KEY (b) REFERENCES p(id))",
        "CREATE TABLE t (a INT CONSTRAINT pk PRIMARY KEY, b INT CONSTRAINT uq UNIQUE, c INT CONSTRAINT ck CHECK (c>0), d INT CONSTRAINT fk REFERENCES db.p(id))",
        "CREATE TABLE t (a INT, CONSTRAINT pk PRIMARY KEY (a), CONSTRAINT uq UNIQUE (a), CONSTRAINT ck CHECK (a>0), CONSTRAINT fk FOREIGN KEY (a) REFERENCES db.p(id))",
        "CREATE TABLE t (a INT REFERENCES p ON DELETE CASCADE ON UPDATE RESTRICT DEFERRABLE INITIALLY DEFERRED)",
        "CREATE TABLE t (a INT, FOREIGN KEY (a) REFERENCES p(id) MATCH FULL ON DELETE SET NULL ON UPDATE SET DEFAULT)",
        "CREATE TABLE t (a INT UNIQUE NULLS NOT DISTINCT, UNIQUE NULLS NOT DISTINCT (a))",
        "CREATE TABLE t (a INT PRIMARY KEY DESC, b INT DEFAULT 1 CONSTRAINT nn NOT NULL CHECK (b>0) ENFORCED)",
        "CREATE TABLE t (a INT, PRIMARY KEY (a) INCLUDE (a) USING INDEX TABLESPACE ts)",
        "CREATE /* create */ TABLE t (a /* col */ INT CONSTRAINT /* constraint */ pk /* name */ PRIMARY KEY /* pk */, b INT REFERENCES /* ref */ db.p /* table */ (id /* id */) ON DELETE /* delete */ CASCADE /* action */, CONSTRAINT ck CHECK /* check */ (a > 0 /* value */) /* closing */) /* end */",
        "CREATE TABLE t (a INT, CONSTRAINT ck CHECK (a>0) UNIQUE (a))"
    ];

    [Fact]
    public async Task Public_Postgres_table_constraints_match_live_Python()
    {
        foreach (var sql in Tables)
        {
            var actual = serde.dump(Assert.Single(new dialects.postgres.Postgres().parse(sql)));
            var expected = JsonNode.Parse(await Python("""
                import sys,json
                sys.path.insert(0,'python')
                from sqlglot.dialects.postgres import Postgres
                from sqlglot.serde import dump
                print(json.dumps(dump(Postgres().parse(json.load(sys.stdin))[0])))
                """, JsonSerializer.Serialize(sql)));
            Assert.True(JsonNode.DeepEquals(expected, JsonSerializer.SerializeToNode(actual, serde.JsonOptions)),
                $"{sql}\nPython: {expected}\nC#: {JsonSerializer.Serialize(actual, serde.JsonOptions)}");
        }
    }

    [Fact]
    public async Task Direct_helpers_match_live_AST_errors_comments_and_cursors()
    {
        List<Case> seeds = Tables.Select(s => new Case(s)).ToList();
        seeds.AddRange(new[] {
            new Case("PRIMARY KEY (a, b)", "constraint"), new Case("CONSTRAINT pk PRIMARY KEY (a)", "constraint"),
            new Case("CONSTRAINT ck CHECK (a>0) UNIQUE (a)", "constraint"), new Case("\"CHECK\" (a)", "constraint"),
            new Case("CHECK", "constraint"), new Case("CHECK x", "column"), new Case("CONSTRAINT x", "column"),
            new Case("CONSTRAINT", "column"), new Case("NOT x", "column"), new Case("UNIQUE NOT NULL", "column"),
            new Case("PRIMARY KEY UNIQUE", "column"), new Case("CONSTRAINT UNIQUE (a)", "constraint"),
            new Case("(a>0) ENFORCED", "check"), new Case("x", "check"), new Case("()", "check"),
            new Case("(a >)", "check"), new Case("(a > 0", "check"),
            new Case("(a,b)", "primary"), new Case("DESC", "primary"), new Case("ASC DEFERRABLE", "primary"),
            new Case("n(a)", "primary", named: true), new Case("a,b", "primary", optional: true, props: true),
            new Case("(a)", "primary", props: true), new Case("(a", "primary"), new Case("()", "primary"),
            new Case("(a) INCLUDE (b) WHERE a > 0", "primary"), new Case("(a) USING BTREE", "primary"),
            new Case("(a) TABLESPACE ts", "primary"),
            new Case("(a,b)", "unique"), new Case("KEY n(a)", "unique"), new Case("NULLS NOT DISTINCT (a)", "unique"),
            new Case("(a) USING HASH", "unique"), new Case("", "unique"),
            new Case("REFERENCES db.p(id)", "references"), new Case("REFERENCES p", "references"),
            new Case("x", "references"), new Case("REFERENCES", "references"), new Case("REFERENCES p(", "references"),
            new Case("(a,b) REFERENCES p(id,k)", "foreign"), new Case("REFERENCES p", "foreign"),
            new Case("(a)", "foreign"), new Case("() REFERENCES p", "foreign"),
            new Case("ON DELETE CASCADE ON UPDATE NO ACTION", "options"),
            new Case("ON DELETE SET NULL ON UPDATE SET DEFAULT", "options"), new Case("ON DELETE RESTRICT", "options"),
            new Case("ON DELETE bad", "options"), new Case("ON", "options"),
            new Case("MATCH FULL DEFERRABLE INITIALLY DEFERRED NOT ENFORCED RELY NORELY USING HASH", "options"),
            new Case("MATCH PARTIAL INITIALLY IMMEDIATE", "options"), new Case("MATCH SIMPLE", "options"),
            new Case("NOT NULL", "options"), new Case("\"DEFERRABLE\"", "options"), new Case("", "options"),
            new Case("REFERENCES /* ref */ p /* p */ (id /* id */) ON DELETE /* action */ CASCADE /* end */", "references"),
            new Case("CONSTRAINT /* c */ ck /* n */ CHECK /* kind */ (a>0 /* value */) /* end */", "column"),
            new Case("(a /* a */, b /* b */) /* list */ INCLUDE /* include */ (c /* c */)", "primary"),
            new Case("(a INT, PRIMARY KEY (a), FOREIGN KEY (a) REFERENCES p(id))", "schema")
        });
        List<Case> cases = [];
        foreach (var d in new[] { "", "postgres" })
            foreach (var level in new[] { "IMMEDIATE", "RAISE", "IGNORE" })
            {
                cases.AddRange(seeds.Select(c => c with { dialect = d, level = level }));
                foreach (var max in new[] { 0, 1, 2, 4, 8, 16, 32 })
                    cases.Add(new(Tables[1], dialect: d, level: level, max_nodes: max));
            }
        var prepared = cases.Select(c => tokens.Tokenizer.ForDialect(c.dialect).tokenize(c.sql)).ToArray();
        var input = cases.Select((c, i) => new { c.sql, c.mode, c.dialect, c.level, c.max_nodes, c.optional, c.props, c.named,
            tokens = prepared[i].Select(Snapshot).ToArray() });
        var actual = cases.Select((c, i) =>
        {
            var p = Dialect.get_or_raise(c.dialect).parser();
            p.sql = c.sql; p.error_level = Enum.Parse<errors.ErrorLevel>(c.level); p.max_nodes = c.max_nodes;
            p._tokens = prepared[i]; p._tokens_size = prepared[i].Count; p._index = -1; p._advance();
            exp.Expr? node = null; List<string>? options = null; List<PyDict<string, object?>> failure = [];
            try
            {
                if (c.mode == "options") options = p._parse_key_constraint_options();
                else node = c.mode switch
                {
                    "column" => p._parse_column_constraint(), "constraint" => p._parse_constraint(),
                    "check" => p._parse_check_constraint(), "primary" => p._parse_primary_key(c.optional, c.props, c.named),
                    "unique" => p._parse_unique(), "references" => p._parse_references(),
                    "foreign" => p._parse_foreign_key(), "schema" => p._parse_schema(), _ => p._parse_statement()
                };
            }
            catch (errors.ParseError e) { failure = e.errors; }
            catch (NotPortedException e) { Assert.Fail($"Unexpected boundary {c}: {e.Message}"); }
            return new { ast = serde.dump(node), options, failure, errors = p.errors.Select(e => e.errors).ToArray(),
                index = p._index, curr = Snapshot(p._curr), next = Snapshot(p._next), prev = Snapshot(p._prev),
                prev_comments = p._prev_comments.ToArray(), token_comments = prepared[i].Select(t => t.comments.ToArray()).ToArray(),
                node_count = p._node_count };
        }).ToArray();
        var expected = JsonNode.Parse(await Python("""
            import sys,json
            sys.path.insert(0,'python')
            from sqlglot.dialects import Dialect
            from sqlglot.tokens import Token,TokenType
            from sqlglot.errors import ParseError,ErrorLevel
            from sqlglot.serde import dump
            def snap(t):
                return dict(type=t.token_type.name,text=t.text,line=t.line,col=t.col,start=t.start,end=t.end,comments=t.comments)
            results=[]
            for c in json.load(sys.stdin):
                tokens=[Token(TokenType[t['type']],t['text'],line=t['line'],col=t['col'],start=t['start'],end=t['end'],comments=t['comments']) for t in c['tokens']]
                p=Dialect.get_or_raise(c['dialect']).parser(error_level=ErrorLevel[c['level']],max_nodes=c['max_nodes'])
                p.sql=c['sql']; p._tokens,p._tokens_size,p._index=tokens,len(tokens),-1; p._advance()
                node,options,failure=None,None,[]
                try:
                    mode=c['mode']
                    if mode=='options': options=p._parse_key_constraint_options()
                    elif mode=='primary': node=p._parse_primary_key(c['optional'],c['props'],c['named'])
                    else:
                        method={'column':'_parse_column_constraint','constraint':'_parse_constraint','check':'_parse_check_constraint','unique':'_parse_unique','references':'_parse_references','foreign':'_parse_foreign_key','schema':'_parse_schema'}.get(mode,'_parse_statement')
                        node=getattr(p,method)()
                except ParseError as e: failure=e.errors
                results.append(dict(ast=dump(node),options=options,failure=failure,errors=[e.errors for e in p.errors],
                    index=p._index,curr=snap(p._curr),next=snap(p._next),prev=snap(p._prev),prev_comments=p._prev_comments[:],
                    token_comments=[t.comments[:] for t in tokens],node_count=p._node_count))
            print(json.dumps(results))
            """, JsonSerializer.Serialize(input)))!.AsArray();
        var observed = JsonSerializer.SerializeToNode(actual, serde.JsonOptions)!.AsArray();
        for (var i = 0; i < cases.Count; i++)
            Assert.True(JsonNode.DeepEquals(expected[i], observed[i]), $"Case {i}: {cases[i]}\nPython: {expected[i]}\nC#: {observed[i]}");
    }

    // Upstream rejects these constraint modifiers with a ParseError (verified against the Python oracle).
    [Theory]
    [InlineData("a INT REFERENCES p ON DELETE SET NULL (a)")]
    [InlineData("a INT REFERENCES p NOT DEFERRABLE")]
    [InlineData("a INT REFERENCES p NOT VALID")]
    [InlineData("a INT CHECK(a>0) NOT ENFORCED")]
    [InlineData("a INT CHECK(a>0) NO INHERIT")]
    public void Unknown_constraint_modifiers_are_parse_errors_like_upstream(string body)
    {
        foreach (var d in new[] { "", "postgres" })
            Assert.Throws<errors.ParseError>(() => SqlGlot.Parse($"CREATE TABLE t ({body})", d));
    }

    // Formerly NotPorted boundaries (index params, EXCLUDE, IDENTITY(...)); expected values come from the Python oracle.
    [Theory]
    [InlineData("CREATE TABLE t (a INT, EXCLUDE USING gist (a WITH =))", "CREATE TABLE t (a INT, EXCLUDE USING gist(a WITH =))")]
    [InlineData("CREATE TABLE t (a INT, PRIMARY KEY (a) WITH (fillfactor=70))", "CREATE TABLE t (a INT, PRIMARY KEY (a) WITH (fillfactor=70))")]
    [InlineData("CREATE TABLE t (id INT IDENTITY(1, 2))", "CREATE TABLE t (id INT GENERATED BY DEFAULT AS IDENTITY (START WITH 1 INCREMENT BY 2))")]
    [InlineData("CREATE TABLE t (a INT COMMENT 'x', b INT COLLATE \"C\")", "CREATE TABLE t (a INT, b INT COLLATE \"C\")")]
    public void Constraint_callbacks_match_upstream(string sql, string expected) =>
        Assert.Equal(expected, Assert.Single(SqlGlot.Transpile(sql, "postgres")));

    private static string Source([CallerFilePath] string source = "") => source;
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
        Assert.True(process.ExitCode == 0, await stderr); return await stdout;
    }
}
