using System.Collections;
using System.Runtime.CompilerServices;

namespace SqlGlotSharp;

// Builder helpers of sqlglot/expressions/core.py (module level), in upstream order.
// Conversion notes (AGENTS.md section 4.5):
//   *expressions + keyword options -> the anchored member takes `object?[] expressions` followed by the options;
//   an unanchored overload `f(params object?[] expressions)` keeps the Python call shape `f("x=1", "y=1")`.
//   **opts (ParserNoDialectArgs) are not supported: parsing uses the dialect's defaults.
public static partial class exp
{
    /// <summary>
    /// Gracefully handle a possible string or expression.
    ///
    /// Example:
    ///     >>> maybe_parse("1")
    ///     Literal(this=1, is_string=False)
    ///     >>> maybe_parse(to_identifier("x"))
    ///     Identifier(this=x, quoted=False)
    ///
    /// Args:
    ///     sql_or_expression: the SQL code string or an expression
    ///     into: the SQLGlot Expr to parse into
    ///     dialect: the dialect used to parse the input expressions (in the case that an
    ///         input expression is a SQL string).
    ///     prefix: a string to prefix the sql with before it gets parsed
    ///         (automatically includes a space)
    ///     copy: whether to copy the expression.
    ///
    /// Returns:
    ///     Expr: the parsed or given expression.
    /// </summary>
    // py: sqlglot/expressions/core.py::maybe_parse #171bd68c00
    public static Expr maybe_parse(object? sql_or_expression, object? into = null, object? dialect = null,
        string? prefix = null, bool copy = false)
    {
        if (sql_or_expression is Expr expression)
        {
            if (copy)
                return expression.copy();
            return expression;
        }

        if (sql_or_expression is null)
            throw new errors.ParseError("SQL cannot be None");

        var sql = py.str(sql_or_expression);
        if (py.truthy(prefix))
            sql = $"{prefix} {sql}";

        return SqlGlot.parse_one(sql, read: dialect, into: into);
    }

    // py: sqlglot/expressions/core.py::_is_wrong_expression #5f717b4aef
    public static bool _is_wrong_expression(object? expression, object? into) =>
        expression is Expr && !_is_wrong_expression__isinstance(expression, into);

    // isinstance(expression, into) for a type or a tuple of types; Python raises TypeError for None.
    private static bool _is_wrong_expression__isinstance(object expression, object? into) => into switch
    {
        Type type => type.IsInstanceOfType(expression),
        IEnumerable<Type> types => types.Any(type => type.IsInstanceOfType(expression)),
        _ => throw new ArgumentException("isinstance() arg 2 must be a type, a tuple of types, or a union"),
    };

    // into(**{key: value, ...}) for an expression class given as a Type.
    private static Expr _new__helper(Type into, params (string Key, object? Value)[] kwargs)
    {
        var args = new PyDict<string, object?>();
        foreach (var (key, value) in kwargs) args[key] = value;
        return (Expr)Activator.CreateInstance(into, [args])!;
    }

    // py: sqlglot/expressions/core.py::_apply_builder #375a57b27a
    public static Expr _apply_builder(object? expression, Expr instance, string arg, bool copy = true,
        string? prefix = null, Type? into = null, object? dialect = null, string into_arg = "this")
    {
        if (_is_wrong_expression(expression, into) && into is not null)
            expression = _new__helper(into, (into_arg, expression));
        instance = maybe_copy(instance, copy)!;
        expression = maybe_parse(
            sql_or_expression: expression,
            prefix: prefix,
            into: into,
            dialect: dialect);
        instance.set(arg, expression);
        return instance;
    }

