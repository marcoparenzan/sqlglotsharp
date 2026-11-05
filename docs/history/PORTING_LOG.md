# Porting log

## 2026-12-19 — Step 0.1 bootstrap

- Updated requirements from .NET 11 to .NET 10 following user approval.
- Created root src solution, empty library and xUnit project, external build paths, naming suppression, upstream metadata, MIT license and initial documentation.
- Python members ported: 0 (bootstrap only; no anchors required yet).
- Build: PASS, exit 0, zero errors and warnings. Artifacts confirmed under D:\Dev\BuildArtifacts\sqlglot-csharp.
- Test command: PASS, exit 0; zero tests discovered, intentionally matching the bootstrap gate. L0–L4: not run; corpus does not exist yet.
- Environment: build uses installed .NET 11 preview SDK targeting net10.0; tests execute on .NET 10.0.14. Only preview 10/11 SDKs are installed; stable .NET 10 SDK is recommended, not installed by this session.
- Failed initial solution-add command due to scaffold tool creating nested directories; corrected project locations and reran successfully.
- Skipped: nested git initialization (no Git/shell tool available), full upstream commit verification, package publishing and tool installation.
- Open points: root layout override, Git initialization, baseline verification and original plan decisions recorded in OPENPOINTS.md.
- Step 0.1 checklist remains formally open pending the Git initialization requirement; build/test bootstrap is complete. Next implementation step after accepting/resolving that operational deviation: 1.1 closure.py.

## 2026-12-19 — Step 1.1 static import closure

- User instructed continuation; Git initialization remains a non-blocking operational deviation.
- Implemented src/tools/closure.py using only Python AST/standard library, plus seven unit tests. Added development-only .NET Python launcher and xUnit integration test.
- Generated scope.json against actual upstream: 54 modules, 1720 top-level definitions/assignments. Records static imports inside functions and TYPE_CHECKING, relative imports, exp alias, dynamic import sites, source paths and member order.
- Differences versus planned scope: diff re-export and extra optimizer dependencies; conservative syntactic graph versus runtime package initialization documented in OPENPOINTS.md.
- Python members ported to library: 0. No parser/tokenizer functionality claimed.
- Python tooling tests: 7 passed, 0 failed. Build: PASS, exit 0, 0 warnings/errors. dotnet test: PASS, 1 xUnit test passed (runs the seven Python tests).
- L0–L4: not run, no corpus yet. Git commit, packaging and installation skipped. Python upstream unchanged.
- Step 1.1 marked complete under its documented-differences gate. Next: 1.2 port_status.py.

## 2026-12-19 — Step 1.2 member hashes and port status

- Implemented src/tools/port_status.py: normalized AST SHA1 hashes, recursive class/member inventory, phase ordering, C# anchor scanning including generated files, missing/ported/stale/orphan/stub plus duplicate diagnostics.
- CLI supports --next N, --hash anchor, --file path, --json, and input path overrides. Verified --next, --hash and filtered JSON against actual upstream.
- Actual empty library gate: 5759 missing members; 0 ported/stale/orphan/stub/duplicate. This includes class methods/attributes unlike the 1720 top-level entries in scope.json.
- Eight new Python tests passed; existing seven closure tests also passed via two xUnit integration cases. Build PASS (exit 0, 0 warnings/errors); dotnet test PASS (2 passed, 0 failed).
- Hash conventions: repeated Python definitions sharing a qualified name (property getter/setter, conditional alternatives) are grouped in source order under one anchor. Class hashes include the full class AST. AST hashes exclude docstrings recursively but retain decorators and other literals; pin Python version during future upstream sync to avoid AST-version drift.
- Python library members ported: 0. L0–L4 not run; corpus absent. No upstream edits, package publishing, installation or Git commits.
- Step 1.2 complete. Next: 1.3 gen_csharp.py.

## 2026-12-19 — Step 1.3, first increment: top-level enums

- Added gen_csharp.py; imports the read-only upstream runtime and generates top-level enums in mirrored *.enums.g.cs files, preserving values/order, string value conversions and source-hash anchors.
- Generated 10 enums and 643 values in eight files, including TokenType, DType, Dialects, ErrorLevel and other scoped module-level enums. String enum aliases retain numeric identity.
- port_status confirms 653 current generated anchors, 5106 missing, 0 stale/orphan/stub/duplicate. Anchor coverage does not indicate SQL functionality or completed algorithm ports.
- Build PASS, exit 0, 0 warnings/errors. dotnet test PASS: 3 integration cases covering 19 Python unit tests (7 closure, 8 status, 4 enum generator).
- Upstream import emits an unset __version__ warning because the clone is not installed. Generation succeeded; no installation or upstream modification performed.
- Step 1.3 remains incomplete: expression shells/traits/constructors, nested enums, expression metadata/registry, merged pure-data attributes, callable-attribute inventory and dialect type registry are still pending. L0–L4 not run; corpus absent.
- No generated files were edited manually. No packages published, tools installed or Git commits made. Next session continues step 1.3.

