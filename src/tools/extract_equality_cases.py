"""Capture actual expression comparisons from selected upstream tests; no test translation."""
import hashlib
import json
import sys
import unittest
from pathlib import Path

root = Path(__file__).resolve().parents[2]
sys.dont_write_bytecode = True
sys.path.insert(0, str(root / 'python'))
from sqlglot import exp, serde
from extract_corpus import normalize


def main():
    original = exp.Expression.__eq__
    cases = {}
    active = False
    def compare(left, right):
        nonlocal active
        result = original(left, right)
        if active or not isinstance(right, exp.Expr): return result
        active = True
        try:
            case = {'left': normalize(serde.dump(left)), 'right': normalize(serde.dump(right)),
                    'same_instance': left is right, 'expected': result}
            encoded = json.dumps(case, sort_keys=True, ensure_ascii=True)
            key = hashlib.sha256(encoded.encode()).hexdigest()
            cases[key] = case
        finally:
            active = False
        return result
    exp.Expression.__eq__ = compare
    try:
        suite = unittest.defaultTestLoader.loadTestsFromNames([
            'tests.test_expressions.TestExprs.test_eq',
            'tests.test_expressions.TestExprs.test_eq_case_sensitive_text',
            'tests.test_expressions.TestExprs.test_eq_on_same_instance_short_circuits',
        ])
        result = unittest.TextTestRunner(verbosity=1).run(suite)
    finally:
        exp.Expression.__eq__ = original
    if not result.wasSuccessful(): return 1
    path = root / 'src/SqlGlotSharp.Tests/expression-equality-cases.json'
    path.write_text(json.dumps([dict(id=key, **cases[key]) for key in sorted(cases)], ensure_ascii=True, indent=2) + '\n', encoding='utf-8')
    print(f'Recorded {len(cases)} equality cases from {result.testsRun} upstream tests')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
