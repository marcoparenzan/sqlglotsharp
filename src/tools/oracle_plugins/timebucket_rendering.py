"""Test-process-only rendering adapters; no upstream files or Timescale plugin are patched.

Evidence: https://docs.tigerdata.com/api/latest/hyperfunctions/time_bucket/
https://raw.githubusercontent.com/timescale/timescaledb/main/src/time_bucket.c
https://www.postgresql.org/docs/current/functions-datetime.html#FUNCTIONS-DATETIME-BIN
The oracle extends the immutable AST contract, not the parser/serde contract.
"""
import datetime
import re
from sqlglot import exp
from sqlglot.generators.postgres import PostgresGenerator
from oracle_plugins.timescale import TimescaleGenerator
from oracle_plugins.timebucket_contract import TimeBucket as TimeBucketContract


# Use the same concrete contract and exact class identity as the parser/serde oracle.
TimeBucket = TimeBucketContract

UNSAFE = "TimeBucket: PostgreSQL DATE_BIN requires a positive constant fixed-width interval, explicit TIMESTAMP/TIMESTAMPTZ CAST, and absent or matching unambiguous typed origin; timezone, offset and named overloads are not ported"


class NotPortedError(Exception):
    def __init__(self, message):
        super().__init__('Not ported: ' + message)


def only_args(node, allowed):
    return all(k in allowed or not v for k, v in node.args.items())


def timestamp_type(node):
    if type(node) is not exp.Cast or not only_args(node, {'this', 'to'}) or node.this is None:
        return None
    to = node.args.get('to')
    if not isinstance(to, exp.DataType) or not only_args(to, {'this'}):
        return None
    return to.this if to.this in (exp.DataType.Type.TIMESTAMP, exp.DataType.Type.TIMESTAMPTZ) else None


def fixed_width(node):
    if not isinstance(node, exp.Interval) or not only_args(node, {'this', 'unit'}):
        return None
    if not isinstance(node.this, exp.Literal) or not isinstance(node.args.get('unit'), exp.Var):
        return None
    text = node.this.name
    if len(text) > 19 or not re.fullmatch(r'[0-9]+', text):
        return None
    factors = {'microsecond': 1, 'millisecond': 1000, 'second': 1000000,
               'minute': 60000000, 'hour': 3600000000, 'day': 86400000000, 'week': 604800000000}
    unit = node.args['unit'].name.lower()
    factor = factors.get(unit, factors.get(unit[:-1]) if unit.endswith('s') else None)
    total = int(text) * (factor or 0)
    return total if 0 < total <= 2**63-1 else None


def unambiguous_origin(text, kind):
    suffix = r'(?:Z|\+00:00)' if kind == exp.DataType.Type.TIMESTAMPTZ else ''
    if not re.fullmatch(r'[0-9]{4}-[0-9]{2}-[0-9]{2}[ T][0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,6})?' + suffix, text):
        return False
    try:
        datetime.datetime.fromisoformat(text.replace('Z', '+00:00'))
        return True
    except ValueError:
        return False


def date_bin(g, e):
    micros, kind = fixed_width(e.args.get('width')), timestamp_type(e.this)
    if not only_args(e, {'width', 'this', 'expressions'}) or micros is None or kind is None or len(e.expressions) > 1:
        raise NotPortedError(UNSAFE)
    if e.expressions:
        origin = e.expressions[0]
        if timestamp_type(origin) != kind or not isinstance(origin.this, exp.Literal) or not origin.this.is_string or not unambiguous_origin(origin.this.name, kind):
            raise NotPortedError(UNSAFE)
    else:
        text = '2000-01-03 00:00:00' + ('+00:00' if kind == exp.DataType.Type.TIMESTAMPTZ else '')
        origin = exp.Cast(this=exp.Literal.string(text), to=exp.DataType(this=kind))
    stride = exp.Interval(this=exp.Literal.string(str(micros)), unit=exp.Var(this='MICROSECONDS'))
    return g.func('DATE_BIN', stride, e.this, origin)


class PostgresTimeBucketGenerator(PostgresGenerator):
    TRANSFORMS = {**PostgresGenerator.TRANSFORMS, TimeBucket: date_bin}


class TimescaleTimeBucketGenerator(TimescaleGenerator):
    TRANSFORMS = {**TimescaleGenerator.TRANSFORMS, TimeBucket: lambda g, e: g.function_fallback_sql(e)}
