"""Unit tests for static closure tooling (never import the upstream package)."""
import ast
import tempfile
import unittest
from pathlib import Path

import closure


class ClosureTests(unittest.TestCase):
    def test_function_local_and_type_checking_imports(self):
        tree = ast.parse('if TYPE_CHECKING:\n import sqlglot.helper\ndef f():\n from sqlglot import exp\n import os\n')
        imports, external, _ = closure.dependencies(tree, 'sqlglot.parser', False,
            {'sqlglot': None, 'sqlglot.helper': None, 'sqlglot.expressions': None})
        self.assertEqual(imports, ['sqlglot', 'sqlglot.expressions', 'sqlglot.helper'])
        self.assertEqual(external, ['os'])

    def test_relative_imports_and_aliases(self):
        tree = ast.parse('from ..helper import f\nfrom . import base as b\n')
        imports, _, _ = closure.dependencies(tree, 'sqlglot.parsers.tsql', False,
            {'sqlglot.helper': None, 'sqlglot.parsers': None, 'sqlglot.parsers.base': None})
        self.assertEqual(imports, ['sqlglot.helper', 'sqlglot.parsers', 'sqlglot.parsers.base'])

    def test_package_relative_import(self):
        imports, _, _ = closure.dependencies(ast.parse('from .core import Expr'),
            'sqlglot.expressions', True, {'sqlglot.expressions.core': None})
        self.assertEqual(imports, ['sqlglot.expressions.core'])

    def test_members_preserve_order_and_skip_class_internals(self):
        tree = ast.parse('A = B = 1\nclass C:\n def method(self): pass\nif True:\n def f(): pass\n')
        self.assertEqual([x['name'] for x in closure.members(tree)], ['A', 'B', 'C', 'f'])

    def test_dynamic_import_is_reported(self):
        _, _, dynamic = closure.dependencies(ast.parse('importlib.import_module(name)'),
            'sqlglot', True, {})
        self.assertEqual(dynamic, [{'line': 1, 'expression': 'importlib.import_module(name)'}])

    def test_cycles_and_deterministic_output(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            package = root / 'sqlglot'
            package.mkdir()
            (package / '__init__.py').write_text('from sqlglot import a\n', encoding='utf-8')
            (package / 'a.py').write_text('import sqlglot\nX = 1\n', encoding='utf-8')
            first = closure.closure(root, ['sqlglot'])
            self.assertEqual(first, closure.closure(root, ['sqlglot']))
            self.assertEqual(first['modules'], ['sqlglot', 'sqlglot.a'])
            self.assertEqual(first['member_count'], 1)

    def test_missing_seed_fails(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(ValueError):
                closure.closure(Path(directory), ['sqlglot.missing'])


if __name__ == '__main__':
    unittest.main()
