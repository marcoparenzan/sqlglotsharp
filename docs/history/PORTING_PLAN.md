# PORTING_PLAN.md — sqlglot → C# (SqlGlotSharp)

Audience: the coding agent doing the port (rules in [AGENTS.md](AGENTS.md)) and Marco, who
reviews the gates. Work strictly in step order. A step is done only when its **Done when**
list is true; tick it in the checklist in §9.

Upstream baseline: `tobymao/sqlglot` **v30.27.0-1-g5c101a25e** (commit `5c101a25e`,
2026-10-30), cloned in `python/`.

---

## 1. Goal and scope

> **Status note (2026-11-03):** this plan is historical. Everything below, including the deferral of T-SQL/Fabric, has been superseded: base, PostgreSQL, TimescaleDB, T-SQL, Fabric and the host `layer3` dialect are done (corpus 26,487/26,487). The current state and open work are in `ROADMAP.md`; the upstream-to-port mapping is in `ALIGNMENT.md`.

### Approved priority and resumption override

The user has changed the delivery priority to **PostgreSQL, then TimescaleDB**. Build the necessary base pipeline first; complete PostgreSQL and immediately follow with TimescaleDB. TSQL and Fabric are deferred: preserve their existing code/tests, but do not prioritize further implementation before PostgreSQL/TimescaleDB. Kusto is not part of the first delivery. This override supersedes the historical target/phase order below (in particular, Timescale precedes phase 9 TSQL/Fabric). The first executable delivery and its packaging/documentation should not wait for the deferred targets.

The user is not manually reviewing the implementation and wants feedback on the final executable result. Work autonomously in coherent batches with automatic verification; do not ask for technical approval on every increment. Parallel execution is being added to Ralf by the user but has not been verified or launched here. Use isolated workspaces and a coordinator only when the environment actually supports it.

**Read repository-root `ROADMAP.md` first on resumption:** it is the current verified state/handoff, including pending work, commands and limitations. Historical checkboxes do not imply that every Python member is ported; use the actual code, corpus and port-status report.

Port the parts of sqlglot needed to **parse, transpile and generate SQL** for:

| Target | Upstream status | How |
|---|---|---|
| base dialect (`""`) | exists | port (everything builds on it) |
| PostgreSQL (`postgres`) | exists | port |
| TimescaleDB (`timescale`) | **does not exist** | priority immediately after PostgreSQL; new dialect, subclass of Postgres (§7) |
| SQL Server (`tsql`) | exists | deferred; preserve existing code/tests |
| Fabric (`fabric`, subclass of tsql) | exists | deferred with TSQL; preserve existing code/tests |
| Kusto / KQL (`kusto`) | **does not exist** | not prioritized for first delivery; new dialect, generator first (§8) |

**In scope** (the import closure of the dialects above — computed exactly by `tools/closure.py`
in step 1.1; this is the expected result):

- `__init__.py`, `errors.py`, `helper.py`, `trie.py`, `time.py`, `jsonpath.py`, `serde.py`,
  `schema.py`, `_typing.py` (types only)
- `tokenizer_core.py`, `tokens.py`
- `expressions/*` (≈1070 classes, ≈8.5k lines)
- `parser.py` (9k lines, ≈430 methods), `generator.py` (5.4k lines, ≈500 methods)
- `dialects/dialect.py`, `dialects/__init__.py`, `transforms.py`
- `dialects|parsers|generators|typing/{postgres,tsql,fabric}.py`, `typing/__init__.py`, `parsers/base.py`
- optimizer pieces the generators/parsers call: `optimizer/scope.py`, `optimizer/annotate_types.py`,
  `optimizer/simplify.py`, `optimizer/qualify_columns.py` (at least `qualify_outputs`) and their closure

Roughly **38k lines of Python** → expect ~50k lines of C#.

**Out of scope for now** (can be added later with the same method): the other 28 dialects,
the full optimizer, `executor/`, `planner.py`, `diff.py`, `lineage.py`, `anonymize.py`,
`__main__.py`, `sqlglotc/` (mypyc build), `sqlglot-integration-tests` (private submodule).

---

## 2. Target repository layout (original proposal)