## 2026-12-19 — Step 1.3, second increment: expression declarations

- Extended generator with AST trait discovery and runtime class inheritance. Generated 1068 declarations: 1051 expression classes including abstract Expr and concrete Expression, plus 17 trait interfaces, in mirrored expression modules. Original class docstrings emitted as XML summaries.
- No expression methods, constructor argument storage or metadata implemented; generated declarations are structure only. Trait methods and constructor semantics remain pending for subsequent increments/phases.
- port_status: 1721 current anchors, 4038 missing; no stale/orphan/stub/duplicate anchors. Coverage is declaration-level, not algorithm correctness.
- Added three Python generator tests and two C# reflection/trait tests. dotnet test PASS: five xUnit cases, including three Python suites (22 Python unit tests). Build PASS, exit 0, zero warnings/errors.
- Step 1.3 still open: constructors, metadata, registry, nested enums, merged data and callable inventory pending. L0–L4 skipped (corpus absent). Upstream unchanged; no installation/publication/Git commit.
- Next: continue 1.3 with metadata and constructor scaffolding, addressing ordered dictionary/runtime dependencies without claiming completed AST behavior.

## 2026-12-19 — Step 1.3, third increment: metadata and expression registry

- Generated runtime metadata for all 1051 expression classes: key, ordered arg_types, required_args, is_var_len_args, var_len_arg_key and raw _sql_names, using virtual properties and static backing fields. Trait interfaces remain markers; concrete classes carry their effective inherited metadata.
- Generated EXPR_CLASSES with 1066 upstream registry entries (including trait entries, matching Python). Added minimal PyDict foundation over .NET OrderedDictionary; Python-specific mapping operations remain phase 2.
- Added generated sql_names__helper snapshots from upstream sql_names(), not a port of that method. Raw _sql_names remains unchanged. Initial new test incorrectly assumed Sum._sql_names contains SUM; retained expected SUM and corrected the test to check the effective-name snapshot after reading upstream fallback logic. First test run: 7 passed, 1 failed; final run: 8 passed, 0 failed.
- Build PASS, exit 0, zero warnings/errors. Eight xUnit cases cover 24 Python unit tests plus five C# tests. No corpus/L0–L4 tests run.
- Constructors intentionally deferred: argument storage and parent links must not be approximated. Step 1.3 remains open; nested enums, merged data tables, callable inventory and dialect registry also pending.
- Python upstream and generated files not manually modified; no installation/publication/Git commit. Next session continues step 1.3.

## 2026-12-19 — Step 1.3, fourth increment: constructors

- Generated named optional constructors and dictionary overloads for 1050 concrete classes. Ported Expr.__init__ and Expression._set_parent as required constructor dependencies, with current hashes. Generated is_primitive metadata; added NotPortedException support.
- Named parameters use OptionalAttribute/Missing.Value to distinguish omitted values from explicit null. Initial enum sentinel default failed with CS1763; fixed generator and regenerated, never editing generated files. Added conversion convention to AGENTS.md.
- List<object?> children preserve original indexes and parent/arg_key links. TimeUnit implementers explicitly fail because their special constructor is unported. No approximation of unit normalization claimed.
- Initial new TimeUnit test selected DateTrunc (not a TimeUnit trait implementer); corrected the target to DateDiff while retaining the expected exception. Initial test run 11 passed/1 failed; final build PASS (exit 0, zero warnings/errors), final test PASS (12 passed, 0 failed).
- Added four C# constructor tests and one Python generation test (25 Python unit tests total). Corpus L0–L4 not run. Step 1.3 still open: TimeUnit constructor, nested enums, merged dialect data/callable inventory and dialect registry remain pending.
- Upstream unchanged. No installation, publishing or Git commit. Next session continues 1.3.

## 2026-12-19 — Step 1.3 dependency batch: TimeUnit and AST mutation

