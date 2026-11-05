# ROADMAP — SqlGlotSharp

One page. Read this first on every resume. For the map against the official project see [ALIGNMENT.md](ALIGNMENT.md); for how to run things see [USAGE.md](USAGE.md).

## Status (2026-11-05)

- **Corpus:** 26,487 of 26,487 cases pass, 0 fail, 0 NotPorted (full run 2026-11-05, levels L0–L3, all corpus dialects).
- **Unit tests:** 946 of 946 (938 library, including 49 builder tests, + 8 Layer 3 extension).
- **Build:** 0 errors. Target `net10.0` (agreed exception to the global .NET 11 rule). No runtime dependency outside .NET. No published packages.
- **Baseline:** official SQLGlot `v30.27.0` (`5c101a25e`), clone in `python/` (read-only, git-ignored).
- **Dialects:** base, PostgreSQL, T-SQL, Fabric, plus the host dialect TimescaleDB; Layer 3 is a separate extension project (`SqlGlotSharp.Extensions.Layer3`). Everything else raises `NotPortedException`. Kusto is out of scope.

## Goal (fixed)

A complete C# port of SQLGlot for **base + PostgreSQL + T-SQL (SQL Server) + Fabric**, then **TimescaleDB** and the host **Layer 3** dialect. Done means: every corpus case passes (or is a documented exclusion), the unit tests are green, and README/USAGE show runnable examples. All of it is met.

## The progress metric

```powershell
$env:SQLGLOT_PYTHON = (Resolve-Path artifacts\oracle-venv\Scripts\python.exe).Path
dotnet run --project src/Scoreboard -- D:\clones\corpus\v30.27.0     # writes to docs/status
```

About 7 minutes. It writes `scoreboard.md`, `failures.json` and `notported.json` (NotPorted causes grouped by Python member: the work list; counts are lower bounds because a case stops at its first missing member). NotPorted is never counted as done. Twelve proven recorder artifacts are excluded with a reason each (`src/SqlGlotSharp.Corpus/known-exclusions.json`).

Milestones (the full table is in `docs/history/ROADMAP-progress-history.md`):

| Date | Milestone | Pass | Fail | NotPorted |
|---|---|---:|---:|---:|
| 2026-11-02 | Baseline (base + PostgreSQL) | 10,159 | 6 | 5,688 |
| 2026-11-03 | B1–B8 done, base + PostgreSQL (full run) | 15,701 | 0 | 142 |
| 2026-11-03 | B11 T-SQL (quick run, all corpus dialects) | 26,255 | 0 | 215 |
| 2026-11-03 | B12 Fabric (quick run) | 26,414 | 0 | 56 |
| 2026-11-03 | B13 optimizer subset (**full run**) | **26,487** | **0** | **0** |
| 2026-11-05 | Logging aligned with Python, Layer 3 moved to an extension (full run) | 26,487 | 0 | 0 |
| 2026-11-05 | SQL builders ported, repository reorganized (full run) | 26,487 | 0 | 0 |

Latest result files are versioned in `docs/status/`.

## How to run (read before the first command)

- **Tests need the oracle Python 3.11.7**, not the system 3.12: anchor hashes (`ast.dump`) differ between versions. Always set `SQLGLOT_PYTHON` as above.
- **Run from PowerShell.** Build output defaults to `artifacts/` inside the repository; if you pass `-p:ArtifactsRoot=<path>` use a real Windows path (bash eats the backslashes and creates stray folders).
- Quick loop (about 25 s instead of 7 min): `SCOREBOARD_THREADS=8`, `SCOREBOARD_MAXSQL=20000`, optionally `SCOREBOARD_ONLY=L1.postgres,L2.postgres`. Checkpoints run the full corpus once, without `MAXSQL`.
- `dotnet test src/SqlGlotSharp.slnx` takes 2–5 minutes.

## Blocks

