using System.Numerics;
using DType = SqlGlotSharp.exp.DType;

namespace SqlGlotSharp.optimizer;

// Port of sqlglot/optimizer/simplify.py. Date/interval arithmetic (python-dateutil relativedelta),
// DATE_TRUNC range rewrites and constant propagation (optimizer/normalize.py) are explicit
// NotPortedException boundaries; every other rule is ported.
public static class simplify_module
{
    // py: sqlglot/optimizer/simplify.py::logger #12092c5f48
    public static py.logging.Logger logger = py.logging.getLogger("sqlglot");

    // Final means that an expression should not be simplified
    public const string FINAL = "final";

    // py: sqlglot/optimizer/simplify.py::simplify
    public static exp.Expr simplify(exp.Expr expression, bool constant_propagation = false,
        bool coalesce_simplification = false, object? dialect = null) =>
        new Simplifier(dialect).simplify(expression, constant_propagation, coalesce_simplification);

    internal static bool IsSimplifiable(exp.Expr node) =>
        node is exp.Binary or exp.Func or exp.Lambda or exp.Predicate or exp.Unary;

    // py: sqlglot/optimizer/simplify.py::flatten
    /// <summary>A AND (B AND C) -> A AND B AND C; A OR (B OR C) -> A OR B OR C</summary>
    public static exp.Expr flatten(exp.Expr expression)
    {
        if (expression is exp.Connector)
            foreach (var node in expression.args.Values.ToList())
                if (node is exp.Expr n)
                {
                    var child = n.unnest();
                    if (child.GetType() == expression.GetType()) n.replace(child);
                }
        return expression;
    }

    // py: sqlglot/optimizer/simplify.py::_is_pure_mul_chain
    private static bool _is_pure_mul_chain(exp.Expr node)
    {
        var factor_types = parser.Parser.FACTOR_.Values.ToList();
        while (factor_types.Any(t => t.IsInstanceOfType(node)))
        {
            if (node is not exp.Mul) return false;
            node = (exp.Expr)node.args["this"]!;
        }
        return true;
    }

    // py: sqlglot/optimizer/simplify.py::simplify_parens
    public static exp.Expr simplify_parens(exp.Expr expression, dialects.dialect.Dialect dialect)
    {
        if (expression is not exp.Paren) return expression;

        var @this = (exp.Expr)expression.args["this"]!;
        var parent = expression.parent;
        var parent_is_predicate = parent is exp.Predicate;

        if (@this is exp.Select) return expression;

        if (parent is exp.SubqueryPredicate or exp.Bracket) return expression;

        if (dialect.REQUIRES_PARENTHESIZED_STRUCT_ACCESS && parent is exp.Dot dot
            && dot.args.get("expression") is exp.Identifier or exp.Star)
            return expression;

        if (@this is exp.Predicate or exp.Not)
        {
            if (parent_is_predicate
                // unary operators that bind tighter than the predicate, unlike NOT
                || parent is exp.Neg or exp.BitwiseNot
                || (parent is exp.Binary && parent is not exp.Connector))
                return expression;
            return @this;
        }

        if (!(parent is exp.Condition or exp.Binary)
            || parent is exp.Paren
            || @this is not exp.Binary
            || (@this is exp.Add && parent is exp.Add)
            || (@this is exp.Mul && parent is exp.Mul
                // a left operand never regroups when unwrapped; a right operand does unless pure Mul
                && (ReferenceEquals(parent.args.get("this"), expression) || _is_pure_mul_chain((exp.Expr)@this.args["this"]!)))
            || (@this is exp.Mul && parent is exp.Add or exp.Sub))
            return @this;

        return expression;
    }

    internal static bool _is_number(exp.Expr expression) => expression.is_number;

    internal static bool _is_nonnull_constant(exp.Expr expression) =>
        expression is exp.Literal or exp.Boolean || _is_date_literal(expression);

    internal static bool _is_constant(exp.Expr expression)
    {
        var expr = expression is exp.Neg ? (exp.Expr)expression.args["this"]! : expression;
        return expr is exp.Literal or exp.Boolean or exp.Null || _is_date_literal(expr);
    }

    internal static bool always_true(object? expression) =>
        (expression is exp.Boolean b && py.truthy(b.args.get("this")))
        || (expression is exp.Literal lit && lit.is_number && !is_zero(lit));

    internal static bool always_false(object? expression) => is_false(expression) || is_null(expression) || is_zero(expression);

    internal static bool is_zero(object? expression) =>
        expression is exp.Literal lit && lit.to_py() is { } v && NumberValue.IsZero(v);

    internal static bool is_false(object? a) => a is exp.Boolean b && b.GetType() == typeof(exp.Boolean) && !py.truthy(b.args.get("this"));

    internal static bool is_null(object? a) => a is not null && a.GetType() == typeof(exp.Null);

    // py: sqlglot/optimizer/simplify.py::_parenthesize_nested_connector
    internal static exp.Expr _parenthesize_nested_connector(exp.Expr expression, exp.Expr? parent)
    {
        if (expression is exp.Connector
            && (parent is exp.Not || (parent is exp.Connector && parent.GetType() != expression.GetType())))
            return new exp.Paren(@this: expression);

        return expression;
    }

    // py: sqlglot/optimizer/simplify.py::boolean_literal
    internal static exp.Boolean boolean_literal(bool condition) => new(@this: condition);

    // py: sqlglot/optimizer/simplify.py::eval_boolean
    internal static exp.Boolean? eval_boolean(exp.Expr expression, object a, object b)
    {
        var cmp = NumberValue.Compare(a, b);
        if (expression is exp.EQ or exp.Is)
        {
            if (expression is exp.Is && py.truthy(expression.args.get("negate"))) return boolean_literal(cmp != 0);
            return boolean_literal(cmp == 0);
        }
        if (expression is exp.NEQ) return boolean_literal(cmp != 0);
        if (expression is exp.GT) return boolean_literal(cmp > 0);
        if (expression is exp.GTE) return boolean_literal(cmp >= 0);
        if (expression is exp.LT) return boolean_literal(cmp < 0);
        if (expression is exp.LTE) return boolean_literal(cmp <= 0);
        return null;
    }

    // py: sqlglot/optimizer/simplify.py::extract_date (casts of ISO date literals); intervals are not ported
    internal static DateTime? extract_date(exp.Expr cast)
    {
        exp.DataType to;
        if (cast is exp.Cast c) to = c.to;
        else if (cast is exp.TsOrDsToDate && !py.truthy(cast.args.get("format"))) to = exp.DataType.build(DType.DATE);
        else return null;

        object? value;
        var inner = cast.args.get("this");
        if (inner is exp.Literal literal) value = literal.name;
        else if (inner is exp.Cast or exp.TsOrDsToDate) value = extract_date((exp.Expr)inner);
        else return null;

        if (value is null || (value is string s && s.Length == 0)) return null;
        DateTime? parsed = value is DateTime dt ? dt : helper.is_iso_datetime((string)value) ? DateTime.Parse((string)value, System.Globalization.CultureInfo.InvariantCulture) : null;
        if (parsed is null) return null;
        if (to.is_type(DType.DATE)) return parsed.Value.Date;
        if (to.is_type([.. exp.DataType.TEMPORAL_TYPES_.Cast<object>()])) return parsed;
        return null;
    }

