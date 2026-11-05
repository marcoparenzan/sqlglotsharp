"""Inventory Python members and compare normalized AST hashes with C# anchors."""
from __future__ import annotations

import argparse
import ast
import copy
import hashlib
import json
import re
from pathlib import Path

ANCHOR = re.compile(r'^\s*//\s*py:\s*(sqlglot/[^\s:]+\.py::[^\s#]+)\s+#([0-9a-fA-F]{10})\s*$', re.MULTILINE)
EARLY = ['errors.py', 'helper.py', 'trie.py', 'tokenizer_core.py', 'tokens.py']
LATE = ['serde.py', 'dialects/__init__.py', 'dialects/dialect.py', 'time.py', 'jsonpath.py',
        'typing/__init__.py', 'parser.py', 'generator.py', 'transforms.py', 'schema.py']


class WithoutDocstrings(ast.NodeTransformer):
    def _visit_body(self, node):
        self.generic_visit(node)
        if node.body and isinstance(node.body[0], ast.Expr) and isinstance(node.body[0].value, ast.Constant) and isinstance(node.body[0].value.value, str):
            node.body = node.body[1:]
        return node

    visit_Module = _visit_body
    visit_ClassDef = _visit_body
    visit_FunctionDef = _visit_body
    visit_AsyncFunctionDef = _visit_body


def normalized_hash(nodes, already_normalized=False):
    # Group repeated definitions (property getter/setter and conditional variants)
    # under one Python name, retaining their source order.
    normalized = [ast.dump(node if already_normalized else WithoutDocstrings().visit(copy.deepcopy(node)), include_attributes=False) for node in nodes]
    return hashlib.sha1('\n'.join(normalized).encode('utf-8')).hexdigest()[:10]


def file_order(path):
    short = path.removeprefix('sqlglot/')
    if short in EARLY:
        return (0, EARLY.index(short), '')
    if short.startswith('expressions/'):
        name = short.split('/')[-1]
        return (1, 0 if name == 'core.py' else 2 if name == 'builders.py' else 1, name)
    if short in LATE:
        return (2, LATE.index(short), '')
    for rank, dialect in enumerate(('postgres', 'tsql', 'fabric')):
        if short.endswith('/' + dialect + '.py'):
            return (3, rank, short)
    return (4, 0, short)


def inventory(upstream, scope):
    result = []
    for path in sorted((scope['details'][name]['path'] for name in scope['modules']), key=file_order):
        tree = ast.parse((upstream / path).read_text(encoding='utf-8'), filename=path)
        tree = WithoutDocstrings().visit(tree)
        grouped = {}

        def add(name, node, kind):
            entry = grouped.setdefault(name, {'nodes': [], 'line': node.lineno, 'kind': kind})
            entry['nodes'].append(node)

        def visit(nodes, prefix=''):
            for node in nodes:
                if isinstance(node, ast.ClassDef):
                    name = prefix + node.name
                    add(name, node, 'class')
                    visit(node.body, name + '.')
                elif isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
                    add(prefix + node.name, node, 'function' if not prefix else 'method')
                    # Local functions/variables belong to the enclosing method, not separate anchors.
                elif isinstance(node, (ast.Assign, ast.AnnAssign)):
                    targets = node.targets if isinstance(node, ast.Assign) else [node.target]
                    for target in targets:
                        for item in ast.walk(target):
                            if isinstance(item, ast.Name) and isinstance(item.ctx, ast.Store):
                                add(prefix + item.id, node, 'attribute')
                else:
                    for field in ('body', 'orelse', 'finalbody'):
                        child = getattr(node, field, [])
                        if isinstance(child, list):
                            visit(child, prefix)
                    for handler in getattr(node, 'handlers', []):
                        visit(handler.body, prefix)
        visit(tree.body)
        for name, entry in grouped.items():
            result.append({'anchor': path + '::' + name, 'path': path, 'line': entry['line'],
                           'kind': entry['kind'], 'hash': normalized_hash(entry['nodes'], already_normalized=True)})
    return result