    // py: sqlglot/expressions/core.py::_apply_child_list_builder #f096fea924
    public static Expr _apply_child_list_builder(object?[] expressions, Expr instance, string arg,
        bool append = true, bool copy = true, string? prefix = null, Type? into = null, object? dialect = null,
        PyDict<string, object?>? properties = null)
    {
        instance = maybe_copy(instance, copy)!;
        var parsed = new List<object?>();
        properties = properties is null ? new PyDict<string, object?>() : properties;

        foreach (var item in expressions)
        {
            var expression = item;
            if (expression is not null)
            {
                if (_is_wrong_expression(expression, into) && into is not null)
                    expression = _new__helper(into, ("expressions", new List<object?> { expression }));

                var parsed_expression = maybe_parse(
                    expression,
                    into: into,
                    dialect: dialect,
                    prefix: prefix);
                foreach (var (k, v) in parsed_expression.args)
                {
                    if (k == "expressions")
                        parsed.AddRange(((IEnumerable)v!).Cast<object?>());
                    else
                        properties[k] = v;
                }
            }
        }

        var existing = instance.args.get(arg);
        if (append && py.truthy(existing))
            parsed = ((Expr)existing!).expressions.Concat(parsed).ToList();
        if (into is null)
            throw new ArgumentException("`into` is required to use `_apply_child_list_builder`");
        var child = _new__helper(into, ("expressions", parsed));
        foreach (var (k, v) in properties)
            child.set(k, v);
        instance.set(arg, child);

        return instance;
    }

    // py: sqlglot/expressions/core.py::_apply_list_builder #b2772fd25a
    public static Expr _apply_list_builder(object?[] expressions, Expr instance, string arg,
        bool append = true, bool copy = true, string? prefix = null, Type? into = null, object? dialect = null)
    {
        var inst = maybe_copy(instance, copy)!;

        var parsed = expressions
            .Where(expression => expression is not null)
            .Select(expression => (object?)maybe_parse(
                sql_or_expression: expression,
                into: into,
                prefix: prefix,
                dialect: dialect))
            .ToList();

        var existing_expressions = inst.args.get(arg);
        if (append && py.truthy(existing_expressions))
            parsed = ((IEnumerable)existing_expressions!).Cast<object?>().Concat(parsed).ToList();

        inst.set(arg, parsed);
        return inst;
    }

    // py: sqlglot/expressions/core.py::_apply_conjunction_builder #f17b21c8c3
    public static Expr _apply_conjunction_builder(object?[] expressions, Expr instance, string arg,
        Type? into = null, bool append = true, bool copy = true, object? dialect = null)
    {
        var filtered = expressions.Where(exp => exp is not null && !(exp is string text && text == "")).ToList();
        if (filtered.Count == 0)
            return instance;

        var inst = maybe_copy(instance, copy)!;

        var existing = inst.args.get(arg);
        if (append && existing is not null)
            filtered = new List<object?> { into is not null ? ((Expr)existing).@this : existing }.Concat(filtered).ToList();

        var node = and_(filtered.ToArray(), dialect: dialect, copy: copy);

        inst.set(arg, into is not null ? _new__helper(into, ("this", node)) : node);
        return inst;
    }

    // py: sqlglot/expressions/core.py::_combine #7de1629381
    public static Expr _combine(IEnumerable<object?> expressions, Type @operator, object? dialect = null,
        bool copy = true, bool wrap = true)
    {
        var conditions = expressions
            .Where(expression => expression is not null)
            .Select(expression => condition(expression, dialect: dialect, copy: copy))
            .ToList();

        if (conditions.Count == 0)
            throw new ArgumentException("not enough values to unpack (expected at least 1, got 0)");
        Expr @this = conditions[0];
        var rest = conditions.Skip(1).ToList();
        if (rest.Count > 0 && wrap)
            @this = _wrap(@this, typeof(Connector))!;
        foreach (var expression in rest)
            @this = _new__helper(@operator, ("this", @this), ("expression", wrap ? _wrap(expression, typeof(Connector)) : expression));

        return @this;
    }

    // py: sqlglot/expressions/core.py::_wrap #9ec9e91b4f
    public static Expr? _wrap(Expr? expression, Type kind) =>
        expression is not null && kind.IsInstanceOfType(expression) ? new Paren(@this: expression) : expression;

    // py: sqlglot/expressions/core.py::_apply_set_operation #f2a4e62040
    public static Expr _apply_set_operation(object?[] expressions, Type set_operation, bool distinct = true,
        object? dialect = null, bool copy = true) =>
        expressions
            .Select(e => maybe_parse(e, dialect: dialect, copy: copy))
            .Aggregate((x, y) => _new__helper(set_operation, ("this", x), ("expression", y), ("distinct", distinct)));

