"""Record actual SQLGlot boundary outputs while running upstream unittest tests."""
from __future__ import annotations

import argparse
import contextvars
import contextlib
import enum
import functools
import hashlib
import inspect
import json
import logging
import os
import platform
import sys
import unittest
import weakref
import subprocess
from collections import Counter
from pathlib import Path

ALLOWED = {'', 'postgres', 'tsql', 'fabric', 'timescale'}
VERSION = '6'


@contextlib.contextmanager
def capture_parser_logger(messages):
    """Observe the actual module logger, including mock calls, without replacing its behaviour.

    Context describes method-call observation (mock) vs logging emission (enabled/disabled).
    It is part of case identity, not inferred from an empty warnings list or source test ID.
    Only the two methods currently used by the upstream parser are instrumented.
    """
    from sqlglot import parser
    from unittest.mock import Mock
    logger = parser.logger
    context = {}
    patches = []
    try:
        for name, level in [('warning', logging.WARNING), ('error', logging.ERROR)]:
            original = getattr(logger, name)
            if isinstance(original, Mock):
                mode = 'mock'
            elif isinstance(logger, logging.Logger):
                mode = 'enabled' if logger.isEnabledFor(level) else 'disabled'
            else:
                raise TypeError('Unsupported parser logger: ' + type(logger).__name__)
            context[name] = mode
            had_local = name in vars(logger)
            local = vars(logger).get(name)
            def wrapped(message, *args, _original=original, _mode=mode, _level=level, **kwargs):
                if _mode != 'disabled':
                    messages.append({'level': logging.getLevelName(_level),
                                     'message': str(message) % args if args else str(message)})
                return _original(message, *args, **kwargs)
            setattr(logger, name, wrapped)
            patches.append((name, had_local, local))
        yield {'parser': context}
    finally:
        for name, had_local, local in reversed(patches):
            if had_local: setattr(logger, name, local)
            else: object.__delattr__(logger, name)  # Mock.__delattr__ would mark its child deleted.


def utf16(text):
    return len(text.encode('utf-16-le', errors='surrogatepass')) // 2


def token_payload(token, sql):
    end = token.end
    line_start = sql.rfind('\n', 0, end + 1) + 1
    return {'token_type': token.token_type.name, 'text': token.text, 'line': token.line,
            'col': utf16(sql[line_start:end + 1]), 'start': utf16(sql[:token.start]),
            'end': utf16(sql[:end + 1]) - 1, 'comments': token.comments}


def normalize(value):
    if value is None or type(value) in (str, int, float, bool): return value
    if isinstance(value, enum.Enum):
        return {'$enum': value.__class__.__module__ + '::' + value.__class__.__qualname__, 'value': value.value}
    if isinstance(value, type): return {'$type': value.__module__ + '::' + value.__qualname__}
    if isinstance(value, (list, tuple)): return [normalize(v) for v in value]
    if isinstance(value, dict) and all(isinstance(k, str) for k in value):
        return {k: normalize(v) for k, v in value.items()}
    raise TypeError('Unsupported corpus value: ' + type(value).__name__)


def normalize_ast(payload, sql):
    """Convert only positional metadata, not user strings or semantic arg values."""
    result = normalize(payload)
    if not sql: return result  # A standalone AST has no source text to map offsets against.
    lines = sql.splitlines(keepends=True)
    for item in result:
        meta = item.get('m')
        if isinstance(meta, dict):
            if isinstance(meta.get('col'), int) and isinstance(meta.get('line'), int):
                line = meta['line'] - 1
                if 0 <= line < len(lines): meta['col'] = utf16(lines[line][:meta['col']])
            if isinstance(meta.get('start'), int): meta['start'] = utf16(sql[:meta['start']])
            if isinstance(meta.get('end'), int): meta['end'] = utf16(sql[:meta['end'] + 1]) - 1
        if isinstance(item.get('t'), list): item['t'] = normalize_ast(item['t'], sql)
    return result