Approved session override: .NET 10; source projects and solution live under repository-root
`src/` (tests in `src/SqlGlotSharp.Tests/`), shared build configuration and Markdown documentation
live at repository root. The specifications (`AGENTS.md`, this plan) now live at the repository root too (the former `csharp/` folder was removed). Python remains at root
`python/`. Resolve all paths in subsequent tooling accordingly. See root `OPENPOINTS.md`.
The tree below preserves the original proposal for reference, not the active bootstrap paths.

```
csharp/
  AGENTS.md  PORTING_PLAN.md  README.md  USAGE.md  ARCHITECTURE.md  ROADMAP.md
  OPENPOINTS.md  PORTING_LOG.md  CHANGELOG.md  LICENSE  UPSTREAM.json
  Directory.Build.props  .editorconfig  SqlGlotSharp.slnx
  src/SqlGlotSharp/               # library, mirrors ../python/sqlglot/
    py/                           # Python runtime shims: py.truthy, PyDict, py.slice, py.re, ...
    sqlglot.cs  errors.cs  helper.cs  trie.cs  time.cs  jsonpath.cs  serde.cs  schema.cs
    tokenizer_core.cs  tokens.cs  parser.cs  generator.cs  transforms.cs
    expressions/  dialects/  parsers/  generators/  typing/  optimizer/
  tests/
    SqlGlotSharp.Tests/           # xUnit, data-driven from the corpus
    corpus/<upstream-tag>/*.jsonl # golden cases recorded from Python (generated, committed)
    custom/*.jsonl                # cases written by Marco (Timescale, Kusto, real queries)
    known_failures.txt
  tools/                          # Python 3.11 scripts, run against python/
    closure.py  extract_corpus.py  gen_csharp.py  port_status.py  sync.py
    scoreboard/                   # small .NET console app: corpus results → report
```

Names: package/assembly `SqlGlotSharp` (fits the *Sharp suite; confirm in OPENPOINTS),
root namespace `SqlGlotSharp`.

`LICENSE`: MIT, **keep upstream notice** `Copyright (c) 2026 Toby Mao` and add
`Copyright (c) 2026 Marco Parenzan` (the port is a derivative work; MIT requires the original notice).

---

## 3. Test strategy — the corpus is the yardstick

Python sqlglot is the **oracle**. We never hand-translate the Python tests. Instead we run the
whole Python test suite once with a recorder installed, and every call that crosses one of three
boundaries is saved as a test case:

