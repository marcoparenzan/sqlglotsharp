import unittest
import gen_csharp as gen


class EnumGeneratorTests(unittest.TestCase):
    def test_identifiers_and_literals(self):
        self.assertEqual(gen.identifier('this'), '@this')
        self.assertEqual(gen.literal('a"b\\c'), '"a\\"b\\\\c"')
        with self.assertRaises(ValueError):
            gen.identifier('not-valid')

    def test_numeric_values_and_anchors(self):
        text = gen.render_enum('E', [('A', 7), ('B', 12)], 'sqlglot/m.py::E',
            {'sqlglot/m.py::E': '1234567890', 'sqlglot/m.py::E.A': 'abcdef1234'})
        self.assertIn('A = 7,', text)
        self.assertIn('B = 12,', text)
        self.assertIn('// py: sqlglot/m.py::E.A #abcdef1234', text)

    def test_string_aliases_and_conversion(self):
        text = gen.render_enum('E', [('A', 'a'), ('ALIAS', 'a'), ('B', 'b')],
            'sqlglot/m.py::E', {'sqlglot/m.py::E': '1234567890'})
        self.assertIn('ALIAS = 1,', text)
        self.assertIn('B = 2,', text)
        self.assertEqual(text.count('"a" => E.A'), 1)
        self.assertIn('public static string ToValue(E value)', text)

    def test_missing_hash_and_mixed_values_fail(self):
        with self.assertRaises(ValueError):
            gen.render_enum('E', [('A', 1)], 'missing', {})
        with self.assertRaises(ValueError):
            gen.render_enum('E', [('A', 1), ('B', 'b')], 'p', {'p': '1234567890'})


class ShellGeneratorTests(unittest.TestCase):
    def model(self, name, bases=(), trait=False):
        return {'name': name, 'path': 'sqlglot/expressions/core.py', 'bases': list(bases),
                'trait': trait, 'doc': 'A <node> & trait.'}

    def render(self, models):
        hashes = {'sqlglot/expressions/core.py::' + m['name']: '1234567890' for m in models}
        return gen.render_shells(models, hashes)['sqlglot/expressions/core.py']

    def test_trait_and_concrete_hierarchy(self):
        text = self.render([self.model('Expr'), self.model('Expression', ['Expr']),
            self.model('Query', ['Expr'], True), self.model('Select', ['Expression', 'Query'])])
        self.assertIn('public abstract partial class Expr', text)
        self.assertIn('public interface Query\n', text)
        self.assertIn('public partial class Select : Expression, Query', text)
        self.assertIn('A &lt;node&gt; &amp; trait.', text)

    def test_trait_inheritance(self):
        text = self.render([self.model('Expr'), self.model('Func', ['Expr'], True),
                           self.model('AggFunc', ['Func'], True)])
        self.assertIn('public interface AggFunc : Func', text)

    def test_invalid_hierarchies_fail(self):
        for models in ([self.model('Expr'), self.model('Expr')],
                       [self.model('Unknown', ['Missing'])],
                       [self.model('Expr'), self.model('Expression', ['Expr']),
                        self.model('Bad', ['Expr', 'Expression'])]):
            with self.assertRaises(ValueError):
                self.render(models)


class MetadataGeneratorTests(unittest.TestCase):
    def test_metadata_values_and_order(self):
        model = {'name': 'Expr', 'path': 'sqlglot/expressions/core.py', 'trait': False,
                 'metadata': {'key': 'expression', 'arg_types': {'this': True, 'to': False},
                              'required_args': ['this'], 'is_var_len_args': False,
                              'var_len_arg_key': 'expressions', '_sql_names': []}}
        text = gen.render_metadata([model], {})[model['path']]
        self.assertIn('["this"] = true, ["to"] = false', text)
        self.assertIn('public virtual string key => key_;', text)
        self.assertIn('HashSet<string>', text)

    def test_traits_are_not_emitted_as_classes(self):
        self.assertEqual(gen.render_metadata([{'trait': True}], {}), {})


class ConstructorGeneratorTests(unittest.TestCase):
    def test_named_parameters_and_dictionary_forwarding(self):
        model = {'name': 'Cast', 'trait': False, 'path': 'sqlglot/expressions/core.py',
                 'metadata': {'arg_types': {'this': True, 'to': True}}}
        text = gen.render_constructors([model])[model['path']]
        self.assertIn('[System.Runtime.InteropServices.Optional] object? @this', text)
        self.assertIn('("this", @this), ("to", to)', text)
        self.assertIn(': base(_constructor_args)', text)


class DataAndManifestTests(unittest.TestCase):
    def test_nullable_and_ordered_data_types(self):
        import gen_data
        self.assertEqual(gen_data.type_for([None, True], set()), 'bool?')
        self.assertEqual(gen_data.type_for([{'a': 'b'}, {}], set()), 'PyDict<string, string>')
        self.assertEqual(gen_data.value_for({'a': True}, gen.literal), 'new() { ["a"] = true }')

    def test_fixed_tuples_and_heterogeneous_values_are_not_stringified(self):
        import gen_data
        self.assertEqual(gen_data.type_for([('a', 1)], set()), '(string, long)')
        self.assertEqual(gen_data.type_for([True, {'hint'}], set()), 'object?')
        text = gen_data.value_for({'x': {0: True}}, gen.literal, 'PyDict<object, object?>')
        self.assertIn('[0L] = true', text)
        self.assertIn('new PyDict<object, object?>()', text)

    def test_generic_argument_parser_handles_nested_tuple_types(self):
        import gen_data
        self.assertEqual(gen_data.generic_args('PyDict<string, (string, long)>'), ['string', '(string, long)'])

    def test_callable_detection_is_recursive(self):
        import gen_data
        self.assertTrue(gen_data.has_callable({'a': [lambda: None]}))
        self.assertFalse(gen_data.has_callable({'a': int}))

    def test_manifest_cleanup_only_removes_owned_obsolete_files(self):
        import tempfile
        import json
        from pathlib import Path
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            gen.WRITTEN.clear()
            old = root / 'obsolete.g.cs'
            old.write_text(gen.HEADER + 'old', encoding='utf-8')
            foreign = root / 'foreign.g.cs'
            foreign.write_text('// another generator', encoding='utf-8')
            current = root / 'current.g.cs'
            gen.write_generated(current, gen.HEADER + 'new')
            manifest = root / 'manifest.json'
            gen.finalize_generated(root, manifest)
            self.assertFalse(old.exists())
            self.assertTrue(foreign.exists())
            self.assertTrue(current.exists())
            data = json.loads(manifest.read_text(encoding='utf-8'))
            self.assertEqual(data['files'][0]['path'], 'current.g.cs')
            self.assertEqual(len(data['files'][0]['sha256']), 64)


if __name__ == '__main__':
    unittest.main()
