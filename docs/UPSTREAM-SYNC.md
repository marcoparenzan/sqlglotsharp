# UPSTREAM-SYNC — keeping SqlGlotSharp aligned with new SQLGlot releases

How to bring the port up to a newer official SQLGlot release, and the record of each sync. It is written for whoever runs the sync, the maintainer or an agent working under [AGENTS.md](../AGENTS.md). The porting rules in AGENTS.md still apply: mirror the Python line by line, keep the `// py:` anchors, never edit `python/` or the recorded corpus, never change an expected value to make a test pass.

[ALIGNMENT.md](ALIGNMENT.md) is the generated map, and its section 7 lists the drift from the newer releases in the local clone. This page is the procedure.

## 1. When to sync

- **Cadence:** every minor upstream release, or every two weeks, whichever comes first (decision 4 in `OPENPOINTS.md`).
- **Ad hoc:** an upstream fix that matters to a host project (for example a parser bug that Layer 3 hits).
- **Target:** go straight to the latest tag and use the per-commit list to review. Step through intermediate tags only if the first Scoreboard run on the new corpus shows too many failures to triage at once.
- **Skip:** a release whose in-scope changes are all for dialects outside the port. Still record it in the sync log (section 8) as "no action".

## 2. What tells you what changed

Five signals. The first is the work list; the others make sure nothing slips through.

| Signal | Tool | What it catches | What it misses |
|---|---|---|---|
| **Changed Python members** | `upstream_diff.py <old> <new>` | Every in-scope Python member whose normalized AST changed, was added or was removed between two refs, with its C# status and location. It compares Python with Python, so it covers every member, whatever its C# anchor looks like | Changes outside the members in scope (new modules: see "scope change") |
| **Stale anchors** | `port_status.py` | After the checkout: hashed C# anchors (3,773) whose Python body no longer matches. Used to confirm that the work is finished | Members with a zero hash (`#0000000000`, 574) or with no hash (300): those 874 anchors never go stale. That is why `upstream_diff.py` comes first |
| **Diff of in-scope files** | `git diff` in `python/`, ALIGNMENT §7 | Every textual change, with the commit that made it | The behaviour impact |
| **New corpus** | `extract_corpus.py` + Scoreboard | Every behaviour change that the upstream tests exercise | Changes that no upstream test covers |
| **Scope change** | `closure.py` → `scope.json` | New modules pulled into the import closure, and removed ones | Nothing within a module |

So the stale list alone is never the work list: `upstream_diff.py` is. Use the diff to understand each change and the new corpus to decide whether the behaviour changed.

## 3. Before you start

- Oracle interpreter: Python **3.11.7** in `artifacts/oracle-venv` (`src/tools/run-oracle.ps1 -CheckOnly` creates and checks it). Anchor hashes depend on the Python version, and 3.12 gives wrong hashes.
- Work in PowerShell, from the repository root, on a clean tree, on a branch named `sync/<tag>`.
- Set the oracle for the whole session:

```powershell
$env:SQLGLOT_PYTHON = (Resolve-Path artifacts\oracle-venv\Scripts\python.exe).Path
$py = $env:SQLGLOT_PYTHON
$old = 'v30.27.0'; $new = 'v30.29.0'          # baseline (UPSTREAM.json) and target
```

- Baseline check: start from a green state. Run `dotnet test src/SqlGlotSharp.slnx`, then a full Scoreboard on the current corpus: 0 fail, 0 NotPorted.

## 4. Procedure

### Step 1: survey (read only)

```powershell
git -C python fetch --tags
git -C python log --oneline "$old..$new" -- sqlglot/
git -C python diff --stat "$old" "$new" -- sqlglot/
```

Then list the changed members (this does not touch `python/`):

```powershell
& $py -B src/tools/upstream_diff.py $old $new --json artifacts\tmp\upstream_diff-$new.json
```

Use the exact baseline commit from `UPSTREAM.json` as `$old` (for v30.27.0 it is `5c101a25e`). The table it prints is the work list for step 8: paste it into the sync log entry.

Regenerate ALIGNMENT.md (`& $py -B src/tools/gen_alignment.py`). Section 7 then lists the in-scope files and the commits that touched them, each marked **review** or **no action (dialect out of scope)**. Copy that commit list into a new row of the sync log (section 8) and triage each commit with the table in section 5.

### Step 2: move the oracle

```powershell
git -C python checkout $new
```

