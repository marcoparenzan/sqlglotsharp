"""List the Python members that changed between two upstream refs, with their C# port status.

Compares the port_status.py inventories of two refs of the official clone (python/), member by member, using the
same normalized AST hash as the anchors. Unlike the "stale" state, this also covers members whose C# anchor has a
zero hash or no hash, because the comparison is Python against Python.

Usage (oracle interpreter, from the repository root):
    python -B src/tools/upstream_diff.py v30.27.0 v30.29.0
    python -B src/tools/upstream_diff.py 5c101a25e v30.29.0 --json artifacts/tmp/upstream_diff.json

The refs are extracted with `git archive` into a temporary folder outside the repository; python/ is not modified.
"""
from __future__ import annotations

import argparse
import io
import json
import subprocess
import sys
import tarfile
import tempfile
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
UPSTREAM = REPO / 'python'
PORT_STATUS = Path(__file__).with_name('port_status.py')


def inventory(ref: str, workdir: Path) -> dict[str, dict]:
    target = workdir / ref.replace('/', '_')
    target.mkdir(parents=True)
    archive = subprocess.run(['git', '-C', str(UPSTREAM), 'archive', ref, 'sqlglot'], check=True, capture_output=True).stdout
    with tarfile.open(fileobj=io.BytesIO(archive)) as tar:
        tar.extractall(target, filter='data')
    out = subprocess.run([sys.executable, '-B', str(PORT_STATUS), '--upstream', str(target), '--json'],
                         check=True, capture_output=True, text=True, encoding='utf-8').stdout
    return {m['anchor']: m for m in json.loads(out)['members'] if 'hash' in m}


def where(member: dict) -> str:
    places = member.get('csharp') or []
    return ', '.join(f"{p['file']}:{p['line']}" for p in places) or '-'


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('old', help='baseline ref (tag or commit), e.g. v30.27.0')
    parser.add_argument('new', help='target ref, e.g. v30.29.0')
    parser.add_argument('--json', type=Path, help='also write the result as JSON to this path')
    args = parser.parse_args()

    with tempfile.TemporaryDirectory(prefix='sqlglot-upstream-diff-') as tmp:
        old = inventory(args.old, Path(tmp))
        new = inventory(args.new, Path(tmp))

    rows = []
    for anchor, member in new.items():
        if anchor not in old:
            rows.append(('added', member['status'], anchor, where(member)))
        elif old[anchor]['hash'] != member['hash']:
            rows.append(('changed', old[anchor]['status'], anchor, where(old[anchor])))
    for anchor, member in old.items():
        if anchor not in new:
            rows.append(('removed', member['status'], anchor, where(member)))
    order = {'changed': 0, 'added': 1, 'removed': 2}
    rows.sort(key=lambda r: (order[r[0]], r[2]))

    print(f'{args.old} -> {args.new}: '
          f"{sum(r[0] == 'changed' for r in rows)} changed, {sum(r[0] == 'added' for r in rows)} added, "
          f"{sum(r[0] == 'removed' for r in rows)} removed (members in scope)")
    print()
    print('| Change | C# status | Python member | C# location |')
    print('|---|---|---|---|')
    for change, status, anchor, location in rows:
        print(f'| {change} | {status} | `{anchor}` | {location} |')

    if args.json:
        args.json.parent.mkdir(parents=True, exist_ok=True)
        args.json.write_text(json.dumps([dict(zip(('change', 'status', 'anchor', 'csharp'), r)) for r in rows], indent=1),
                             encoding='utf-8')
    return 0


if __name__ == '__main__':
    sys.exit(main())
