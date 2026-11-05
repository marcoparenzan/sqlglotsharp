"""Test-only TimescaleDB identity oracle. No production dependency or mappings.

Import this module before resolving Dialect.get_or_raise("timescale"). sqlglot's
metaclass registers the Timescale(Postgres) subclass under its lowercase name.
FIRST/LAST are not PostgreSQL-style generic aggregates in this dialect: keep all
positional arguments in Anonymous, just like the unknown extension functions.
"""
from sqlglot.dialects.postgres import Postgres
from sqlglot.parsers.postgres import PostgresParser
from sqlglot.generators.postgres import PostgresGenerator
from oracle_plugins.timebucket_contract import TimeBucket


class TimescaleParser(PostgresParser):
    FUNCTIONS = {
        **{k: v for k, v in PostgresParser.FUNCTIONS.items() if k not in {"FIRST", "LAST"}},
        "TIME_BUCKET": TimeBucket.from_arg_list,
    }


class TimescaleGenerator(PostgresGenerator):
    # Own the identity transform: a PostgreSQL rewrite must never leak into Timescale.
    TRANSFORMS = {
        **PostgresGenerator.TRANSFORMS,
        TimeBucket: lambda self, expression: self.function_fallback_sql(expression),
    }


class Timescale(Postgres):
    Parser = TimescaleParser
    Generator = TimescaleGenerator
