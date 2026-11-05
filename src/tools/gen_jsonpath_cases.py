"""Record JSONPath parse ASTs/errors for every concrete JSONPath L0 input."""
import json
import sys
from pathlib import Path
root = Path(__file__).resolve().parents[2]
sys.dont_write_bytecode = True
sys.path.insert(0, str(root / 'python'))
from sqlglot import jsonpath, serde
from extract_corpus import normalize

inputs = set()
directory = Path(r'D:\clones\corpus') / 'v30.27.0'
manifest = json.loads((directory / 'MANIFEST.json').read_text(encoding='utf-8'))
for name in sorted(name for name in manifest['counts'] if name.startswith('L0.')):
    shared = manifest.get('shared_files', {}).get(name)
    file = directory.parent / shared if shared else directory / name
    for line in file.open(encoding='utf-8'):
        case = json.loads(line)
        if case.get('tokenizer', '').endswith('::JSONPathTokenizer'):
            inputs.add((case['read'], case['sql']))
for path in ('', '$', 'field', '$..name', '$[1:2]', '$[-3]', '$[?(@.a==1)]'):
    inputs.add(('', path))
cases = []
for dialect, path in sorted(inputs):
    try:
        expected = {'ast': normalize(serde.dump(jsonpath.parse(path, dialect=dialect)))}
    except Exception as error:
        expected = {'error': {'class': type(error).__name__, 'message': str(error)}}
    cases.append({'read': dialect, 'path': path, 'expected': expected})
(root / 'src/SqlGlotSharp.Tests/jsonpath-parse-cases.json').write_text(json.dumps(cases, ensure_ascii=True, indent=2) + '\n', encoding='utf-8')
print(f'Recorded {len(cases)} JSONPath parse cases')
