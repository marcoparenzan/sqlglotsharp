# Architecture

## Principle

The C# code mirrors the official Python code member by member: same file layout, class and method names, parameter names and statement order. It is deliberately not idiomatic C#, so that every change in the official project can be located and ported mechanically. The rules are in [AGENTS.md](../AGENTS.md); the map against the official project is [ALIGNMENT.md](ALIGNMENT.md).

## Repository layout

| Path | Content |
|---|---|
| `python/` | Official SQLGlot v30.27.0 (`5c101a25e`), read-only oracle, git-ignored |
| `src/SqlGlotSharp/` | The library; paths mirror `python/sqlglot/` |
| `src/SqlGlotSharp.Tests/` | xUnit tests with expected values from the Python oracle |
| `src/SqlGlotSharp.Extensions.Layer3/` (+ `.Tests`) | The `layer3` dialect, an extension outside SQLGlot built on the public extension points |
| `src/SqlGlotSharp.Corpus/`, `src/Scoreboard/` | Corpus replay and the progress metric |
| `src/SqlGlotSharp.CorpusTests/` | One xUnit test per corpus case (heavy) |
| `src/SqlGlotSharp.Demo/` | Command-line transpiler |
| `src/Benchmarks/` | Parse/transpile timings on the upstream benchmark queries (`src/tools/bench_compare.py` drives it) |
| `src/PortingTools/`, `src/tools/` | .NET bridge and the Python generators/checks; `src/tools/run-oracle.ps1` and `oracle-requirements.txt` set up the oracle environment |
| `src/SqlGlotSharp.slnx`, `Directory.Build.props` | Solution; .NET 10, nullable references, C# 14, build output under `artifacts/` |
| `docs/` | Documentation; `docs/status/` the versioned corpus and anchor results, `docs/history/` the porting plan, log and old roadmaps |
| `artifacts/` | Local only, git-ignored: build output, oracle venv, logs, scratch |
| `corpus.json` | Host corpus for the Layer 3 extension (optional; `HostCorpusTests` skips itself when absent) |
| Root `*.md` | Documentation, see the map in [README.md](../README.md) |

## Layers (same as the official project)

1. **Tokenizer** (`tokenizer_core.cs`, `tokens.cs`): SQL text → tokens. Dialects customise `KEYWORDS`, quotes and escapes through nested `Tokenizer` classes.
2. **Parser** (`parser.*.cs`, `parsers/`): tokens → AST of `exp.*` nodes. Recursive descent, `_parse_*` methods, token tables (`RANGE_PARSERS`, `FUNCTIONS`, `STATEMENT_PARSERS`, ...). Unparseable statements fall back to `exp.Command`.
3. **Expressions** (`expressions/`): the AST node classes, split into `*.shells.g.cs`, `*.meta.g.cs`, `*.constructors.g.cs` (generated) and hand-written partial classes. Every node has `args`, `key`, `arg_types` and the navigation/mutation API (`walk`, `find_all`, `copy`, `replace`, ...).
4. **Generator** (`generator.*.cs`, `generators/`): AST → SQL. Handlers are `<name>_sql` methods discovered by reflection plus `TRANSFORMS` tables; per-dialect overrides live in `generators/<dialect>.cs`.
5. **Dialects** (`dialects/`): a `Dialect` bundles tokenizer, parser and generator and the feature flags. `Dialect.CreateTokenizer/CreateParser/CreateGenerator` are virtual: that is the extension point for dialects defined outside the library (the Layer 3 extension uses it, registering itself with `Dialect.register`).
6. **Optimizer subset** (`optimizer/`): `TypeAnnotator` (expression level) and `Simplifier`.

Logging follows Python: each module has a static `logger = py.logging.getLogger("sqlglot")` (all the same object); `py/logging.cs` is a minimal `logging` equivalent (levels, handlers, stderr `lastResort`) with no external dependency.

## Generated versus hand-written code

`*.g.cs` files are produced by `src/tools/gen_csharp.py` from the official sources (enums, node shells, metadata, constructors, pure-data class attributes). They are never edited by hand and are listed in `src/tools/generated_manifest.json`. Class attributes that contain code (lambdas, builders) are ported by hand and read through a virtual instance property backed by a static field, as described in AGENTS.md section 4.3.

## Verification

- **Anchors:** every ported member carries `// py: path::Name #hash`; `port_status.py` reports coverage.
- **Corpus:** `D:\clones\corpus\v30.27.0`, recorded from the official test suite, replayed by `src/Scoreboard` (levels L0–L3). Unported members raise `NotPortedException`, counted separately from failures.
- **Unit tests:** expected values come from the Python oracle, never invented.

## Not ported

Other upstream dialects, the executor, planner, lineage, diff, anonymize, the scope-based optimizer rules, `schema`. See `ALIGNMENT.md` section 5.
