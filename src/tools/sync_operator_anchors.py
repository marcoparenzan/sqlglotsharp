"""Refresh verified operator anchors only; incomplete branches retain zero hashes."""
import json
import re
from pathlib import Path
import port_status as p

root = Path(__file__).resolve().parents[2]
members = p.inventory(root / 'python', json.loads((root / 'src/tools/scope.json').read_text()))
path = root / 'src/SqlGlotSharp/generator.operators.cs'
entries = p.report(members, p.scan_anchors(root / 'src/SqlGlotSharp'))['members']
hashes = {x['anchor']: x['hash'] for x in entries if any(a['file'] == 'generator.operators.cs' for a in x['csharp'])}
partial = {'div_sql', 'dpipe_sql', 'is_sql'}
text = path.read_text(encoding='utf-8')
def replace(match):
    anchor = match.group(1)
    value = '0000000000' if anchor.split('.')[-1] in partial else hashes[anchor]
    return '// py: ' + anchor + ' #' + value
text, count = re.subn(r'// py: (sqlglot/generator\.py::Generator\.\w+) #[0-9a-f]{10}', replace, text)
assert count == 38, count
path.write_text(text, encoding='utf-8')
print(f'Refreshed {count - len(partial)} verified anchors; retained {len(partial)} partial anchors.')
