# Usage

## Installation

Requirements: .NET 10 SDK (a newer SDK such as 11 builds it too; the project targets `net10.0`) and, only for the tests and the tools, the oracle Python 3.11. From the repository root, in PowerShell:

```powershell
dotnet build src/SqlGlotSharp.slnx
$env:SQLGLOT_PYTHON = (Resolve-Path artifacts\oracle-venv\Scripts\python.exe).Path
dotnet test src/SqlGlotSharp.Tests
```

`SQLGLOT_PYTHON` matters: the port-status tests hash Python sources, and any other Python version gives wrong results. The official SQLGlot sources must be present in `python/` (tag `v30.27.0`, see `UPSTREAM.json`; the folder is git-ignored). The oracle environment is created by `src/tools/run-oracle.ps1 -CheckOnly` (packages in `src/tools/oracle-requirements.txt`). Build output, the oracle virtual environment, logs and scratch files go to `artifacts/` inside the repository (git-ignored); the versioned results are in `docs/status/`; `-p:ArtifactsRoot=<path>` redirects the build output elsewhere. Nothing is installed globally and nothing is published.

## Use the library from your own project

Nothing is published on nuget.org. Either pack it into a local feed or reference the project. `dotnet pack -c Release` on `src/SqlGlotSharp` and `src/SqlGlotSharp.Extensions.Layer3` writes `SqlGlotSharp.<version>.nupkg` and `SqlGlotSharp.Extensions.Layer3.<version>.nupkg` to `D:\Dev\NuGetLocalFeed` (`PackageOutputPath` in the `.csproj`; pass `-o <folder>` for another feed). Then:

```powershell
dotnet add MyApp package SqlGlotSharp --version 30.27.0-net10-preview.2
dotnet add MyApp package SqlGlotSharp.Extensions.Layer3 --version 30.27.0-net10-preview.2   # optional
```

A change in content needs a new version (`-net10-preview.<n+1>`), otherwise the NuGet cache keeps the old copy.

Or reference the project:

```powershell
dotnet new console -n MyApp -f net10.0
dotnet add MyApp reference D:\clones\sqlglot\src\SqlGlotSharp\SqlGlotSharp.csproj
```

```csharp
using SqlGlotSharp;

// Transpile: read in one dialect, write in another. Dialects: "" (base), postgres, tsql, fabric, timescale
var tsql = SqlGlot.transpile("SELECT a FROM t LIMIT 5 OFFSET 2", read: "postgres", write: "tsql");
// SELECT a FROM t ORDER BY (SELECT NULL) OFFSET 2 ROWS FETCH FIRST 5 ROWS ONLY

var pg = SqlGlot.transpile("SELECT TOP 5 ISNULL(a, 0) FROM [dbo].[t]", read: "tsql", write: "postgres");
// SELECT COALESCE(a, 0) FROM "dbo"."t" LIMIT 5

// Parse into an AST and walk it
var ast = SqlGlot.parse_one("SELECT x FROM t WHERE y > 1", read: "postgres");
foreach (var column in ast.find_all<exp.Column>()) Console.WriteLine(column.name);

// Generate from an AST in any dialect
var sql = ast.sql("tsql");                       // same as Dialect.get_or_raise("tsql").generator().generate(ast)
var col = exp.column("a", table: "t");           // t.a
```

Method names follow Python (`parse_one`, `find_all`, `exp.column`), not PascalCase. Errors: `errors.ParseError` / `errors.TokenError` for invalid SQL, `NotPortedException` for grammar or transforms that are not part of the port (there is never a silent fallback).

The base dialect (`""`) is SQLGlot's own superset of common SQL, not strict ANSI. A request to read or write any other official dialect (MySQL, BigQuery, Snowflake, ...) raises `NotPortedException`.

TimescaleDB: `read: "timescale"` parses `time_bucket(...)`; writing PostgreSQL rewrites only the provably equivalent fixed-width form to `DATE_BIN`, every other overload raises `NotPortedException`.

## Logging

As in Python, the parser, generator, dialects and optimizer log to one named logger, `"sqlglot"`: unsupported syntax falling back to `Command`, `ErrorLevel.WARN` parse errors and unsupported generator features. `py.logging` is a minimal equivalent of Python's `logging` module, with no dependency outside .NET. With no handler attached, WARNING and above go to stderr (Python's `lastResort`). To route them elsewhere, or to silence them:

```csharp
var logger = py.logging.getLogger("sqlglot");           // same object as parser.logger, generator.logger, ...
logger.addHandler(new py.logging.CallbackHandler(record =>
    myLog.Warning("{Module}: {Message}", record.module, record.getMessage())));   // e.g. bridge to Microsoft.Extensions.Logging or Serilog
logger.setLevel(py.logging.ERROR);                      // or logger.disabled = true
```

A handler is a subclass of `py.logging.Handler` (override `emit`) or a `CallbackHandler`. The logger is process-wide, like in Python: a handler that must separate concurrent callers has to do it itself (the corpus replay does it with `AsyncLocal`, see `src/SqlGlotSharp.Corpus/LogCapture.cs`).

## Use an extension (Layer 3)

Dialects outside SQLGlot are separate projects. Reference both the library and the extension, and register the dialect once at startup:

```powershell
dotnet add MyApp reference D:\clones\sqlglot\src\SqlGlotSharp\SqlGlotSharp.csproj
dotnet add MyApp reference D:\clones\sqlglot\src\SqlGlotSharp.Extensions.Layer3\SqlGlotSharp.Extensions.Layer3.csproj
```

