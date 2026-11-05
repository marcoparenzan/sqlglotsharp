"""Inventory direct expression attributes and generate pure class data."""
import ast
import enum
import importlib
import json
from pathlib import Path


def generate(upstream, scope, output, hashes, header, identifier, literal, write_generated, models):
    import gen_data
    metadata = set(models[0]['metadata'])
    trait_names = {m['name'] for m in models if m['trait']}
    classes = {m['name']: getattr(importlib.import_module(m['path'][:-3].replace('/', '.')), m['name']) for m in models}
    supported = set(classes.values())
    rows, generated = [], {}
    for model in models:
        cls = classes[model['name']]
        tree = ast.parse((upstream / model['path']).read_text(encoding='utf-8'))
        node = next(n for n in tree.body if isinstance(n, ast.ClassDef) and n.name == cls.__name__)
        for member in node.body:
            if not isinstance(member, (ast.Assign, ast.AnnAssign)) or member.value is None:
                continue
            targets = member.targets if isinstance(member, ast.Assign) else [member.target]
            for target in targets:
                if not isinstance(target, ast.Name): continue
                name = target.id
                anchor = model['path'] + '::' + cls.__name__ + '.' + name
                value = getattr(cls, name)
                row = {'anchor': anchor, 'name': name, 'class': cls.__name__}
                if name in metadata:
                    row['status'] = 'metadata'
                elif isinstance(value, type) and issubclass(value, enum.Enum):
                    row['status'] = 'enum_alias'
                elif name.startswith('__'):
                    row['status'] = 'structural'
                elif gen_data.has_callable(value):
                    row['status'] = 'handwritten'
                elif cls.__name__ == 'TimeUnit' and name in ('VAR_LIKE', 'UNABBREVIATED_UNIT_NAME'):
                    row['status'] = 'existing_trait_data'
                elif cls.__name__ in trait_names:
                    row['status'] = 'unsupported_trait_data'
                else:
                    try:
                        if type(value).__module__ == 'sqlglot.expressions.core' and type(value).__name__ == 'Literal':
                            # These upstream constants are primitive Literal data, not algorithms.
                            type_name = gen_data.qualified(type(value))
                            args = gen_data.value_for(value.args, literal, 'PyDict<string, object?>', supported)
                            expression = 'new ' + type_name + '(' + args + ')'
                        else:
                            type_name = gen_data.type_for([value], supported)
                            expression = gen_data.value_for(value, literal, type_name, supported)
                        row['status'] = 'generated'
                        generated[(cls, name)] = (type_name, expression, anchor, model['path'])
                    except ValueError as error:
                        row['status'] = 'unsupported'
                        row['reason'] = str(error)
                rows.append(row)
    files = {}
    for (cls, name), (type_name, expression, anchor, path) in generated.items():
        parents = [base for base in cls.__mro__[1:] if (base, name) in generated]
        if parents and generated[(parents[0], name)][0] != type_name:
            raise ValueError(f'Incompatible expression attribute override: {anchor}')
        inherited = bool(parents)
        lines = [f'    public partial class {identifier(cls.__name__)}', '    {',
            f'        // py: {anchor} #{hashes[anchor]}',
            f'        public static {"new " if inherited else ""}readonly {type_name} {identifier(name + "_")} = {expression};',
            f'        public {"override" if inherited else "virtual"} {type_name} {identifier(name)} => {identifier(name + "_")};', '    }']
        files.setdefault(path, []).append('\n'.join(lines))
    for path, declarations in files.items():
        destination = output / Path(path.removeprefix('sqlglot/')).with_suffix('.data.g.cs')
        content = header + 'namespace SqlGlotSharp;\npublic static partial class exp\n{\n' + '\n\n'.join(declarations) + '\n}\n'
        write_generated(destination, content)
    tools = Path(__file__).parent
    counts = {status: sum(r['status'] == status for r in rows) for status in sorted({r['status'] for r in rows})}
    (tools / 'expression_data_inventory.json').write_text(json.dumps({'counts': counts, 'attributes': rows}, indent=2) + '\n', encoding='utf-8')
    handwritten_path = tools / 'handwritten_attrs.txt'
    handwritten = set(handwritten_path.read_text(encoding='utf-8').splitlines())
    handwritten.update(r['anchor'] for r in rows if r['status'] == 'handwritten')
    handwritten_path.write_text('\n'.join(sorted(handwritten)) + '\n', encoding='utf-8')
    print('Expression attribute inventory: ' + ', '.join(f'{k}={v}' for k, v in counts.items()))