class Recorder:
    def __init__(self):
        self.depth = contextvars.ContextVar('record_depth', default={})
        self.test_id = contextvars.ContextVar('test_id', default='outside-test')
        self.sql_context = contextvars.ContextVar('sql_context', default='')
        self.cases = {}
        self.skipped = Counter()
        self.patches = []
        self.helper_context = contextvars.ContextVar('validation_helper', default=None)
        self.origins = {}

    def remember_origin(self, node, sql, read, options):
        key = id(node)
        try:
            reference = weakref.ref(node, lambda reference: self.origins.pop(key, None))
            self.origins[key] = (reference, sql, read, normalize(options))
        except TypeError:
            self.skipped['AST origin unavailable'] += 1

    def store(self, case):
        encoded = json.dumps(case, sort_keys=True, ensure_ascii=True, allow_nan=False)
        key = hashlib.sha256(encoded.encode()).hexdigest()
        entry = self.cases.setdefault(key, {**case, 'id': key, 'sources': []})
        source = self.test_id.get()
        if source not in entry['sources']: entry['sources'].append(source)

    def capture_helper_generation(self, case, bound):
        helper = self.helper_context.get()
        if not helper: return
        expression = bound.get('self') if case['boundary'] == 'sql' else bound.get('expression')
        origin = self.origins.get(id(expression))
        if not origin or origin[0]() is not expression: return
        _, sql, read, parse_options = origin
        if read not in ALLOWED or case['write'] not in ALLOWED: return
        self.store({'boundary': helper, 'pipeline': 'parse_one_then_sql', 'level': 'L3',
                    'sql': sql, 'read': read, 'write': case['write'],
                    'options': {'parse': parse_options, 'generate': case['options']},
                    'runtime_context': case['runtime_context'],
                    'expected': case['expected'], 'warnings': case['warnings']})

    def patch_helper(self, owner, name):
        original = getattr(owner, name)
        @functools.wraps(original)
        def wrapped(*args, **kwargs):
            token = self.helper_context.set(name)
            try: return original(*args, **kwargs)
            finally: self.helper_context.reset(token)
        setattr(owner, name, wrapped)
        self.patches.append((owner, name, original))

    def dialect(self, value):
        from sqlglot.dialects.dialect import Dialect
        if value is None: return ''
        if isinstance(value, str): return value.lower()
        cls = value if isinstance(value, type) else type(value)
        if cls is Dialect: return ''
        return cls.__name__.lower()

    def patch(self, owner, name, level, describe, encode):
        original = getattr(owner, name)
        signature = inspect.signature(original)

        @functools.wraps(original)
        def wrapped(*args, **kwargs):
            depths = self.depth.get()
            if depths.get(level, 0): return original(*args, **kwargs)
            try:
                bound = signature.bind(*args, **kwargs).arguments
            except TypeError:
                # Preserve upstream's own argument-error message and behavior.
                return original(*args, **kwargs)
            try:
                case = describe(bound)
                dialects = {case.get('read', ''), case.get('write', '')}
                if not dialects <= ALLOWED: return original(*args, **kwargs)
                case = normalize(case)
                json.dumps(case, allow_nan=False)
            except Exception as error:
                self.skipped[type(error).__name__ + ': ' + str(error)] += 1
                return original(*args, **kwargs)
            token = self.depth.set({**depths, level: 1})
            sql_token = self.sql_context.set(case.get('sql', self.sql_context.get()))
            messages = []
            class Capture(logging.Handler):
                def emit(self, record):
                    if record.levelno >= logging.WARNING:
                        messages.append({'level': record.levelname, 'message': record.getMessage()})
            logger = logging.getLogger('sqlglot')
            capture = Capture()
            observation = contextlib.ExitStack()
            capture_ready = True
            try:
                try:
                    if level == 'L1' and case.get('boundary') in ('parse', 'parse_one'):
                        case['logger_context'] = observation.enter_context(capture_parser_logger(messages))
                    else:
                        logger.addHandler(capture)
                        observation.callback(logger.removeHandler, capture)
                except Exception as error:
                    self.skipped['logger context: ' + str(error)] += 1
                    capture_ready = False
                result = original(*args, **kwargs)
                try: case['expected'] = {'result': normalize(encode(result, bound, self.sql_context.get()))}
                except Exception as error:
                    self.skipped['output: ' + str(error)] += 1
                    return result
                return result
            except Exception as error:
                case['expected'] = {'error': {'class': type(error).__name__, 'module': type(error).__module__, 'message': str(error)}}
                raise
            finally:
                observation.close()
                self.sql_context.reset(sql_token)
                self.depth.reset(token)
                if capture_ready and 'expected' in case:
                    case.update(level=level, warnings=messages)
                    try:
                        self.store(case)
                        if level == 'L2': self.capture_helper_generation(case, bound)
                    except (ValueError, TypeError) as error: self.skipped['serialize: ' + str(error)] += 1
        setattr(owner, name, wrapped)
        self.patches.append((owner, name, original))

    def install(self):
        import sqlglot
        from sqlglot import exp, serde, tokens, generator
        def pretty_context():
            # Generator.__init__ reads this global only when pretty is omitted/None.
            # Record the environment independently of options and observed output.
            if type(sqlglot.pretty) is not bool:
                raise ValueError('unsupported sqlglot.pretty runtime context')
            return {'sqlglot.pretty': sqlglot.pretty}
        def parse_case(b):
            opts = dict(b.get('opts', {}))
            if b.get('into') is not None: opts['into'] = b['into']
            return {'boundary': 'parse_one' if 'into' in b else 'parse', 'sql': b['sql'],
                    'read': self.dialect(b.get('read') or b.get('dialect')), 'options': opts}
        def parse_output(result, b, sql):
            nodes = result if isinstance(result, list) else [result]
            for node in nodes:
                if node is not None:
                    options = dict(b.get('opts', {}))
                    if b.get('into') is not None: options['into'] = b['into']
                    self.remember_origin(node, sql, self.dialect(b.get('read') or b.get('dialect')), options)
            return [normalize_ast(serde.dump(node), sql) if node is not None else None for node in nodes]
        self.patch(sqlglot, 'parse', 'L1', parse_case, parse_output)
        self.patch(sqlglot, 'parse_one', 'L1', lambda b: {**parse_case(b), 'boundary': 'parse_one'}, parse_output)
        def tokenize_case(b):
            cls = type(b['self'])
            read = self.dialect(b['self'].dialect)
            # An explicit out-of-scope dialect tokenizer may have been constructed with dialect=None.
            # Its owning module still identifies an involved dialect (e.g. BigQuery in test_jinja).
            if cls.__module__.startswith('sqlglot.dialects.'):
                owner = cls.__module__.rsplit('.', 1)[-1]
                if owner != 'dialect' and owner not in ALLOWED:
                    read = owner
            return {'boundary': 'tokenize', 'sql': b['sql'], 'read': read,
                    'tokenizer': cls.__module__ + '::' + cls.__qualname__, 'options': {}}
        self.patch(tokens.Tokenizer, 'tokenize', 'L0', tokenize_case,
            lambda result, b, sql: [token_payload(t, sql) for t in result])
        self.patch(exp.Expression, 'sql', 'L2',
            lambda b: {'boundary': 'sql', 'runtime_context': pretty_context(), 'ast': normalize_ast(serde.dump(b['self']), self.sql_context.get()),
                       'write': self.dialect(b.get('dialect')), 'options': {**b.get('opts', {}), **({'copy': b['copy']} if 'copy' in b else {})}},
            lambda result, b, sql: result)
        # Direct Generator.generate calls use the instance's effective settings.
        settings = ('pretty', 'identify', 'normalize', 'pad', 'normalize_functions', 'unsupported_level',
                    'max_unsupported', 'leading_comma', 'max_text_width', 'comments')
        self.patch(generator.Generator, 'generate', 'L2',
            lambda b: {'boundary': 'generate', 'runtime_context': pretty_context(), 'ast': normalize_ast(serde.dump(b['expression']), self.sql_context.get()),
                       'write': self.dialect(b['self'].dialect), 'options':
                       {**{k: getattr(b['self'], k) for k in settings}, 'indent': b['self']._indent,
                        **({'copy': b['copy']} if 'copy' in b else {})}}, lambda result, b, sql: result)
        def transpile_case(b):
            read = self.dialect(b.get('read'))
            write = self.dialect(b.get('write')) if b.get('write') is not None else read if b.get('identity', True) else ''
            opts = dict(b.get('opts', {}))
            for key in ('identity', 'error_level'):
                if key in b: opts[key] = b[key]
            return {'boundary': 'transpile', 'runtime_context': pretty_context(),
                    'sql': b['sql'], 'read': read, 'write': write, 'options': opts}
        self.patch(sqlglot, 'transpile', 'L3', transpile_case, lambda result, b, sql: normalize(result))
        from tests.dialects import test_dialect as validators
        Validator = validators.Validator
        # The helper module may already be loaded from an earlier recorder session.
        original_alias = inspect.unwrap(validators.parse_one)
        validators.parse_one = sqlglot.parse_one
        self.patches.append((validators, 'parse_one', original_alias))
        for name in ('validate_identity', 'validate_all', 'validate_transpile'):
            self.patch_helper(Validator, name)

    def uninstall(self):
        for owner, name, original in reversed(self.patches): setattr(owner, name, original)
        self.patches.clear()

    def write(self, output, metadata):
        output.mkdir(parents=True, exist_ok=True)
        groups = {}
        for case in self.cases.values():
            dialect = case.get('write', case.get('read', '')) or 'base'
            groups.setdefault(case['level'] + '.' + dialect + '.jsonl', []).append(case)
        counts = {}
        for filename, cases in sorted(groups.items()):
            cases.sort(key=lambda c: c['id'])
            for case in cases: case['sources'].sort()
            (output / filename).write_text(''.join(json.dumps(case, sort_keys=True, ensure_ascii=True) + '\n' for case in cases), encoding='utf-8')
            counts[filename] = len(cases)
        (output / 'MANIFEST.json').write_text(json.dumps({**metadata, 'recorder_version': VERSION,
            'python_version': platform.python_version(), 'counts': counts, 'skipped_capture': dict(self.skipped),
            'reentrancy': 'per-level; internal calls within the same level suppressed'}, indent=2) + '\n', encoding='utf-8')
        print(f'Recorded {sum(counts.values())} cases in {len(counts)} files.')


