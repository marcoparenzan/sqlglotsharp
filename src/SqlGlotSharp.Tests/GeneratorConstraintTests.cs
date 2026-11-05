using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Pg = SqlGlotSharp.generators.postgres.PostgresGenerator;

namespace SqlGlotSharp.Tests;

public class GeneratorConstraintTests
{
    private sealed record Case(bool postgres, bool pretty, bool comments, bool copy, bool leading, bool identify,
        bool normalize, long width, string method);
    private sealed record Result(JsonElement node, string? sql, string? error, JsonElement state);

    // Parsing happens only in the live Python fixture producer. The C# side builds
    // exactly that production AST and calls the real Generator, never a test renderer.
    private const string Oracle = """
        import sys,json
        sys.path.insert(0,'python')
        import sqlglot
        from sqlglot import exp
        from sqlglot.generator import Generator
        from sqlglot.generators.postgres import PostgresGenerator
        fixtures=[
          'CREATE TABLE t (id INT PRIMARY KEY, code TEXT UNIQUE, qty INT CHECK (qty > 0), parent INT REFERENCES public.p (id) ON DELETE SET NULL)',
          'CREATE TABLE IF NOT EXISTS public.t (id INT, code TEXT, CONSTRAINT pk PRIMARY KEY (id, code), CONSTRAINT uq UNIQUE (code), CONSTRAINT ck CHECK (id > 0), CONSTRAINT fk FOREIGN KEY (id) REFERENCES public.p (id) MATCH FULL ON UPDATE CASCADE ON DELETE RESTRICT DEFERRABLE INITIALLY IMMEDIATE)',
          'CREATE TABLE t (id INT CONSTRAINT pk PRIMARY KEY, code TEXT CONSTRAINT uq UNIQUE NULLS NOT DISTINCT, qty INT CONSTRAINT ck CHECK (qty >= 0), p INT CONSTRAINT ref REFERENCES p (id))',
          'CREATE TABLE t (id INT, code TEXT, PRIMARY KEY (id), UNIQUE NULLS NOT DISTINCT (code), CHECK (id > 1), FOREIGN KEY (id) REFERENCES p (id))',
          'CREATE TABLE t (a INT PRIMARY KEY USING INDEX TABLESPACE "pg_default" DEFERRABLE INITIALLY DEFERRED)',
          'CREATE TABLE t (a INT UNIQUE USING INDEX TABLESPACE "pg_default" DEFERRABLE)',
          'CREATE TABLE t (a INT UNIQUE NULLS NOT DISTINCT WITH (fillfactor=70) USING INDEX TABLESPACE pg_default)',
          'CREATE TABLE t (a INT, b INT, PRIMARY KEY (a) INCLUDE (b) WITH (fillfactor=70) USING INDEX TABLESPACE pg_default DEFERRABLE)',
          'CREATE TABLE t (a INT, b INT, CONSTRAINT uq UNIQUE (a) INCLUDE (b) WITH (fillfactor=70) USING INDEX TABLESPACE pg_default DEFERRABLE)',
          'CREATE TABLE t (a INT NOT NULL DEFAULT 1 PRIMARY KEY, b INT UNIQUE, c INT CHECK (c > 0)) AS SELECT 1',
        ]
        def dump(n):
            if isinstance(n,list): return [dump(x) for x in n]
            if isinstance(n,exp.DType): return n.name
            if not isinstance(n,exp.Expr): return n
            # Expr is the concrete Python root, called Expression in the C# AST.
            return dict(type='Expression' if type(n) is exp.Expr else type(n).__name__,args={k:dump(v) for k,v in n.args.items()},comments=n.comments)
        def nodes(n):
            out=[n]
            for v in n.args.values():
                if isinstance(v,exp.Expr): out.extend(nodes(v))
                elif isinstance(v,list):
                    for x in v:
                        if isinstance(x,exp.Expr): out.extend(nodes(x))
            return out
        def state(original):
            return [dict(node=dump(x),parent=type(x.parent).__name__ if x.parent is not None else None,key=x.arg_key,index=x.index) for x in original]
        def extras():
            i=lambda name: exp.Identifier(this=name,quoted=False)
            opts=[exp.Var(this='DEFERRABLE'),exp.Var(this='INITIALLY DEFERRED')]
            ref=exp.Reference(this=exp.Table(this=i('p')),expressions=[i('id')],options=opts)
            yield exp.ForeignKey(this=i('fk_index'),expressions=[i('id')],reference=ref,delete='CASCADE',update='SET NULL',options=[exp.Var(this='NOT VALID')])
            yield exp.ForeignKey()
            yield exp.PrimaryKey(this=i('pk_index'),expressions=[i('id')],options=[exp.Var(this='DEFERRABLE')])
            yield exp.PrimaryKey(expressions=[])
            yield exp.Reference(this=exp.Table(this=i('p')),expressions=[i('a'),i('b')],options=[exp.Var(this='MATCH SIMPLE')])
            yield exp.Reference(this=exp.Table(this=i('p')))
            for desc in [None,False,True]:
                yield exp.PrimaryKeyColumnConstraint(desc=desc,include=exp.IncludeProperty(this=exp.Tuple(expressions=[i('b')])),options=[exp.Var(this='DEFERRABLE')])
            for nulls in [None,False,True]:
                yield exp.UniqueColumnConstraint(this=exp.Schema(expressions=[i('a')]),index_type='BTREE',nulls=nulls,on_conflict=exp.OnConflict(action=exp.Var(this='IGNORE')),options=[exp.Var(this='DEFERRABLE')])
            for enforced in [None,False,True]:
                yield exp.CheckColumnConstraint(this=exp.GT(this=exp.Column(this=i('a')),expression=exp.Literal.number(0)),enforced=enforced)
            yield exp.Check(this=exp.Boolean(this=True))
            yield exp.Constraint(this=i('named'),expressions=[exp.PrimaryKey(expressions=[i('a')]),exp.Check(this=exp.Boolean(this=True))])
            yield exp.Constraint(this=i('empty'),expressions=[])
            yield exp.IncludeProperty(this=exp.Var(this='DEFAULTS'),column_def=exp.ColumnDef(this=i('a'),kind=exp.DataType(this=exp.DType.INT)),alias=i('alias'))
            # A child with no handler must preserve the production unknown-expression error.
            yield exp.Check(this=exp.Expr())
        out=[]
        for c in json.load(sys.stdin):
            roots=[sqlglot.parse_one(s,read='postgres') for s in fixtures]+list(extras())
            for root in roots:
                for n in nodes(root):
                    if isinstance(n,(exp.Create,exp.Schema,exp.ColumnDef,exp.ColumnConstraint,exp.Constraint,exp.PrimaryKey,exp.PrimaryKeyColumnConstraint,exp.UniqueColumnConstraint,exp.Check,exp.CheckColumnConstraint,exp.Reference,exp.ForeignKey)):
                        n.comments=['constraint\ncomment','*/ /*']
                # Ensure the dialect override still removes parameter mode wrappers only.
                if isinstance(root,exp.Create):
                    root.this.expressions[0].args.setdefault('constraints',[]).insert(0,exp.ColumnConstraint(kind=exp.InOutColumnConstraint(input_=True,output=True)))
                    root.this.expressions[0]._set_parent('constraints',root.this.expressions[0].args['constraints'])
                n=root; original=nodes(n); before=dump(n)
                g=(PostgresGenerator if c['postgres'] else Generator)(dialect='postgres' if c['postgres'] else '',pretty=c['pretty'],comments=c['comments'],leading_comma=c['leading'],identify=c['identify'],normalize=c['normalize'],max_text_width=c['width'])
                try:
                    sql=g.generate(n,copy=c['copy']) if c['method']=='generate' else g.sql(n)
                    error=None
                except ValueError as e:
                    sql=None; error=str(e).replace('Unsupported expression type Expr','Unsupported expression type Expression')
                out.append(dict(node=before,sql=sql,error=error,state=state(original)))
        print(json.dumps(out))
        """;

