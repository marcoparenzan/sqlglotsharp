"""Hash and status tests; includes the real empty-library step 1.2 gate."""
import ast
import tempfile
import unittest
from pathlib import Path

import port_status as ps


class PortStatusTests(unittest.TestCase):
    def test_module_report_tracks_partial_coverage_and_unique_files(self):
        rows = ps.module_report([
            {'path': 'sqlglot/parser.py', 'status': 'ported', 'csharp': [{'file': 'parser.cs'}]},
            {'path': 'sqlglot/parser.py', 'status': 'stub', 'csharp': [{'file': 'parser.cs'}]},
            {'path': 'sqlglot/errors.py', 'status': 'ported', 'csharp': [{'file': 'errors.cs'}]},
        ])
        self.assertEqual(rows[0]['coverage'], 'anchors-covered-not-functionally-certified')
        self.assertEqual(rows[1]['coverage'], 'incomplete')
        self.assertEqual(rows[1]['counts'], {'ported': 1, 'stub': 1})
        self.assertEqual(rows[1]['csharp_files'], ['parser.cs'])

    def test_module_report_empty(self):
        self.assertEqual(ps.module_report([]), [])

    def test_hash_ignores_comments_positions_and_docstrings(self):
        a = ast.parse('def f(x):\n "first"\n return x + 1\n').body
        b = ast.parse('\n# comment\ndef f(x):\n "second"\n return x+1\n').body
        self.assertEqual(ps.normalized_hash(a), ps.normalized_hash(b))
        self.assertEqual(len(ps.normalized_hash(a)), 10)

    def test_pre_normalized_tree_has_identical_hash(self):
        import copy
        nodes = ast.parse('class C:\n "doc"\n def f(self):\n  "method doc"\n  return 1\n').body
        normalized = ps.WithoutDocstrings().visit(copy.deepcopy(ast.Module(body=nodes, type_ignores=[]))).body
        self.assertEqual(ps.normalized_hash(nodes), ps.normalized_hash(normalized, already_normalized=True))

    def test_hash_detects_semantic_changes(self):
        self.assertNotEqual(ps.normalized_hash(ast.parse('A=1').body),
                            ps.normalized_hash(ast.parse('A=2').body))

    def test_hash_keeps_non_docstring_literals(self):
        self.assertNotEqual(ps.normalized_hash(ast.parse('def f():\n return "a"').body),
                            ps.normalized_hash(ast.parse('def f():\n return "b"').body))

    def test_nested_class_members_and_property_variants(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'm.py').write_text('class C:\n A=1\n @property\n def x(self): return 1\n @x.setter\n def x(self,v): pass\n class N:\n  def f(self):\n   def local(): pass\n', encoding='utf-8')
            scope = {'modules': ['m'], 'details': {'m': {'path': 'm.py'}}}
            rows = ps.inventory(root, scope)
            self.assertEqual([row['anchor'] for row in rows],
                ['m.py::C', 'm.py::C.A', 'm.py::C.x', 'm.py::C.N', 'm.py::C.N.f'])

    def test_statuses(self):
        rows = [{'anchor': name, 'path': 'p', 'hash': 'abcdef1234'}
                for name in ('missing', 'ported', 'stale', 'stub', 'duplicate')]
        anchors = {'ported': [{'hash': 'abcdef1234'}], 'stale': [{'hash': '1234567890'}],
                   'stub': [{'hash': '0000000000'}], 'duplicate': [{'hash': 'abcdef1234'}]*2,
                   'orphan': [{'hash': 'abcdef1234'}]}
        result = ps.report(rows, anchors)
        self.assertEqual(result['counts'], dict.fromkeys(('missing', 'ported', 'stale', 'orphan', 'stub', 'duplicate'), 1))

    def test_scanner_includes_generated_and_duplicates(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            text = '// py: sqlglot/errors.py::A #ABCDEF1234\n'
            (root / 'a.cs').write_text(text, encoding='utf-8')
            (root / 'b.g.cs').write_text(text, encoding='utf-8')
            self.assertEqual(len(ps.scan_anchors(root)['sqlglot/errors.py::A']), 2)

    def test_phase_order(self):
        paths = ['sqlglot/parser.py', 'sqlglot/expressions/builders.py', 'sqlglot/errors.py',
                 'sqlglot/expressions/aggregate.py', 'sqlglot/expressions/core.py']
        self.assertEqual(sorted(paths, key=ps.file_order),
            ['sqlglot/errors.py', 'sqlglot/expressions/core.py', 'sqlglot/expressions/aggregate.py',
             'sqlglot/expressions/builders.py', 'sqlglot/parser.py'])

    def test_real_scope_inventory_is_unique_and_currently_missing(self):
        import json
        repo = Path(__file__).resolve().parents[2]
        scope = json.loads(Path(__file__).with_name('scope.json').read_text(encoding='utf-8'))
        rows = ps.inventory(repo / 'python', scope)
        self.assertGreater(len(rows), 1720)
        self.assertEqual(len(rows), len({row['anchor'] for row in rows}))
        # Use an empty source directory: this gate stays valid after real ports are added.
        result = ps.report(rows, {})
        self.assertEqual(result['counts']['missing'], len(rows))
        self.assertEqual(sum(result['counts'].values()), len(rows))


if __name__ == '__main__':
    unittest.main()
