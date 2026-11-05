# Open points

Known limits and decisions. The current verified state and how to resume are in `ROADMAP.md`; this file holds what is still open and, at the bottom, the historical bootstrap notes (kept for context, not current).

## Open now

| Item | Detail | Where it bites |
|---|---|---|
| Scope-based optimizer not ported | `traverse_scope`, `qualify_columns`, `schema` (MappingSchema). Annotating whole queries (`annotate_types` on a `SELECT`) raises `NotPortedException`; only expression-level annotation works | `optimizer/annotate_types.cs` |
| Date/interval simplification | Needs python-dateutil `relativedelta` semantics; `Simplifier` raises `NotPortedException` for date literals with intervals and DATE_TRUNC range rewrites | `optimizer/simplify.cs` |
| Constant propagation | Needs `optimizer/normalize.py` (`normalized`); `simplify(constant_propagation=True)` raises `NotPortedException` | `optimizer/simplify.cs` |
| Other official dialects | Out of scope; they raise `NotPortedException` from the dialect registry | `dialects/dialect.host-registry.cs` |
| Realignment with v30.28.0 / v30.29.0 | 8 in-scope files changed, 18 members changed and 2 added (`upstream_diff.py`) | `UPSTREAM-SYNC.md` |
| Corpus version hard-coded | `"v30.27.0"` appears in ~15 test files and in the `extract_corpus.py` default; replace it with one constant at the first sync | `UPSTREAM-SYNC.md` step 6 |
| TimescaleDB overloads | Only the fixed-width `time_bucket` to `DATE_BIN` rewrite is proven; timezone, offset and named overloads raise `NotPortedException` by design | `generators/postgres.timebucket.cs` |
| Layer 3 `MEMBER OF` | Written from the host description (right side parsed with `_parse_column`) and verified on the host corpus only; the host's `layer3.py` was not available | `src/SqlGlotSharp.Extensions.Layer3/Layer3.cs` |
| Remaining builders | `builders.py`: `insert`, `update`, `delete`, `merge`, `cast`, `func`, `case`, `to_table`, `to_column`, `values`, ...; `Select.ctas` (needs `Properties.from_dict`); Python operator overloads (`__add__`, `__lt__`, ...) | `expressions/query.builders.cs` |
| Parser options in builders | `**opts` (error_level, max_errors, ...) of `maybe_parse` and the builders are not supported; the dialect defaults apply | `expressions/core.conditions.cs` |
| Performance | Three stress inputs make the parser quadratic (95 KB and 260 KB `UNION ALL` chains, a 2 MB `x=x=...` chain); quick runs cap the SQL size | `ROADMAP.md` |
| Unit-test hygiene | A test that calls `Dialect.register` must call `Dialect.unregister`, and a test that adds a handler to the `"sqlglot"` logger must remove it (both are process-wide, as in Python) | `Layer3Tests.cs`, `AttributeAndLoggingTests.cs` |

## Decisions carried forward

1. Target `net10.0` (exception to the global .NET 11 rule); no multi-targeting.
2. Nothing is published on nuget.org. Packages go to `D:\Dev\NuGetLocalFeed` as `<upstream x.y.z>-net10-preview.<n>` (GitHub release `30.27.0-net10-preview.2`, 2026-11-05, packages attached). The IDs `SqlGlotSharp` and `SqlGlotSharp.Extensions.Layer3` were free on nuget.org on 2026-11-05.
3. TimescaleDB and Layer 3 are host extensions (Layer 3 as a separate project, `SqlGlotSharp.Extensions.Layer3`) with no official oracle: expected values come from the Python plugins in `src/tools/oracle_plugins/`.
4. Sync with the official project: every minor release or every two weeks, following `ALIGNMENT.md` section 8.
5. Whether the full optimizer and an L4 level are needed later: not decided.

## Historical bootstrap notes (not current)

Everything below was written during the bootstrap and the first steps (1.1 to 1.4) and is kept as a record. Where it disagrees with the sections above or with `ROADMAP.md`, those win (for example: T-SQL and Fabric are done, and the former `csharp/` folder no longer exists).

### Bootstrap decisions / deviations

- User approved .NET 10 instead of .NET 11; requirements and preview version suffix updated to net10.
- Session layout rules require source and solution under root `src/` and documentation at repository root. Use those paths instead of the originally proposed `csharp/src`, `csharp/tests` and `csharp/` documentation. Existing specification files remain in `csharp/`. Future tooling must resolve upstream as root `python/`.
- Nested `git init` not executed: available tools do not expose a shell or Git command. Repository initialization remains a manual operational task.
- Baseline commit is currently the abbreviated value provided by the plan, not independently verified. Verify full upstream commit before corpus recording.

- Installed SDKs include only preview .NET 10 and .NET 11 (plus legacy .NET 6). Current build selects SDK 11 preview but targets net10.0; tests run on runtime 10.0.14. A stable .NET 10 SDK is recommended before adding an SDK pin.

### Step 1.1 — observed static scope

`src/tools/scope.json` contains 54 modules and 1720 top-level definitions/assignments (not class methods). Compared with the expected scope, `sqlglot.diff` is included because `sqlglot/__init__.py` directly re-exports it. Additional optimizer modules include canonicalize, normalize, normalize_identifiers and resolver through transitive imports. Do not silently discard these dependencies; decide whether to exclude the public diff export when porting the API.

The graph follows syntactic imports including TYPE_CHECKING and function-local imports; it is not the full Python runtime load graph. Dynamic dialect imports are recorded but not expanded to every dialect. Implicit parent-package initialization is not added unless referenced by a syntactic import. This avoids dragging every dialect/optimizer into the initial scope, but requires review at code generation/corpus time.