Update `UPSTREAM.json`: `tag`, `commit` (`git -C python rev-parse --short=9 HEAD`), `baseline_description` (`git -C python describe --tags`), `ported_at`, `status`. Note the exact commit, not only the tag: the v30.27.0 baseline was `v30.27.0-1-g5c101a25e`, one commit after the tag.

### Step 3: scope

```powershell
& $py -B src/tools/closure.py
git diff --stat src/tools/scope.json
```

A new module in scope is ported in full: anchors and tests, in Python order. A module that left the scope keeps its C# code until nothing calls it. Then move it to `artifacts/trash/` and say so in the sync log.

### Step 4: regenerate the generated code

```powershell
& $py -B src/tools/gen_csharp.py
& $py -B src/tools/gen_typing_metadata.py
dotnet build src/SqlGlotSharp.slnx
```

Never edit a `*.g.cs` file by hand. Build errors at this point usually mean that a new expression class or argument is missing hand-written support, or that a data table now names a member that is not ported yet. Fix them so the build compiles again (stub with `NotPortedException` if needed, AGENTS.md §2.5).

### Step 5: the change list

The work list is the `upstream_diff.py` table from step 1. How to read each row:

| Change | C# status | Action |
|---|---|---|
| changed | ported | Port the change; refresh the anchor hash (`port_status.py --hash <anchor>`) |
| changed | stub | Port the change; if the member is now complete, give it a real hash instead of `#0000000000` |
| changed | missing | Not ported before: port it only if the port needs it now (the corpus tells) |
| changed, in a `*.g.cs` location | any | Handled by the regeneration in step 4: check the generated diff |
| added | missing | New in this release: port it if the parser, the generator or a ported dialect uses it |
| removed | any | Remove the C# twin once nothing calls it (move a whole file to `artifacts/trash/`) |

After the porting, confirm with the anchors (0 **stale** is required):

```powershell
& $py -B src/tools/port_status.py --json > artifacts\tmp\port_status-$new.json
& $py -B src/tools/port_status.py --modules
```

- **stale** = a hashed anchor whose Python body changed and was not ported yet.
- **orphan** = a Python member was renamed or removed and its C# twin was not followed.

### Step 6: record the new corpus

```powershell
dotnet run --project src/PortingTools -- -B src/tools/extract_corpus.py --output D:\clones\corpus\$new
```

The corpus lives outside the repository and is never edited. Then point the corpus-based tests at the new version. **Known gap:** the version is hard-coded as `"v30.27.0"` (and `"v30.27.0-logger-context-full"`) in about 15 test files and in the `extract_corpus.py` default. On the first sync, replace them with one constant (for example a `CorpusReader.Version` read from `UPSTREAM.json`), then remove this note.

### Step 7: measure

```powershell
dotnet run --project src/Scoreboard -- D:\clones\corpus\$new artifacts\reports\score-$new
```

For quick loops, set `SCOREBOARD_THREADS=8` and `SCOREBOARD_MAXSQL=20000` (about 25 s instead of 7 min). `failures.json` and `notported.json` are the work list. A case that changed between the two corpora is expected. A case that was passing and now fails without an upstream change is a regression in the port.

Review `src/SqlGlotSharp.Corpus/known-exclusions.json`. Case ids are content hashes, so re-recorded cases may get new ids or disappear. Each exclusion must still be a proven recorder artifact. Remove the ones that no longer match.

### Step 8: port

Work through the stale list, the hand-checked list from step 5 and the Scoreboard work list, in Python order within each file. One commit per upstream commit when practical: `sync(v30.29.0): parser.py _parse_describe (d6e787519)`. Add oracle-based unit tests for each behaviour change.

### Step 9: extensions and host dialects

- **Layer 3:** `& $py -B src/tools/gen_layer3_expected.py` regenerates `src/SqlGlotSharp.Extensions.Layer3.Tests/Layer3/expected.json` with the new oracle. Review every difference: it is a behaviour change visible to the host project. The host `corpus.json` was recorded with sqlglot 30.23.0, so a difference there is not by itself a port bug.
- **TimescaleDB:** the expected values come from `src/tools/oracle_plugins/`. Re-run the plugins and check that the `time_bucket` → `DATE_BIN` proof still holds.
- **Extension points:** if upstream changed `Dialect`, `Tokenizer`, `Parser` or `Generator` construction, check that `CreateTokenizer/CreateParser/CreateGenerator` and `Dialect.register` still behave the same for extensions.

