namespace SqlGlotSharp.dialects;

public static partial class dialect
{
    private static exp.Expr Construct(Type type, params (string Key, object? Value)[] arguments)
    {
        var kwargs = new PyDict<string, object?>();
        foreach (var (key, value) in arguments) kwargs[key] = value;
        return (exp.Expr)Activator.CreateInstance(type, [kwargs])!;
    }

    // py: sqlglot/dialects/dialect.py::build_formatted_time #e0b66abd42
    public static Func<IReadOnlyList<object?>, Dialect, exp.Expr> build_formatted_time(Type exp_class, string? dialect_override = null, object? @default = null) => (args, dialect) =>
    {
        var target = dialect_override is not null ? Dialect.get_or_raise(dialect_override) : dialect;
        var fmt = helper.seq_get(args, 1);
        if (!py.truthy(fmt)) fmt = @default is true ? target.TIME_FORMAT : py.truthy(@default) ? @default : null;
        return Construct(exp_class, ("this", helper.seq_get(args, 0)), ("format", target.format_time(fmt)));
    };

    // py: sqlglot/dialects/dialect.py::build_date_delta #056e7a53db
    public static Func<IReadOnlyList<object?>, exp.Expr> build_date_delta(Type exp_class, PyDict<string, string>? unit_mapping = null,
        string? default_unit = "DAY", bool supports_timezone = false) => args =>
    {
        var unit_based = args.Count >= 3;
        var has_timezone = args.Count == 4;
        var @this = unit_based ? args[2] : helper.seq_get(args, 0);
        exp.Expr? unit = null;
        if (unit_based || !string.IsNullOrEmpty(default_unit))
        {
            unit = unit_based ? (exp.Expr)args[0]! : exp.Literal.@string(default_unit);
            if (unit_mapping is { Count: > 0 }) unit = new exp.Var(@this: unit_mapping.get(py.lower(unit.name), unit.name));
        }
        var expression = Construct(exp_class, ("this", @this), ("expression", helper.seq_get(args, 1)), ("unit", unit));
        if (supports_timezone && has_timezone) expression.set("zone", args[^1]);
        return expression;
    };

    // py: sqlglot/dialects/dialect.py::binary_from_function #ac95ded819
    public static Func<IReadOnlyList<object?>, exp.Expr> binary_from_function(Type expr_type) => args =>
        Construct(expr_type, ("this", helper.seq_get(args, 0)), ("expression", helper.seq_get(args, 1)));

    // py: sqlglot/dialects/dialect.py::build_timestamp_trunc #b341432c80
    public static exp.TimestampTrunc build_timestamp_trunc(IReadOnlyList<object?> args) =>
        new(@this: helper.seq_get(args, 1), unit: helper.seq_get(args, 0), zone: helper.seq_get(args, 2));

    // py: sqlglot/dialects/dialect.py::isnull_to_is_null #3513a8d16a
    public static exp.Expr isnull_to_is_null(IReadOnlyList<object?> args)
    {
        var @this = helper.seq_get(args, 0);
        if (@this is exp.Predicate or exp.Connector or exp.Not) @this = new exp.Paren(@this: @this);
        return new exp.Paren(@this: new exp.Is(@this: @this, expression: new exp.Null()));
    }

    // py: sqlglot/dialects/dialect.py::unit_to_str #e5cf8c2313
    public static object? unit_to_str(exp.Expr expression, string @default = "DAY")
    {
        var unit = expression.args.get("unit");
        if (!py.truthy(unit)) return @default.Length != 0 ? exp.Literal.@string(@default) : null;
        if (unit is exp.WeekStart) return exp.Literal.@string("WEEK");
        if (unit is exp.Placeholder || unit!.GetType() != typeof(exp.Var) && unit.GetType() != typeof(exp.Literal)) return unit;
        return exp.Literal.@string(((exp.Expr)unit).name);
    }

    // py: sqlglot/dialects/dialect.py::unit_to_var #447b87a00f
    public static exp.Expr? unit_to_var(exp.Expr expression, string @default = "DAY")
    {
        var unit = expression.args.get("unit");
        if (unit is exp.Var or exp.Placeholder or exp.WeekStart or exp.Column) return (exp.Expr)unit;
        var value = py.truthy(unit) ? unit is exp.Expr node ? node.name : (string)py.getattr(unit!, "name")! : @default;
        return value.Length != 0 ? new exp.Var(@this: value) : null;
    }

    // py: sqlglot/dialects/dialect.py::WEEK_START_DAY_TO_DOW #0f2e5d449b
    public static readonly PyDict<string, long> WEEK_START_DAY_TO_DOW = new()
    { ["MONDAY"] = 1, ["TUESDAY"] = 2, ["WEDNESDAY"] = 3, ["THURSDAY"] = 4, ["FRIDAY"] = 5, ["SATURDAY"] = 6, ["SUNDAY"] = 7 };

    // py: sqlglot/dialects/dialect.py::week_offset_to_dow #6c55fa793f
    public static long week_offset_to_dow(long offset) => ((offset % 7 + 7) % 7) + 1;

    // py: sqlglot/dialects/dialect.py::week_unit_to_dow #35b7098082
    public static long? week_unit_to_dow(exp.Expr? unit)
    {
        if (unit is exp.Var && py.upper(unit.name) is "WEEK" or "ISOWEEK") return 1;
        if (unit is exp.WeekStart && WEEK_START_DAY_TO_DOW.TryGetValue(py.upper(unit.name), out var day)) return day;
        return null;
    }

    // py: sqlglot/dialects/dialect.py::map_date_part #b20a63f477
    public static exp.Expr? map_date_part(exp.Expr? part, object? dialect = null)
    {
        var mapped = part is not null && !(part is exp.Column column && column.parts.Count != 1)
            ? Dialect.get_or_raise(dialect).DATE_PART_MAPPING.get(py.upper(part.name)) : null;
        if (!string.IsNullOrEmpty(mapped)) return part!.is_string ? exp.Literal.@string(mapped) : new exp.Var(@this: mapped);
        return part;
    }
}