    private static object? BuildValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => Build(value),
        JsonValueKind.Array => value.EnumerateArray().Select(BuildValue).ToList(),
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true, JsonValueKind.False => false,
        JsonValueKind.Number => value.GetInt64(), _ => null
    };
    private static exp.Expr Build(JsonElement node)
    {
        var type = node.GetProperty("type").GetString()!;
        var args = new PyDict<string, object?>();
        foreach (var arg in node.GetProperty("args").EnumerateObject())
            args[arg.Name] = type == "DataType" && arg.Name == "this" ? Enum.Parse<exp.DType>(arg.Value.GetString()!) : BuildValue(arg.Value);
        var expression = (exp.Expr)Activator.CreateInstance(exp.EXPR_CLASSES.Values.Single(t => t.Name == type), [args])!;
        expression.comments = node.GetProperty("comments").ValueKind == JsonValueKind.Null ? null
            : node.GetProperty("comments").EnumerateArray().Select(x => x.GetString()!).ToList();
        return expression;
    }
    private static List<exp.Expr> Nodes(exp.Expr root)
    {
        var result = new List<exp.Expr>();
        void Visit(exp.Expr n)
        {
            result.Add(n);
            foreach (var v in n.args.Values)
                if (v is exp.Expr e) Visit(e);
                else if (v is List<object?> list) foreach (var item in list) if (item is exp.Expr entry) Visit(entry);
        }
        Visit(root); return result;
    }
    private static object? Dump(object? value) => value switch
    {
        exp.Expr n => new { type = n.GetType().Name, args = n.args.ToDictionary(p => p.Key, p => Dump(p.Value)), comments = n.comments },
        exp.DType d => d.ToString(), List<object?> l => l.Select(Dump).ToArray(), _ => value
    };
    private static JsonElement State(List<exp.Expr> nodes) => JsonSerializer.SerializeToElement(nodes.Select(n => new
    {
        node = Dump(n), parent = n.parent?.GetType().Name, key = n.arg_key, index = n.index
    }).ToArray());
    private static bool Equal(JsonElement a, JsonElement b) => a.ValueKind == b.ValueKind && (a.ValueKind switch
    {
        JsonValueKind.Object => a.EnumerateObject().Count() == b.EnumerateObject().Count() && a.EnumerateObject().All(p => b.TryGetProperty(p.Name, out var r) && Equal(p.Value, r)),
        JsonValueKind.Array => a.GetArrayLength() == b.GetArrayLength() && a.EnumerateArray().Zip(b.EnumerateArray()).All(p => Equal(p.First, p.Second)),
        _ => a.ToString() == b.ToString()
    });
    private static generator.Generator G(Case c)
    {
        generator.Generator g = c.postgres ? new Pg() : new generator.Generator();
        g.dialect = dialects.dialect.Dialect.get_or_raise(c.postgres ? "postgres" : "");
        g.pretty = c.pretty; g.comments = c.comments; g.leading_comma = c.leading; g.identify = c.identify;
        g.normalize = c.normalize; g.max_text_width = c.width; return g;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Production_constraints_match_live_Python_SQL_and_AST(bool postgres)
    {
        var cases = new List<Case>();
        foreach (var pretty in new[] { false, true })
        foreach (var comments in new[] { false, true })
        foreach (var copy in new[] { false, true })
        foreach (var style in new[] { false, true })
        foreach (var method in new[] { "generate", "sql" })
            cases.Add(new(postgres, pretty, comments, copy, style, style, style, style ? 12 : 80, method));
        var results = JsonSerializer.Deserialize<Result[]>(await Python(JsonSerializer.Serialize(cases)))!;
        Assert.True(results.Length > cases.Count);
        var perCase = results.Length / cases.Count;
        for (var index = 0; index < results.Length; index++)
        {
            var result = results[index]; var c = cases[index / perCase]; var n = Build(result.node); var original = Nodes(n);
            var g = G(c); string? sql = null; string? error = null;
            try { sql = c.method == "generate" ? g.generate(n, copy: c.copy) : g.sql(n); }
            catch (ArgumentException e) { error = e.Message; }
            catch (NotPortedException e) { Assert.Fail($"Case {index}: {e.Message}"); }
            var state = State(original);
            Assert.True(result.sql == sql && result.error == error && Equal(result.state, state),
                $"Case {index}: {c}\nAST: {result.node}\nPython SQL: {result.sql}\nC# SQL: {sql}\nPython error: {result.error}\nC# error: {error}\nAST state equal: {Equal(result.state, state)}");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unsupported_attributes_and_option_children_fail_explicitly(bool postgres)
    {
        var c = new Case(postgres, false, true, false, false, false, false, 80, "generate");
        foreach (var type in new[] { typeof(exp.PrimaryKeyColumnConstraint), typeof(exp.UniqueColumnConstraint), typeof(exp.CheckColumnConstraint),
            typeof(exp.Constraint), typeof(exp.Check), typeof(exp.PrimaryKey), typeof(exp.ForeignKey), typeof(exp.Reference), typeof(exp.IncludeProperty) })
        foreach (var value in new object?[] { false, true, new exp.Var(new PyDict<string, object?> { ["this"] = "UNKNOWN" }) })
        {
            var n = (exp.Expr)Activator.CreateInstance(type, [new PyDict<string, object?> { ["future_modifier"] = value }])!;
            Assert.Contains("future_modifier", Assert.Throws<NotPortedException>(() => G(c).generate(n)).Message);
        }
        foreach (var type in new[] { typeof(exp.PrimaryKeyColumnConstraint), typeof(exp.PrimaryKey), typeof(exp.UniqueColumnConstraint), typeof(exp.ForeignKey), typeof(exp.Reference) })
        {
            var n = (exp.Expr)Activator.CreateInstance(type, [new PyDict<string, object?> { ["options"] = new List<object?> { new exp.IndexConstraintOption() } }])!;
            Assert.Contains("indexconstraintoption", Assert.Throws<NotPortedException>(() => G(c).generate(n)).Message);
        }
        var check = new exp.Check(new PyDict<string, object?> { ["enforced"] = false });
        Assert.Contains("enforced", Assert.Throws<NotPortedException>(() => G(c).generate(check)).Message);
    }

    private static string SourcePath([CallerFilePath] string source = "") => source;
    private static async Task<string> Python(string input)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("SQLGLOT_PYTHON") ?? "python")
        {
            WorkingDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath())!, "..", "..")),
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-B"); start.ArgumentList.Add("-c"); start.ArgumentList.Add(Oracle);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token); process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, await stderr); return await stdout;
    }
}