    /// <summary>
    /// Initialize a logical condition expression.
    ///
    /// Example:
    ///     >>> condition("x=1").sql()
    ///     'x = 1'
    ///
    ///     This is helpful for composing larger logical syntax trees:
    ///     >>> where = condition("x=1")
    ///     >>> where = where.and_("y=1")
    ///     >>> where.sql()
    ///     'x = 1 AND y = 1'
    ///
    /// Args:
    ///     *expression: the SQL code string to parse.
    ///         If an Expr instance is passed, this is used as-is.
    ///     dialect: the dialect used to parse the input expression (in the case that the
    ///         input expression is a SQL string).
    ///     copy: Whether to copy `expression` (only applies to expressions).
    ///
    /// Returns:
    ///     The new Condition instance
    /// </summary>
    // py: sqlglot/expressions/core.py::condition #7f67277c4c
    public static Expr condition(object? expression, object? dialect = null, bool copy = true) =>
        maybe_parse(
            expression,
            into: typeof(Condition),
            dialect: dialect,
            copy: copy);

    /// <summary>
    /// Combine multiple conditions with an AND logical operator.
    ///
    /// Example:
    ///     >>> and_("x=1", and_("y=1", "z=1")).sql()
    ///     'x = 1 AND (y = 1 AND z = 1)'
    ///
    /// Args:
    ///     *expressions: the SQL code strings to parse.
    ///         If an Expr instance is passed, this is used as-is.
    ///     dialect: the dialect used to parse the input expression.
    ///     copy: whether to copy `expressions` (only applies to Exprs).
    ///     wrap: whether to wrap the operands in `Paren`s. This is true by default to avoid
    ///         precedence issues, but can be turned off when the produced AST is too deep and
    ///         causes recursion-related issues.
    ///
    /// Returns:
    ///     The new condition
    /// </summary>
    // py: sqlglot/expressions/core.py::and_ #6789a74408
    public static Expr and_(object?[] expressions, object? dialect = null, bool copy = true, bool wrap = true) =>
        _combine(expressions, typeof(And), dialect, copy: copy, wrap: wrap);

    public static Expr and_(params object?[] expressions) => and_(expressions, null, true, true);

    /// <summary>
    /// Combine multiple conditions with an OR logical operator.
    ///
    /// Example:
    ///     >>> or_("x=1", or_("y=1", "z=1")).sql()
    ///     'x = 1 OR (y = 1 OR z = 1)'
    /// </summary>
    // py: sqlglot/expressions/core.py::or_ #cbb88bed01
    public static Expr or_(object?[] expressions, object? dialect = null, bool copy = true, bool wrap = true) =>
        _combine(expressions, typeof(Or), dialect, copy: copy, wrap: wrap);

    public static Expr or_(params object?[] expressions) => or_(expressions, null, true, true);

    /// <summary>
    /// Combine multiple conditions with an XOR logical operator.
    ///
    /// Example:
    ///     >>> xor("x=1", xor("y=1", "z=1")).sql()
    ///     'x = 1 XOR (y = 1 XOR z = 1)'
    /// </summary>
    // py: sqlglot/expressions/core.py::xor #b0db5d96ef
    public static Expr xor(object?[] expressions, object? dialect = null, bool copy = true, bool wrap = true) =>
        _combine(expressions, typeof(Xor), dialect, copy: copy, wrap: wrap);

    public static Expr xor(params object?[] expressions) => xor(expressions, null, true, true);

    /// <summary>
    /// Wrap a condition with a NOT operator.
    ///
    /// Example:
    ///     >>> not_("this_suit='black'").sql()
    ///     "NOT this_suit = 'black'"
    ///
    /// Args:
    ///     expression: the SQL code string to parse.
    ///         If an Expr instance is passed, this is used as-is.
    ///     dialect: the dialect used to parse the input expression.
    ///     copy: whether to copy the expression or not.
    ///
    /// Returns:
    ///     The new condition.
    /// </summary>
    // py: sqlglot/expressions/core.py::not_ #eca0439ef3
    public static Not not_(object? expression, object? dialect = null, bool copy = true)
    {
        var @this = condition(
            expression,
            dialect: dialect,
            copy: copy);
        return new Not(@this: _wrap(@this, typeof(Connector)));
    }