### Step 10: close

1. `dotnet test src/SqlGlotSharp.slnx`: all green.
2. Full Scoreboard (no `MAXSQL`) on the new corpus: **0 fail, 0 NotPorted**, or each remaining case documented in `known-exclusions.json` with a reason (the maintainer reviews that list).
3. Run the full Scoreboard without an output argument (it writes to `docs/status/`), run `port_status.py --json > docs/status/port_status.json`, then `gen_alignment.py`.
4. Bump the package version in the `.csproj` files to `<new upstream>-net10-preview.1`. Pack into `D:\Dev\NuGetLocalFeed` only if asked.
5. Update README (numbers, baseline), ROADMAP (status, milestone row), OPENPOINTS, `history/PORTING_LOG.md` (session note) and the sync log below.
6. Merge `sync/<tag>` and tag the repository `sync-<tag>`.

## 5. Triage rules for an upstream commit

| The commit changes | Action |
|---|---|
| Only a dialect outside the port (`snowflake.py`, `oracle.py`, ...) | No action, unless it also touches a shared file that is in scope |
| Shared code that is in scope (`parser.py`, `generator.py`, `expressions/*`, `tokens.py`, `dialect.py`, ...) | Port it, even if the trigger was another dialect: the shared code must stay a mirror |
| A ported dialect (postgres, tsql, fabric) | Port it |
| The optimizer outside the ported subset | No action; note it in the log if it affects `annotate_types` or `simplify` |
| Tests only | No code action; the new corpus picks it up |
| A bug fix that the port had marked `// py-bug?` | Port the fix, remove the marker and the matching `OPENPOINTS.md` line |
| Refactor or rename with no behaviour change | Port it anyway (same shape, same names). Skipping it makes the next diff harder |
| A new runtime dependency in Python | Stop and ask: the library has no dependency beyond .NET |

## 6. Definition of done

- [ ] `UPSTREAM.json` names the new tag and the exact commit.
- [ ] `port_status.py`: 0 stale; every orphan explained.
- [ ] Zero-hash and unhashed anchors in the changed files checked by hand (step 5).
- [ ] Unit tests all green; full Scoreboard on the new corpus: 0 fail, 0 NotPorted (or documented exclusions).
- [ ] Layer 3 `expected.json` regenerated and its differences reviewed.
- [ ] ALIGNMENT.md regenerated; README, ROADMAP, OPENPOINTS and PORTING_LOG updated.
- [ ] Package version bumped; a row added to the sync log.

## 7. Known weak points

| Weak point | Effect | How to reduce it |
|---|---|---|
| 874 anchors without a real hash | `port_status.py` never marks them stale | `upstream_diff.py` compares Python with Python and covers them; hash members as they are completed |
| Corpus version hard-coded in tests | Each sync needs edits in ~15 files | One constant read from `UPSTREAM.json` (step 6) |
| Hashes depend on the Python version | A different interpreter marks everything stale | Always use the oracle venv (3.11.7) |
| Behaviour not covered by upstream tests | Neither the corpus nor the stale list catches it | The diff review (step 1) is the only guard |
| Layer 3 host corpus recorded with sqlglot 30.23.0 | Drifts further at each sync | Ask the host project to re-record it when it upgrades |
| Parser quadratic on three stress inputs | A full run takes about 7 minutes | Not a sync issue; quick runs cap the SQL size |

## 8. Sync log

| Date | From → to | Upstream commits in scope | Ported | Corpus | Notes |
|---|---|---|---|---|---|
| 2026-11-03 | baseline `v30.27.0-1-g5c101a25e` | — | — | 26,487 / 26,487 | Port complete |
| 2026-11-05 | (no sync) | — | — | 26,487 / 26,487 | Clean-up for publication: logging aligned with Python, Layer 3 moved to an extension |
| pending | `5c101a25e` → `v30.29.0` | 8 commits to review (`4912880ca`, `d06bde083`, `9619dd185`, `8a524dfa1`, `c9e673a2f`, `d6e787519`, `c241c2ff1`, `90659e63f`), 2 no action (`59db279da` snowflake, `49a6467dc` oracle). `upstream_diff.py`: 18 members changed (including the stubs `_parse_describe`, `_parse_table`, `values_sql`; 10 are in generated files), 2 added (`InteractiveProperty`, for Snowflake) | | | Both tags are already in the local clone |
