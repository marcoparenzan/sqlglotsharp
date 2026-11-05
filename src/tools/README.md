# src/tools

Python 3.11 scripts that generate code, record the corpus and check the port against the official sources in `python/`. Run them from the repository root with the oracle interpreter: `artifacts\oracle-venv\Scripts\python.exe -B src/tools/<script>`. Details: [docs/USAGE.md](../../docs/USAGE.md#development-tooling).

| Group | Files |
|---|---|
| Oracle environment | `run-oracle.ps1` (creates `artifacts/oracle-venv`; `-CheckOnly` to verify), `oracle-requirements.txt`, `check_oracle_environment.py` |
| Scope and coverage | `closure.py` → `scope.json` (modules in scope), `port_status.py` (anchors vs Python members), `upstream_diff.py` (members changed between two upstream refs) |
| Code generation | `gen_csharp.py` (all `*.g.cs`; owned files in `generated_manifest.json`), `gen_typing_metadata.py`, `gen_expression_data.py`, `gen_expression_exports.py`, `gen_serde_classnames.py`, `gen_tokenizer_state.py`, `gen_data.py`; inputs `data_inventory.json`, `expression_data_inventory.json`, `typing_inventory.json`, `handwritten_attrs.txt` |
| Test data from the oracle | `gen_corpus_tests.py`, `gen_decimal_cases.py`, `gen_dialect_builder_cases.py`, `gen_jsonpath_cases.py`, `gen_similarity_cases.py`, `gen_time_cases.py`, `gen_unicode_case.py`, `extract_equality_cases.py`, `gen_layer3_expected.py` |
| Corpus | `extract_corpus.py` (records the corpus from the upstream test suite); `oracle_plugins/` (Python reference implementations for the host dialects TimescaleDB and Layer 3) |
| Documentation | `gen_alignment.py` (writes `docs/ALIGNMENT.md` from `docs/status/`) |
| Performance | `bench_compare.py` (Python vs C# on the upstream benchmark queries; C# side in `src/Benchmarks`) |
| Maintenance | `sync_operator_anchors.py` |
| Tests of the tools | `test_closure.py`, `test_extract_corpus.py`, `test_gen_csharp.py`, `test_port_status.py` |