Tools are in root `src/tools/` rather than `csharp/tools/`, following the active source layout override. The development-only `PortingTools` console launches existing Python; it does not install anything or add runtime dependencies to the library. Python tooling tests also run via xUnit and require Python 3.11+ on PATH (or SQLGLOT_PYTHON).

User's instruction to proceed is treated as approval to continue implementation with Git initialization left as an explicit non-blocking operational point.

### Step 1.3 — enum increment

- Runtime import succeeds but upstream warns that __version__ cannot be set in the uninstalled clone. Continue using declared baseline metadata until commit/version verification; do not install upstream just to silence the warning.
- Generator now handles top-level enums and expression/trait declarations. Constructors, metadata, nested enums and all expression/data/dialect registries remain pending; step 1.3 is not complete. Class shells now have metadata and EXPR_CLASSES but still must not be treated as usable AST nodes. PyDict currently only supplies .NET insertion-order behavior; Python-specific operations remain phase 2. Constructors now preserve dictionary args and scalar/List<object?> parent links. TimeUnit-specific initialization has now been translated with its dependencies and tested; the constructor guard was removed. Runtime shims are incremental (truthy/enumerate/slice/trait checks and PyDict operations used here); the full phase 2 gate is not claimed. Effective SQL names are available as generated sql_names__helper snapshots; the actual sql_names method is not ported.
- Owned-file SHA256 manifest and obsolete-file cleanup are now implemented and tested. Identical regeneration verified for all 66 generated files. Cleanup only runs after successful generation and preserves files from other generators.

### Step 1.3 — dialect/data increment

- Current generation produces 1753 runtime properties across 17 dialect/parser/generator/tokenizer shells. The scoped registry includes only base/Postgres/TSQL/Fabric; it is not a functional dialect API yet.
- The eight previously unsupported data families are now covered by heterogeneous/tuple serialization, tokenizer shells and incremental regex support. The selected data inventory reports zero unsupported families; this is not proof of full runtime compatibility.
- Callable inventory covers the selected 17 families (101 runtime attributes), including inherited attributes; direct expression class data inventory is now complete: no unsupported assigned attributes remain. Generation gate 1.3 passes; SQL algorithm/corpus gates remain pending.
- DataType.Type and Properties.Location aliases are represented as nested static constants of DType/PropertiesLocation. Values preserve enum identity, but C# signatures must use the original enum type, not the alias table.
- Generation does not yet stage all output atomically: a failure may leave some updated files; rerun successfully before building. No cleanup occurs on a failed run.

### Step 1.4 — initial recorder

- Full-suite capture and helper-level validate_identity/validate_all recording remain pending; smoke data is under external tmp, not the committed corpus.
- Capture guards are per level, preserving independent L0–L3 coverage. Standalone L2 ASTs without source SQL preserve original metadata offsets; agree this contract before C# replay.
- Options with enums/types use explicit tagged JSON; unsupported values are counted in MANIFEST.skipped_capture, not silently replaced. Review skipped captures before accepting the gate.
- Full upstream commit verification and clean corpus regeneration/obsolete-file management remain pending.

### Phase 4 gate / phase 5 current status

Historical incremental notes above are not the current completion status; see ROADMAP.md.

The expression representation spike has been validated beyond the initial 20-class target: the L1 serialization gate covers 591 distinct class tags, including Select and trait-heavy expressions, across 7,855 records. All 7,799 recorded outputs (including null outputs) round-trip. Traversal, parent links, type metadata, copying and trait dispatch have C# tests. Forty-four actual comparisons captured from three upstream expression equality tests match in C#. This is not a parsing gate: these ASTs were produced by Python.

Phase 5 started: dialect registry lookup, settings/version initialization, plugins, equality/hash and tokenizer/JSONPath-tokenizer factories are implemented. Identifier normalization and quoting now use Python Unicode 14.0.0 data and final-sigma context, validated by 2,947 oracle cases. PostgreSQL keeps its ASCII-only normalization policy; these four helpers now carry completion anchors. Time helpers and JSONPath parsing are now implemented: 531 JSONPath AST/error outputs match Python, including arbitrary-size integer indexes carried as BigInteger in object-valued arguments. Use serde.dump_json/serde.JsonOptions to retain those values exactly. Full Python Decimal arithmetic/context is not implemented; PyDecimal is an exact literal-value representation. Base typing metadata and its two helper functions are now ported (294 class entries, including 130 callable annotators). The generated callbacks target an explicit ITypeAnnotator contract whose default methods fail as NotPorted; actual optimizer algorithms remain phase 7. Per-dialect typing overrides and parser/generator-dependent dialect methods remain pending. Temporal/operator dialect builders now have 34 Python oracle comparisons; parser state/matching foundation is staged and tested, but no SQL parsing gate has passed. Unicode casing data is generated for the current Python oracle runtime and must be regenerated if its Unicode version changes. No full SQL pipeline is claimed.

### Decisions carried forward from the plan

1. Confirm `SqlGlotSharp` package/namespace and check NuGet ID collisions before packaging.
2. Fabric remains in initial scope unless explicitly removed.
3. Decide Python plugins first versus C# only for TimescaleDB and Kusto.
4. Decide upstream PRs versus plugin subclasses for generic PostgreSQL/TSQL fixes.
5. Confirm whether full optimizer and L4 will be required later.
6. Choose sync cadence: minor releases or every two weeks.