- User approved batching related points. Ported 13 Python members: seq_get; Expression.this/expression/expressions/text/name/set; Column.parts; Star/Null/Dot.name; TimeUnit.__init__/unit. Added exact generated TimeUnit unit-name and type tables with current anchors.
- Removed TimeUnit NotPortedException guard. Trait constructor is hosted in Expr and invoked after base initialization, preserving runtime normalization for exact Var/Literal/Column types and Week. Qualified columns and raw strings remain unchanged; lower-case abbreviations are only uppercased, not expanded, matching upstream.
- set supports scalar removal, indexed overwrite/insertion/splice, reindexing and ancestor hash invalidation. Negative splice indexing is recomputed after removal, preserving the upstream slice algorithm rather than 'fixing' its surprising ordering.
- Incremental runtime support added: PyDict get/pop/setdefault/items/copy; py truthy/or/enumerate/start-only slice/require_trait. Full phase 2 remains incomplete.
- Added TimeUnit, mutation and runtime-mapping cases; replaced the obsolete expected-not-ported test with normalization behavior now explicitly requested by the user. Existing test expected values unchanged.
- Initial batches build PASS and test PASS (23, then 32 cases). Final verification: build PASS, exit 0, zero warnings/errors; test PASS, 33 xUnit cases, zero failures. port_status: 2664 current anchors, 3095 missing, zero stale/orphan/stub/duplicate (mostly generated declaration/metadata coverage, not completed SQL algorithms). Python tooling suites contain 25 unit tests. L0–L4 skipped (no corpus yet).
- No upstream edits, manual generated-file edits, installation, publishing or Git commit. Step 1.3 remains open for nested enums, merged data/callable inventory and dialect registry; future work continues in plan order.

## 2026-12-19 — Step 1.3 batch: dialect data, enum aliases, generator lifecycle

- Added gen_data.py: generated 1557 final runtime data properties on 12 dialect/parser/generator class shells, preserving base/Postgres/TSQL/Fabric inheritance and virtual dispatch. Added four-dialect Type registry support; no parsing/generation methods implemented.
- Generated nested alias constant tables for DataType.Type and Properties.Location; these are aliases to existing enums, not distinct nested enum definitions. Added C# conversion convention to AGENTS.md.
- Generated handwritten_attrs.txt listing 101 callable-bearing runtime attributes and data_inventory.json listing eight unsupported data families. Tokenizer data and broader attribute coverage remain pending; 1.3 not marked complete.
- Added owned-file manifest with SHA256 for 66 generated files and safe obsolete-file cleanup, executed only after all generators succeed. Foreign generated files are not removed. Verified an identical rerun produces exactly the same manifest/hashes.
- Initial generator run failed because the base Dialect does not expose a capitalized Parser attribute; corrected discovery to check availability and regenerated successfully.
- Build PASS, exit 0, zero warnings/errors. dotnet test PASS: 36 xUnit cases, including 28 Python tooling unit tests. port_status: 3186 matching anchors, 2573 missing, zero stale/orphan/stub/duplicate. These counts include scaffolding and do not claim algorithm completion.
- Corpus and L0–L4 not run. Upstream unchanged. No package publication, installation or Git commits. Next: complete tokenizer data and the eight unsupported families within 1.3, then recorder/harness phases.

## 2026-12-19 — Step 1.3: tokenizer data verification

- Found additional local implementation since the previous log: tokenizer class generation, heterogeneous/tuple data serialization and incremental regex adapter. Preserved those changes rather than overwriting them.
- Regenerated successfully: 1753 runtime properties across 17 shells, zero unsupported data families in this selected scope, 101 callable-bearing attributes, 67 generated files. Broader expression attribute inventory remains pending; 1.3 is still open.
- Added four C# tests for inherited token tables, Fabric/TSQL TIMESTAMP separation and supported regex cache/anchor/Unicode behavior.
- Build PASS, exit 0, zero warnings/errors; dotnet test PASS, 40 passed, zero failed. No L0–L4 corpus gates run; tokenizer scanning algorithm is not implemented.
- Python upstream unchanged. No publishing, installation or Git commits. User requested shorter increments and reporting.

## 2026-12-19 — Step 1.3: expression data inventory completed

- Added gen_expression_data.py, inventorying direct class assignments across all expression models. Counts: 954 metadata assignments, 24 generated data attributes, two enum aliases, two existing trait-data entries and one structural __slots__ entry; no unsupported attributes remain.
- Generated remaining node-kind/hash flags through virtual metadata and pure expression tables, including UnixToTime scale constants serialized as primitive Literal args. No algorithm replaced with generated snapshots beyond the already documented SQL-name helper.
- Generated manifest now covers 70 files. port_status: 3314 matching anchors, 2445 missing; zero stale/orphan/stub/duplicate. Coverage includes generated declarations and data, not completed SQL functionality.
- Added three C# tests for dispatch flags, scale constants and exhaustive inventory status validation. Build PASS, exit 0, zero warnings/errors; dotnet test PASS, 43 cases. Corpus L0–L4 not run.
- Step 1.3 generated-code gate completed for the scoped classes; runtime regex support remains incremental and actual parsers/generators remain unported. Next: step 1.4 oracle corpus recorder.
- Python upstream unchanged. No publishing, installation or Git commits.

