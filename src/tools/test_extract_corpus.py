import json
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace

import extract_corpus as corpus


class RecorderTests(unittest.TestCase):
    def test_parser_logger_capture_preserves_methods_calls_and_context(self):
        import logging
        import sys
        from unittest.mock import Mock, patch
        sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'python'))
        from sqlglot import parser
        actual = parser.logger
        original_level, original_disabled = actual.level, actual.disabled
        try:
            actual.disabled = False
            actual.setLevel(logging.ERROR)
            for mocked in [False, True]:
                with patch.object(parser, 'logger', Mock() if mocked else actual):
                    logger = parser.logger
                    original_warning, original_error = logger.warning, logger.error
                    messages = []
                    with corpus.capture_parser_logger(messages) as context:
                        logger.warning('warning %s', 'formatted')
                        logger.error('error %s', 'formatted')
                    self.assertEqual(logger.warning, original_warning)
                    self.assertEqual(logger.error, original_error)
                    self.assertEqual(context['parser'], dict(warning='mock' if mocked else 'disabled',
                                                             error='mock' if mocked else 'enabled'))
                    self.assertEqual([m['message'] for m in messages],
                                     ['warning formatted', 'error formatted'] if mocked else ['error formatted'])
                    if mocked:
                        logger.warning.assert_called_once_with('warning %s', 'formatted')
                        logger.error.assert_called_once_with('error %s', 'formatted')
            # Global logging.disable and logger.disabled are both effective provenance,
            # while a method-level mock still observes its call in that environment.
            previous_disable = logging.root.manager.disable
            try:
                logging.disable(logging.CRITICAL)
                messages = []
                with corpus.capture_parser_logger(messages) as context:
                    actual.error('globally disabled')
                self.assertEqual(context['parser'], dict(warning='disabled', error='disabled'))
                self.assertEqual(messages, [])
                with patch.object(actual, 'error') as method:
                    with corpus.capture_parser_logger(messages) as context:
                        actual.error('mocked method')
                    self.assertIs(actual.error, method)
                    method.assert_called_once_with('mocked method')
                    self.assertEqual(context['parser'], dict(warning='disabled', error='mock'))
                    self.assertEqual(messages, [dict(level='ERROR', message='mocked method')])
            finally: logging.disable(previous_disable)
            original_warning = actual.warning
            with self.assertRaisesRegex(ValueError, 'caller'):
                with corpus.capture_parser_logger([]): raise ValueError('caller')
            self.assertEqual(actual.warning, original_warning)
        finally:
            actual.setLevel(original_level)
            actual.disabled = original_disabled

    def test_real_three_mock_paths_match_uninstrumented_oracle_and_remain_distinct(self):
        import io
        import logging
        import sys
        sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'python'))
        import sqlglot
        from sqlglot import parser, serde
        from sqlglot.errors import ErrorLevel
        names = ['tests.test_parser.TestParser.test_comment_error_n',
                 'tests.test_parser.TestParser.test_comment_error_r',
                 'tests.test_transpile.TestTranspile.test_error_level']
        # Run the actual decorators/assertions: instrumentation must not interfere with Mock calls.
        recorder = corpus.Recorder()
        recorder.install()
        try:
            suite = unittest.defaultTestLoader.loadTestsFromNames(names)
            recorder.cases.clear()  # Imports are not source-test executions (same as the CLI).
            result = unittest.TextTestRunner(stream=io.StringIO()).run(suite)
            self.assertTrue(result.wasSuccessful(), str(result.failures) + str(result.errors))
        finally: recorder.uninstall()
        cases = [c for c in recorder.cases.values() if c['level'] == 'L1' and c['warnings']]
        self.assertEqual(len(cases), 3)
        self.assertEqual({c['sql'] for c in cases}, {'SUM\n(\n-- test\n)', 'SUM(-- test\r)', 'x + 1. ('})
        for case in cases:
            self.assertEqual(case['logger_context']['parser'], dict(warning='mock', error='mock'))
            with self.assertLogs(parser.logger, level=logging.WARNING) as logs:
                nodes = getattr(sqlglot, case['boundary'])(case['sql'], error_level=ErrorLevel.WARN)
            if not isinstance(nodes, list): nodes = [nodes]
            self.assertEqual(case['expected']['result'],
                             [corpus.normalize_ast(serde.dump(n), case['sql']) if n else None for n in nodes])
            self.assertEqual(case['warnings'], [dict(level=r.levelname, message=r.getMessage()) for r in logs.records])
        # Identical SQL/options in a different environment cannot deduplicate across provenance.
        recorder = corpus.Recorder()
        recorder.install()
        try:
            with self.assertLogs(parser.logger, level=logging.WARNING):
                sqlglot.parse_one('SUM()', error_level=ErrorLevel.WARN)
            with unittest.mock.patch.object(parser, 'logger') as mock:
                sqlglot.parse_one('SUM()', error_level=ErrorLevel.WARN)
                mock.error.assert_called_once()
            self.assertEqual(len([c for c in recorder.cases.values() if c['level'] == 'L1']), 2)
        finally: recorder.uninstall()

    def test_pretty_global_and_overrides_match_live_uninstrumented_boundaries(self):
        import sys
        from unittest.mock import patch
        sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'python'))
        import sqlglot
        from sqlglot.generator import Generator
        sql = 'SELECT col FROM x'
        single, pretty = sql, 'SELECT\n  col\nFROM x'
        oracle = []
        for global_pretty in (False, True):
            with patch.object(sqlglot, 'pretty', global_pretty):
                for opts in ({}, {'pretty': None}, {'pretty': False}, {'pretty': True}):
                    effective = opts.get('pretty')
                    if effective is None: effective = global_pretty
                    expected = pretty if effective else single
                    for boundary in ('sql', 'generate', 'transpile'):
                        expression = sqlglot.parse_one(sql)
                        if boundary == 'sql': result = expression.sql(**opts)
                        elif boundary == 'generate': result = Generator(**opts).generate(expression)
                        else: result = sqlglot.transpile(sql, **opts)
                        self.assertEqual(result, [expected] if boundary == 'transpile' else expected)
                        oracle.append((global_pretty, opts, boundary, result))
        recorder = corpus.Recorder()
        recorder.install()
        try:
            for global_pretty, opts, boundary, expected in oracle:
                with patch.object(sqlglot, 'pretty', global_pretty):
                    expression = sqlglot.parse_one(sql)
                    recorder.test_id.set(f'{global_pretty}/{opts}/{boundary}')
                    if boundary == 'sql': result = expression.sql(**opts)
                    elif boundary == 'generate': result = Generator(**opts).generate(expression)
                    else: result = sqlglot.transpile(sql, **opts)
                    self.assertEqual(result, expected)
                    cases = [c for c in recorder.cases.values()
                             if recorder.test_id.get() in c['sources'] and c['boundary'] == boundary]
                    self.assertEqual(len(cases), 1)
                    case = cases[0]
                    self.assertEqual(case['runtime_context'], {'sqlglot.pretty': global_pretty})
                    self.assertEqual(case['expected']['result'], expected)
                    if boundary != 'generate': self.assertEqual(case['options'], opts)
                    else: self.assertEqual(case['options']['pretty'], opts.get('pretty') if opts.get('pretty') is not None else global_pretty)
            # A generator's effective setting belongs to its construction, not the call-time global.
            with patch.object(sqlglot, 'pretty', True): generator = Generator()
            with patch.object(sqlglot, 'pretty', False):
                recorder.test_id.set('preconstructed')
                self.assertEqual(generator.generate(sqlglot.parse_one(sql)), pretty)
            case = next(c for c in recorder.cases.values() if 'preconstructed' in c['sources'] and c['boundary'] == 'generate')
            self.assertEqual(case['options']['pretty'], True)
            self.assertEqual(case['runtime_context'], {'sqlglot.pretty': False})
        finally: recorder.uninstall()

    def test_upstream_pretty_override_source_retains_distinct_global_provenance(self):
        import io
        import sys
        sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'python'))
        name = 'tests.test_parser.TestParser.test_pretty_config_override'
        recorder = corpus.Recorder()
        recorder.install()
        try:
            suite = unittest.defaultTestLoader.loadTestsFromNames([name])
            recorder.cases.clear()
            result = unittest.TextTestRunner(stream=io.StringIO()).run(suite)
            self.assertTrue(result.wasSuccessful(), str(result.failures) + str(result.errors))
        finally: recorder.uninstall()
        cases = [c for c in recorder.cases.values() if c['boundary'] == 'sql']
        self.assertEqual(len(cases), 3)
        self.assertEqual({(c['runtime_context']['sqlglot.pretty'], json.dumps(c['options'], sort_keys=True), c['expected']['result']) for c in cases},
                         {(False, '{}', 'SELECT col FROM x'), (True, '{}', 'SELECT\n  col\nFROM x'),
                          (False, '{"pretty": true}', 'SELECT\n  col\nFROM x')})

    def test_utf16_inclusive_offsets(self):
        token = SimpleNamespace(token_type=SimpleNamespace(name='STRING'), text='😀x', line=1,
                                start=0, end=1, comments=[])
        value = corpus.token_payload(token, '😀x')
        self.assertEqual((value['start'], value['end'], value['col']), (0, 2, 3))

    def test_ast_metadata_only_and_standalone_preservation(self):
        payload = [{'c': 'Identifier', 'm': {'start': 1, 'end': 1, 'col': 2, 'line': 1}}, {'v': 'x'}]
        result = corpus.normalize_ast(payload, '😀x')
        self.assertEqual(result[0]['m'], {'start': 2, 'end': 2, 'col': 3, 'line': 1})
        self.assertEqual(payload[0]['m']['start'], 1)
        self.assertEqual(corpus.normalize_ast(payload, ''), payload)

    def test_dedup_keeps_multiple_sources_and_restores_method(self):
        class Owner:
            def call(self, sql): return sql.upper()
        original = Owner.call
        recorder = corpus.Recorder()
        recorder.patch(Owner, 'call', 'L3', lambda b: {'sql': b['sql'], 'read': '', 'write': '', 'options': {}},
                       lambda result, b, sql: result)
        try:
            recorder.test_id.set('first')
            self.assertEqual(Owner().call('abc'), 'ABC')
            recorder.test_id.set('second')
            Owner().call('abc')
            self.assertEqual(len(recorder.cases), 1)
            self.assertEqual(next(iter(recorder.cases.values()))['sources'], ['first', 'second'])
        finally: recorder.uninstall()
        self.assertIs(Owner.call, original)

    def test_exception_is_recorded_and_rethrown(self):
        class Owner:
            def call(self): raise ValueError('actual error')
        recorder = corpus.Recorder()
        recorder.patch(Owner, 'call', 'L1', lambda b: {'read': '', 'options': {}}, lambda *args: None)
        try:
            with self.assertRaisesRegex(ValueError, 'actual error'): Owner().call()
            error = next(iter(recorder.cases.values()))['expected']['error']
            self.assertEqual(error['class'], 'ValueError')
        finally: recorder.uninstall()

    def test_filter_preserves_original_execution(self):
        class Owner:
            def call(self): return 42
        recorder = corpus.Recorder()
        recorder.patch(Owner, 'call', 'L2', lambda b: {'write': 'mysql'}, lambda *args: None)
        try:
            self.assertEqual(Owner().call(), 42)
            self.assertFalse(recorder.cases)
        finally: recorder.uninstall()

    def test_validation_helpers_record_observed_sql_without_extra_execution(self):
        import sys
        sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'python'))
        recorder = corpus.Recorder()
        recorder.install()
        try:
            from tests.dialects.test_dialect import Validator
            validator = Validator()
            validator.validate_identity('SELECT 1')
            validator.validate_all('SELECT 2', read={'postgres': 'SELECT 2'}, write={'tsql': 'SELECT 2'})
            cases = [case for case in recorder.cases.values() if case['level'] == 'L3']
            self.assertEqual({case['boundary'] for case in cases}, {'validate_identity', 'validate_all'})
            self.assertTrue(any(case['read'] == 'postgres' and case['write'] == '' for case in cases))
            self.assertTrue(any(case['read'] == '' and case['write'] == 'tsql' for case in cases))
            self.assertTrue(all(case['pipeline'] == 'parse_one_then_sql' for case in cases))
            self.assertTrue(all(isinstance(case['expected']['result'], str) for case in cases))
        finally: recorder.uninstall()

    def test_capture_failures_do_not_change_original_execution(self):
        class Owner:
            def call(self): return 42
        def fail(*args): raise KeyError('missing AST argument')
        for describe, encode in [(fail, lambda *a: None), (lambda b: {'read': ''}, fail)]:
            recorder = corpus.Recorder()
            recorder.patch(Owner, 'call', 'L2', describe, encode)
            try:
                self.assertEqual(Owner().call(), 42)
                self.assertFalse(recorder.cases)
                self.assertEqual(sum(recorder.skipped.values()), 1)
            finally: recorder.uninstall()

    def test_cli_selected_test_is_not_marked_full_suite(self):
        import subprocess
        import sys
        name = 'tests.test_expressions.TestExprs.test_transform_node_removal'
        with tempfile.TemporaryDirectory() as directory:
            result = subprocess.run([sys.executable, '-B', str(Path(corpus.__file__)),
                                     '--test', name, '--output', directory,
                                     '--log', str(Path(directory) / 'run.log')],
                                    capture_output=True, text=True, timeout=60)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            manifest = json.loads((Path(directory) / 'MANIFEST.json').read_text(encoding='utf-8'))
            self.assertFalse(manifest['full_suite'])
            self.assertEqual(manifest['selected_tests'], [name])
            self.assertEqual(manifest['tests_run'], 1)
            self.assertTrue(manifest['suite_passed'])

    def test_tokenizer_identity_prevents_dialect_only_ambiguity(self):
        import sys
        sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'python'))
        from sqlglot.tokens import Tokenizer
        from sqlglot.jsonpath import JSONPathTokenizer
        recorder = corpus.Recorder()
        recorder.install()
        try:
            Tokenizer().tokenize('$')
            JSONPathTokenizer().tokenize('$')
            cases = [c for c in recorder.cases.values() if c['level'] == 'L0']
            self.assertEqual(len(cases), 2)
            self.assertEqual({c['tokenizer'] for c in cases},
                             {'sqlglot.tokens::Tokenizer', 'sqlglot.jsonpath::JSONPathTokenizer'})
        finally: recorder.uninstall()

    def test_result_is_snapshotted_before_caller_mutates_nested_lists(self):
        class Owner:
            def call(self): return {'comments': ['original']}
        recorder = corpus.Recorder()
        recorder.patch(Owner, 'call', 'L0', lambda b: {'read': ''}, lambda result, *args: result)
        try:
            result = Owner().call()
            result['comments'].append('later parser mutation')
            expected = next(iter(recorder.cases.values()))['expected']['result']
            self.assertEqual(expected, {'comments': ['original']})
        finally: recorder.uninstall()

    def test_out_of_scope_tokenizer_is_filtered_even_with_base_dialect(self):
        import sys
        sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'python'))
        from sqlglot.dialects.bigquery import BigQuery
        recorder = corpus.Recorder()
        recorder.install()
        try:
            result = BigQuery.Tokenizer().tokenize('SELECT 1')
            self.assertTrue(result)
            self.assertFalse(any(c['level'] == 'L0' for c in recorder.cases.values()))
        finally: recorder.uninstall()

    def test_live_boundaries_and_manifest(self):
        import sys
        repo = Path(__file__).resolve().parents[2]
        sys.path.insert(0, str(repo / 'python'))
        recorder = corpus.Recorder()
        recorder.install()
        try:
            import sqlglot
            from sqlglot import exp
            self.assertEqual(sqlglot.transpile('SELECT 1', read='postgres', write='tsql'), ['SELECT 1'])
            sqlglot.parse_one('SELECT 2').sql()
            levels = {case['level'] for case in recorder.cases.values()}
            self.assertEqual(levels, {'L0', 'L1', 'L2', 'L3'})
            self.assertTrue(any(case.get('boundary') == 'parse_one' for case in recorder.cases.values()))
            with tempfile.TemporaryDirectory() as directory:
                recorder.write(Path(directory), {'tag': 'test'})
                manifest = json.loads((Path(directory) / 'MANIFEST.json').read_text(encoding='utf-8'))
                self.assertEqual(sum(manifest['counts'].values()), len(recorder.cases))
        finally: recorder.uninstall()


if __name__ == '__main__':
    unittest.main()
