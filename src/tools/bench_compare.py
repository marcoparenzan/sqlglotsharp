"""Compare parse/transpile times of SQLGlot (Python, from python/) and SqlGlotSharp on the queries of
python/benchmarks/parse.py, with the same method (best of 5 runs, stop after a run longer than 1 s).

Usage (oracle interpreter, from the repository root):
    python -B src/tools/bench_compare.py [--mode parse|transpile] [--json OUT.json]

Prints a Markdown table. The C# side runs `dotnet run -c Release --project src/Benchmarks` (one warm-up run per
query is not timed, so JIT compilation is excluded). Numbers depend on the machine; compare ratios, not seconds.
"""
from __future__ import annotations

import argparse
import json
import platform
import subprocess
import sys
import tempfile
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / 'python'))
sys.path.insert(0, str(REPO / 'python' / 'benchmarks'))

import logging  # noqa: E402

logging.getLogger('sqlglot').setLevel(logging.CRITICAL)

import parse as upstream  # noqa: E402  (python/benchmarks/parse.py)
import sqlglot  # noqa: E402


def best_of(fn, sql, iterations=5):
    best = float('inf')
    for _ in range(iterations):
        t0 = time.perf_counter()
        fn(sql)
        elapsed = time.perf_counter() - t0
        best = min(best, elapsed)
        if elapsed > 1:
            break
    return best


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument('--mode', choices=['parse', 'transpile'], default='parse')
    ap.add_argument('--json', type=Path)
    args = ap.parse_args()

    fn = upstream.sqlglot_transpile if args.mode == 'transpile' else upstream.sqlglot_parse
    python_times = {}
    for name, sql in upstream.QUERIES.items():
        try:
            python_times[name] = best_of(fn, sql)
        except Exception as e:  # report, do not hide
            print(f'python {name}: {type(e).__name__}: {e}', file=sys.stderr)
            python_times[name] = None

    with tempfile.TemporaryDirectory(prefix='sqlglot-bench-') as tmp:
        queries = Path(tmp) / 'queries.json'
        queries.write_text(json.dumps(upstream.QUERIES), encoding='utf-8')
        out = subprocess.run(['dotnet', 'run', '-c', 'Release', '--project', str(REPO / 'src' / 'Benchmarks'), '--',
                              str(queries), args.mode], check=True, capture_output=True, text=True, cwd=REPO)
        if out.stderr.strip():
            print(out.stderr, file=sys.stderr)
        csharp_times = json.loads(out.stdout.strip().splitlines()[-1])

    print(f'Mode: {args.mode}. Python {platform.python_version()} (pure Python sqlglot from python/), .NET Release build. Seconds, best of 5.')
    print()
    print('| Query | sqlglot (Python) | SqlGlotSharp | ratio |')
    print('| ---: | ---: | ---: | ---: |')
    for name in upstream.QUERIES:
        p, c = python_times.get(name), csharp_times.get(name)
        ratio = f'{c / p:.2f}' if p and c else 'N/A'
        print(f"| {name} | {p:.6f} | {c:.6f} | {ratio} |" if p and c else f'| {name} | {p} | {c} | N/A |')
    if args.json:
        args.json.write_text(json.dumps({'mode': args.mode, 'python': python_times, 'csharp': csharp_times}, indent=1), encoding='utf-8')
    return 0


if __name__ == '__main__':
    sys.exit(main())