| Level | Boundary recorded | Case content | What it checks in C# |
|---|---|---|---|
| **L0 tokens** | `Tokenizer.tokenize(sql)` for a dialect | sql, dialect → list of `(token_type, text, line, col, start, end, comments)` or error | tokenizer only |
| **L1 parse** | `parse` / `parse_one` | sql, read dialect, options → list of `serde.dump()` ASTs, or error class + message | tokenizer + parser (+ expressions) |
| **L2 generate** | `Expr.sql(...)` / `Generator.generate` | `serde.dump()` of the input AST, write dialect, options (`pretty`, `identify`, `unsupported_level`, …) → SQL string, or error, plus logged warnings | generator only (input AST is loaded with `serde.load`, so it does not depend on the C# parser) |
| **L3 end-to-end** | `transpile`, `validate_identity`, `validate_all` | sql, read, write, options → SQL | the whole pipeline |
| **L4 optimizer** | optional, later | fixture pairs from `tests/fixtures/optimizer/*.sql` | optimizer passes |

Why this design:
- Each level can be made green **independently**, in the order we port (tokenizer → parser → generator).
- A failing L3 case usually also fails at L1 or L2, which tells the agent *where* the bug is.
- It works for every test, whatever the shape of the Python test code (helper methods,
  plain `assertEqual`, fixtures files like `identity.sql`, `pretty.sql`).
- At every upstream update the corpus is simply re-recorded (§6).

### 3.1 Recorder rules (`tools/extract_corpus.py`)
- Runs `python -m unittest discover tests` in `python/` with `PYTHONPATH` pointing there and
  monkeypatches the boundaries. **Only top-level calls** are recorded (re-entrancy guard), so the
  generator's internal `self.sql(child)` calls are not separate cases.
- Records the **actual Python output** (not the literal in the test). It also stores the test id
  (`tests.dialects.test_tsql.TestTSQL.test_types`) for traceability.
- **Dialect filter**: keep a case only if every dialect involved is in
  `{"", "postgres", "tsql", "fabric"}` (plus `timescale`, `kusto` once they exist in Python, §7–8).
  Cases from *any* test file qualify — e.g. a `read={"tsql": ...}` inside `test_snowflake.py` with
  write dialect `""` counts.
- Positions (`start`, `end`, `col`) are converted to **UTF-16 offsets**, the unit C# strings use.
- Deduplicated by a hash of the case content; stable sorted; one JSONL file per level and dialect:
  `corpus/v30.27.0/L1.tsql.jsonl`.
- Writes `corpus/<tag>/MANIFEST.json`: tag, commit, counts per file, Python version, recorder version.

### 3.2 C# side
- `SqlGlotSharp.Tests` loads JSONL as xUnit `[Theory]` data. Traits: `Level`, `Dialect`,
  `Source` (test id). One case = one test, so `dotnet test --filter` can target anything.
- L1 compares `serde.dump(csharp_ast)` (as JSON, key order irrelevant) with the recorded dump.
- Error cases compare the error **class** and the message.
- Tests run on a thread with a 64 MB stack (deep ASTs; Python recursion is deeper than the .NET default).
- `tools/scoreboard` writes `D:\Dev\BuildArtifacts\sqlglot-csharp\reports\scoreboard.md`:
  pass/fail/not-ported per level × dialect, and the delta against the previous run.

### 3.3 Marco's own tests
`tests/custom/*.jsonl`, same schema as the corpus (fields `level`, `sql`, `read`, `write`,
`options`, `expected`, plus `note`). For Timescale and Kusto these are the main spec until the
Python side exists. Real-world queries from your projects belong here too.

### 3.4 Gates
A phase gate passes when, for its levels and dialects: **100 % of cases pass, except those in
`known_failures.txt`**, and every entry there has a reason and was accepted by Marco.
After a gate, the pass count may never go down (regression check in the scoreboard).

---

## 4. Phases and steps

Each step lists **Do** and **Done when**. Size hint: one agent session ≈ one step or 10–40 members.

### Phase 0 — Bootstrap

**0.1 Repository skeleton**
- Do: `git init` in `csharp/`; create the layout of §2; `Directory.Build.props` with
  `TargetFramework=net10.0`, `Nullable=enable`, `LangVersion=latest`,
  `BaseOutputPath`/`BaseIntermediateOutputPath` under `D:\Dev\BuildArtifacts\sqlglot-csharp\<project>\`;
  `.editorconfig` suppressing IDE1006, CA1707, CA1708, CA1716, CS8981; empty library and test projects;
  `UPSTREAM.json` = `{ "repo": "https://github.com/tobymao/sqlglot", "tag": "v30.27.0", "commit": "5c101a25e…", "ported_at": "…" }`;
  LICENSE per §2; README/USAGE/ARCHITECTURE/ROADMAP/OPENPOINTS stubs.
- Done when: `dotnet build` and `dotnet test` succeed (0 tests), nothing written under `csharp/bin` or `csharp/obj`.

### Phase 1 — Tooling (Python scripts in `tools/`)

**1.1 `closure.py`** — static import graph (including imports inside functions) from the seed
modules `sqlglot/dialects/{postgres,tsql,fabric}.py` + `sqlglot/__init__.py` + `sqlglot/serde.py`.
Output `tools/scope.json` (list of modules, and per module the list of top-level members).
- Done when: the list matches §1 or the differences are written in OPENPOINTS.

**1.2 `port_status.py`** — for every member in `scope.json` computes a hash of its normalized
Python source (AST dump without docstrings/positions, sha1, first 10 hex). Scans `src/**/*.cs`
for anchors. Reports **missing** (no anchor), **stale** (hash differs), **orphan** (anchor with no
Python member), **stub** (hash `0000000000`). Options: `--next N` (missing in Python file order,
following the file order of §4 phases), `--hash <anchor>`, `--file <py path>`, `--json`.
- Done when: running it on the empty project lists every in-scope member as missing.

**1.3 `gen_csharp.py`** — imports the Python package and **generates** (all into `*.g.cs`):
- `TokenType` enum (order and values from Python `IntEnum`), `exp.DType` and the other enums;
- for every `exp` class: the class shell (`partial`, correct base class + trait interfaces),
  `key`, `arg_types` (ordered), `required_args`, `is_var_len_args`, `var_len_arg_key`,
  `_sql_names`, the constructor with named optional parameters, the `exp.EXPR_CLASSES` registry;
- the trait marker interfaces;
- pure-data class attributes of tokenizers, parsers, generators, dialects (the **final, merged**
  value as Python sees it at runtime): `KEYWORDS`, `SINGLE_TOKENS`, `QUOTES`, `TYPE_MAPPING`,
  `TIME_MAPPING`, `INDEX_OFFSET`, `NULL_ORDERING`, sets of `TokenType`, etc.;
- the dialect registry (`Dialects` enum + name → type table).
  Attributes containing callables are **not** generated; they are listed in
  `tools/handwritten_attrs.txt` and ported by hand.
- Done when: generated code compiles; `port_status.py` treats generated members as ported.

**1.4 `extract_corpus.py`** — recorder of §3.1.
- Done when: `corpus/v30.27.0/` exists, MANIFEST counts are plausible (expected order of
  magnitude: thousands of cases per dialect at L1/L2/L3), the Python suite still passes while recording.

**1.5 Corpus test harness** — `SqlGlotSharp.Tests` loads the corpus; all tests fail with
`NotPortedException` or "not implemented" — that is the expected starting point. Scoreboard app works.
- Done when: `dotnet test` runs every case and the scoreboard shows 0 % everywhere.

### Phase 2 — Python runtime shims (`src/SqlGlotSharp/py/`)
`py.truthy`, `py.or`, `py.and`, `py.len`, `py.slice`, `py.repr`, `py.str`, `py.@int`, `py.@float`,
`py.enumerate`, `py.zip`, `py.sorted` (stable, Python ordering of mixed keys not needed),
`PyDict<K,V>` (on top of `OrderedDictionary<K,V>`), `PyKwargs`, `py.merge`, `py.re`
(translate Python regex syntax: `(?P<n>…)`→`(?<n>…)`, `(?P=n)`→`\k<n>`, `\Z`→`\z`; `match` anchored at
start; `sub` replacement `\1`/`\g<n>` → `$1`/`${n}`; `RegexOptions.CultureInvariant`),
`py.getattr`, `py.logging`, `py.require_trait`, `NotPortedException`.
- Each shim has unit tests written from the Python behaviour (e.g. `py.slice("abc", -5, 2) == "ab"`).
- Done when: shim unit tests green. Add shims later whenever the table in AGENTS.md §4 grows.

### Phase 3 — Errors, helper, trie, tokenizer
Order: `errors.py` → `helper.py` → `trie.py` → `tokenizer_core.py` → `tokens.py`.
- Done when: **L0 gate for base dialect** (tokens of `""`) — the dialect-specific tokenizers
  come with their dialects but most of them are data already generated in 1.3, so also run L0 for
  postgres/tsql and record the rate.

### Phase 4 — Expressions and serde
Order: `expressions/core.py` (Expr/Expression methods first: `args`, `set`, `append`, `parent`,
`arg_key`, `iter_expressions`, `walk`, `bfs`, `dfs`, `find`, `find_all`, `transform`, `replace`,
`pop`, `copy`, `__eq__`, `__hash__`, `meta`, `comments`, `type`, `name`, `alias`, …), then the other
`expressions/*.py` files in alphabetical order, `builders.py` last; then `serde.py`.
- Start with a **spike**: port 20 classes incl. one trait-heavy class (e.g. `Select`) and confirm
  the conventions of AGENTS.md §4.4 compile and dispatch correctly. Record the outcome in OPENPOINTS.
- Done when: `serde.load(dump)` → `serde.dump` round-trips every AST in the L1 corpus (this test is
  generated from the corpus: input = recorded dump, expected = same dump); `__eq__`/`__hash__`
  unit tests written from `tests/test_expressions.py` pass.

### Phase 5 — Dialect core
`dialects/__init__.py`, `dialects/dialect.py` **class part** (settings, `get_or_raise`, registry,
`tokenize`, `parse`, `generate`, normalization, `quote_identifier`), `time.py`, `jsonpath.py`,
`typing/__init__.py`. The `_Dialect` metaclass logic becomes the static registry generated in 1.3
plus a public `Dialect.register(name, type)` for plugins. The module-level builder functions of
`dialect.py` (`rename_func`, `build_formatted_time`, …) are ported here too, but the ones that need
the generator may stay stubs until Phase 7.
- Done when: `Dialect.get_or_raise("tsql")` returns a TSQL instance with the correct generated settings.

### Phase 6 — Parser (base)
`parser.py` top-down, class attributes with lambdas first (`FUNCTIONS`, `STATEMENT_PARSERS`,
`EXPRESSION_PARSERS`, `RANGE_PARSERS`, `COLUMN_OPERATORS`, …), then methods in file order. Use
`port_status.py --next 15 --file sqlglot/parser.py`. `sqlglot.cs` gets `parse`, `parse_one`, `tokenize`.
- Done when: **L1 gate, dialect `""`**.

### Phase 7 — Generator (base) + support modules
`generator.py` (TRANSFORMS with lambdas, then methods in order), the `_build_dispatch` logic
(reflection over `*_sql` methods + `TRANSFORMS`, cached per generator type), `transforms.py`,
then the optimizer pieces in this order: `optimizer/scope.py`, `annotate_types.py`, `simplify.py`,
`qualify_columns.py` (only the members in `scope.json`). `sqlglot.cs` gets `transpile`.
- Done when: **L2 and L3 gates, dialect `""`** (includes `identity.sql` and `pretty.sql` fixtures).

### Phase 8 — PostgreSQL
`typing/postgres.py`, `parsers/postgres.py`, `generators/postgres.py`, `dialects/postgres.py`.
- Done when: **L0–L3 gates for `postgres`** (including cases `postgres ↔ ""`).

### Phase 9 — SQL Server (+ Fabric)
`typing/tsql.py`, `parsers/tsql.py`, `generators/tsql.py`, `dialects/tsql.py`, then fabric files.
- Done when: **L0–L3 gates for `tsql`, `fabric`**, and all cross cases `tsql ↔ postgres`.

### Phase 10 — TimescaleDB (§7)
### Phase 11 — Kusto (§8)

### Phase 12 — Packaging and docs
`dotnet pack` → `D:\Dev\NuGetLocalFeed`, version `30.27.0-net10-preview.1`. README, USAGE
(installation first), ARCHITECTURE (the mirror conventions), ROADMAP, OPENPOINTS up to date.
Optional: a thin idiomatic facade (`SqlGlot.Transpile(sql, from, to)`) in a **separate** file
`Facade.cs` — the only place where .NET naming is allowed.
- Done when: a sample console app consuming the package from the local feed transpiles
  `SELECT TOP 10 a FROM t` (tsql) → `SELECT a FROM t LIMIT 10` (postgres).

---

## 5. Per-session loop for the agent

1. Read AGENTS.md, this plan's checklist (§9), the tail of PORTING_LOG.md.
2. `python tools/port_status.py --next 15` restricted to the current phase files.
3. Port, anchor, build, run the step's tests (AGENTS.md §6).
4. Run the scoreboard; no regression allowed.
5. Commit, update PORTING_LOG.md, tick the checklist when a step's **Done when** is met.

---

## 6. Upstream update strategy (Python repo updated → C# updated)

The update is driven by **hashes in the anchors** and by the **re-recorded corpus**, not by
reading git diffs by hand.

### 6.1 Cadence
sqlglot releases very often. Sync on **every minor release** (or every 2 weeks, whichever comes
first). Never jump many releases at once: the task list grows non-linearly. Only sync to **release
tags**, never to `main`.

### 6.2 Procedure (`tools/sync.py <new-tag>` automates 1–6)
1. `git -C python fetch --tags` and check out `<new-tag>` (old tag known from `UPSTREAM.json`).
2. Create branch `sync/<new-tag>` in the repository.
3. `closure.py` → diff of `scope.json`: modules entering/leaving scope.
4. `gen_csharp.py` → regenerate `*.g.cs`. Build. Compile errors here mean a generated type/attribute
   changed shape — they are the first tasks.
5. `extract_corpus.py` → `corpus/<new-tag>/`; print case-count delta per file. Keep the old corpus
   folder until the sync is merged, then move it to `D:\Dev\BuildArtifacts\trash`.
6. `port_status.py --json` → `sync/<new-tag>/TASKS.md`, one task per member:
   - **stale**: shows the unified diff of the Python member old tag → new tag
     (`git show <old>:<path>` vs new), and the C# anchor location;
   - **missing**: new Python member, with the member that precedes it (to place it in the same order);
   - **orphan**: removed upstream → delete the C# member (after checking `rename candidates`: the
     tool pairs orphans with missing members whose body is ≥ 80 % similar).
7. The agent works TASKS.md top to bottom with the normal loop (§5), updating each anchor hash.
8. Done when: build green; `port_status.py` reports 0 stale / 0 missing / 0 orphan in scope;
   scoreboard on the new corpus ≥ gate rules (§3.4), no regression vs the old corpus for
   cases that still exist.
9. Update `UPSTREAM.json`, `CHANGELOG.md` (link the upstream changelog section), bump the version
   to `<new x.y.z>-net10-preview.1`, pack to the local feed, merge the branch.

### 6.3 Why this works
- Line-by-line mirroring (AGENTS.md §1) makes each stale task a small local edit.
- Generated code absorbs the bulk of upstream churn (new functions, new expression classes,
  new keywords/type mappings are mostly data).
- The corpus captures behaviour changes even where no in-scope member hash changed
  (e.g. a change in a data attribute of a base class).

---

## 7. TimescaleDB

TimescaleDB is PostgreSQL plus extension functions and DDL options, so the Postgres dialect already
round-trips most of it (unknown functions become `exp.Anonymous`). What a dedicated dialect adds is
**transpilation** of the time-series functions.

Recommended: write the dialect **in Python first**, as a sqlglot plugin package (sqlglot loads
dialects from the `sqlglot.dialects` entry-point group), in a sibling folder
`../python-ext/` (own git repo), with Python tests in the same `Validator` style. Then it gets ported
exactly like the upstream dialects and the recorder picks up its tests. This keeps one source of
truth and one update process.

Content (start list, refine with Marco's tests):
- `class Timescale(Postgres)`, key `timescale`.
- `time_bucket(bucket_width, ts [, offset | origin, timezone])` ↔ a canonical node. Candidate:
  `exp.DateBin` (already used for Postgres `date_bin`) — **research step**: check its `arg_types`
  and whether origin/offset semantics match; otherwise add `TimeBucket` in the plugin.
  Mappings: postgres `date_bin`, tsql `DATE_BUCKET(datepart, number, date [, origin])`
  (not parsed by upstream tsql today), kusto `bin(ts, 1h)`.
- `time_bucket_gapfill`, `locf`, `interpolate`: identity only (no equivalent elsewhere → unsupported warning).
- `first(value, time)` / `last(value, time)`: Timescale semantics differ from generic `FIRST` — parse to
  a dedicated node or keep `Anonymous`; decide with tests.
- DDL: `SELECT create_hypertable(...)`, `CREATE TABLE … WITH (tsdb.hypertable, tsdb.partition_column = 'time')`,
  `CREATE MATERIALIZED VIEW … WITH (timescaledb.continuous) AS …`, `add_continuous_aggregate_policy(...)`,
  `add_retention_policy(...)`, compression options — identity tests first.
- Done when: Marco's `custom/timescale*.jsonl` and the plugin's Python tests (via corpus) pass in C#.

---

## 8. Kusto (KQL)

KQL is a pipe language, not SQL: `T | where x > 1 | summarize count() by k | order by k asc | take 10`.
Treat it in two sub-phases:

**K1 — generator only (SQL AST → KQL).** Override `select_sql` wholesale to emit the pipeline in this
order: `FROM` (table, or `let` for CTEs) → `| where` → `| summarize aggs by keys` (GROUP BY) →
`| where` (HAVING) → `| extend`/`| project` → `| distinct` → `| order by … asc|desc` (always explicit:
KQL defaults to **desc**) → `| take n` (or `| top n by …` when ORDER BY + LIMIT).
Operators: `=`→`==`, `<>`→`!=`, `AND/OR/NOT`→`and/or/not()`, `IS NULL`→`isnull()`, `LIKE 'a%'`→`startswith`/
`matches regex` (decide), `IN`→`in`, `BETWEEN`→`between (a .. b)`. Joins:
`| join kind=inner (T2) on $left.a == $right.b` (only equi-joins; others unsupported). `UNION ALL`→`union`.
Functions: `COUNT(*)`→`count()`, `COUNT(DISTINCT x)`→`dcount(x)` (**approximate** — warn) or
`count_distinct(x)`, `DATE_TRUNC`/`time_bucket`→`bin()`/`startofday()`…, string and math functions via a mapping table.
Literals: `datetime(2026-01-01)`, `1h` timespans, identifiers `['name with space']`.
Everything else raises `UnsupportedError` (respecting `unsupported_level`).

**K2 — tokenizer + parser (KQL → AST)**, only if needed. sqlglot already turns BigQuery pipe syntax
(`|>`) into `Select` trees (`tests/dialects/test_pipe_syntax.py`); reuse that approach for KQL operators.

As for Timescale, write it in Python in `../python-ext/` first (recommended) or directly in C# (faster
start, but then this dialect has no oracle and no automatic sync — its spec is only `custom/kusto*.jsonl`).

Done when: Marco's `custom/kusto*.jsonl` pass.

---

## 9. Checklist

- [ ] 0.1 skeleton — library/solution and zero-test build gate passed; Git initialization pending (see root PORTING_LOG.md and OPENPOINTS.md)
- [x] 1.1 closure (scope differences recorded in root OPENPOINTS.md) · [x] 1.2 port_status · [x] 1.3 gen_csharp (scoped data and expression attribute inventory complete; generated code builds and anchors verified) · [x] 1.4 extract_corpus (full suite: 1558 tests passed; 26498 cases across 16 files; DuckDB 2.0.0) · [x] 1.5 harness + scoreboard (26498 per-case tests; Level/Dialect/Source traits; full TRX scoreboard: 0% baseline)
- [x] 2 runtime shims — initial tested foundation; extend shims when required by subsequent ported members (limits in root ROADMAP.md)
- [x] 3 tokenizer — L0 gate passed for base/postgres/tsql/fabric: 7323 xUnit cases, 0 failed or skipped (recorder v4); residual metadata adaptation tracked in ROADMAP.md
- [x] 4 expressions + serde — L1 serialization gate passed (7855 records, all recorded ASTs round-trip); 44 captured upstream equality comparisons pass. Residual methods/explicit stubs remain tracked.
- [ ] 5 dialect core
- [ ] 6 parser — L1 gate `""`
- [ ] 7 generator + support — L2/L3 gates `""`
- [ ] 8 postgres — L0–L3 gates
- [ ] 9 tsql + fabric — L0–L3 gates
- [ ] 10 timescale
- [ ] 11 kusto K1 (· K2 optional)
- [ ] 12 packaging + docs

---

## 10. Decisions for Marco (copy to OPENPOINTS.md in step 0.1)

1. Package/namespace name `SqlGlotSharp`? (NuGet id collisions to check before any publish.)
2. Fabric in scope? (recommended yes: small, Microsoft world.)
3. Timescale and Kusto: Python plugin first in `../python-ext/` (recommended) or C# only?
4. Generic fixes to upstream `postgres`/`tsql` (e.g. parsing `DATE_BUCKET`): contribute upstream as PRs
   (keeps the mirror clean — recommended) or carry them in the plugin as subclasses?
5. Full optimizer (`qualify`, `optimize`) needed later? It changes the scope and adds L4.
6. Sync cadence: every minor release or every 2 weeks?