    internal static bool _is_date_literal(exp.Expr expression) => extract_date(expression) is not null;

    // py: sqlglot/optimizer/simplify.py::_is_interval
    internal static bool _is_interval(exp.Expr expression) =>
        expression is exp.Interval ? throw new NotPortedException("sqlglot/optimizer/simplify.py::extract_interval (dateutil relativedelta)") : false;

    /// <summary>Python-style numeric helpers over the long/BigInteger/double values returned by <c>to_py()</c>.</summary>
    internal static class NumberValue
    {
        public static bool IsInt(object v) => v is long or int or BigInteger;
        public static BigInteger Big(object v) => v switch { long l => l, int i => i, BigInteger b => b, _ => throw new ArgumentException() };
        public static double Dbl(object v) => v switch { long l => l, int i => i, BigInteger b => (double)b, double d => d, _ => throw new ArgumentException() };
        public static bool IsZero(object v) => IsInt(v) ? Big(v).IsZero : v is double d && d == 0;
        public static int Compare(object a, object b)
        {
            if (a is string sa && b is string sb) return string.CompareOrdinal(sa, sb);
            if (a is DateTime da && b is DateTime db) return da.CompareTo(db);
            if (IsInt(a) && IsInt(b)) return Big(a).CompareTo(Big(b));
            return Dbl(a).CompareTo(Dbl(b));
        }
    }
}

public partial class Simplifier
{
    // Value ranges for byte-sized signed/unsigned integers
    private const int TINYINT_MIN = -128, TINYINT_MAX = 127, UTINYINT_MIN = 0, UTINYINT_MAX = 255;

    private static readonly Dictionary<Type, Type> COMPLEMENT_COMPARISONS = new()
    {
        [typeof(exp.LT)] = typeof(exp.GTE), [typeof(exp.GT)] = typeof(exp.LTE), [typeof(exp.LTE)] = typeof(exp.GT),
        [typeof(exp.GTE)] = typeof(exp.LT), [typeof(exp.EQ)] = typeof(exp.NEQ), [typeof(exp.NEQ)] = typeof(exp.EQ),
    };

    private static readonly Dictionary<Type, Type> COMPLEMENT_SUBQUERY_PREDICATES = new()
    {
        [typeof(exp.All)] = typeof(exp.Any), [typeof(exp.Any)] = typeof(exp.All),
    };

    private static readonly Type[] COMPARISONS =
        [typeof(exp.LT), typeof(exp.LTE), typeof(exp.GT), typeof(exp.GTE), typeof(exp.EQ), typeof(exp.NEQ), typeof(exp.Is)];

    private static readonly Type[] CONNECTOR_COMBINABLE = [typeof(exp.Boolean), typeof(exp.Literal), typeof(exp.Null), .. COMPARISONS];

    private static readonly Dictionary<Type, Type> INVERSE_COMPARISONS = new()
    {
        [typeof(exp.LT)] = typeof(exp.GT), [typeof(exp.GT)] = typeof(exp.LT),
        [typeof(exp.LTE)] = typeof(exp.GTE), [typeof(exp.GTE)] = typeof(exp.LTE),
    };

    private static readonly Type[] NONDETERMINISTIC = [typeof(exp.Rand), typeof(exp.Randn)];

    private static readonly Dictionary<Type, Type> INVERSE_DATE_OPS = new()
    {
        [typeof(exp.DateAdd)] = typeof(exp.Sub), [typeof(exp.DateSub)] = typeof(exp.Add),
        [typeof(exp.DatetimeAdd)] = typeof(exp.Sub), [typeof(exp.DatetimeSub)] = typeof(exp.Add),
    };

    private static readonly Dictionary<Type, Type> INVERSE_OPS = new(INVERSE_DATE_OPS)
    {
        [typeof(exp.Add)] = typeof(exp.Sub), [typeof(exp.Sub)] = typeof(exp.Add),
    };

    private static readonly Type[] NULL_OK = [typeof(exp.NullSafeEQ), typeof(exp.NullSafeNEQ), typeof(exp.PropertyEQ)];
    private static readonly Type[] CONCATS = [typeof(exp.Concat), typeof(exp.DPipe)];
    private static readonly Type[] DATETRUNCS = [typeof(exp.DateTrunc), typeof(exp.TimestampTrunc)];
    private static readonly Type[] SAFE_CONNECTOR_ELIMINATION_RESULT = [typeof(exp.Connector), typeof(exp.Boolean)];

    private static bool IsAny(object? node, Type[] types) => node is not null && types.Any(t => t.IsInstanceOfType(node));

    public readonly dialects.dialect.Dialect dialect;
    public readonly bool annotate_new_expressions;
    private readonly TypeAnnotator _annotator;

    // py: sqlglot/optimizer/simplify.py::Simplifier.__init__
    public Simplifier(object? dialect = null, bool annotate_new_expressions = true)
    {
        this.dialect = dialects.dialect.Dialect.get_or_raise(dialect);
        this.annotate_new_expressions = annotate_new_expressions;
        _annotator = new TypeAnnotator(new typing.ExpressionSchema(this.dialect), overwrite_types: false);
    }

    // py: sqlglot/optimizer/simplify.py::annotate_types_on_change
    private exp.Expr? OnChange(exp.Expr expression, exp.Expr? new_expression)
    {
        if (new_expression is null) return null;

        if (annotate_new_expressions && !expression.Equals(new_expression))
        {
            _annotator.clear();

            // We annotate this to ensure new children nodes are also annotated
            new_expression = _annotator.annotate(new_expression, annotate_scope: false);

            // Whatever expression the original expression is transformed into needs to preserve
            // the original type, otherwise the simplification could result in a different schema
            new_expression.type = expression.type;
        }

        return new_expression;
    }

    // py: sqlglot/optimizer/simplify.py::while_changing
    private static exp.Expr while_changing(exp.Expr expression, Func<exp.Expr, exp.Expr> func)
    {
        while (true)
        {
            var start_hash = expression.GetHashCode();
            expression = func(expression);
            var end_hash = expression.GetHashCode();

            if (start_hash == end_hash) break;
        }
        return expression;
    }