```csharp
using SqlGlotSharp;
using SqlGlotSharp.Extensions.Layer3;

Layer3.Register();                                         // registers the name "layer3"
var ast = SqlGlot.parse_one("'a' MEMBER OF t.tags AND b = 1", read: "layer3");
var sql = ast.sql("layer3");                               // JSON_ARRAY_CONTAINS('a', t.tags) AND b = 1
```

## Command line

```powershell
dotnet run --project src/SqlGlotSharp.Demo -- --read postgres --write tsql "SELECT a FROM t LIMIT 5 OFFSET 2"
dotnet run --project src/SqlGlotSharp.Demo -- --pretty --read postgres "SELECT a, COUNT(*) FROM m GROUP BY a"
```

`--read` / `--write` accept `base`, `postgres`, `timescale`, `tsql`, `fabric`. Without arguments a built-in example runs. Unsupported grammar exits with code 2, SQL errors with code 1. No database connection is made.

## Define your own dialect (outside the library)

A dialect is a subclass of `Dialect` with three factories, the equivalents of Python's `Tokenizer`, `Parser` and `Generator` class attributes. Register it by name and use it like a built-in one:

```csharp
class MyDialect : Dialect
{
    public override Parser CreateParser(ErrorLevel? level = null) => new MyParser(level, this);   // e.g. extra RANGE_PARSERS
    public override Generator CreateGenerator(bool pretty = false) => new Generator(dialect: this, pretty: pretty);
    public override Tokenizer CreateTokenizer() => new MyTokenizer(this);                          // extra KEYWORDS (+ trie)
}
Dialect.register("mine", typeof(MyDialect));         // Dialect.unregister("mine") removes it
var sql = SqlGlot.transpile("x MEMBER OF t.c", read: "mine", write: "mine");
```

The Layer 3 extension (`src/SqlGlotSharp.Extensions.Layer3/Layer3.cs`: tokenizer, parser, generator and dialect in one file) is a complete example built this way.

## Measure the port

```powershell
$env:SQLGLOT_PYTHON = (Resolve-Path artifacts\oracle-venv\Scripts\python.exe).Path
dotnet run --project src/Scoreboard -- D:\clones\corpus\v30.27.0 artifacts\reports\score-quick
```

A full run takes about 7 minutes; leave the output argument out and the results go to `docs/status/`, replacing the versioned snapshot (use another folder, as above, for experiments). Environment variables: `SCOREBOARD_THREADS=8`, `SCOREBOARD_MAXSQL=20000` (quick run: skips three stress inputs), `SCOREBOARD_ONLY=L1.postgres,L2.postgres`. The report folder gets `scoreboard.md`, `scoreboard.json`, `failures.json` and `notported.json` (the work list). After a full run into `docs/status/`, regenerate `ALIGNMENT.md` (see its last section).

### The comparison corpus

The corpus lives outside the repository at `D:\clones\corpus` (version directory `v30.27.0`), shared read-only by the tests and the scoreboard; never copy it into the repository and never edit it. It was recorded from the official Python test suite: L0 tokenizer, L1 parse, L2 generate/transpile, L3 helper cases (`validate_identity`, `validate_all`). Re-record it into a fresh directory outside the repository:

```powershell
dotnet run --project src/PortingTools -- -B src/tools/extract_corpus.py --output $env:TEMP\sqlglot-corpus-new
```

(`--pattern test_transpile.py` limits the run to one upstream test file.) The Layer 3 extension has its own host corpus, `corpus.json` in the repository root when present (recorded with sqlglot 30.23.0 by the host project; `HostCorpusTests` in `src/SqlGlotSharp.Extensions.Layer3.Tests` skips itself otherwise).

## Development tooling

All tools are Python 3.11 scripts in `src/tools/`; run them from the repository root with the oracle interpreter (`python -B` avoids bytecode files). `dotnet run --project src/PortingTools -- -B <script>` runs the same scripts through the .NET bridge.

| Tool | Purpose |
|---|---|
| `closure.py` | Writes the static import graph of the official package to `scope.json` (the modules in scope). `test_closure.py` tests it. |
| `port_status.py` | Compares official members with the `// py:` anchors in the C# sources. `--next 10`, `--hash <anchor>`, `--file <module> --json`, `--json` for the whole report. |
| `gen_csharp.py` | Generates the `*.g.cs` files (enums, expression shells/metadata/constructors, dialect/parser/generator data tables). Never edit generated files; rerun the generator. `generated_manifest.json` records the files it owns. |
| `gen_typing_metadata.py`, `gen_*.py` | Generate the typing metadata and the data-driven test cases. |
| `gen_layer3_expected.py` | Regenerates `src/SqlGlotSharp.Extensions.Layer3.Tests/Layer3/expected.json` from the Python oracle (plugin `oracle_plugins/layer3.py`). |
| `gen_alignment.py` | Regenerates `ALIGNMENT.md`. |
| `bench_compare.py` | Times SQLGlot (Python) and SqlGlotSharp on the queries of `python/benchmarks/parse.py`, same method; `--mode parse|transpile`. The C# side is `src/Benchmarks`. |
| `upstream_diff.py` | Lists the in-scope Python members changed, added or removed between two upstream refs, with their C# status: the work list of a sync ([UPSTREAM-SYNC.md](UPSTREAM-SYNC.md)). |
| `extract_corpus.py` | The oracle recorder (see the corpus section). |

Anchor states in `port_status.py`: missing (no anchor), ported (matching hash), stale (the official code changed), stub (zero hash: implemented, completeness not proven), orphan (unknown official member), duplicate (several C# anchors for one member). Hashes are the first ten hex digits of SHA1 over the AST dump without positions or docstrings, so compare only with the same Python version. This is coverage, not proof of correctness: the corpus is the functional measure.
