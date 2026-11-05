# SqlGlotSharp

> **An unofficial C# port of [SQLGlot](https://github.com/tobymao/sqlglot)**, the SQL parser and transpiler by Toby Mao and the SQLGlot contributors. This README follows SQLGlot's own README section by section; every example is the C# equivalent of the original, run against this port and checked against the Python oracle. Questions about the port belong here, not to the SQLGlot team. See [About this Port](#about-this-port) and [License](#license).

SqlGlotSharp is a no-dependency SQL parser and transpiler for .NET. It can be used to format SQL or translate between the dialects it ports from SQLGlot: [PostgreSQL](https://www.postgresql.org/), [T-SQL](https://learn.microsoft.com/sql/t-sql/) (SQL Server) and [Microsoft Fabric](https://learn.microsoft.com/fabric/), plus SQLGlot's own base dialect. It aims to read a wide variety of SQL inputs and output syntactically and semantically correct SQL in the targeted dialects, exactly as SQLGlot does.

It is a port of a very comprehensive generic SQL parser, verified against a corpus recorded from SQLGlot's [test suite](https://github.com/tobymao/sqlglot/blob/main/tests/): **26,487 of 26,487 cases pass**. It is written purely in C#, with no Python at runtime and no dependency beyond the .NET base library.

You can easily [customize](#custom-dialects) the parser, [analyze](#metadata) queries, traverse expression trees, and programmatically [modify](#build-and-modify-sql) SQL.

SqlGlotSharp can detect a variety of [syntax errors](#parser-errors), such as unbalanced parentheses, incorrect usage of reserved keywords, and so on. These errors are highlighted and dialect incompatibilities can warn or raise depending on configurations.

The C# code mirrors the Python code line by line: same modules, same class and method names (`parse_one`, `find_all`, `TRANSFORMS`), same order. So SQLGlot's API [documentation](https://sqlglot.com/) and the expression tree [primer](https://github.com/tobymao/sqlglot/blob/main/posts/ast_primer.md) apply to this port as well.

Contributions are welcome; read [AGENTS.md](AGENTS.md) (the porting rules) and [UPSTREAM-SYNC.md](docs/UPSTREAM-SYNC.md) (how the port follows new SQLGlot releases) to get started.

## Table of Contents

- [Install](#install)
- [Versioning](#versioning)
- [Get in Touch](#get-in-touch)
- [FAQ](#faq)
- [Examples](#examples)
  - [Formatting and Transpiling](#formatting-and-transpiling)
  - [Metadata](#metadata)
  - [Parser Errors](#parser-errors)
  - [Unsupported Errors](#unsupported-errors)
  - [Build and Modify SQL](#build-and-modify-sql)
  - [SQL Optimizer](#sql-optimizer)
  - [AST Introspection](#ast-introspection)
  - [AST Diff](#ast-diff)
  - [Custom Dialects](#custom-dialects)
  - [SQL Execution](#sql-execution)
- [Used By](#used-by)
- [Documentation](#documentation)
- [Run Tests and Lint](#run-tests-and-lint)
- [Benchmarks](#benchmarks)
- [Optional Dependencies](#optional-dependencies)
- [Supported Dialects](#supported-dialects)
- [About this Port](#about-this-port)
- [License](#license)

## Install

No NuGet package has been published on nuget.org yet. Build from a local checkout (.NET 10 SDK):

```powershell
git clone https://github.com/marcoparenzan/sqlglotsharp.git SqlGlotSharp
cd SqlGlotSharp
dotnet build src/SqlGlotSharp.slnx
```

Then reference the library from your project:

```powershell
dotnet add MyApp reference path\to\SqlGlotSharp\src\SqlGlotSharp\SqlGlotSharp.csproj
```

Or pack it into a local NuGet feed and add it as a package (packages `SqlGlotSharp` and `SqlGlotSharp.Extensions.Layer3`; [USAGE.md](docs/USAGE.md) shows how).

Requirements for development (optional): the tests and tools compare the port with the official Python sources, so they need a clone of SQLGlot at the baseline tag in `python/` and the oracle Python 3.11 environment in `artifacts/oracle-venv`. [USAGE.md](docs/USAGE.md) explains how to set them up.

## Versioning

The version follows the SQLGlot release that the port mirrors: `MAJOR.MINOR.PATCH` is SQLGlot's version, and the suffix marks the .NET port and its own revision, for example `30.27.0-net10-preview.1`.

For SQLGlot's own numbering, given `MAJOR`.`MINOR`.`PATCH`:

- `PATCH` is incremented for backwards-compatible changes.
- `MINOR` is incremented for backwards-incompatible changes.
- `MAJOR` is incremented for significant backwards-incompatible changes.

## Get in Touch

For anything about the port, open an [issue](https://github.com/marcoparenzan/sqlglotsharp/issues) in this repository. Please do not report port bugs to the SQLGlot team. For SQLGlot itself, they would love to hear from you in their community [Slack channel](https://tobikodata.com/slack).

## FAQ

**I tried to parse SQL that should be valid, but it failed. Why?**

You probably didn't specify the source dialect. Without it, `parse_one` assumes the "SQLGlot dialect", which is designed to be a superset of all supported dialects. Always pass the dialect when you know it, e.g. `SqlGlot.parse_one(sql, read: "postgres")`. If parsing still fails, please file an issue.

**The generated SQL is not in the correct dialect!**

The target dialect must also be specified explicitly. For example, to transpile a query from PostgreSQL to T-SQL, do `SqlGlot.parse_one(sql, read: "postgres").sql("tsql")`, or `SqlGlot.transpile(sql, read: "postgres", write: "tsql")`.

**Why does SqlGlotSharp parse my invalid SQL without complaining?**

The parser is intentionally lenient, so it can accept queries that a real engine would reject. SQLGlot is a transpiler, not a validator, and so is this port. A query that parses successfully may still fail at execution time.

**Why doesn't the output preserve my formatting, casing or quoting?**

The query is parsed into an AST and SQL is generated back from it, so the meaning of the query is preserved rather than its exact text. Cosmetic details can change in the process. Use `pretty: true` for formatted output and the generator's `identify` option to quote all identifiers. Comments are preserved on a best-effort basis.

**Why are the method names snake_case?**

On purpose. The port has to follow every SQLGlot release, which is only cheap if a C# member can be put side by side with its Python twin. Every member keeps the Python name and carries a `// py:` anchor with a hash of the Python source ([AGENTS.md](AGENTS.md)).

**My dialect isn't supported. What are my options?**

You can subclass an existing dialect ([Custom Dialects](#custom-dialects)), or ship one as a separate project ([Creating a Dialect Plugin](#creating-a-dialect-plugin)). A dialect that SQLGlot supports but this port does not raises `NotPortedException`; there is never a silent fallback.

## Examples

All examples start with `using SqlGlotSharp;`.

### Formatting and Transpiling

Easily translate from one dialect to another. For example, functions, identifier quoting and row limits vary between dialects:

```csharp
SqlGlot.transpile("SELECT TOP 5 ISNULL(a, 0), GETDATE() FROM [dbo].[t]", read: "tsql", write: "postgres")[0]
```

```sql
SELECT COALESCE(a, 0), CURRENT_TIMESTAMP FROM "dbo"."t" LIMIT 5
```

SqlGlotSharp can even translate custom time formats:

```csharp
SqlGlot.transpile("SELECT TO_CHAR(x, 'YYYY-MM-DD') FROM t", read: "postgres", write: "tsql")[0]
```

```sql
SELECT FORMAT(x, 'yyyy-MM-dd') FROM t
```

Identifier delimiters and data types can be translated as well:

```csharp
using Dialect = SqlGlotSharp.dialects.dialect.Dialect;

// T-SQL uses brackets for delimited identifiers and FLOAT over REAL
var sql = """WITH baz AS (SELECT a, c FROM foo WHERE a = 1) SELECT f.a, b.b, baz.c, CAST("b"."a" AS REAL) d FROM foo f JOIN bar b ON f.a = b.a LEFT JOIN baz ON f.a = baz.a""";

// Translates the query into T-SQL, formats it, and delimits all of its identifiers
var generator = Dialect.get_or_raise("tsql").generator(pretty: true);
generator.identify = true;
Console.WriteLine(generator.generate(SqlGlot.parse_one(sql, read: "postgres")));
```

```sql
WITH [baz] AS (
  SELECT
    [a] AS [a],
    [c] AS [c]
  FROM [foo]
  WHERE
    [a] = 1
)
SELECT
  [f].[a],
  [b].[b],
  [baz].[c],
  CAST([b].[a] AS FLOAT) AS [d]
FROM [foo] AS [f]
JOIN [bar] AS [b]
  ON [f].[a] = [b].[a]
LEFT JOIN [baz]
  ON [f].[a] = [baz].[a]
```

(`SqlGlot.transpile` takes `pretty`; other generator options, such as `identify` and `unsupported_level`, are set on the generator as above.)

Comments are also preserved on a best-effort basis:

```csharp
var sql = """
/* multi
   line
   comment
*/
SELECT
  tbl.cola /* comment 1 */ + tbl.colb /* comment 2 */,
  CAST(x AS INT), -- comment 3
  y               -- comment 4
FROM
  bar /* comment 5 */,
  tbl --          comment 6
""";

// Note: line comments (`--`) are converted into block comments
Console.WriteLine(SqlGlot.transpile(sql, read: "postgres", write: "tsql", pretty: true)[0]);
```

```sql
/* multi
   line
   comment
*/
SELECT
  tbl.cola /* comment 1 */ + tbl.colb /* comment 2 */,
  CAST(x AS INTEGER), /* comment 3 */
  y /* comment 4 */
FROM bar /* comment 5 */, tbl /*          comment 6 */
```

### Metadata

You can explore SQL with expression helpers to do things like find columns and tables in a query:

```csharp
// print all column references (a and b)
foreach (var column in SqlGlot.parse_one("SELECT a, b + 1 AS c FROM d").find_all<exp.Column>())
    Console.WriteLine(column.alias_or_name);

// find all projections in select statements (a and c)
foreach (var select in SqlGlot.parse_one("SELECT a, b + 1 AS c FROM d").find_all<exp.Select>())
    foreach (exp.Expr projection in select.expressions)
        Console.WriteLine(projection.alias_or_name);

// find all tables (x, y, z)
foreach (var table in SqlGlot.parse_one("SELECT * FROM x JOIN y JOIN z").find_all<exp.Table>())
    Console.WriteLine(table.name);
```

Read SQLGlot's [ast primer](https://github.com/tobymao/sqlglot/blob/main/posts/ast_primer.md) to learn more about the internals: the C# tree has the same node classes and the same `args`.

### Parser Errors

When the parser detects an error in the syntax, it raises a `ParseError`:

```csharp
SqlGlot.transpile("SELECT foo FROM (SELECT baz FROM t");
```

```text
SqlGlotSharp.errors+ParseError: Expecting ). Line 1, Col: 34.
  SELECT foo FROM (SELECT baz FROM t
```

(The offending token is underlined with ANSI escape codes in a terminal.)

Structured syntax errors are accessible for programmatic use:

```csharp
try
{
    SqlGlot.transpile("SELECT foo FROM (SELECT baz FROM t");
}
catch (errors.ParseError e)
{
    foreach (var error in e.errors)
        Console.WriteLine(string.Join(", ", error.Select(kv => $"{kv.Key}={py.repr(kv.Value)}")));
}
```

```text
description='Expecting )', line=1, col=34, start_context='SELECT foo FROM (SELECT baz FROM ', highlight='t', end_context='', into_expression=None
```

### Unsupported Errors

It may not be possible to translate some queries between certain dialects. For these cases, SqlGlotSharp may emit a warning and will proceed to do a best-effort translation by default:

```csharp
SqlGlot.transpile("SELECT STRING_AGG(DISTINCT a, ',') FROM foo", read: "postgres", write: "tsql")
```

```sql
T-SQL STRING_AGG doesn't support DISTINCT.
'SELECT STRING_AGG(a, ',') FROM foo'
```

As in Python, the warning goes to the logger `"sqlglot"`, by default on stderr. Attach a handler with `py.logging.getLogger("sqlglot").addHandler(...)` to route it elsewhere ([USAGE.md](docs/USAGE.md#logging)).

This behavior can be changed by setting the `unsupported_level` attribute. For example, we can set it to either `RAISE` or `IMMEDIATE` to ensure an exception is raised instead:

```csharp
var generator = Dialect.get_or_raise("tsql").generator();
generator.unsupported_level = errors.ErrorLevel.RAISE;
generator.generate(SqlGlot.parse_one("SELECT STRING_AGG(DISTINCT a, ',') FROM foo", read: "postgres"));
```

```text
SqlGlotSharp.errors+UnsupportedError: T-SQL STRING_AGG doesn't support DISTINCT.
```

Some queries need additional information to be transpiled accurately, such as the schemas of the referenced tables. In SQLGlot the `qualify` and `annotate_types` optimizer rules provide it; this port has `annotate_types` at expression level only, and `qualify` is not ported (see [SQL Optimizer](#sql-optimizer)).

Transpilation is a hard problem, and SQLGlot solves it incrementally. This port follows its releases ([UPSTREAM-SYNC.md](docs/UPSTREAM-SYNC.md)), so improvements made upstream arrive here at the next sync.

### Build and Modify SQL

SqlGlotSharp supports incrementally building SQL expressions:

```csharp
var where = exp.condition("x=1").and_("y=1");
exp.select("*").from_("y").where(where).sql();
```

```sql
'SELECT * FROM y WHERE x = 1 AND y = 1'
```

It's possible to modify a parsed tree:

```csharp
((exp.Select)SqlGlot.parse_one("SELECT x FROM y")).from_("z").sql();
```

```sql
'SELECT x FROM z'
```

The builders keep their Python names and arguments: `select`, `from_`, `where`, `group_by`, `having`, `order_by`, `limit`, `offset`, `join` (with `on`, `using`, `join_type`, `join_alias`), `with_`, `distinct`, `union`/`intersect`/`except_`, `condition`, `and_`/`or_`/`xor`/`not_`, `alias_`, `paren`, `convert`, and on any expression `eq`, `neq`, `is_`, `like`, `isin`, `between`, `as_`, `asc`, `desc`. By default they return a modified copy, as in Python. Not ported yet: `insert`, `update`, `delete`, `merge`, `cast`, `func`, `case` and the other helpers of `builders.py`.

Parsed expressions can also be transformed recursively by applying a mapping function to each node in the tree:

```csharp
var expression_tree = SqlGlot.parse_one("SELECT a FROM x");

object? transformer(exp.Expr node) =>
    node is exp.Column column && column.name == "a" ? SqlGlot.parse_one("FUN(a)") : node;

var transformed_tree = (exp.Expr)expression_tree.transform(transformer);
transformed_tree.sql();
```

```sql
'SELECT FUN(a) FROM x'
```

### SQL Optimizer

SQLGlot can rewrite queries into an "optimized" canonical form with the full `optimize` pipeline. This port has a subset: `simplify` and expression-level `annotate_types`. The scope-based rules (`qualify`, `qualify_columns`, `pushdown_predicates`, ...), and therefore `optimize` itself, are not ported and raise `NotPortedException`.

```csharp
using SqlGlotSharp.optimizer;

Console.WriteLine(simplify_module.simplify(
    SqlGlot.parse_one("SELECT A OR (B OR (C AND D)) FROM x WHERE 1 = 1 AND (TRUE OR Z = 2)")
).sql(pretty: true));
```

```sql
SELECT
  A OR B OR (
    C AND D
  )
FROM x
```

### AST Introspection

SQLGlot shows the AST of the parsed SQL with `repr`. The `repr` format is **not ported**; the tree can be inspected through `args`, `walk()` and `find_all()`, or serialized with `serde.dump`, which produces the same payload as SQLGlot's `sqlglot.serde.dump`:

```csharp
var payload = serde.dump(SqlGlot.parse_one("SELECT a + 1 AS z"));
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(payload, serde.JsonOptions));
```

```json
[{"c":"sqlglot.expressions.query.Select"},{"i":0,"k":"expressions","a":true,"c":"sqlglot.expressions.core.Alias"},{"i":1,"k":"this","c":"sqlglot.expressions.core.Add"}, ...]
```

### AST Diff

SQLGlot's semantic diff (`sqlglot.diff`) is **not ported**. See [Semantic Diff for SQL](https://github.com/tobymao/sqlglot/blob/main/posts/sql_diff.md) for what it does.

### Custom Dialects

Dialects can be added by subclassing `Dialect`. Python uses nested `Tokenizer`, `Parser` and `Generator` classes; in C# the dialect overrides three factories, and the subclasses override the class-level tables (`KEYWORDS`, `RANGE_PARSERS`, `TRANSFORMS`, ...):

```csharp
using SqlGlotSharp.dialects;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

public class Custom : dialect.Dialect
{
    public override parser.Parser CreateParser(errors.ErrorLevel? error_level = null) =>
        new SqlGlotSharp.parsers.@base.BaseParser(error_level, 100, 3, -1, this);
    public override generator.Generator CreateGenerator(bool pretty = false) =>
        new generator.Generator(dialect: this, pretty: pretty);
    public override tokens.Tokenizer CreateTokenizer() => new CustomTokenizer(this);
}

public class CustomTokenizer(object? dialect = null) : tokens.Tokenizer(dialect)
{
    public static readonly PyDict<string, TokenType> KEYWORDS_ = Build();
    public override PyDict<string, TokenType> KEYWORDS => KEYWORDS_;

    private static PyDict<string, TokenType> Build()
    {
        var keywords = new PyDict<string, TokenType>();
        foreach (var (key, value) in global::SqlGlotSharp.tokens.Tokenizer.KEYWORDS_) keywords[key] = value;
        keywords["INT64"] = TokenType.BIGINT;
        keywords["FLOAT64"] = TokenType.DOUBLE;
        return keywords;
    }
}

dialect.Dialect.register("custom", typeof(Custom));
SqlGlot.transpile("SELECT CAST(a AS INT64), CAST(b AS FLOAT64) FROM t", read: "custom", write: "postgres")[0]
```

```sql
SELECT CAST(a AS BIGINT), CAST(b AS DOUBLE PRECISION) FROM t
```

A multi-word keyword also needs its prefix in the keyword trie (`_KEYWORD_TRIE`). The Layer 3 extension ([`src/SqlGlotSharp.Extensions.Layer3/Layer3.cs`](src/SqlGlotSharp.Extensions.Layer3/Layer3.cs)) is a complete, tested example: tokenizer, parser and generator in one file.

### SQL Execution

SQLGlot's Python SQL engine (`sqlglot.executor`) is **not ported**. See [Writing a Python SQL engine from scratch](https://github.com/tobymao/sqlglot/blob/main/posts/python_sql_engine.md) for what it does.

## Used By

- Layer 3, a host project that uses SqlGlotSharp as its SQL expression layer, through the [Layer 3 extension](#supported-dialects).

SQLGlot itself is used by [SQLMesh](https://github.com/TobikoData/sqlmesh), [Apache Superset](https://github.com/apache/superset), [Dagster](https://github.com/dagster-io/dagster), [Ibis](https://github.com/ibis-project/ibis), [dlt](https://github.com/dlt-hub/dlt) and many others.

## Documentation

The API is SQLGlot's, under the same names: its hosted [documentation](https://sqlglot.com/) applies to every ported member. Python docstrings are kept as C# XML comments. The port's own documentation:

| Document | What it answers |
| --- | --- |
| [USAGE.md](docs/USAGE.md) | How to build, use, extend and measure the port; logging; the tools |
| [ARCHITECTURE.md](docs/ARCHITECTURE.md) | How the code is organised and verified |
| [ROADMAP.md](docs/ROADMAP.md) | Current state and open work |
| [ALIGNMENT.md](docs/ALIGNMENT.md) | Map against the official project: modules, dialects, drift from newer releases (generated) |
| [UPSTREAM-SYNC.md](docs/UPSTREAM-SYNC.md) | How to align the port with a new SQLGlot release, and the log of each sync |
| [AGENTS.md](AGENTS.md) | Porting rules: mirror the Python code, anchors, conversion table |
| [OPENPOINTS.md](docs/OPENPOINTS.md) | Known limits and decisions |
| [PORTING_PLAN.md](docs/history/PORTING_PLAN.md), [PORTING_LOG.md](docs/history/PORTING_LOG.md) | History: the original migration plan and the session log |

## Run Tests and Lint

From PowerShell, with the oracle Python set (see [USAGE.md](docs/USAGE.md)):

```powershell
$env:SQLGLOT_PYTHON = (Resolve-Path artifacts\oracle-venv\Scripts\python.exe).Path
dotnet build src/SqlGlotSharp.slnx                                   # Build (0 errors required)
dotnet test src/SqlGlotSharp.slnx                                    # Unit tests (2-5 minutes)
dotnet run --project src/Scoreboard -- D:\clones\corpus\v30.27.0   # Full corpus replay (about 7 minutes), results in docs/status
& $env:SQLGLOT_PYTHON -B src/tools/port_status.py --modules          # Coverage of Python members by C# anchors
```

## Benchmarks

The [benchmark queries](https://github.com/tobymao/sqlglot/blob/main/benchmarks/parse.py) of SQLGlot, timed with the same method (best of 5 runs), parse only, in seconds. SQLGlot is the pure Python version of the same baseline (`v30.27.0`) on Python 3.11.7; SqlGlotSharp is a .NET 10 Release build, with one untimed warm-up run per query so JIT compilation is not counted. Both on the same machine (Intel Core i7-11370H, Windows 11). The ratio is SqlGlotSharp / sqlglot: lower is faster.

|             Query |         sqlglot |    SqlGlotSharp | ratio |
| ----------------- | --------------- | --------------- | ----- |
|              tpch |        0.002997 |        0.001615 |  0.54 |
|             short |        0.000210 |        0.000120 |  0.57 |
|   deep_arithmetic |        0.007941 |        0.004311 |  0.54 |
|          large_in |        0.528162 |        0.208707 |  0.40 |
|            values |        0.630470 |        0.240330 |  0.38 |
|        many_joins |        0.012501 |        0.001807 |  0.14 |
|       many_unions |        0.050545 |        0.012397 |  0.25 |
| nested_subqueries |        0.001209 |        0.000197 |  0.16 |
|      many_columns |        0.013380 |        0.001870 |  0.14 |
|        large_case |        0.041258 |        0.012141 |  0.29 |
|     complex_where |        0.034861 |        0.008596 |  0.25 |
|         many_ctes |        0.021118 |        0.003553 |  0.17 |
|      many_windows |        0.023349 |        0.005804 |  0.25 |
|  nested_functions |        0.000747 |        0.000152 |  0.20 |
|     large_strings |        0.006834 |        0.001745 |  0.26 |
|      many_numbers |        0.120654 |        0.049989 |  0.41 |

Transpiling (parse and generate) gives similar ratios, 0.16 to 0.58. SQLGlot's own table, measured with Python 3.14 on another machine, shows its mypyc-compiled build (`sqlglot[c]`) at about 0.20–0.33 of pure Python, so SqlGlotSharp is in the same range as the compiled version, not clearly faster. The port was written to mirror the Python code, not for speed; nothing has been optimized yet. Known weakness: three very large stress inputs of SQLGlot's test suite (`UNION ALL` chains of 95 KB and 260 KB, a 2 MB `x=x=...` chain) make the parser quadratic.

```powershell
& $env:SQLGLOT_PYTHON -B src/tools/bench_compare.py --mode parse       # or --mode transpile
```

## Optional Dependencies

None. SQLGlot uses [dateutil](https://github.com/dateutil/dateutil) to simplify constant date and interval arithmetic, for example `DATE '2021-01-01' + INTERVAL '1' MONTH` to `CAST('2021-02-01' AS DATE)`. That simplification is not ported: it raises `NotPortedException` instead of leaving the arithmetic unevaluated.

## Supported Dialects

| Dialect | Support Level in SQLGlot | In SqlGlotSharp |
| --- | --- | --- |
| Athena | Official | Not ported |
| BigQuery | Official | Not ported |
| ClickHouse | Official | Not ported |
| Databricks | Official | Not ported |
| DAX | Community | Not ported |
| Doris | Community | Not ported |
| Dremio | Community | Not ported |
| Drill | Community | Not ported |
| Druid | Community | Not ported |
| DuckDB | Official | Not ported |
| Dune | Community | Not ported |
| Exasol | Community | Not ported |
| Fabric | Community | **Ported** (157 / 157 corpus cases) |
| Hive | Official | Not ported |
| Materialize | Community | Not ported |
| MySQL | Official | Not ported |
| Oracle | Official | Not ported |
| Postgres | Official | **Ported** (3,394 / 3,394) |
| Presto | Official | Not ported |
| PRQL | Community | Not ported |
| Redshift | Official | Not ported |
| RisingWave | Community | Not ported |
| SingleStore | Community | Not ported |
| Snowflake | Official | Not ported |
| Solr | Community | Not ported |
| Spark | Official | Not ported |
| SQLite | Official | Not ported |
| StarRocks | Official | Not ported |
| Tableau | Official | Not ported |
| Teradata | Community | Not ported |
| Trino | Official | Not ported |
| TSQL | Official | **Ported** (3,166 / 3,166) |
| YDB | Plugin | Not ported |
| MaxCompute | Plugin | Not ported |

The base "SQLGlot dialect" (`""`) is ported as well (12,438 / 12,438 corpus cases). Reading or writing a dialect that is not ported raises `NotPortedException`.

Dialects that are not part of SQLGlot:

| Dialect | Name | Where | What it is |
| --- | --- | --- | --- |
| TimescaleDB | `timescale` | built in | PostgreSQL plus `time_bucket`; writing PostgreSQL rewrites only the provably equivalent fixed-width form to `DATE_BIN`, every other overload raises `NotPortedException` |
| Layer 3 | `layer3` | extension, [`src/SqlGlotSharp.Extensions.Layer3`](src/SqlGlotSharp.Extensions.Layer3/Layer3.cs) | The base dialect plus `x MEMBER OF y`, parsed as `JSONArrayContains` |

**Official** and **Community** are SQLGlot's support levels for its own dialects, kept here for reference. In this port, a dialect is either ported in full (every corpus case passes) or not ported.

### Creating a Dialect Plugin

SQLGlot discovers plugin dialects through Python entry points. In .NET, a plugin is a separate project (or package) that references SqlGlotSharp, defines the dialect as in [Custom Dialects](#custom-dialects), and registers it by name at startup:

```csharp
using SqlGlotSharp.Extensions.Layer3;

Layer3.Register();   // Dialect.register("layer3", typeof(Layer3))
SqlGlot.parse_one("'a' MEMBER OF t.tags AND b = 1", read: "layer3").sql("layer3");
```

```sql
JSON_ARRAY_CONTAINS('a', t.tags) AND b = 1
```

The dialect can then be used like any built-in dialect. [USAGE.md](docs/USAGE.md#use-an-extension-layer-3) shows how to reference an extension project.

## About this Port

- **Baseline:** SQLGlot `v30.27.0` (commit `5c101a25e`, see [UPSTREAM.json](UPSTREAM.json)). Newer releases are followed with the procedure in [UPSTREAM-SYNC.md](docs/UPSTREAM-SYNC.md).
- **Method:** each Python module maps to a C# file with the same path, each Python member to a C# member with the same name. Each C# member carries a `// py: sqlglot/<module>.py::<Class>.<member> #<hash>` anchor, so tools can tell which members changed upstream ([AGENTS.md](AGENTS.md)). Upstream bugs are ported as they are, not fixed.
- **Verification:** a corpus recorded from SQLGlot's own test suite (tokenizer, parse, generate/transpile, helper cases) is replayed against the port. All 26,487 cases pass, with 0 fail and 0 not ported. Unit tests take their expected values from the Python oracle. Twelve cases are excluded as proven recorder artifacts, each with a reason (`src/SqlGlotSharp.Corpus/known-exclusions.json`).
- **Not ported:** the dialects marked above, part of the SQL builders (`insert`, `update`, `delete`, `merge`, `cast`, `func`, `case`, ...), the scope-based optimizer, `diff`, the executor, `repr`. They raise `NotPortedException`; there is never a silent fallback.
- **Target:** .NET 10 (`net10.0`), no runtime dependency beyond the .NET base library.
- **Status:** unofficial, not endorsed by the SQLGlot team. Port bugs belong to this repository.

## License

MIT, see [LICENSE](LICENSE).

- SQLGlot: Copyright (c) 2026 Toby Mao, under the MIT License. The port is a derivative work of SQLGlot's code and keeps its copyright notice.
- The C# port: Copyright (c) 2026 Marco Parenzan, under the same MIT License.

All design credit belongs to SQLGlot and its contributors. This project would not exist without their code and, above all, their test suite, which is what every number on this page is measured against.