    // py: sqlglot/optimizer/simplify.py::Simplifier.simplify
    public exp.Expr simplify(exp.Expr expression, bool constant_propagation = false, bool coalesce_simplification = false)
    {
        var wheres = new List<exp.Where>();
        var joins = new List<exp.Join>();

        foreach (var node in expression.walk(prune: n => n is exp.Condition || py.truthy(n.meta_get(simplify_module.FINAL))).ToList())
        {
            if (py.truthy(node.meta_get(simplify_module.FINAL))) continue;

            // group by expressions cannot be simplified, for example
            // select x + 1 + 1 FROM y GROUP BY x + 1 + 1
            // the projection must exactly match the group by key
            var group = node.args.get("group") as exp.Expr;

            if (group is not null && node is exp.Select or exp.SetOperation or exp.Subquery)
            {
                var groups = new HashSet<exp.Expr>(group.expressions.Cast<exp.Expr>());
                group.meta[simplify_module.FINAL] = true;

                foreach (var s in generators.dialect_helpers.selects(node))
                    foreach (var n in s.walk())
                        if (groups.Contains(n))
                        {
                            s.meta[simplify_module.FINAL] = true;
                            break;
                        }

                if (node.args.get("having") is exp.Expr having)
                    foreach (var n in having.walk())
                        if (groups.Contains(n))
                        {
                            having.meta[simplify_module.FINAL] = true;
                            break;
                        }
            }

            if (node is exp.Condition)
            {
                var simplified = while_changing(node, e => _simplify(e, constant_propagation, coalesce_simplification));

                if (ReferenceEquals(node, expression)) expression = simplified;
            }
            else if (node is exp.Where where) wheres.Add(where);
            else if (node is exp.Join join)
            {
                // snowflake match_conditions have very strict ordering rules
                if (join.args.get("match_condition") is exp.Expr match) match.meta[simplify_module.FINAL] = true;

                joins.Add(join);
            }
        }

        foreach (var where in wheres)
            if (simplify_module.always_true(where.args.get("this")) && where.parent is not exp.Filter)
                where.pop();

        foreach (var join in joins)
        {
            // Only an inner join can become a CROSS JOIN: a cross join is empty as soon as either
            // side is empty, whereas an outer join keeps the rows of its outer side and pads them with NULLs.
            if (simplify_module.always_true(join.args.get("on"))
                && !py.truthy(join.args.get("using"))
                && !py.truthy(join.args.get("method"))
                && !py.truthy(join.args.get("side"))
                && py.str(join.args.get("kind")).ToUpperInvariant() is "" or "INNER")
            {
                ((exp.Expr)join.args["on"]!).pop();
                join.set("side", null);
                join.set("kind", "CROSS");
            }
        }

        return expression;
    }

    // py: sqlglot/optimizer/simplify.py::Simplifier._simplify
    private exp.Expr _simplify(exp.Expr expression, bool constant_propagation, bool coalesce_simplification)
    {
        var pre_transformation_stack = new Stack<exp.Expr>();
        pre_transformation_stack.Push(expression);
        var post_transformation_stack = new Stack<(exp.Expr, exp.Expr?)>();
        exp.Expr node = expression;

        while (pre_transformation_stack.Count > 0)
        {
            var original = pre_transformation_stack.Pop();
            node = original;

            if (!simplify_module.IsSimplifiable(node))
            {
                if (node is exp.Query) simplify(node, constant_propagation, coalesce_simplification);
                continue;
            }

            var parent = node.parent;
            var root = ReferenceEquals(node, expression);

            node = rewrite_between(node);
            node = uniq_sort(node, root);
            node = absorb_and_eliminate(node, root);
            node = simplify_concat(node);
            node = simplify_conditionals(node);

            if (constant_propagation)
                throw new NotPortedException("sqlglot/optimizer/simplify.py::propagate_constants (optimizer/normalize.py)");

            if (!ReferenceEquals(node, original)) original.replace(node);

            foreach (var n in node.iter_expressions(reverse: true))
                if (!py.truthy(n.meta_get(simplify_module.FINAL))) pre_transformation_stack.Push(n);
            post_transformation_stack.Push((node, parent));
        }

        while (post_transformation_stack.Count > 0)
        {
            var (original, parent) = post_transformation_stack.Pop();
            var root = ReferenceEquals(original, expression);

            // Resets parent, arg_key, index pointers: needed because some of the previous
            // transformations mutate the AST, leading to an inconsistent state.
            foreach (var (k, v) in original.args.ToList())
            {
                if (v is null) original.args.Remove(k);
                else original._set_parent(k, v);
            }

            // Post-order transformations
            node = simplify_not(original);
            node = simplify_module.flatten(node);
            node = simplify_connectors(node, root);
            node = remove_complements(node, root);

            if (coalesce_simplification) node = simplify_coalesce(node);
            node.parent = parent;

            node = simplify_literals(node, root);
            node = simplify_equality(node);
            node = simplify_module.simplify_parens(node, dialect);
            node = simplify_datetrunc(node);
            node = sort_comparison(node);
            node = simplify_startswith(node);

            if (!ReferenceEquals(node, original)) original.replace(node);
        }

        return node;
    }

    // py: sqlglot/optimizer/simplify.py::Simplifier.rewrite_between
    /// <summary>Rewrite x between y and z to x >= y AND x &lt;= z.</summary>
    public exp.Expr rewrite_between(exp.Expr expression)
    {
        if (expression is exp.Between)
        {
            var negate = expression.parent is exp.Not;

            var low = new exp.GTE(@this: ((exp.Expr)expression.args["this"]!).copy(), expression: expression.args["low"]);
            var high = new exp.LTE(@this: ((exp.Expr)expression.args["this"]!).copy(), expression: expression.args["high"]);
            exp.Expr result = generators.dialect_helpers.and_(low, high)!;

            if (negate) result = new exp.Paren(@this: result);
            return OnChange(expression, result)!;
        }
        return expression;
    }

    // py: sqlglot/optimizer/simplify.py::Simplifier.simplify_not
    /// <summary>Demorgan's Law: NOT (x OR y) -> NOT x AND NOT y; NOT (x AND y) -> NOT x OR NOT y</summary>
    public exp.Expr simplify_not(exp.Expr expression) => OnChange(expression, SimplifyNot(expression))!;