    // py: sqlglot/expressions/core.py::_lazy_unnest #dd243a6449
    public static Expr _lazy_unnest(PyKwargs kwargs) => new Unnest(kwargs);

    /// <summary>
    /// Convert a python value into an expression object.
    ///
    /// Raises an error if a conversion is not possible.
    ///
    /// Args:
    ///     value: A python object.
    ///     copy: Whether to copy `value` (only applies to Exprs and collections).
    ///
    /// Returns:
    ///     The equivalent expression object.
    /// </summary>
    /// <remarks>
    /// .NET equivalents: <c>datetime</c> is <see cref="DateTime"/> (naive unless <c>Kind</c> is UTC) or
    /// <see cref="DateTimeOffset"/>; <c>date</c> is <see cref="DateOnly"/>; <c>time</c> is <see cref="TimeOnly"/>;
    /// <c>tuple</c> is any <see cref="ITuple"/>; <c>list</c> is any other <see cref="IList"/>; <c>dict</c> is any
    /// <see cref="IDictionary"/>; an object's <c>__dict__</c> is its public instance properties.
    /// </remarks>
    // py: sqlglot/expressions/core.py::convert #b68f5cb256
    public static Expr convert(object? value, bool copy = false)
    {
        if (value is Expr expression)
            return maybe_copy(expression, copy)!;
        if (value is string text)
            return Literal.@string(text);
        if (value is bool boolean)
            return new Boolean(@this: boolean);
        if (value is null || (value is double d && double.IsNaN(d)) || (value is float f && float.IsNaN(f)))
            return new Null();
        if (value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal
            or System.Numerics.BigInteger)
            return Literal.number(value);
        if (value is byte[] bytes)
            return new HexString(@this: System.Convert.ToHexStringLower(bytes));
        if (value is DateTime or DateTimeOffset)
        {
            var (iso, zone) = convert__datetime(value);
            var datetime_literal = Literal.@string(iso);
            Expr? tz = null;
            if (zone is not null)
                // this works for zoneinfo.ZoneInfo, pytz.timezone and datetime.datetime.utc to return IANA timezone names like "America/Los_Angeles"
                // instead of abbreviations like "PDT". This is for consistency with other timezone handling functions in SQLGlot
                tz = Literal.@string(zone);
            return new TimeStrToTime(@this: datetime_literal, zone: tz);
        }
        if (value is DateOnly date)
        {
            var date_literal = Literal.@string(date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            return new DateStrToDate(@this: date_literal);
        }
        if (value is TimeOnly time)
        {
            var time_literal = Literal.@string(convert__time(time.ToTimeSpan()));
            return new TsOrDsToTime(@this: time_literal);
        }
        if (value is ITuple tuple)
            return new Tuple(expressions: Enumerable.Range(0, tuple.Length).Select(i => (object?)convert(tuple[i], copy: copy)).ToList());
        if (value is IDictionary dictionary)
        {
            var keys = new List<object?>();
            var values = new List<object?>();
            foreach (DictionaryEntry entry in dictionary)
            {
                keys.Add(convert(entry.Key, copy: copy));
                values.Add(convert(entry.Value, copy: copy));
            }
            return new Map(keys: new Array(expressions: keys), values: new Array(expressions: values));
        }
        if (value is IList list)
            return new Array(expressions: list.Cast<object?>().Select(v => (object?)convert(v, copy: copy)).ToList());
        var properties = value.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0).ToList();
        if (properties.Count > 0)
            return new Struct(
                expressions: properties
                    .Select(property => (object?)new PropertyEQ(@this: to_identifier(property.Name), expression: convert(property.GetValue(value), copy: copy)))
                    .ToList());
        throw new ArgumentException($"Cannot convert {value}");
    }