def scan_anchors(source):
    result = {}
    for path in sorted(source.rglob('*.cs')):
        for match in ANCHOR.finditer(path.read_text(encoding='utf-8-sig')):
            result.setdefault(match[1], []).append({'hash': match[2].lower(),
                'file': path.relative_to(source).as_posix(),
                'line': match.string.count('\n', 0, match.start()) + 1})
    return result


def report(members, anchors):
    entries = []
    names = {member['anchor'] for member in members}
    for member in members:
        matches = anchors.get(member['anchor'], [])
        state = 'missing' if not matches else 'duplicate' if len(matches) > 1 else 'stub' if matches[0]['hash'] == '0000000000' else 'ported' if matches[0]['hash'] == member['hash'] else 'stale'
        entries.append({**member, 'status': state, 'csharp': matches})
    for anchor in sorted(anchors.keys() - names):
        entries.append({'anchor': anchor, 'path': anchor.split('::')[0], 'status': 'orphan', 'csharp': anchors[anchor]})
    counts = {state: sum(entry['status'] == state for entry in entries)
              for state in ('missing', 'ported', 'stale', 'orphan', 'stub', 'duplicate')}
    return {'schema_version': 1, 'hash_algorithm': 'sha1-ast-no-docstrings-v1', 'counts': counts, 'members': entries}


def module_report(entries):
    """Group by original module; anchor coverage is not functional completeness."""
    modules = {}
    for entry in entries:
        row = modules.setdefault(entry['path'], {
            'path': entry['path'], 'counts': {}, 'csharp_files': set(),
            'coverage': 'incomplete',
        })
        state = entry['status']
        row['counts'][state] = row['counts'].get(state, 0) + 1
        row['csharp_files'].update(match['file'] for match in entry['csharp'])
    for row in modules.values():
        row['csharp_files'] = sorted(row['csharp_files'])
        if set(row['counts']) == {'ported'}:
            row['coverage'] = 'anchors-covered-not-functionally-certified'
    return [modules[path] for path in sorted(modules)]


def main():
    repo = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--upstream', type=Path, default=repo / 'python')
    parser.add_argument('--scope', type=Path, default=Path(__file__).with_name('scope.json'))
    parser.add_argument('--source', type=Path, default=repo / 'src' / 'SqlGlotSharp')
    parser.add_argument('--next', type=int, metavar='N')
    parser.add_argument('--hash', dest='hash_anchor')
    parser.add_argument('--file', help='Python path relative to upstream, e.g. sqlglot/parser.py')
    parser.add_argument('--json', action='store_true')
    parser.add_argument('--modules', action='store_true', help='Summarize coverage by original Python file')
    args = parser.parse_args()
    if args.next is not None and args.next < 0:
        parser.error('--next must be nonnegative')
    members = inventory(args.upstream, json.loads(args.scope.read_text(encoding='utf-8')))
    if args.hash_anchor:
        found = next((member for member in members if member['anchor'] == args.hash_anchor), None)
        if found is None:
            parser.error('Unknown anchor: ' + args.hash_anchor)
        print(found['hash'])
        return
    result = report(members, scan_anchors(args.source))
    if args.file:
        result['members'] = [entry for entry in result['members'] if entry['path'] == args.file.replace('\\', '/')]
    if args.next is not None:
        result['members'] = [entry for entry in result['members'] if entry['status'] == 'missing'][:args.next]
    # Counts always describe the full scope; selected rows are separately counted.
    result['selected_count'] = len(result['members'])
    if args.modules:
        rows = module_report(result['members'])
        if args.json:
            print(json.dumps({'modules': rows, 'note': 'Anchor coverage is not functional certification.'}, indent=2))
        else:
            for row in rows:
                counts = ' '.join(f'{key}={value}' for key, value in sorted(row['counts'].items()))
                print(f"{row['path']} {counts} csharp_files={len(row['csharp_files'])} {row['coverage']}")
        return
    if args.json:
        print(json.dumps(result, indent=2))
    else:
        print(' '.join(f'{key}={value}' for key, value in result['counts'].items()))
        for entry in result['members']:
            print(f"{entry['status']:9} {entry['anchor']} #{entry.get('hash', '----------')}")


if __name__ == '__main__':
    main()