    private exp.Expr SimplifyNot(exp.Expr expression)
    {
        if (expression is exp.Not)
        {
            var @this = (exp.Expr)expression.args["this"]!;
            if (simplify_module.is_null(@this))
                return simplify_module._parenthesize_nested_connector(
                    generators.dialect_helpers.and_(new exp.Null(), new exp.Boolean(@this: true))!, expression.parent);
            if (COMPLEMENT_COMPARISONS.TryGetValue(@this.GetType(), out var complement))
            {
                var right = @this.args["expression"];
                if (right is not null && COMPLEMENT_SUBQUERY_PREDICATES.TryGetValue(right.GetType(), out var complement_subquery_predicate))
                    right = Activator.CreateInstance(complement_subquery_predicate, [new PyDict<string, object?> { ["this"] = ((exp.Expr)right).args.get("this") }]);

                return new exp.Paren(@this: Activator.CreateInstance(complement,
                    [new PyDict<string, object?> { ["this"] = @this.args.get("this"), ["expression"] = right }]));
            }
            if (@this is exp.Paren)
            {
                var condition = @this.unnest();
                if (condition is exp.And)
                    return new exp.Paren(@this: generators.dialect_helpers.or_(
                        new exp.Not(@this: condition.args["this"]), new exp.Not(@this: condition.args["expression"])));
                if (condition is exp.Or)
                    return new exp.Paren(@this: generators.dialect_helpers.and_(
                        new exp.Not(@this: condition.args["this"]), new exp.Not(@this: condition.args["expression"])));
                if (simplify_module.is_null(condition))
                    return simplify_module._parenthesize_nested_connector(
                        generators.dialect_helpers.and_(new exp.Null(), new exp.Boolean(@this: true))!, expression.parent);
            }
            if (simplify_module.always_true(@this)) return new exp.Boolean(@this: false);
            if (simplify_module.is_false(@this)) return new exp.Boolean(@this: true);
            if (@this is exp.Not && dialect.SAFE_TO_ELIMINATE_DOUBLE_NEGATION)
            {
                var inner = (exp.Expr)@this.args["this"]!;
                if (inner.is_type(DType.BOOLEAN))
                    // double negation: NOT NOT x -> x, if x is BOOLEAN type
                    return inner;
            }
        }
        return expression;
    }

    // py: sqlglot/optimizer/simplify.py::Simplifier.simplify_connectors
    public exp.Expr simplify_connectors(exp.Expr expression, bool root = true)
    {
        if (expression is exp.Connector)
        {
            var original_parent = expression.parent;
            var result = _flat_simplify(expression, (e, left, right) => _simplify_connectors(e, left, right), root);

            // If we reduced a connector to, e.g., a column (t1 AND ... AND tn -> Tk), then we need
            // to ensure that the resulting type is boolean.
            if (!IsAny(result, SAFE_CONNECTOR_ELIMINATION_RESULT) && !result.is_type(DType.BOOLEAN))
            {
                while (true)
                {
                    if (original_parent is exp.Connector) break;
                    if (original_parent is not exp.Paren)
                    {
                        result = new exp.And(@this: result, expression: new exp.Boolean(@this: true));
                        break;
                    }

                    original_parent = original_parent.parent;
                }
            }
            return OnChange(expression, result)!;
        }
        return expression;
    }

    private exp.Expr? _simplify_connectors(exp.Expr expression, exp.Expr left, exp.Expr right)
    {
        if (expression is exp.And)
        {
            if (simplify_module.is_false(left) || simplify_module.is_false(right)) return new exp.Boolean(@this: false);
            if (simplify_module.is_zero(left) || simplify_module.is_zero(right)) return new exp.Boolean(@this: false);
            if ((simplify_module.is_null(left) && simplify_module.is_null(right))
                || (simplify_module.is_null(left) && simplify_module.always_true(right))
                || (simplify_module.always_true(left) && simplify_module.is_null(right)))
                return new exp.Null();
            if (simplify_module.always_true(left) && simplify_module.always_true(right)) return new exp.Boolean(@this: true);
            if (simplify_module.always_true(left)) return right;
            if (simplify_module.always_true(right)) return left;
            return _simplify_comparison(expression, left, right);
        }
        if (expression is exp.Or)
        {
            if (simplify_module.always_true(left) || simplify_module.always_true(right)) return new exp.Boolean(@this: true);
            if ((simplify_module.is_null(left) && simplify_module.is_null(right))
                || (simplify_module.is_null(left) && simplify_module.always_false(right))
                || (simplify_module.always_false(left) && simplify_module.is_null(right)))
                return new exp.Null();
            if (simplify_module.is_false(left)) return right;
            if (simplify_module.is_false(right)) return left;
            return _simplify_comparison(expression, left, right, or_: true);
        }
        return null;
    }

    // py: sqlglot/optimizer/simplify.py::Simplifier._simplify_comparison
    private exp.Expr? _simplify_comparison(exp.Expr expression, exp.Expr left, exp.Expr right, bool or_ = false)
    {
        var result = SimplifyComparison(expression, left, right, or_);
        return OnChange(expression, result);
    }

    private exp.Expr? SimplifyComparison(exp.Expr expression, exp.Expr left, exp.Expr right, bool or_)
    {
        if (IsAny(left, COMPARISONS) && IsAny(right, COMPARISONS))
        {
            if (new[] { left, right }.Any(e => e is exp.Is && py.truthy(e.args.get("negate")))) return null;

            var ll = (exp.Expr)left.args["this"]!;
            var lr = (exp.Expr)left.args["expression"]!;
            var rl = (exp.Expr)right.args["this"]!;
            var rr = (exp.Expr)right.args["expression"]!;

            var largs = new List<exp.Expr>();
            foreach (var e in new[] { ll, lr }) if (!largs.Contains(e)) largs.Add(e);
            var rargs = new List<exp.Expr>();
            foreach (var e in new[] { rl, rr }) if (!rargs.Contains(e)) rargs.Add(e);

            var matching = largs.Where(rargs.Contains).ToList();
            var columns = matching.Where(m => !simplify_module._is_constant(m) && m.find(NONDETERMINISTIC) is null).ToList();

            if (matching.Count > 0 && columns.Count > 0)
            {
                exp.Expr? l = largs.FirstOrDefault(a => !columns.Contains(a));
                exp.Expr? r = rargs.FirstOrDefault(a => !columns.Contains(a));
                if (l is null || r is null) return expression;

                object lv, rv;
                if (l.is_number && r.is_number)
                {
                    lv = l.to_py()!;
                    rv = r.to_py()!;
                }
                else if (l.is_string && r.is_string)
                {
                    lv = l.name;
                    rv = r.name;
                }
                else
                {
                    var ld = simplify_module.extract_date(l);
                    if (ld is null) return null;
                    var rd = simplify_module.extract_date(r);
                    if (rd is null) return null;
                    // python won't compare date and datetime, but many engines will upcast
                    lv = ld.Value;
                    rv = rd.Value;
                }

                exp.Expr? @false = Equals(left.meta_get("nonnull"), true) && Equals(right.meta_get("nonnull"), true)
                    ? new exp.Boolean(@this: false) : null;

                foreach (var ((a, av), (b, bv)) in new[] { ((left, lv), (right, rv)), ((right, rv), (left, lv)) })
                {
                    var cmp = simplify_module.NumberValue.Compare(av, bv);
                    if (a is exp.LT or exp.LTE && b is exp.LT or exp.LTE)
                        return or_ ? (cmp > 0 ? left : right) : (cmp <= 0 ? left : right);
                    if (a is exp.GT or exp.GTE && b is exp.GT or exp.GTE)
                        return or_ ? (cmp < 0 ? left : right) : (cmp >= 0 ? left : right);

                    if (!or_)
                    {
                        if (a is exp.LT && b is exp.GT or exp.GTE)
                        {
                            if (cmp <= 0) return @false;
                        }
                        else if (a is exp.GT && b is exp.LT or exp.LTE)
                        {
                            if (cmp >= 0) return @false;
                        }
                        else if (a is exp.EQ)
                        {
                            if (b is exp.LT) return cmp >= 0 ? @false : a;
                            if (b is exp.LTE) return cmp > 0 ? @false : a;
                            if (b is exp.GT) return cmp <= 0 ? @false : a;
                            if (b is exp.GTE) return cmp < 0 ? @false : a;
                            if (b is exp.NEQ) return cmp == 0 ? @false : a;
                        }
                    }
                }
            }
        }
        return null;
    }