    // datetime.isoformat(sep=" ") and str(tzinfo) for DateTime / DateTimeOffset (microsecond precision, as Python).
    private static (string Iso, string? Zone) convert__datetime(object value)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        var local = value is DateTimeOffset offset ? offset.DateTime : (DateTime)value;
        var iso = local.ToString("yyyy-MM-dd ", invariant) + convert__time(local.TimeOfDay);
        if (value is DateTimeOffset aware)
        {
            var span = aware.Offset;
            var sign = span < TimeSpan.Zero ? "-" : "+";
            var abs = span.Duration();
            var suffix = $"{sign}{abs.Hours:00}:{abs.Minutes:00}";
            return (iso + suffix, span == TimeSpan.Zero ? "UTC" : "UTC" + suffix);
        }
        if (((DateTime)value).Kind == DateTimeKind.Utc)
            return (iso + "+00:00", "UTC");
        return (iso, null);
    }

    // time.isoformat(): HH:MM:SS, plus .ffffff when there are microseconds.
    private static string convert__time(TimeSpan time)
    {
        var microseconds = (time.Ticks % TimeSpan.TicksPerSecond) / 10;
        var text = $"{time.Hours:00}:{time.Minutes:00}:{time.Seconds:00}";
        return microseconds == 0 ? text : $"{text}.{microseconds:000000}";
    }

    /// <summary>
    /// Wrap an expression in parentheses.
    ///
    /// Example:
    ///     >>> paren("5 + 3").sql()
    ///     '(5 + 3)'
    /// </summary>
    // py: sqlglot/expressions/core.py::paren #d7d5afda5a
    public static Paren paren(object? expression, bool copy = true) =>
        new(@this: maybe_parse(expression, copy: copy));

    /// <summary>
    /// Create an Alias expression.
    ///
    /// Example:
    ///     >>> alias_('foo', 'bar').sql()
    ///     'foo AS bar'
    ///
    ///     >>> alias_('(select 1, 2)', 'bar', table=['a', 'b']).sql()
    ///     '(SELECT 1, 2) AS bar(a, b)'
    ///
    /// Args:
    ///     expression: the SQL code strings to parse.
    ///         If an Expr instance is passed, this is used as-is.
    ///     alias: the alias name to use. If the name has
    ///         special characters it is quoted.
    ///     table: Whether to create a table alias, can also be a list of columns.
    ///     quoted: whether to quote the alias
    ///     dialect: the dialect used to parse the input expression.
    ///     copy: Whether to copy the expression.
    ///
    /// Returns:
    ///     Alias: the aliased expression
    /// </summary>
    // py: sqlglot/expressions/core.py::alias_ #09d0199e9f
    public static Expr alias_(object? expression, object? alias, object? table = null, bool? quoted = null,
        object? dialect = null, bool copy = true)
    {
        var exp = maybe_parse(expression, dialect: dialect, copy: copy);
        var alias_identifier = to_identifier(alias, quoted: quoted);

        if (py.truthy(table))
        {
            var table_alias = new TableAlias(@this: alias_identifier);
            exp.set("alias", table_alias);

            if (table is not bool)
                foreach (var column in (IEnumerable)table!)
                    table_alias.append("columns", to_identifier(column, quoted: quoted));

            return exp;
        }

        // We don't set the "alias" arg for Window expressions, because that would add an IDENTIFIER node in
        // the AST, representing a "named_window" [1] construct (eg. bigquery). What we want is an ALIAS node
        // for the complete Window expression.
        //
        // [1]: https://cloud.google.com/bigquery/docs/reference/standard-sql/window-function-calls

        if (exp.arg_types.ContainsKey("alias") && exp.GetType().Name != "Window")
        {
            exp.set("alias", alias_identifier);
            return exp;
        }
        return new Alias(@this: exp, alias: alias_identifier);
    }

    public abstract partial class Expr
    {
        // py: sqlglot/expressions/core.py::Expression.and_ #6a61f7bb48
        public Expr and_(object?[] expressions, object? dialect = null, bool copy = true, bool wrap = true) =>
            exp.and_([this, .. expressions], dialect: dialect, copy: copy, wrap: wrap);

        public Expr and_(params object?[] expressions) => and_(expressions, null, true, true);

        // py: sqlglot/expressions/core.py::Expression.or_ #8cc5f3932f
        public Expr or_(object?[] expressions, object? dialect = null, bool copy = true, bool wrap = true) =>
            exp.or_([this, .. expressions], dialect: dialect, copy: copy, wrap: wrap);

        public Expr or_(params object?[] expressions) => or_(expressions, null, true, true);

        // py: sqlglot/expressions/core.py::Expression.not_ #20e9f62770
        public Not not_(bool copy = true) => exp.not_(this, copy: copy);

        // py: sqlglot/expressions/core.py::Expression.as_ #fa1308a896
        public Expr as_(object? alias, bool? quoted = null, object? dialect = null, bool copy = true, object? table = null) =>
            alias_(this, alias, quoted: quoted, dialect: dialect, copy: copy, table: table);

        // py: sqlglot/expressions/core.py::Expression._binop #970fd5ad05
        public Expr _binop(Type klass, object? other, bool reverse = false)
        {
            Expr @this = copy();
            Expr other_expression = convert(other, copy: true);
            if (!klass.IsInstanceOfType(@this) && !klass.IsInstanceOfType(other_expression))
            {
                @this = _wrap(@this, typeof(Binary))!;
                other_expression = _wrap(other_expression, typeof(Binary))!;
            }
            if (reverse)
                return _new__helper(klass, ("this", other_expression), ("expression", @this));
            return _new__helper(klass, ("this", @this), ("expression", other_expression));
        }

        // py: sqlglot/expressions/core.py::Expression.isin #0913f5cc8d
        public In isin(object?[] expressions, object? query = null, object? unnest = null, object? dialect = null, bool copy = true)
        {
            Expr? subquery = null;
            if (py.truthy(query))
            {
                subquery = maybe_parse(query, dialect: dialect, copy: copy);
                if (subquery is Query)
                    subquery = subquery.subquery(copy: false);
            }

            var unnest_list = helper.ensure_list(unnest);
            return new In(
                @this: maybe_copy(this, copy),
                expressions: expressions.Select(e => (object?)convert(e, copy: copy)).ToList(),
                query: subquery,
                unnest: py.truthy(unnest)
                    ? _lazy_unnest(new PyKwargs
                    {
                        ["expressions"] = unnest_list.Cast<object?>().Select(e => (object?)maybe_parse(e, dialect: dialect, copy: copy)).ToList(),
                    })
                    : null);
        }

        public In isin(params object?[] expressions) => isin(expressions, null, null, null, true);

        // py: sqlglot/expressions/core.py::Expression.between #19e45fc35b
        public Between between(object? low, object? high, bool copy = true, bool? symmetric = null)
        {
            var between = new Between(
                @this: maybe_copy(this, copy),
                low: convert(low, copy: copy),
                high: convert(high, copy: copy));
            if (symmetric is not null)
                between.set("symmetric", symmetric);
            return between;
        }

        // py: sqlglot/expressions/core.py::Expression.is_ #43ff801369
        public Is is_(object? other) => (Is)_binop(typeof(Is), other);

        // py: sqlglot/expressions/core.py::Expression.like #2009914238
        public Like like(object? other) => (Like)_binop(typeof(Like), other);

        // py: sqlglot/expressions/core.py::Expression.ilike #7e729e9108
        public ILike ilike(object? other) => (ILike)_binop(typeof(ILike), other);

        // py: sqlglot/expressions/core.py::Expression.eq #cfacd7c5c2
        public EQ eq(object? other) => (EQ)_binop(typeof(EQ), other);

        // py: sqlglot/expressions/core.py::Expression.neq #c8c3b08d39
        public NEQ neq(object? other) => (NEQ)_binop(typeof(NEQ), other);

        // py: sqlglot/expressions/core.py::Expression.rlike #43f3484a8a
        public RegexpLike rlike(object? other) => (RegexpLike)_binop(typeof(RegexpLike), other);

        // py: sqlglot/expressions/core.py::Expression.div #69e128c59d
        public Div div(object? other, bool typed = false, bool safe = false)
        {
            var div = (Div)_binop(typeof(Div), other);
            div.set("typed", typed);
            div.set("safe", safe);
            return div;
        }

        // py: sqlglot/expressions/core.py::Expression.asc #0f4aaec800
        public Ordered asc(bool nulls_first = true) => new(@this: copy(), nulls_first: nulls_first);

        // py: sqlglot/expressions/core.py::Expression.desc #5702024e30
        public Ordered desc(bool nulls_first = false) => new(@this: copy(), desc: true, nulls_first: nulls_first);
    }
}