| # | Block | State | Main content |
|---|---|---|---|
| B0 | Harness and hygiene | done | `parse_into`, L3 helper cases, recorder fixes |
| B1 | Expressions and operators | done | `RANGE_PARSERS`, struct/kv args, lambdas, JSON ops, `_parse_operator`, `_parse_comprehension` |
| B2 | Types | done | `_parse_types` in full, `DataType.from_str`, type literals/converters |
| B3 | FROM and query clauses | done | PIVOT/UNPIVOT, TABLESAMPLE, QUALIFY, locks, SORT/CLUSTER BY, CONNECT BY, versions, hints, pipe syntax |
| B4 | Special-grammar functions | done | CAST/TRY_CAST/CONVERT/DECODE/XMLELEMENT/XMLTABLE/MATCH/GAP_FILL/WEIGHT_STRING, OPENJSON, UDTF builders |
| B5 | PostgreSQL-specific | done | `FUNCTIONS` builders, `DATE_PART`, `JSON_AGG`, `VARIADIC`, `&&`/`@@`/`?`, ~60 generator `TRANSFORMS`, `unnest_sql` |
| B6 | DDL | done | full CREATE/DROP/ALTER, column constraints, index params, computed/identity columns, property parsers |
| B7 | Other statements | done | GRANT/REVOKE, COMMENT, transactions, USE, MERGE, SET, COPY, ANALYZE, CACHE, LOAD DATA, DESCRIBE, KILL, INSERT |
| B8 | Long tail | done | ~120 small `*_sql` handlers and parsers |
| B9 | TimescaleDB | done | identity, DDL, replay and the proven fixed-width `time_bucket` → `DATE_BIN` rewrite; other overloads raise NotPorted by design (no upstream behaviour to follow) |
| B10 | Delivery | done | README, USAGE (installation first), ARCHITECTURE, ALIGNMENT, OPENPOINTS; demo accepts tsql/fabric |
| B11 | T-SQL (SQL Server) | done (3,166 / 3,166) | `TSQLParser`, `TSQLGenerator`, DECLARE/WHILE/EXEC, OPENJSON, system versioning, NULL-ordering simulation, `ensure_bools`, CTE hoisting |
| B12 | Fabric | done (157 / 157) | `FabricParser`, `FabricGenerator` (type mapping, temporal precision cap, AT TIME ZONE) |
| B13 | Optimizer subset | done (56 / 56) | `TypeAnnotator` (expression level), `Simplifier`; date/interval arithmetic and constant propagation raise NotPorted |
| B14 | Layer 3 and extension points | done (Layer 3 moved to `SqlGlotSharp.Extensions.Layer3` on 2026-11-05) | `layer3` dialect (base + `MEMBER OF` → `JSONArrayContains`), virtual `CreateParser/CreateGenerator/CreateTokenizer`, `exp.column`, `Expr.sql(dialect)`; host `corpus.json` (sqlglot 30.23.0) passes |
| B15 | SQL builders | done (66 members, 47 oracle cases) | `maybe_parse`, `_apply_*_builder`, `condition`/`and_`/`or_`/`xor`/`not_`/`paren`/`alias_`/`convert`; `Expression.and_/or_/not_/as_/isin/between/eq/neq/is_/like/ilike/rlike/div/asc/desc`; `Query.select/limit/offset/order_by/where/with_/union/intersect/except_/ctes`; `Select.from_/group_by/sort_by/cluster_by/select/lateral/join/having/window/qualify/distinct/lock/hint`; `builders.select/from_`. Not yet: `insert`, `update`, `delete`, `merge`, `cast`, `func`, `case`, `ctas` |

## Open items

Publication (in progress): clean-up done 2026-11-05 (logging aligned, Layer 3 as an extension, stray folders quarantined in `artifacts/trash/`, documentation moved to `docs/`, `.ralfai` deleted, `ops/` merged into `src/tools/`). Repository: https://github.com/marcoparenzan/sqlglotsharp. NuGet packages `SqlGlotSharp` and `SqlGlotSharp.Extensions.Layer3` `30.27.0-net10-preview.1` are in the local feed `D:\Dev\NuGetLocalFeed` (2026-11-05; not on nuget.org). First local commit done 2026-11-05. Remaining: push to GitHub, a note to the SQLGlot author.

Nothing is open on the corpus. Known limits (details in `OPENPOINTS.md`):

- The scope-based optimizer (`traverse_scope`, `qualify_columns`, schema) is not ported: annotating whole queries, date/interval simplification (needs `relativedelta`) and constant propagation (needs `normalize`) raise `NotPortedException`.
- Other upstream dialects are out of scope.
- Realignment with newer official releases (v30.28.0 and v30.29.0 are in the local clone; 18 Python members changed, 2 added): procedure and log in `UPSTREAM-SYNC.md`.
- Performance, low priority: three stress inputs make the parser quadratic (`UNION ALL` chains of 95 KB and 260 KB, a 2 MB `x=x=…` chain). They pass but dominate a full run; this is why quick runs cap the SQL size.

## Rules while working

- Port the Python algorithm faithfully (`python/` is read-only) and keep the `// py:` anchors (`AGENTS.md`).
- Work from `notported.json`, highest count first. No hand-picked helpers.
- After each block: build, unit tests, Scoreboard, update this page. A new **Fail** is fixed before moving on.
- Never patch golden corpus files to make C# pass.
- Build output, the oracle venv, logs and scratch live in `artifacts/` (git-ignored); nothing there is versioned; the result snapshot lives in `docs/status/`. Recorded corpus copies never go inside the repository (a test asserts it).