    // py: sqlglot/optimizer/simplify.py::Simplifier.remove_complements
    /// <summary>A AND NOT A -> FALSE (only for non-NULL A); A OR NOT A -> TRUE (only for non-NULL A)</summary>
    public exp.Expr remove_complements(exp.Expr expression, bool root = true)
    {
        if (expression is exp.And or exp.Or && (root || !expression.same_parent))
        {
            var ops = new HashSet<exp.Expr>(expression.flatten());
            foreach (var op in ops)
                if (op is exp.Not && ops.Contains((exp.Expr)op.args["this"]!))
                    if (Equals(expression.meta_get("nonnull"), true))
                        return OnChange(expression, expression is exp.And ? new exp.Boolean(@this: false) : new exp.Boolean(@this: true))!;
        }
        return expression;
    }

    // py: sqlglot/optimizer/simplify.py::Simplifier.uniq_sort
    /// <summary>Uniq and sort a connector: C AND A AND B AND B -> A AND B AND C</summary>
    public exp.Expr uniq_sort(exp.Expr expression, bool root = true)
    {
        if (expression is exp.Connector && (root || !expression.same_parent))
        {
            var flattened = expression.flatten().ToList();

            Func<IEnumerable<exp.Expr>, exp.Expr> result_func;
            Dictionary<string, exp.Expr>? deduped;
            List<(string, exp.Expr)> arr;
            if (expression is exp.Xor)
            {
                result_func = es => generators.dialect_helpers.xor_(es.ToArray())!;
                // Do not deduplicate XOR as A XOR A != A if A == True
                deduped = null;
                arr = flattened.Select(e => (SimplifyGen.gen(e), e)).ToList();
            }
            else
            {
                result_func = expression is exp.And ? es => generators.dialect_helpers.and_(es.ToArray())! : es => generators.dialect_helpers.or_(es.ToArray())!;
                deduped = new Dictionary<string, exp.Expr>();
                foreach (var e in flattened) deduped[SimplifyGen.gen(e)] = e;
                arr = deduped.Select(kv => (kv.Key, kv.Value)).ToList();
            }

            // check if the operands are already sorted, if not sort them: A AND C AND B -> A AND B AND C
            var sorted_needed = false;
            for (var i = 1; i < arr.Count; i++)
                if (string.CompareOrdinal(arr[i].Item1, arr[i - 1].Item1) < 0) { sorted_needed = true; break; }

            exp.Expr result = expression;
            if (sorted_needed)
                result = result_func(arr.OrderBy(x => x.Item1, StringComparer.Ordinal).Select(x => x.Item2).ToList());
            else if (deduped is not null && deduped.Count < flattened.Count)
            {
                // we didn't have to sort but maybe we need to dedup
                var unique_operand = flattened[0];
                result = deduped.Count == 1
                    ? new exp.And(@this: unique_operand, expression: new exp.Boolean(@this: true))
                    : result_func(deduped.Values.ToList());
            }
            return OnChange(expression, result)!;
        }
        return expression;
    }

    // py: sqlglot/optimizer/simplify.py::Simplifier.absorb_and_eliminate
    public exp.Expr absorb_and_eliminate(exp.Expr expression, bool root = true)
    {
        if (expression is exp.And or exp.Or && (root || !expression.same_parent))
        {
            var kind = expression is exp.And ? typeof(exp.Or) : typeof(exp.And);

            var ops = expression.flatten().ToList();

            // Set of all operands, used to find complements for absorption.
            var op_set = new HashSet<exp.Expr>();
            // Sub-operands, used to find subsets for absorption.
            var subops = new Dictionary<exp.Expr, List<HashSet<exp.Expr>>>();
            // Pairs of complements, used for elimination.
            var pairs = new Dictionary<(exp.Expr, exp.Expr), List<(exp.Expr, exp.Expr)>>(new UnorderedPairComparer());

            foreach (var op in ops)
            {
                op_set.Add(op);

                if (!kind.IsInstanceOfType(op))
                {
                    // In cases like: A OR (A AND B)
                    if (!subops.TryGetValue(op, out var list)) subops[op] = list = [];
                    list.Add([op]);
                    continue;
                }

                // In cases like: (A AND B) OR (A AND B AND C)
                var subset = new HashSet<exp.Expr>(op.flatten());
                foreach (var i in subset)
                {
                    if (!subops.TryGetValue(i, out var list)) subops[i] = list = [];
                    list.Add(subset);
                }

                var operands = op.unnest_operands();
                var a = operands[0];
                var b = operands[1];

                if (a is exp.Not && Equals(((exp.Expr)a.args["this"]!).meta_get("nonnull"), true))
                    AddPair(pairs, ((exp.Expr)a.args["this"]!, b), (op, b));
                if (b is exp.Not && Equals(((exp.Expr)b.args["this"]!).meta_get("nonnull"), true))
                    AddPair(pairs, (a, (exp.Expr)b.args["this"]!), (op, a));
            }

            foreach (var op in ops)
            {
                if (!kind.IsInstanceOfType(op)) continue;

                var operands = op.unnest_operands();
                var a = operands[0];
                var b = operands[1];

                // Absorb
                if (a is exp.Not && op_set.Contains((exp.Expr)a.args["this"]!) && Equals(((exp.Expr)a.args["this"]!).meta_get("nonnull"), true))
                {
                    a.replace(kind == typeof(exp.And) ? new exp.Boolean(@this: true) : new exp.Boolean(@this: false));
                    continue;
                }
                if (b is exp.Not && op_set.Contains((exp.Expr)b.args["this"]!) && Equals(((exp.Expr)b.args["this"]!).meta_get("nonnull"), true))
                {
                    b.replace(kind == typeof(exp.And) ? new exp.Boolean(@this: true) : new exp.Boolean(@this: false));
                    continue;
                }
                var superset = new HashSet<exp.Expr>(op.flatten());
                if (superset.Any(i => subops.TryGetValue(i, out var subs) && subs.Any(subset => subset.IsProperSubsetOf(superset))))
                {
                    op.replace(kind == typeof(exp.And) ? new exp.Boolean(@this: false) : new exp.Boolean(@this: true));
                    continue;
                }

                // Eliminate
                if (pairs.TryGetValue((a, b), out var found))
                    foreach (var (other, complement) in found.ToList())
                    {
                        op.replace(complement);
                        other.replace(complement);
                    }
            }
        }

        return expression;
    }

