"""Incremental pure-data generation for dialect/parser/generator class families."""
import enum
import importlib
import re
from pathlib import Path


def qualified(cls):
    if cls.__module__.startswith('sqlglot.expressions.'):
        return 'global::SqlGlotSharp.exp.' + cls.__qualname__
    parts = cls.__module__.split('.')[1:]
    return 'global::SqlGlotSharp.' + '.'.join(parts) + '.' + cls.__qualname__


def has_callable(value):
    if isinstance(value, type):
        return False
    if callable(value):
        return True
    if isinstance(value, dict):
        return any(has_callable(k) or has_callable(v) for k, v in value.items())
    if isinstance(value, (list, tuple, set, frozenset)):
        return any(has_callable(v) for v in value)
    return False


def type_for(values, supported):
    if any(v is None for v in values):
        present = [v for v in values if v is not None]
        return (type_for(present, supported).removesuffix('?') + '?') if present else 'object?'
    if all(isinstance(v, enum.Enum) for v in values) and len({type(v) for v in values}) == 1:
        return qualified(type(values[0]))
    if all(isinstance(v, re.Pattern) for v in values): return 'py.re.Pattern'
    if all(type(v) is bool for v in values): return 'bool'
    if all(type(v) is int for v in values): return 'long'
    if all(type(v) is str for v in values): return 'string'
    if all(isinstance(v, type) and (v in supported or v.__module__.startswith('sqlglot.expressions.')) for v in values): return 'Type'
    if all(type(v) is dict for v in values):
        keys = [k for v in values for k in v]
        items = [x for v in values for x in v.values()]
        if not keys: return 'PyDict<string, object?>'
        return 'PyDict<' + type_for(keys, supported).removesuffix('?') + ', ' + type_for(items, supported) + '>'
    if all(type(v) is tuple for v in values) and len({len(v) for v in values}) == 1 and 2 <= len(values[0]) <= 7:
        return '(' + ', '.join(type_for([v[i] for v in values], supported) for i in range(len(values[0]))) + ')'
    if all(type(v) is list for v in values) or all(type(v) is tuple for v in values):
        items = [x for v in values for x in v]
        return 'List<' + (type_for(items, supported) if items else 'object?') + '>'
    if all(type(v) in (set, frozenset) for v in values):
        items = [x for v in values for x in v]
        return 'HashSet<' + (type_for(items, supported) if items else 'object') + '>'
    if all(isinstance(v, (str, int, bool, enum.Enum, re.Pattern, dict, list, tuple, set, frozenset)) or
           isinstance(v, type) and (v in supported or v.__module__.startswith('sqlglot.expressions.')) for v in values):
        # Heterogeneous Python values remain object-valued; never stringify them.
        for v in values:
            if isinstance(v, (dict, list, tuple, set, frozenset)):
                type_for([v], supported)
        return 'object?'
    raise ValueError('Unsupported values: ' + ','.join(sorted({type(v).__name__ for v in values})))


def generic_args(type_name):
    inner = type_name[1:-1] if type_name.startswith('(') else type_name[type_name.index('<') + 1:type_name.rindex('>')]
    depth, start, result = 0, 0, []
    for i, c in enumerate(inner):
        if c in '<(': depth += 1
        elif c in '>)': depth -= 1
        elif c == ',' and depth == 0:
            result.append(inner[start:i].strip())
            start = i + 1
    result.append(inner[start:].strip())
    return result


def value_for(value, literal, type_name=None, supported=()):
    if value is None: return 'null'
    if isinstance(value, re.Pattern): return 'py.re.compile(' + literal(value.pattern) + ', ' + str(value.flags) + ')'
    if type_name is not None and isinstance(value, (dict, list, tuple, set, frozenset)):
        type_name = type_name.removesuffix('?')
        if type_name in ('object', 'object?'):
            type_name = 'PyDict<object, object?>' if isinstance(value, dict) else type_for([value], supported)
        parts = generic_args(type_name)
        if type_name.startswith('('):
            return '(' + ', '.join(value_for(v, literal, t, supported) for v, t in zip(value, parts)) + ')'
        if isinstance(value, dict):
            items = ['[' + value_for(k, literal, parts[0], supported) + '] = ' + value_for(v, literal, parts[1], supported) for k, v in value.items()]
        else:
            items = [value_for(v, literal, parts[0], supported) for v in value]
            if isinstance(value, (set, frozenset)): items.sort()
        return 'new ' + type_name.removesuffix('?') + '() { ' + ', '.join(items) + ' }'
    if isinstance(value, enum.Enum): return qualified(type(value)) + '.' + value.name
    if isinstance(value, type): return 'typeof(' + qualified(value) + ')'
    if isinstance(value, bool): return str(value).lower()
    if isinstance(value, int) and type_name is not None: return str(value) + 'L'
    if isinstance(value, (str, int)): return literal(value)
    if isinstance(value, dict):
        return 'new() { ' + ', '.join('[' + value_for(k, literal) + '] = ' + value_for(v, literal) for k, v in value.items()) + ' }'
    if isinstance(value, (set, frozenset)):
        return 'new() { ' + ', '.join(sorted(value_for(v, literal) for v in value)) + ' }'
    if isinstance(value, (list, tuple)):
        return 'new() { ' + ', '.join(value_for(v, literal) for v in value) + ' }'
    raise ValueError('Unsupported value')


