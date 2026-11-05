"""Check previously failing oracle imports and tests without running the full suite."""
import importlib
import sys
import unittest
from pathlib import Path

from extract_corpus import Recorder


def main():
    sys.dont_write_bytecode = True
    sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'python'))
    for name in ('pytz', 'tzdata', 'duckdb', 'tests.dialects.test_bigquery',
                 'tests.test_executor', 'tests.test_optimizer'):
        importlib.import_module(name)
        print(f'Import OK: {name}', flush=True)
    recorder = Recorder()
    recorder.install()
    try:
        suite = unittest.defaultTestLoader.loadTestsFromNames([
            'tests.test_expressions.TestExprs.test_convert',
            'tests.test_expressions.TestExprs.test_convert_python39',
            'tests.test_expressions.TestExprs.test_transform_node_removal',
        ])
        result = unittest.TextTestRunner(verbosity=2).run(suite)
        print(f'Captured cases: {len(recorder.cases)}; capture skips: {dict(recorder.skipped)}')
        return 0 if result.wasSuccessful() else 1
    finally:
        recorder.uninstall()


if __name__ == '__main__':
    raise SystemExit(main())