def main():
    repo = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--upstream', type=Path, default=repo / 'python')
    parser.add_argument('--output', type=Path, default=Path(r'D:\clones\corpus') / 'v30.27.0')
    parser.add_argument('--pattern', default='test*.py')
    parser.add_argument('--test', action='append', default=[], help='Fully qualified unittest name; repeat for small batches')
    parser.add_argument('--log', type=Path, default=Path(__file__).resolve().parents[2] / 'artifacts' / 'logs' / 'oracle-suite.log')
    args = parser.parse_args()
    upstream = args.upstream.resolve()
    output = args.output.resolve()
    log_path = args.log.resolve()
    if log_path.is_relative_to(upstream): parser.error('Log must not be inside upstream')
    if output.is_relative_to(upstream):
        parser.error('Corpus output must not be inside the read-only upstream directory')
    sys.dont_write_bytecode = True
    sys.path.insert(0, str(upstream))
    os.chdir(upstream)
    baseline = json.loads((repo / 'UPSTREAM.json').read_text(encoding='utf-8'))
    commit_info = {'commit': baseline['commit'], 'commit_verified': False}
    try:
        git = subprocess.run(['git', '-C', str(upstream), 'rev-parse', 'HEAD'], capture_output=True, text=True, timeout=10)
        if git.returncode == 0:
            commit = git.stdout.strip()
            commit_info = {'commit': commit, 'commit_verified': True,
                           'baseline_matches': commit.startswith(baseline['commit'])}
    except (OSError, subprocess.TimeoutExpired): pass
    recorder = Recorder()
    recorder.install()
    class Result(unittest.TextTestResult):
        def startTest(self, test):
            self.source_token = recorder.test_id.set(test.id())
            super().startTest(test)
        def stopTest(self, test):
            super().stopTest(test)
            recorder.test_id.reset(self.source_token)
    try:
        log_path.parent.mkdir(parents=True, exist_ok=True)
        with log_path.open('w', encoding='utf-8') as log, contextlib.redirect_stdout(log), contextlib.redirect_stderr(log):
            suite = (unittest.defaultTestLoader.loadTestsFromNames(args.test) if args.test else
                     unittest.defaultTestLoader.discover(str(upstream / 'tests'), pattern=args.pattern, top_level_dir=str(upstream)))
            # Exclude calls executed during module import, without a source test ID.
            recorder.cases.clear()
            recorder.skipped.clear()
            result = unittest.TextTestRunner(stream=log, verbosity=1, resultclass=Result).run(suite)
    finally: recorder.uninstall()
    print(f'Upstream tests: {result.testsRun}; failures={len(result.failures)}; errors={len(result.errors)}; log={log_path}')
    recorder.write(output, {'tag': baseline['tag'], **commit_info,
        'pattern': args.pattern, 'selected_tests': args.test,
        'full_suite': not args.test and args.pattern == 'test*.py', 'tests_run': result.testsRun,
        'failures': len(result.failures), 'errors': len(result.errors), 'skipped_tests': len(result.skipped),
        'suite_passed': result.wasSuccessful()})
    return 0 if result.wasSuccessful() else 1


if __name__ == '__main__':
    raise SystemExit(main())
