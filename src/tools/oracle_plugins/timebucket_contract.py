"""Shared canonical AST contract for the Timescale extension, independent of dialect routing."""
from sqlglot import exp


class TimeBucket(exp.Expression, exp.Func):
    arg_types = {"width": True, "this": True, "expressions": False}
    is_var_len_args = True
    var_len_arg_key = "expressions"
    _sql_names = ["TIME_BUCKET"]


# This custom contract fixes diagnostic order; upstream Expr's set otherwise
# makes IMMEDIATE errors depend on Python's randomized hash seed.
TimeBucket.required_args = tuple(k for k, required in TimeBucket.arg_types.items() if required)
