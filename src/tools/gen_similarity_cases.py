"""Small deterministic Python difflib oracle for the runtime shim."""
import difflib
import json
import random
from pathlib import Path

rng = random.Random(17)
pairs = [('tide', 'diet'), ('diet', 'tide'), ('', ''), ('😀abc', '😀axc'),
         ('a' * 250, 'b' + 'a' * 250), ('b' + 'a' * 250, 'a' * 250),
         ('ab' * 150, 'ba' * 150)]
for _ in range(100):
    pairs.append(tuple(''.join(rng.choices('abc😀', k=rng.randrange(25))) for _ in range(2)))
cases = {'ratios': [{'first': a, 'second': b, 'expected': difflib.SequenceMatcher(None, a, b).ratio()} for a,b in pairs],
         'closest': []}
for word, candidates in [('postgre', ['postgres', 'tsql']), ('cat', ['bat', 'hat']),
                         ('zzz', ['postgres', 'tsql']), ('', ['', 'a']), ('select', ['select', 'selector']),
                         ('😀a', ['😀b', '😀c'])]:
    result = difflib.get_close_matches(word, candidates, n=1)
    cases['closest'].append({'word': word, 'candidates': candidates, 'expected': result[0] if result else None})
root = Path(__file__).resolve().parents[2]
(root / 'src/SqlGlotSharp.Tests/similarity-cases.json').write_text(json.dumps(cases, ensure_ascii=True, indent=2) + '\n', encoding='utf-8')