def generate(upstream, scope, output, hashes, header, identifier, literal, write_generated):
    from sqlglot import parser, generator, tokens
    from sqlglot.dialects.dialect import Dialect
    from sqlglot.dialects.postgres import Postgres
    from sqlglot.dialects.tsql import TSQL
    from sqlglot.dialects.fabric import Fabric
    from sqlglot.jsonpath import JSONPathTokenizer
    classes = [Dialect, Postgres, TSQL, Fabric, parser.Parser, generator.Generator]
    classes += [d.Parser for d in (Postgres, TSQL, Fabric)] + [d.Generator for d in (Postgres, TSQL, Fabric)]
    classes += [tokens._TokenizerBase, tokens.Tokenizer, Postgres.Tokenizer, TSQL.Tokenizer, Fabric.Tokenizer, JSONPathTokenizer]
    supported = set(classes)
    values = {cls: {name: getattr(cls, name) for name in dir(cls) if name.lstrip('_').isupper() and not name.startswith('__')} for cls in classes}
    # Include runtime class links whose Python names are not uppercase.
    for cls in (Dialect, Postgres, TSQL, Fabric):
        for name in ('Parser', 'Generator'):
            if hasattr(cls, name):
                values[cls][name] = getattr(cls, name)
    all_names = sorted({name for attrs in values.values() for name in attrs})
    types, skipped, handwritten = {}, [], []
    for name in all_names:
        candidates = [attrs[name] for attrs in values.values() if name in attrs]
        if any(has_callable(v) for v in candidates):
            for cls in classes:
                if name in values[cls] and has_callable(values[cls][name]):
                    handwritten.append(cls.__module__.replace('.', '/') + '.py::' + cls.__qualname__ + '.' + name)
            continue
        try:
            types[name] = 'PyDict<object, object?>' if name.endswith('_TRIE') and all(isinstance(v, dict) for v in candidates) else type_for(candidates, supported)
        except ValueError as error: skipped.append(name + ': ' + str(error))
    counts = 0
    file_groups = {}
    for cls in classes:
        path = scope['details'][cls.__module__]['path']
        short = path.removeprefix('sqlglot/')
        parts = short.split('/')
        namespace = 'SqlGlotSharp' + ('.' + '.'.join(parts[:-1]) if len(parts) > 1 else '')
        container = Path(parts[-1]).stem
        bases = [base for base in cls.__bases__ if base in supported]
        base = bases[0] if bases else None
        anchor = path + '::' + cls.__qualname__
        outers = cls.__qualname__.split('.')[:-1]
        lines = []
        for outer in outers:
            lines.extend([f'    public partial class {identifier(outer)}', '    {'])
        hides_nested = bool(outers and any(cls.__name__ in vars(parent) for outer_cls in classes if outer_cls.__name__ == outers[-1] for parent in outer_cls.__bases__))
        lines.extend([f'    // py: {anchor} #{hashes[anchor]}',
                 f'    public {"new " if hides_nested else ""}partial class {cls.__name__}' + (' : ' + qualified(base) if base else ''), '    {'])
        for name in sorted(values[cls]):
            if name not in types: continue
            value = values[cls][name]
            inherited = base and name in values[base] and name in types
            anchor = path + '::' + cls.__qualname__ + '.' + name
            if anchor in hashes:
                lines.append(f'        // py: {anchor} #{hashes[anchor]}')
            lines.append(f'        public static {"new " if inherited else ""}readonly {types[name]} {identifier(name + "_")} = {value_for(value, literal, types[name], supported)};')
            lines.append(f'        public {"override" if inherited else "virtual"} {types[name]} {identifier(name)} => {identifier(name + "_")};')
            counts += 1
        lines.append('    }')
        lines.extend('    }' for _ in outers)
        file_groups.setdefault((short, namespace, container), []).append('\n'.join(lines))
    for (short, namespace, container), declarations in file_groups.items():
        destination = output / Path(short).with_suffix('.data.g.cs')
        destination.parent.mkdir(parents=True, exist_ok=True)
        content = header + f'namespace {namespace};\npublic static partial class {identifier(container)}\n{{\n'
        content += '\n\n'.join(declarations) + '\n}\n'
        write_generated(destination, content, encoding='utf-8')
    tools = Path(__file__).parent
    (tools / 'handwritten_attrs.txt').write_text('\n'.join(sorted(set(handwritten))) + '\n', encoding='utf-8')
    # This is a tooling inventory, not a test corpus or scratch report.
    import json
    (tools / 'data_inventory.json').write_text(json.dumps({'class_count': len(classes), 'property_count': counts,
        'unsupported_data': skipped, 'handwritten_attribute_count': len(set(handwritten)),
        'coverage': '18 scoped dialect/parser/generator/tokenizer classes, including JSONPath'}, indent=2) + '\n', encoding='utf-8')
    print(f'Generated {counts} runtime data properties across {len(classes)} class shells; {len(skipped)} unsupported attribute families, {len(set(handwritten))} callable-bearing attributes recorded.')
    # Supported dialect registry deliberately excludes unported dialects.
    entries = [('', Dialect), ('postgres', Postgres), ('tsql', TSQL), ('fabric', Fabric)]
    content = header + 'namespace SqlGlotSharp.dialects;\npublic static partial class dialect\n{\n'
    content += '    public static readonly PyDict<string, Type> DIALECT_REGISTRY__helper = new()\n    {\n'
    content += '\n'.join('        [' + literal(name) + '] = typeof(' + qualified(cls) + '),' for name, cls in entries)
    content += '\n    };\n}\n'
    write_generated(output / 'dialects' / 'dialect.registry.g.cs', content, encoding='utf-8')