## 2026-12-19 — Step 1.4 initial recorder and smoke run

- Implemented extract_corpus.py with L0 token/L1 parse/L2 generation/L3 transpile boundary capture, actual errors/warnings, normalized options, source test IDs, SHA256 deduplication and stable JSONL/manifest output.
- Guards are per level: nested calls of the same level are suppressed while lower levels remain independently recordable. No upstream monkeypatch survives uninstall.
- Token/L1 positions map to UTF-16. Standalone L2 ASTs with no original SQL preserve supplied metadata rather than invent offsets; this convention needs review for the final corpus contract.
- Added six recorder tests and a fourth xUnit Python-suite integration case. Smoke run on upstream test_transpile.py: 28 tests passed, 4986 cases across four files under external artifacts tmp/corpus-smoke (not committed corpus).
- Build PASS, exit 0, zero warnings/errors; dotnet test PASS, 44 xUnit cases. Input/output paths are resolved before changing working directory, and output inside upstream is rejected.
- Step 1.4 remains open: full-suite recording, baseline commit verification, helper-level validate_identity/validate_all L3 capture and capture-skip review still pending. No L0–L4 C# corpus gates run.
- Python upstream unchanged. No publication, installation or Git commits.

## 2026-11-03 — Blocks B9 to B14 and documentation

- **B11 T-SQL, B12 Fabric:** `Dialect.parser()/generator()` routing, `TSQLParser`/`TSQLGenerator`, `FabricParser`/`FabricGenerator`; base additions (table hints, `NEXT VALUE FOR`, DECLARE, WHILE, OPENJSON, system versioning, NULL-ordering simulation, `ensure_bools`, CTE hoisting).
- **B13 optimizer subset:** `TypeAnnotator` (expression level), `Simplifier` (date/interval and constant propagation are NotPorted), PostgreSQL `unnest_sql`, `convert_concat_args`, `apply_index_offset`.
- **B14 Layer 3:** `layer3` dialect, virtual `CreateParser/CreateGenerator/CreateTokenizer`, `exp.column`, `Expr.sql(dialect)`, `Dialect.unregister`; host `corpus.json` passes.
- **Results:** corpus 26,487 / 26,487 (0 fail, 0 NotPorted), unit tests 897 / 897 (full run, 2026-11-03).
- **Documentation:** README (map of documents), USAGE (installation first, own projects, custom dialects), ARCHITECTURE, ROADMAP (short, history archived in `artifacts/docs-archive/`), OPENPOINTS (open items first), ALIGNMENT (generated map against the official project, drift and realignment procedure). `csharp/` merged into the root; `.gitignore` added; result snapshots versioned in `artifacts/`.
- **Skipped / not tested:** no Git repository; the host's `layer3.py` and its test 12 were not available; full-corpus replay not rerun after the documentation-only changes.
- **Build artifacts moved into the repository (2026-11-03):** `D:\Dev\BuildArtifacts\sqlglot-csharp` merged into `artifacts/` (bin/obj per project, oracle venv, logs, reports); `ArtifactsRoot` now defaults to `artifacts/`; hard-coded paths in tools, tests and docs replaced; `.gitignore` keeps only the small snapshots. Old corpus copies (103 MB reports/corpus, tmp/corpus-smoke, tmp/pretty-agent) and the leftover duplicates were moved to `D:\Dev\BuildArtifacts	rash`. Verified after the move: unit tests 897/897, corpus 26,487/26,487.

## 2026-11-05 · clean-up for publication