    private static void AddPair(Dictionary<(exp.Expr, exp.Expr), List<(exp.Expr, exp.Expr)>> pairs, (exp.Expr, exp.Expr) key, (exp.Expr, exp.Expr) value)
    {
        if (!pairs.TryGetValue(key, out var list)) pairs[key] = list = [];
        list.Add(value);
    }

    /// <summary>frozenset((a, b)) semantics: the pair is unordered.</summary>
    private sealed class UnorderedPairComparer : IEqualityComparer<(exp.Expr, exp.Expr)>
    {
        public bool Equals((exp.Expr, exp.Expr) x, (exp.Expr, exp.Expr) y) =>
            (x.Item1.Equals(y.Item1) && x.Item2.Equals(y.Item2)) || (x.Item1.Equals(y.Item2) && x.Item2.Equals(y.Item1));
        public int GetHashCode((exp.Expr, exp.Expr) p) => p.Item1.GetHashCode() ^ p.Item2.GetHashCode();
    }

    // py: sqlglot/optimizer/simplify.py::Simplifier.simplify_equality
    /// <summary>Use the subtraction and addition properties of equality: x + 1 = 3 becomes x = 2</summary>
    public exp.Expr simplify_equality(exp.Expr expression)
    {
        if (IsAny(expression, COMPARISONS))
        {
            var l = (exp.Expr)expression.args["this"]!;
            var r = (exp.Expr)expression.args["expression"]!;

            if (!INVERSE_OPS.ContainsKey(l.GetType())) return expression;

            Func<exp.Expr, bool> a_predicate, b_predicate;
            if (r.is_number)
            {
                a_predicate = simplify_module._is_number;
                b_predicate = simplify_module._is_number;
            }
            else if (simplify_module._is_date_literal(r))
                throw new NotPortedException("sqlglot/optimizer/simplify.py::simplify_equality (date arithmetic, dateutil relativedelta)");
            else return expression;

            exp.Expr a, b;
            if (INVERSE_DATE_OPS.ContainsKey(l.GetType()))
                throw new NotPortedException("sqlglot/optimizer/simplify.py::simplify_equality (date arithmetic, dateutil relativedelta)");
            a = (exp.Expr)l.args["this"]!;
            b = (exp.Expr)l.args["expression"]!;

            if (!a_predicate(a) && b_predicate(b)) { }
            else if (!a_predicate(b) && b_predicate(a))
            {
                if (l is exp.Sub)
                {
                    var cls = INVERSE_COMPARISONS.TryGetValue(expression.GetType(), out var inv) ? inv : expression.GetType();
                    return OnChange(expression, (exp.Expr)Activator.CreateInstance(cls, [new PyDict<string, object?>
                    {
                        ["this"] = b, ["expression"] = new exp.Sub(@this: a, expression: r),
                    }])!)!;
                }
                (a, b) = (b, a);
            }
            else return expression;

            var inverse = INVERSE_OPS[l.GetType()];
            return OnChange(expression, (exp.Expr)Activator.CreateInstance(expression.GetType(), [new PyDict<string, object?>
            {
                ["this"] = a,
                ["expression"] = Activator.CreateInstance(inverse, [new PyDict<string, object?> { ["this"] = r, ["expression"] = b }]),
            }])!)!;
        }
        return expression;
    }

    // py: sqlglot/optimizer/simplify.py::Simplifier.simplify_literals
    public exp.Expr simplify_literals(exp.Expr expression, bool root = true)
    {
        if (expression is exp.Binary && expression is not exp.Connector)
            return OnChange(expression, _flat_simplify(expression, _simplify_binary, root))!;

        if (expression is exp.Neg && expression.args.get("this") is exp.Neg inner)
            return OnChange(expression, (exp.Expr)inner.args["this"]!)!;

        if (INVERSE_DATE_OPS.ContainsKey(expression.GetType()))
            throw new NotPortedException("sqlglot/optimizer/simplify.py::simplify_literals (date arithmetic, dateutil relativedelta)");

        return expression;
    }

    private static exp.Expr _simplify_integer_cast(exp.Expr expr)
    {
        if (expr is exp.Cast cast)
        {
            var @this = expr.args.get("this") is exp.Cast inner ? _simplify_integer_cast(inner) : (exp.Expr)expr.args["this"]!;
            if (!@this.is_int) return expr;

            var num = simplify_module.NumberValue.Big(@this.to_py()!);

            // Remove the (up)cast from small (byte-sized) integers in predicates which is side-effect free. Downcasts on any
            // integer type might cause overflow, thus the cast cannot be eliminated and the behavior is engine-dependent
            var to_type = cast.to.args.get("this");
            if ((num >= TINYINT_MIN && num <= TINYINT_MAX && to_type is DType t1 && exp.DataType.SIGNED_INTEGER_TYPES_.Contains(t1))
                || (num >= UTINYINT_MIN && num <= UTINYINT_MAX && to_type is DType t2 && exp.DataType.UNSIGNED_INTEGER_TYPES_.Contains(t2)))
                return @this;
        }

        return expr;
    }

