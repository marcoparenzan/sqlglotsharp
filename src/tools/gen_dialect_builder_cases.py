"""Capture native Python dialect-builder outputs for direct C# AST comparisons."""
import json
import sys
from pathlib import Path
root = Path(__file__).resolve().parents[2]
sys.dont_write_bytecode = True
sys.path.insert(0, str(root / 'python'))
from sqlglot import exp, serde
from sqlglot.dialects import dialect as d
from extract_corpus import normalize
cases = []
def record(kind, args, callback, **settings):
    inputs = [normalize(serde.dump(arg)) if isinstance(arg, exp.Expr) else arg for arg in args]
    result = callback(args)
    expected = {'ast': normalize(serde.dump(result))} if isinstance(result, exp.Expr) else {'value': result}
    cases.append({'kind': kind, 'args': inputs, 'settings': settings, 'expected': expected})
record('binary', [exp.Var(this='x'), exp.Literal.number(1)], d.binary_from_function(exp.Add))
record('binary', [], d.binary_from_function(exp.Add))
record('timestamp_trunc', [exp.Var(this='month'), exp.Var(this='ts'), exp.Literal.string('UTC')], d.build_timestamp_trunc)
record('isnull', [exp.EQ(this=exp.Var(this='x'), expression=exp.Literal.number(1))], d.isnull_to_is_null)
record('isnull', [exp.Var(this='x')], d.isnull_to_is_null)
record('date_delta', [exp.Var(this='x'), exp.Literal.number(3)], d.build_date_delta(exp.DateAdd), cls='DateAdd')
record('date_delta', [exp.Literal.string('d'), exp.Var(this='a'), exp.Var(this='b')], d.build_date_delta(exp.DateDiff, unit_mapping={'d': 'day'}), cls='DateDiff', mapping={'d':'day'})
record('formatted_time', [exp.Var(this='x'), exp.Literal.string('YYYY-MM-DD')], lambda args: d.build_formatted_time(exp.StrToTime)(args, d.Dialect.get_or_raise('postgres')), cls='StrToTime', dialect='postgres')
record('formatted_time', [exp.Var(this='x')], lambda args: d.build_formatted_time(exp.StrToTime, default=True)(args, d.Dialect.get_or_raise('postgres')), cls='StrToTime', dialect='postgres', default=True)
for unit in (None, exp.Literal.string('month'), exp.Var(this='MONTH'), exp.Placeholder(this='unit'), 'raw'):
    record('unit_to_str', [exp.Interval(unit=unit)], lambda args: d.unit_to_str(args[0]))
for unit in (None, exp.Literal.string('month'), exp.Var(this='MONTH'), exp.Placeholder(this='unit')):
    record('unit_to_var', [exp.Interval(unit=unit)], lambda args: d.unit_to_var(args[0]))
for unit in (None, exp.Var(this='week'), exp.Var(this='ISOWEEK'), exp.WeekStart(this=exp.Var(this='SUNDAY')), exp.Var(this='day')):
    record('week_unit', [unit], lambda args: d.week_unit_to_dow(args[0]))
for offset in (-15, -8, -1, 0, 1, 6, 7, 15):
    record('week_offset', [offset], lambda args: d.week_offset_to_dow(args[0]))
for part in (exp.Var(this='D'), exp.Literal.string('D'), exp.Column(this=exp.Identifier(this='D'), table=exp.Identifier(this='t'))):
    record('map_date_part', [part], lambda args: d.map_date_part(args[0]))
(root / 'src/SqlGlotSharp.Tests/dialect-builder-cases.json').write_text(json.dumps(cases, ensure_ascii=True, indent=2) + '\n', encoding='utf-8')
print(f'Recorded {len(cases)} dialect builder cases')
