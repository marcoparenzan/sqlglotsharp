# ROADMAP progress history

Full progress table of the porting work, moved out of `ROADMAP.md` on 2026-11-03 to keep that page short.
Counts are L1+L2+L3 corpus cases (the rows from the T-SQL block onward include every corpus dialect). "quick run" means
`SCOREBOARD_MAXSQL=20000` (three stress inputs over 20 KB skipped); "full run" has no cap.

| Date | Pass | Fail | NotPorted | Pass % |
|---|---:|---:|---:|---:|
| 2026-11-02 (baseline) | 10,159 | 6 | 5,688 | 64.1 |
| 2026-11-02 after B0 (`parse_into`, L3 helpers) | 10,481 | 0 | 5,357 | 66.2 |
| 2026-11-02 B1 started (JSONPath, IN/EXISTS/BETWEEN generators) | 11,061 | 0 | 4,777 | 69.8 |
| 2026-11-02 + brackets, subquery predicates, full `_parse_types` | 11,484 | 0 | 4,354 | 72.5 |
| 2026-11-02 + leading FROM, LATERAL, UNNEST, FUNCTIONS builders | 11,871 | 0 | 3,967 | 75.0 |
| 2026-11-02 + properties, CREATE index/trigger/function, Command fallback | 12,103 | 0 | 3,735 | 76.4 |
| 2026-11-03 + DDL generator (create/properties/index/trigger/function/command) | 12,470 | 0 | 3,368 | 78.7 |
| 2026-11-03 + GRANT/REVOKE, COMMENT, transactions, USE, MERGE | 12,800 | 0 | 3,038 | 80.8 |
| 2026-11-03 + UNNEST/LATERAL/CONCAT/TRIM/ARRAY generators | 13,015 | 0 | 2,823 | 82.2 |
| 2026-11-03 + special-grammar function parsers | 13,177 | 0 | 2,661 | 83.2 |
| 2026-11-03 + misc generators, type literals, column ops, star ops, lambdas | 13,537 | 0 | 2,301 | 85.5 |
| 2026-11-03 quick run (base+postgres only; `MAXSQL=20000`) | 13,796 | 0 | 2,042 | 87.1 |
| 2026-11-03 + SET/UNSET, COPY (quick run, base+postgres) | 13,876 | 0 | 1,961 | 87.6 |
| 2026-11-03 + full DROP, full ALTER (parser + generator) (quick run, base+postgres) | 14,021 | 0 | 1,816 | 88.5 |
| 2026-11-03 + column constraints (EXCLUDE, COMMENT, COLLATE...), index params, columndef (quick run, base+postgres) | 14,166 | 0 | 1,671 | 89.4 |
| 2026-11-03 + PostgreSQL parser: FUNCTIONS builders, DATE_PART, JSON_AGG, VARIADIC, `&&`/`@@`/`?` (quick run, base+postgres) | 14,318 | 0 | 1,519 | 90.4 |
| 2026-11-03 + PostgreSQL generator transforms (renames, DateDiff, Levenshtein, UnixToTime, TO_CHAR/TO_DATE...) (quick run, base+postgres) | 14,486 | 0 | 1,351 | 91.5 |
| 2026-11-03 + ANALYZE, CACHE/UNCACHE, LOAD DATA, DESCRIBE, TABLESAMPLE generator (quick run, base+postgres) | 14,622 | 0 | 1,215 | 92.3 |
| 2026-11-03 + RANGE_PARSERS (OPERATOR, GLOB, `@>`...), AUTO/simple TRANSFORMS, small leaf generators (quick run, base+postgres) | 14,900 | 0 | 936 | 94.1 |
| 2026-11-03 + struct/kv args, UDTFs, full CAST/CONVERT/DECODE/XML*/MATCH parsers, div_sql, tsords*, json (quick run, base+postgres) | 15,153 | 0 | 683 | 95.7 |
| 2026-11-03 + PostgreSQL dialect-helper transforms (date_add, array_*, getbit, sha2...), 10 exclusions (quick run, base+postgres) | 15,226 | 0 | 608 | 96.2 |
| 2026-11-03 + INSERT (full), SORT/CLUSTER BY, locks, CONNECT BY, KILL, INTO, WITH SEARCH/CYCLE, hint (quick run, base+postgres) | 15,312 | 0 | 522 | 96.7 |
| 2026-11-03 + generic small generators (hex, bit, connect, tonumber, blocks, ceil/floor...) (quick run, base+postgres) | 15,371 | 0 | 463 | 97.1 |
| 2026-11-03 + PG MERGE/percentiles/arraycontains/matchagainst, full `_parse_column_def`/identity, property parsers (quick run, base+postgres) | 15,454 | 0 | 380 | 97.6 |
| 2026-11-03 + pipe syntax, hints, versions, select_sql/datatype_sql faithful, constraint modifiers (**full run**, base+postgres, 7.4 min) | 15,701 | 0 | 142 | 99.1 |
| 2026-11-03 + IGNORE/RESPECT NULLS, QUALIFY rewrite, LAST_DAY, ArrayFilter, TsOrDsAdd, 12 exclusions (quick run, base+postgres) | 15,710 | 0 | 122 | 99.2 |
| 2026-11-03 B11 T-SQL: dialect wiring, full TSQLParser/TSQLGenerator, DECLARE/WHILE/EXEC, OPENJSON, system versioning, NULL-ordering simulation, ensure_bools, CTE hoisting (quick run, all corpus dialects) | 26,255 | 0 | 215 | 99.2 |
| 2026-11-03 B12 Fabric (parser, generator, type mapping, AT TIME ZONE), `partitionrange_sql` (quick run, all corpus dialects) | 26,414 | 0 | 56 | 99.8 |
| 2026-11-03 final checkpoint, **full run** (no size cap, all corpus dialects, B1–B12) | 26,431 | 0 | 56 | 99.8 |
| 2026-11-03 B13 optimizer: `TypeAnnotator` (expression level), `Simplifier`, PG UNNEST/ROUND/REGEXP_REPLACE, `convert_concat_args`, `apply_index_offset` (**full run**, all corpus dialects) | 26,487 | 0 | 0 | 100.0 |