    private exp.Expr? _simplify_binary(exp.Expr expression, exp.Expr a, exp.Expr b)
    {
        if (IsAny(expression, COMPARISONS))
        {
            a = _simplify_integer_cast(a);
            b = _simplify_integer_cast(b);
        }

        if (expression is exp.Is)
        {
            exp.Expr c;
            bool not_;
            if (b is exp.Not) { c = (exp.Expr)b.args["this"]!; not_ = true; }
            else { c = b; not_ = false; }

            if (py.truthy(expression.args.get("negate"))) not_ = !not_;

            if (simplify_module.is_null(c))
            {
                if (a is exp.Literal) return new exp.Boolean(@this: not_);
                if (simplify_module.is_null(a)) return new exp.Boolean(@this: !not_);
            }
        }
        else if (IsAny(expression, NULL_OK)) return null;
        else if ((simplify_module.is_null(a) || simplify_module.is_null(b)) && expression.parent is exp.If) return new exp.Null();

        if (a.is_number && b.is_number)
        {
            var num_a = a.to_py()!;
            var num_b = b.to_py()!;
            var ints = simplify_module.NumberValue.IsInt(num_a) && simplify_module.NumberValue.IsInt(num_b);

            if (expression is exp.Add)
                return exp.Literal.number(ints ? simplify_module.NumberValue.Big(num_a) + simplify_module.NumberValue.Big(num_b)
                    : (object)(simplify_module.NumberValue.Dbl(num_a) + simplify_module.NumberValue.Dbl(num_b)));
            if (expression is exp.Mul)
                return exp.Literal.number(ints ? simplify_module.NumberValue.Big(num_a) * simplify_module.NumberValue.Big(num_b)
                    : (object)(simplify_module.NumberValue.Dbl(num_a) * simplify_module.NumberValue.Dbl(num_b)));

            // We only simplify Sub, Div if a and b have the same parent because they're not associative
            if (expression is exp.Sub)
                return ReferenceEquals(a.parent, b.parent)
                    ? exp.Literal.number(ints ? simplify_module.NumberValue.Big(num_a) - simplify_module.NumberValue.Big(num_b)
                        : (object)(simplify_module.NumberValue.Dbl(num_a) - simplify_module.NumberValue.Dbl(num_b)))
                    : null;
            if (expression is exp.Div)
            {
                // engines have differing int div behavior so intdiv is not safe
                if (ints || !ReferenceEquals(a.parent, b.parent) || simplify_module.NumberValue.IsZero(num_b)) return null;
                return exp.Literal.number(simplify_module.NumberValue.Dbl(num_a) / simplify_module.NumberValue.Dbl(num_b));
            }

            var boolean = simplify_module.eval_boolean(expression, num_a, num_b);
            if (boolean is not null) return boolean;
        }
        else if (a.is_string && b.is_string)
        {
            var boolean = simplify_module.eval_boolean(expression, a.args["this"]!, b.args["this"]!);
            if (boolean is not null) return boolean;
        }
        else if (simplify_module._is_date_literal(a) && b is exp.Interval || a is exp.Interval && simplify_module._is_date_literal(b))
            throw new NotPortedException("sqlglot/optimizer/simplify.py::_simplify_binary (date arithmetic, dateutil relativedelta)");
        else if (simplify_module._is_date_literal(a) && simplify_module._is_date_literal(b))
        {
            if (expression is exp.Predicate)
            {
                var boolean = simplify_module.eval_boolean(expression, simplify_module.extract_date(a)!.Value, simplify_module.extract_date(b)!.Value);
                if (boolean is not null) return boolean;
            }
        }

        return null;
    }

    // py: sqlglot/optimizer/simplify.py::Simplifier.simplify_coalesce
    public exp.Expr simplify_coalesce(exp.Expr expression)
    {
        // COALESCE(x) -> x
        if (expression is exp.Coalesce
            && (!(expression.args.get("expressions") is List<object?> { Count: > 0 }) || simplify_module._is_nonnull_constant((exp.Expr)expression.args["this"]!))
            // COALESCE is also used as a Spark partitioning hint
            && expression.parent is not exp.Hint)
            return OnChange(expression, (exp.Expr)expression.args["this"]!)!;

        if (dialect.COALESCE_COMPARISON_NON_STANDARD) return expression;
        if (!IsAny(expression, COMPARISONS)) return expression;

        var left = (exp.Expr)expression.args["this"]!;
        var right = (exp.Expr)expression.args["expression"]!;
        exp.Expr coalesce, other;
        if (left is exp.Coalesce) { coalesce = left; other = right; }
        else if (right is exp.Coalesce) { coalesce = right; other = left; }
        else return expression;

        // This transformation is valid for non-constants, but it really only does anything if they are both constants.
        if (!simplify_module._is_constant(other)) return expression;

        // Find the first non-NULL constant arg
        var coalesce_expressions = coalesce.expressions;
        var arg_index = coalesce_expressions.FindIndex(arg => simplify_module._is_nonnull_constant((exp.Expr)arg!));
        if (arg_index < 0) return expression;
        var arg = (exp.Expr)coalesce_expressions[arg_index]!;

        coalesce.set("expressions", coalesce_expressions.Take(arg_index).ToList());

        // Remove the COALESCE function. This is an optimization, skipping a simplify iteration.
        var @this = coalesce.expressions.Count > 0 ? coalesce : (exp.Expr)coalesce.args["this"]!;

        // The constant takes the COALESCE's side of the comparison
        var substituted = expression.copy();
        substituted.set(ReferenceEquals(coalesce, left) ? "this" : "expression", arg.copy());

        var d = generators.dialect_helpers.and_(
            new exp.Not(@this: new exp.Is(@this: @this, expression: new exp.Null())), expression.copy());
        var e = generators.dialect_helpers.and_(new exp.Is(@this: @this, expression: new exp.Null()), substituted);
        return OnChange(expression, new exp.Paren(@this: generators.dialect_helpers.or_(d!, e!)))!;
    }

    // py: sqlglot/optimizer/simplify.py::Simplifier.simplify_concat
    /// <summary>Reduces all groups that contain string literals by concatenating them.</summary>
    public exp.Expr simplify_concat(exp.Expr expression)
    {
        if (!IsAny(expression, CONCATS)
            // We can't reduce a CONCAT_WS call if we don't statically know the separator
            || (expression is exp.ConcatWs && !((exp.Expr)expression.expressions[0]!).is_string))
            return expression;

        exp.Expr? sep_expr = null;
        string sep;
        List<exp.Expr> expressions;
        Type concat_type;
        if (expression is exp.ConcatWs)
        {
            sep_expr = (exp.Expr)expression.expressions[0]!;
            sep = sep_expr.name;
            expressions = expression.expressions.Skip(1).Cast<exp.Expr>().ToList();
            concat_type = typeof(exp.ConcatWs);
        }
        else
        {
            expressions = expression.expressions.Cast<exp.Expr>().ToList();
            sep = "";
            concat_type = typeof(exp.Concat);
        }

        var safe = expression.args.get("safe");
        var coalesce = expression.args.get("coalesce");

        var source = expressions.Count > 0 ? expressions : expression.flatten(unnest: false).ToList();
        var new_args = new List<exp.Expr>();
        var i = 0;
        while (i < source.Count)
        {
            var is_string_group = source[i].is_string;
            var group = new List<exp.Expr>();
            while (i < source.Count && source[i].is_string == is_string_group) group.Add(source[i++]);
            if (is_string_group) new_args.Add(exp.Literal.@string(string.Join(sep, group.Select(s => s.name))));
            else new_args.AddRange(group);
        }

        if (new_args.Count == 1 && new_args[0].is_string) return OnChange(expression, new_args[0])!;

        if (concat_type == typeof(exp.ConcatWs)) new_args.Insert(0, sep_expr!);
        else if (expression is exp.DPipe)
        {
            var reduced = new_args.Aggregate((x, y) => new exp.DPipe(@this: x, expression: y, safe: safe));
            return OnChange(expression, reduced)!;
        }

        return OnChange(expression, (exp.Expr)Activator.CreateInstance(concat_type, [new PyDict<string, object?>
        {
            ["expressions"] = new_args.Cast<object?>().ToList(), ["safe"] = safe, ["coalesce"] = coalesce,
        }])!)!;
    }