- Logging aligned with Python: `py.logging` is a minimal `logging` equivalent (named loggers, levels, handlers, stderr lastResort); module-level `logger = getLogger("sqlglot")` with anchors in `__init__`, helper, expressions/core, parser, generator, dialects/dialect, optimizer/annotate_types, optimizer/simplify (8 members). Removed the `Microsoft.Extensions.Logging.Abstractions` dependency, the Generator `logger` parameter and the `Dialect.Logger` property (not in Python); restored the dropped warning in `TypeAnnotator._annotate_binary`. Corpus replay captures through one handler on the `"sqlglot"` logger (`LogCapture`, AsyncLocal routing).
- Layer 3 moved out of the library into `src/SqlGlotSharp.Extensions.Layer3` (+ `.Tests`), registered with `Layer3.Register()`.
- `src/SqlGlotSharp.slnx` is no longer git-ignored. Empty folders removed; a stray old build tree and the three former `layer3.cs` files moved to `artifacts/trash/`.
- README rewritten for publication (unofficial port, credits, extensions); USAGE gains Logging and "Use an extension"; ALIGNMENT regenerated.
- Tests: build 0 errors; unit tests 897/897 (889 + 8); full Scoreboard 26,487/26,487, 0 fail, 0 NotPorted. Not run: `SqlGlotSharp.CorpusTests` (heavy, not in the solution).

## 2026-11-05 · builders, benchmarks, repository layout

- Ported the SQL builders (66 members, anchors with hashes): core.py `maybe_parse`, `_is_wrong_expression`, `_apply_builder`, `_apply_child_list_builder`, `_apply_list_builder`, `_apply_conjunction_builder`, `_combine`, `_wrap`, `_apply_set_operation`, `condition`, `and_`, `or_`, `xor`, `not_`, `_lazy_unnest`, `convert`, `paren`, `alias_`, `Expression.and_/or_/not_/as_/_binop/isin/between/is_/like/ilike/eq/neq/rlike/div/asc/desc`; query.py `_apply_cte_builder`, `Query.ctes/select/limit/offset/order_by/where/with_/union/intersect/except_`, `SetOperation.select`, `Join.method/kind/side`, `Select.from_/group_by/sort_by/cluster_by/select/lateral/join/having/window/qualify/distinct/lock/hint`, `Subquery.select`, `union/intersect/except_`; builders.py `select`, `from_`. Conversion rules added to AGENTS.md 4.5 (params overloads, generic extension methods for `self: Q -> Q` trait members, covariant overrides, assert).
- New `BuilderTests`: 47 calls whose expected output comes from the Python oracle, plus 2 behaviour tests.
- Benchmarks: `src/Benchmarks` + `src/tools/bench_compare.py` on the queries of `python/benchmarks/parse.py`; SqlGlotSharp 0.14–0.57 of pure-Python SQLGlot time (parse), 0.16–0.58 (transpile). Results in README.
- Repository layout: documentation in `docs/` (`docs/status/` versioned results, `docs/history/` plan, log, old roadmaps); `ops/` merged into `src/tools/`; `artifacts/` fully git-ignored; `.ralfai/` deleted (asked by Marco); old build trees moved to `artifacts/trash/`. Scoreboard default output is now `docs/status/`.
- Tests: build 0 errors; unit tests 946/946 (938 + 8); full Scoreboard 26,487/26,487, 0 fail, 0 NotPorted; port_status 3,840 ported, 0 stale. Not run: `SqlGlotSharp.CorpusTests`.

## 2026-11-05 · anchors, packaging, first commit

- Anchor hygiene: removed a second `exists_sql` (zero hash) from `PostgresGenerator` (identical to the base `Generator.exists_sql`); the comment on `defaultcolumnconstraint_sql` is no longer an anchor (it is the body of a `TRANSFORMS` lambda, not a Python member). port_status: 3,841 ported, 0 stale, 0 orphan, 0 duplicate (571 stub, 1,347 missing: modules outside the scope).
- NuGet: package metadata on `SqlGlotSharp` and `SqlGlotSharp.Extensions.Layer3`; both packed as `30.27.0-net10-preview.1` into `D:\Dev\NuGetLocalFeed`. Checked from a console outside the repository (package restore, transpile postgres↔tsql, Layer 3 registration).
- Tests: build 0 errors; unit tests 946/946 (938 + 8); full Scoreboard 26,487/26,487, 0 fail, 0 NotPorted. Not run: `SqlGlotSharp.CorpusTests`.
- First Git commit (local). Not pushed.
- Layer 3 package README (`src/SqlGlotSharp.Extensions.Layer3/README.md`), packed as `SqlGlotSharp.Extensions.Layer3` `30.27.0-net10-preview.2` (preview.1 stays in the feed, without README). README example checked from the package: `JSON_ARRAY_CONTAINS('a', t.tags) AND b = 1`.
- Pushed `main` to https://github.com/marcoparenzan/sqlglotsharp (public), authorized by Marco.
- Library bumped to `30.27.0-net10-preview.2` (no code change) so both packages share the version; tag and GitHub release `v30.27.0-net10-preview.2` with both `.nupkg` attached. Not published on nuget.org.