    // py: sqlglot/optimizer/simplify.py::Simplifier.simplify_conditionals
    /// <summary>Simplifies expressions like IF, CASE if their condition is statically known.</summary>
    public exp.Expr simplify_conditionals(exp.Expr expression)
    {
        if (expression is exp.Case)
        {
            var @this = expression.args.get("this") as exp.Expr;
            foreach (var case_ in ((List<object?>)expression.args["ifs"]!).Cast<exp.Expr>().ToList())
            {
                var cond = (exp.Expr)case_.args["this"]!;
                if (@this is not null)
                {
                    // Convert CASE x WHEN matching_value ... to CASE WHEN x = matching_value ...
                    var eq = new exp.EQ(@this: @this.pop(), expression: cond.copy());
                    cond.replace(eq);
                    cond = eq;
                }

                if (simplify_module.always_true(cond))
                {
                    var ifs = (List<object?>)expression.args["ifs"]!;
                    var index = ifs.IndexOf(case_);
                    if (index == 0)
                        return OnChange(expression, new exp.Paren(@this: case_.args["true"]))!;

                    expression.set("default", case_.args["true"]);
                    expression.set("ifs", ifs.Take(index).ToList());
                    break;
                }

                if (simplify_module.always_false(cond))
                {
                    case_.pop();
                    if (!(expression.args.get("ifs") is List<object?> { Count: > 0 }))
                        return OnChange(expression, new exp.Paren(@this: expression.args.get("default") ?? new exp.Null()))!;
                }
            }
        }
        else if (expression is exp.If && expression.parent is not exp.Case)
        {
            if (simplify_module.always_true(expression.args.get("this")))
                return OnChange(expression, new exp.Paren(@this: expression.args["true"]))!;
            if (simplify_module.always_false(expression.args.get("this")))
                return OnChange(expression, new exp.Paren(@this: expression.args.get("false") ?? new exp.Null()))!;
        }

        return expression;
    }

    // py: sqlglot/optimizer/simplify.py::Simplifier.simplify_startswith
    public exp.Expr simplify_startswith(exp.Expr expression)
    {
        if (expression is exp.StartsWith && ((exp.Expr)expression.args["this"]!).is_string && ((exp.Expr)expression.args["expression"]!).is_string)
            return OnChange(expression, new exp.Boolean(@this: expression.name.StartsWith(((exp.Expr)expression.args["expression"]!).name, StringComparison.Ordinal)))!;

        return expression;
    }

    // py: sqlglot/optimizer/simplify.py::Simplifier.simplify_datetrunc
    public exp.Expr simplify_datetrunc(exp.Expr expression)
    {
        // DATE_TRUNC range rewrites need python-dateutil relativedelta arithmetic: not ported.
        if (IsAny(expression, DATETRUNCS))
        {
            var date = simplify_module.extract_date((exp.Expr)expression.args["this"]!);
            if (date is not null && py.truthy(expression.args.get("unit")))
                throw new NotPortedException("sqlglot/optimizer/simplify.py::simplify_datetrunc (dateutil relativedelta)");
        }
        else if (expression is exp.Binary or exp.In)
        {
            var left = expression is exp.In ? expression.args.get("this") : expression.args.get("this");
            if (IsAny(left, DATETRUNCS) && (expression is exp.In
                    ? expression.expressions.OfType<exp.Expr>().Any(simplify_module._is_date_literal)
                    : simplify_module._is_date_literal((exp.Expr)expression.args["expression"]!)))
                throw new NotPortedException("sqlglot/optimizer/simplify.py::simplify_datetrunc (dateutil relativedelta)");
        }
        return expression;
    }

    // py: sqlglot/optimizer/simplify.py::Simplifier.sort_comparison
    public exp.Expr sort_comparison(exp.Expr expression)
    {
        if (COMPLEMENT_COMPARISONS.ContainsKey(expression.GetType()))
        {
            var l = (exp.Expr)expression.args["this"]!;
            var r = (exp.Expr)expression.args["expression"]!;
            var l_column = l is exp.Column;
            var r_column = r is exp.Column;
            var l_const = simplify_module._is_constant(l);
            var r_const = simplify_module._is_constant(r);

            if ((l_column && !r_column) || (r_const && !l_const) || r is exp.SubqueryPredicate)
                return expression;
            if ((r_column && !l_column) || (l_const && !r_const)
                || string.CompareOrdinal(SimplifyGen.gen(l), SimplifyGen.gen(r)) > 0)
            {
                var cls = INVERSE_COMPARISONS.TryGetValue(expression.GetType(), out var inv) ? inv : expression.GetType();
                return OnChange(expression, (exp.Expr)Activator.CreateInstance(cls,
                    [new PyDict<string, object?> { ["this"] = r, ["expression"] = l }])!)!;
            }
        }
        return expression;
    }

    // py: sqlglot/optimizer/simplify.py::Simplifier._flat_simplify
    private exp.Expr _flat_simplify(exp.Expr expression, Func<exp.Expr, exp.Expr, exp.Expr, exp.Expr?> simplifier, bool root = true)
    {
        if (root || !expression.same_parent)
        {
            var operands = new List<exp.Expr>();
            var queue = new LinkedList<exp.Expr>(expression.flatten(unnest: false));
            var size = queue.Count;

            // The pairwise scan below is O(n^2). For connectors, a pair only combines if one side is a constant or
            // both are comparisons; if no operand is combinable the scan is a guaranteed no-op.
            if (expression is exp.Connector && !queue.Any(op => IsAny(op, CONNECTOR_COMBINABLE)))
                return expression;

            while (queue.Count > 0)
            {
                var a = queue.First!.Value;
                queue.RemoveFirst();

                var combined = false;
                for (var node = queue.First; node is not null; node = node.Next)
                {
                    var result = simplifier(expression, a, node.Value);

                    if (result is not null && !ReferenceEquals(result, expression))
                    {
                        queue.Remove(node);
                        queue.AddFirst(result);
                        combined = true;
                        break;
                    }
                }
                if (!combined) operands.Add(a);
            }

            if (operands.Count < size)
                return operands.Aggregate((a, b) => (exp.Expr)Activator.CreateInstance(expression.GetType(),
                    [new PyDict<string, object?> { ["this"] = a, ["expression"] = b }])!);
        }
        return expression;
    }
}
