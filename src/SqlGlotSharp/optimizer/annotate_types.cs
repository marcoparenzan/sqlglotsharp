using DType = SqlGlotSharp.exp.DType;

namespace SqlGlotSharp.optimizer;

// Port of sqlglot/optimizer/annotate_types.py for expression-level annotation. Scope-aware paths
// (queries, DDL with queries, column lookup in a scope, pivot types) need optimizer/scope.py and
// optimizer/qualify_columns, which are not ported: they raise NotPortedException.
public static partial class annotate_types_module
    {
        // py: sqlglot/optimizer/annotate_types.py::logger #12092c5f48
        public static py.logging.Logger logger = py.logging.getLogger("sqlglot");

        // EXTRACT/DATE_PART specifiers that return BIGINT instead of INT
        // py: sqlglot/optimizer/annotate_types.py::BIGINT_EXTRACT_DATE_PARTS
        internal static readonly HashSet<string> BIGINT_EXTRACT_DATE_PARTS =
            ["EPOCH_SECOND", "EPOCH_MILLISECOND", "EPOCH_MICROSECOND", "EPOCH_NANOSECOND", "NANOSECOND"];

        // py: sqlglot/optimizer/annotate_types.py::annotate_types
        public static E annotate_types<E>(E expression, object? schema = null, object? dialect = null,
            bool overwrite_types = true) where E : exp.Expr
        {
            var annotator = new TypeAnnotator(new typing.ExpressionSchema(dialects.dialect.Dialect.get_or_raise(dialect)),
                overwrite_types: overwrite_types);
            annotator.annotate(expression);
            return expression;
        }

        // py: sqlglot/optimizer/annotate_types.py::_coerce_date_literal
        internal static DType _coerce_date_literal(exp.Expr l, exp.Expr? unit)
        {
            var date_text = l.name;
            var is_iso_date_ = helper.is_iso_date(date_text);

            if (is_iso_date_ && helper.is_date_unit(unit)) return DType.DATE;

            // An ISO date is also an ISO datetime, but not vice versa
            if (is_iso_date_ || helper.is_iso_datetime(date_text)) return DType.DATETIME;

            return DType.UNKNOWN;
        }

        // py: sqlglot/optimizer/annotate_types.py::_coerce_date
        internal static object _coerce_date(exp.Expr l, exp.Expr? unit)
        {
            if (l.is_type(DType.DATE) && !helper.is_date_unit(unit)) return DType.DATETIME;
            return (object?)l.type ?? DType.UNKNOWN;
        }

        // py: sqlglot/optimizer/annotate_types.py::_build_coerces_to
        internal static Dictionary<DType, HashSet<DType>> _build_coerces_to()
        {
            // Highest-to-lowest type precedence, as specified in Spark's docs (ANSI):
            // https://spark.apache.org/docs/3.2.0/sql-ref-ansi-compliance.html
            var text_precedence = new[] { DType.TEXT, DType.NVARCHAR, DType.VARCHAR, DType.NCHAR, DType.CHAR };
            var numeric_precedence = new[] { DType.DECFLOAT, DType.DOUBLE, DType.FLOAT, DType.BIGDECIMAL, DType.DECIMAL,
                DType.BIGINT, DType.INT, DType.SMALLINT, DType.TINYINT };
            var timelike_precedence = new[] { DType.TIMESTAMPLTZ, DType.TIMESTAMPTZ, DType.TIMESTAMP, DType.DATETIME, DType.DATE };

            var result = new Dictionary<DType, HashSet<DType>>();
            foreach (var type_precedence in new[] { text_precedence, numeric_precedence, timelike_precedence })
            {
                var coerces_to = new HashSet<DType>();
                foreach (var data_type in type_precedence)
                {
                    result[data_type] = [.. coerces_to];
                    coerces_to.Add(data_type);
                }
            }
            return result;
        }

        internal static readonly Dictionary<DType, HashSet<DType>> _COERCES_TO = _build_coerces_to();

        // py: sqlglot/optimizer/annotate_types.py::TypeAnnotator.BINARY_COERCIONS
        internal static readonly Dictionary<(DType, DType), Func<exp.Expr, exp.Expr, object?>> BINARY_COERCIONS = BuildBinaryCoercions();

        private static Dictionary<(DType, DType), Func<exp.Expr, exp.Expr, object?>> BuildBinaryCoercions()
        {
            var result = new Dictionary<(DType, DType), Func<exp.Expr, exp.Expr, object?>>();
            void SwapAll(Dictionary<(DType, DType), Func<exp.Expr, exp.Expr, object?>> coercions)
            {
                foreach (var (key, func) in coercions) result[key] = func;
                foreach (var ((a, b), func) in coercions) result[(b, a)] = (l, r) => func(r, l);
            }

            var text_interval = new Dictionary<(DType, DType), Func<exp.Expr, exp.Expr, object?>>();
            foreach (var t in exp.DataType.TEXT_TYPES_)
                text_interval[(t, DType.INTERVAL)] = (l, r) => _coerce_date_literal(l, r.args.get("unit") as exp.Expr);
            SwapAll(text_interval);

            // text + numeric will yield the numeric type to match most dialects' semantics
            var text_numeric = new Dictionary<(DType, DType), Func<exp.Expr, exp.Expr, object?>>();
            foreach (var text in exp.DataType.TEXT_TYPES_)
                foreach (var numeric in exp.DataType.NUMERIC_TYPES_)
                    // `l.type in NUMERIC_TYPES` compares a DataType against DType members: never true upstream
                    text_numeric[(text, numeric)] = (l, r) => r.type;
            SwapAll(text_numeric);

            SwapAll(new Dictionary<(DType, DType), Func<exp.Expr, exp.Expr, object?>>
            {
                [(DType.DATE, DType.INTERVAL)] = (l, r) => _coerce_date(l, r.args.get("unit") as exp.Expr),
            });
            return result;
        }
}

    public partial class TypeAnnotator : typing.ITypeAnnotator
    {
        public readonly HashSet<DType> NESTED_TYPES = [DType.ARRAY];

        public typing.ITypeSchema schema { get; }
        public dialects.dialect.Dialect dialect;
        public PyDict<Type, PyDict<string, object?>> expression_metadata;
        public Dictionary<DType, HashSet<DType>> coerces_to;
        public Dictionary<(DType, DType), Func<exp.Expr, exp.Expr, object?>> binary_coercions;

        // Keep annotated nodes alive so their ids cannot be reused while cached
        private readonly Dictionary<exp.Expr, exp.Expr> _visited = new(ReferenceEqualityComparer.Instance);

        // Candidates for replacing NULL types with the dialect's default after inference.
        private readonly Dictionary<exp.Expr, exp.Expr> _null_expressions = new(ReferenceEqualityComparer.Instance);

        // Databricks and Spark >= v3 actually support NULL (i.e., VOID) as a type
        private readonly bool _supports_null_type;

        // Keep ordered output columns until a parent set operation consumes them.
        private readonly Dictionary<exp.Expr, List<(string, object)>> _setop_column_types = new(ReferenceEqualityComparer.Instance);

        // When set to False, this enables partial annotation by skipping already-annotated nodes
        private readonly bool _overwrite_types;

        // py: sqlglot/optimizer/annotate_types.py::TypeAnnotator.__init__
        public TypeAnnotator(typing.ExpressionSchema schema, PyDict<Type, PyDict<string, object?>>? expression_metadata = null,
            Dictionary<DType, HashSet<DType>>? coerces_to = null, bool overwrite_types = true)
        {
            this.schema = schema;
            dialect = schema.dialect;
            this.expression_metadata = expression_metadata ?? dialect.EXPRESSION_METADATA;
            this.coerces_to = coerces_to ?? annotate_types_module._COERCES_TO;
            binary_coercions = annotate_types_module.BINARY_COERCIONS;
            _supports_null_type = dialect.SUPPORTS_NULL_TYPE;
            _overwrite_types = overwrite_types;
        }

        // py: sqlglot/optimizer/annotate_types.py::TypeAnnotator.clear
        public void clear()
        {
            _visited.Clear();
            _null_expressions.Clear();
            _setop_column_types.Clear();
        }

        private static exp.DataType ToDataType(object target) =>
            target is exp.DataType data_type ? data_type : exp.DataType.build(target);

        // py: sqlglot/optimizer/annotate_types.py::TypeAnnotator._set_type
        public exp.Expr _set_type(exp.Expr expression, object? target_type)
        {
            var prev_type = expression._type;

            expression._type = ToDataType(py.truthy(target_type) ? target_type! : DType.UNKNOWN);
            _visited[expression] = expression;

            if (!_supports_null_type && Equals(expression._type.args.get("this"), DType.NULL))
                _null_expressions[expression] = expression;
            else if (prev_type is not null && Equals(prev_type.args.get("this"), DType.NULL))
                _null_expressions.Remove(expression);

            return expression;
        }

        // py: sqlglot/optimizer/annotate_types.py::TypeAnnotator.annotate
        public E annotate<E>(E expression, bool annotate_scope = true) where E : exp.Expr
        {
            // Queries are annotated scope by scope upstream; the scope optimizer is not ported.
            if (annotate_scope && (expression is exp.Query || expression is exp.DDL && expression.args.get("expression") is exp.Query))
                throw new NotPortedException("sqlglot/optimizer/scope.py::traverse_scope (annotate_types over queries)");

            // This takes care of non-traversable expressions
            annotate_expression(expression);

            // Replace NULL type with the default type of the targeted dialect, since the former is not an actual type;
            // it is mostly used to aid type coercion, e.g. in query set operations.
            foreach (var expr in _null_expressions.Values.ToList())
                _set_type(expr, dialect.DEFAULT_NULL_TYPE);

            return expression;
        }

        // py: sqlglot/optimizer/annotate_types.py::TypeAnnotator.annotate_expression
        /// <summary>Annotate an expression and its descendants (no scope: column lookups are unavailable).</summary>
        public void annotate_expression(exp.Expr expression)
        {
            var stack = new Stack<(exp.Expr, bool)>();
            stack.Push((expression, false));

            while (stack.Count > 0)
            {
                var (expr, children_annotated) = stack.Pop();

                if (_visited.ContainsKey(expr) || (!_overwrite_types && expr.type is not null && !expr.is_type(DType.UNKNOWN)))
                    continue; // We've already inferred the expression's type

                if (!children_annotated)
                {
                    stack.Push((expr, true));
                    foreach (var child_expr in expr.iter_expressions()) stack.Push((child_expr, false));
                    continue;
                }

                if (expr is exp.SetOperation set_operation) _annotate_set_operation(set_operation);

                expression_metadata.TryGetValue(expr.GetType(), out var spec);

                if (spec is not null && spec.TryGetValue("annotator", out var annotator) && annotator is typing.Annotation annotation)
                    annotation(this, expr);
                else if (spec is not null && spec.TryGetValue("returns", out var returns) && returns is not null)
                    _set_type(expr, returns);
                else
                    _set_type(expr, DType.UNKNOWN);

                _restore_dot_parts(expr);
            }
        }

        // py: sqlglot/optimizer/annotate_types.py::TypeAnnotator._restore_dot_parts
        private void _restore_dot_parts(exp.Expr expr)
        {
            // Dot access into semi-structured values is a case sensitive data lookup, i.e. the
            // engine doesn't resolve the keys as identifiers, so we undo their normalization.
            var dot_parts = expr.meta_get("dot_parts") as List<string>;
            if (dot_parts is not { Count: > 0 } || !expr.is_type(DType.JSON, DType.MAP, DType.VARIANT))
            {
                if (dot_parts is { Count: > 0 })
                {
                    // A struct field can itself contain semi-structured data. Resolve each
                    // struct access before deciding whether the remaining keys are identifiers.
                    if (expr.is_type(DType.STRUCT) && expr.parent is exp.Dot)
                        expr.parent.meta["dot_parts"] = dot_parts.Skip(1).ToList();
                    if (expr.type is null || expr.is_type(DType.UNKNOWN)) return;
                    expr.meta.Remove("dot_parts");
                }
                return;
            }

            var parent = expr.parent;
            foreach (var part in dot_parts)
            {
                if (parent is not exp.Dot dot) break;

                var identifier = (exp.Expr)dot.args["expression"]!;
                if (identifier is exp.Identifier)
                {
                    // Rename in place to preserve the identifier's meta, e.g. token positions
                    identifier.set("this", part);
                    identifier.set("quoted", true);
                }
                else identifier.replace(exp.to_identifier(part, quoted: true));

                parent = parent.parent;
            }

            expr.meta.Remove("dot_parts");
        }

        // py: sqlglot/optimizer/annotate_types.py::TypeAnnotator._maybe_coerce
        /// <summary>
        /// Returns type2 if type1 can be coerced into it, otherwise type1. If either type is parameterized
        /// (e.g. DECIMAL(18, 2) contains two parameters), type1 does not coerce into type2.
        /// </summary>
        public object _maybe_coerce(object type1, object type2)
        {
            DType type1_value, type2_value;
            if (type1 is exp.DataType dt1)
            {
                if (dt1.expressions.Count > 0 || dt1.args.get("this") is not DType) return type1;
                type1_value = (DType)dt1.args["this"]!;
            }
            else type1_value = (DType)type1;

            if (type2 is exp.DataType dt2)
            {
                if (dt2.expressions.Count > 0 || dt2.args.get("this") is not DType) return type2;
                type2_value = (DType)dt2.args["this"]!;
            }
            else type2_value = (DType)type2;

            // We propagate the UNKNOWN type upwards if found
            if (type1_value == DType.UNKNOWN || type2_value == DType.UNKNOWN) return DType.UNKNOWN;

            if (type1_value == DType.NULL) return type2_value;
            if (type2_value == DType.NULL) return type1_value;

            return coerces_to.TryGetValue(type1_value, out var targets) && targets.Contains(type2_value) ? type2_value : type1_value;
        }

        // py: sqlglot/optimizer/annotate_types.py::TypeAnnotator._get_setop_column_types
        private List<(string, object)> _get_setop_column_types(exp.Expr expression) =>
            _setop_column_types.TryGetValue(expression, out var columns) ? columns : [];

        // py: sqlglot/optimizer/annotate_types.py::TypeAnnotator._annotate_set_operation
        private void _annotate_set_operation(exp.SetOperation expression)
        {
            // The expression annotation pass visits children first. Preserve duplicate names until positional matching is done.
            var operands = new List<List<(string, object)>>();
            foreach (var operand in new[] { ((exp.Expr)expression.args["this"]!).unnest(), ((exp.Expr)expression.args["expression"]!).unnest() })
            {
                if (operand is exp.SetOperation)
                {
                    operands.Add(_setop_column_types.Remove(operand, out var cols) ? cols : []);
                }
                else
                    operands.Add(generators.dialect_helpers.selects(operand)
                        .Select(s => (s.alias_or_name, (object?)s.type ?? DType.UNKNOWN)).ToList());
            }
            var left = operands[0];
            var right = operands[1];

            var columns = new List<(string, object)>();
            if (left.Count > 0 && right.Count > 0)
            {
                if (py.truthy(expression.args.get("by_name")))
                {
                    var left_types = new Dictionary<string, object>();
                    foreach (var (n, ty) in left) left_types[n] = ty;
                    var right_types = new Dictionary<string, object>();
                    foreach (var (n, ty) in right) right_types[n] = ty;
                    List<string> names;
                    if (py.truthy(expression.args.get("on")))
                        names = ((System.Collections.IEnumerable)expression.args["on"]!).Cast<exp.Expr>().Select(c => c.name).ToList();
                    else if (py.str(expression.args.get("kind")).ToUpperInvariant() == "INNER")
                        names = left_types.Keys.Where(right_types.ContainsKey).ToList();
                    else if (py.str(expression.args.get("side")).ToUpperInvariant() == "LEFT")
                        names = left_types.Keys.ToList();
                    else
                        names = left_types.Keys.Concat(right_types.Keys).Distinct().ToList();

                    // Missing columns are NULL-filled, preserving the other operand's type.
                    columns = names.Select(name => (name, _maybe_coerce(
                        left_types.TryGetValue(name, out var lt) ? lt : DType.NULL,
                        right_types.TryGetValue(name, out var rt) ? rt : DType.NULL))).ToList();
                }
                else if (left.Count == right.Count)
                    columns = left.Zip(right, (l, r) => (l.Item1, _maybe_coerce(l.Item2, r.Item2))).ToList();
            }

            _setop_column_types[expression] = columns;
        }

        // py: sqlglot/optimizer/annotate_types.py::TypeAnnotator._annotate_binary
        public exp.Expr _annotate_binary(exp.Expr expression)
        {
            var left = expression.args.get("this") as exp.Expr;
            var right = expression.args.get("expression") as exp.Expr;
            if (left is null || right is null)
            {
                var expression_sql = expression.sql(dialect);
                annotate_types_module.logger.warning($"Failed to annotate badly formed binary expression: {expression_sql}");
                _set_type(expression, null);
                return expression;
            }

            var left_type = left.type is not null ? left.type.args["this"]! : DType.UNKNOWN;
            var right_type = right.type is not null ? right.type.args["this"]! : DType.UNKNOWN;

            if (expression is exp.Connector or exp.Predicate)
                _set_type(expression, DType.BOOLEAN);
            else if (left_type is DType lt && right_type is DType rt && binary_coercions.TryGetValue((lt, rt), out var coercion))
                _set_type(expression, coercion(left, right));
            else
                _annotate_by_args(expression, args: [left, right]);

            if (expression is exp.Is || (Equals(left.meta_get("nonnull"), true) && Equals(right.meta_get("nonnull"), true)))
                expression.meta["nonnull"] = true;

            return expression;
        }

        // py: sqlglot/optimizer/annotate_types.py::TypeAnnotator._annotate_unary
        public exp.Expr _annotate_unary(exp.Expr expression)
        {
            var @this = (exp.Expr)expression.args["this"]!;
            if (expression is exp.Not) _set_type(expression, DType.BOOLEAN);
            else _set_type(expression, @this.type);

            if (Equals(@this.meta_get("nonnull"), true)) expression.meta["nonnull"] = true;

            return expression;
        }

        // py: sqlglot/optimizer/annotate_types.py::TypeAnnotator._annotate_literal
        public exp.Expr _annotate_literal(exp.Expr expression)
        {
            if (expression.is_string) _set_type(expression, DType.VARCHAR);
            else if (expression.is_int) _set_type(expression, DType.INT);
            else _set_type(expression, DType.DOUBLE);

            expression.meta["nonnull"] = true;

            return expression;
        }

        // py: sqlglot/optimizer/annotate_types.py::TypeAnnotator._annotate_by_args
        public exp.Expr _annotate_by_args(exp.Expr expression, bool array = false, bool promote = false, params object?[] args)
        {
            object? literal_type = null;
            object? non_literal_type = null;
            exp.DataType? nested_type = null;

            foreach (var arg in args)
            {
                var expressions = arg is string key ? expression.args.get(key) : arg;

                foreach (var expr in helper.ensure_list(expressions).Cast<exp.Expr>())
                {
                    var expr_type = expr.type;

                    if (expr_type is null || expr_type.is_type(DType.UNKNOWN))
                    {
                        _set_type(expression, DType.UNKNOWN);
                        return expression;
                    }

                    if (nested_type is not null)
                    {
                        // Only resolve ARRAY<NULL>; preserve existing coercion for other nested types.
                        if (nested_type.is_type(DType.ARRAY) && nested_type.expressions.Count > 0
                            && ((exp.Expr)nested_type.expressions[0]!).is_type(DType.NULL)
                            && expr_type.is_type(DType.ARRAY) && expr_type.expressions.Count > 0)
                            nested_type = expr_type;
                        continue;
                    }

                    // Prefer the first nested data type, resolving ARRAY<NULL> from later arrays.
                    if (py.truthy(expr_type.args.get("nested"))) nested_type = expr_type;
                    else if (expr is exp.Literal) literal_type = _maybe_coerce(literal_type ?? expr_type, expr_type);
                    else non_literal_type = _maybe_coerce(non_literal_type ?? expr_type, expr_type);
                }
            }

            object? result_type;

            if (nested_type is not null) result_type = nested_type;
            else if (literal_type is not null && non_literal_type is not null)
            {
                result_type = null;
                if (dialect.PRIORITIZE_NON_LITERAL_TYPES)
                {
                    var literal_this_type = literal_type is exp.DataType ld ? ld.args["this"] : literal_type;
                    var non_literal_this_type = non_literal_type is exp.DataType nd ? nd.args["this"] : non_literal_type;
                    if ((literal_this_type is DType l1 && non_literal_this_type is DType n1
                            && exp.DataType.INTEGER_TYPES_.Contains(l1) && exp.DataType.INTEGER_TYPES_.Contains(n1))
                        || (literal_this_type is DType l2 && non_literal_this_type is DType n2
                            && exp.DataType.REAL_TYPES_.Contains(l2) && exp.DataType.REAL_TYPES_.Contains(n2)))
                        result_type = non_literal_type;
                }

                result_type ??= _maybe_coerce(non_literal_type, literal_type);
            }
            else result_type = literal_type ?? non_literal_type ?? (array ? DType.NULL : DType.UNKNOWN);

            _set_type(expression, result_type);

            if (promote)
            {
                var this_type = result_type is exp.DataType rd ? rd.args["this"] : result_type;
                if (this_type is DType p && exp.DataType.INTEGER_TYPES_.Contains(p)) _set_type(expression, DType.BIGINT);
                else if (this_type is DType f && exp.DataType.FLOAT_TYPES_.Contains(f)) _set_type(expression, DType.DOUBLE);
            }

            if (array && !expression.is_type(DType.UNKNOWN))
                _set_type(expression, new exp.DataType(@this: DType.ARRAY, expressions: new List<object?> { expression.type }, nested: true));

            return expression;
        }

        // py: sqlglot/optimizer/annotate_types.py::TypeAnnotator._annotate_timeunit
        public exp.Expr _annotate_timeunit(exp.Expr expression)
        {
            var @this = (exp.Expr)expression.args["this"]!;
            var this_type = @this.type!.args["this"];
            object datatype;
            if (this_type is DType t1 && exp.DataType.TEXT_TYPES_.Contains(t1))
                datatype = annotate_types_module._coerce_date_literal(@this, expression.args.get("unit") as exp.Expr);
            else if (this_type is DType t2 && exp.DataType.TEMPORAL_TYPES_.Contains(t2))
                datatype = annotate_types_module._coerce_date(@this, expression.args.get("unit") as exp.Expr);
            else datatype = DType.UNKNOWN;

            _set_type(expression, datatype);
            return expression;
        }

        // py: sqlglot/optimizer/annotate_types.py::TypeAnnotator._annotate_bracket
        public exp.Expr _annotate_bracket(exp.Expr expression)
        {
            var bracket_arg = (exp.Expr)expression.expressions[0]!;
            var @this = (exp.Expr)expression.args["this"]!;

            if (bracket_arg is exp.Slice) _set_type(expression, @this.type);
            else if (@this.type!.is_type(DType.ARRAY))
                _set_type(expression, @this.type.expressions.Count > 0 ? @this.type.expressions[0] : null);
            else if (@this is exp.Map or exp.VarMap && ((System.Collections.IEnumerable)(@this.args.get("keys") is exp.Expr ks ? ks.expressions : new List<object?>())).Cast<object?>().Any(k => Equals(k, bracket_arg)))
            {
                var keys = ((exp.Expr)@this.args["keys"]!).expressions;
                var index = keys.FindIndex(k => Equals(k, bracket_arg));
                var values = @this.args.get("values") is exp.Expr vs ? vs.expressions : new List<object?>();
                var value = index < values.Count ? values[index] as exp.Expr : null;
                _set_type(expression, value?.type);
            }
            else _set_type(expression, DType.UNKNOWN);

            return expression;
        }

        // py: sqlglot/optimizer/annotate_types.py::TypeAnnotator._annotate_div
        public exp.Expr _annotate_div(exp.Expr expression)
        {
            var left = (exp.Expr)expression.args["this"]!;
            var right = (exp.Expr)expression.args["expression"]!;
            var left_type = left.type is not null ? left.type.args["this"]! : DType.UNKNOWN;
            var right_type = right.type is not null ? right.type.args["this"]! : DType.UNKNOWN;

            if (py.truthy(expression.args.get("typed"))
                && left_type is DType l && exp.DataType.INTEGER_TYPES_.Contains(l)
                && right_type is DType r && exp.DataType.INTEGER_TYPES_.Contains(r))
                _set_type(expression, DType.BIGINT);
            else
            {
                _set_type(expression, _maybe_coerce(left_type, right_type));
                if (expression.type is not null && !(expression.type.args["this"] is DType t && exp.DataType.REAL_TYPES_.Contains(t)))
                    _set_type(expression, _maybe_coerce(expression.type, DType.DOUBLE));
            }

            return expression;
        }

        // py: sqlglot/optimizer/annotate_types.py::TypeAnnotator._annotate_dot
        public exp.Expr _annotate_dot(exp.Expr expression)
        {
            _set_type(expression, null);

            // Propagate type from qualified UDF calls (e.g., db.my_udf(...))
            if (expression.args.get("expression") is exp.Anonymous udf)
            {
                _set_type(expression, udf.type);
                return expression;
            }

            var this_type = ((exp.Expr)expression.args["this"]!).type;

            if (this_type is not null && this_type.is_type(DType.STRUCT))
                foreach (var e in this_type.expressions.Cast<exp.Expr>())
                    if (e.name == ((exp.Expr)expression.args["expression"]!).name)
                    {
                        _set_type(expression, e.args.get("kind"));
                        break;
                    }

            return expression;
        }

        // py: sqlglot/optimizer/annotate_types.py::TypeAnnotator._annotate_explode
        public exp.Expr _annotate_explode(exp.Expr expression)
        {
            var input_type = ((exp.Expr)expression.args["this"]!).type;
            if (input_type is not null && input_type.is_type(DType.ARRAY))
                _set_type(expression, input_type.expressions.Count > 0 ? input_type.expressions[0] : null);
            else _set_type(expression, null);
            return expression;
        }

        // py: sqlglot/optimizer/annotate_types.py::TypeAnnotator._annotate_unnest
        public exp.Expr _annotate_unnest(exp.Expr expression)
        {
            var child = expression.expressions.Count > 0 ? expression.expressions[0] as exp.Expr : null;

            object? expr_type = null;
            if (child is not null && child.is_type(DType.ARRAY))
                expr_type = child.type!.expressions.Count > 0 ? child.type.expressions[0] : null;

            _set_type(expression, expr_type);
            return expression;
        }

        // py: sqlglot/optimizer/annotate_types.py::TypeAnnotator._annotate_subquery
        public exp.Expr _annotate_subquery(exp.Expr expression)
        {
            // For scalar subqueries (subqueries with a single projection), infer the type from that single projection.
            var query = expression.unnest();

            if (query is exp.Query)
            {
                if (Equals(query.args.get("kind"), "STRUCT"))
                {
                    _set_type(expression, query.meta_get("query_type"));
                    return expression;
                }
                var selects = generators.dialect_helpers.selects(query);
                if (selects.Count == 1)
                {
                    _set_type(expression, selects[0].type);
                    return expression;
                }
            }

            _set_type(expression, DType.UNKNOWN);
            return expression;
        }

        // py: sqlglot/optimizer/annotate_types.py::TypeAnnotator._annotate_struct_value
        private object? _annotate_struct_value(exp.Expr expression, out bool unknown)
        {
            // Case: STRUCT(key AS value)
            exp.Expr? @this = null;
            var kind = expression.type;
            unknown = false;

            if (expression.args.get("alias") is exp.Expr alias) @this = alias.copy();
            else if (expression.args.get("expression") is exp.Expr value)
            {
                // Case: STRUCT(key = value) or STRUCT(key := value)
                @this = ((exp.Expr)expression.args["this"]!).copy();
                kind = value.type;
            }
            else if (expression is exp.Column)
                // Case: STRUCT(c)
                @this = ((exp.Expr)expression.args["this"]!).copy();

            if (kind is not null && kind.is_type(DType.UNKNOWN))
            {
                unknown = true;
                return null;
            }

            if (@this is not null) return new exp.ColumnDef(@this: @this, kind: kind);

            return kind;
        }

        // py: sqlglot/optimizer/annotate_types.py::TypeAnnotator._annotate_struct
        public exp.Expr _annotate_struct(exp.Expr expression)
        {
            var expressions = new List<object?>();
            foreach (var expr in expression.expressions.Cast<exp.Expr>())
            {
                var struct_field_type = _annotate_struct_value(expr, out var unknown);
                if (unknown)
                {
                    _set_type(expression, null);
                    return expression;
                }

                expressions.Add(struct_field_type);
            }

            _set_type(expression, new exp.DataType(@this: DType.STRUCT, expressions: expressions, nested: true));
            return expression;
        }

        // py: sqlglot/optimizer/annotate_types.py::TypeAnnotator._annotate_map
        public exp.Expr _annotate_map(exp.Expr expression)
        {
            var keys = expression.args.get("keys");
            var values = expression.args.get("values");

            var map_type = new exp.DataType(@this: DType.MAP);
            if (keys is exp.Array && values is exp.Array)
            {
                var key_type = ((exp.Expr)keys).type!.expressions.FirstOrDefault() as exp.Expr;
                var value_type = ((exp.Expr)values).type!.expressions.FirstOrDefault() as exp.Expr;

                if (key_type is null || key_type.is_type(DType.UNKNOWN) || value_type is null || value_type.is_type(DType.UNKNOWN))
                {
                    _set_type(expression, DType.UNKNOWN);
                    return expression;
                }

                map_type.set("expressions", new List<object?> { key_type, value_type });
                map_type.set("nested", true);
            }

            _set_type(expression, map_type);
            return expression;
        }

        // py: sqlglot/optimizer/annotate_types.py::TypeAnnotator._annotate_to_map
        public exp.Expr _annotate_to_map(exp.Expr expression)
        {
            var map_type = new exp.DataType(@this: DType.MAP);
            var arg = (exp.Expr)expression.args["this"]!;
            if (arg.is_type(DType.STRUCT))
                foreach (var coldef in arg.type!.expressions.Cast<exp.Expr>())
                {
                    var kind = coldef.args.get("kind");
                    if (!(kind is exp.DataType kd && Equals(kd.args.get("this"), DType.UNKNOWN)) && !Equals(kind, DType.UNKNOWN))
                    {
                        map_type.set("expressions", new List<object?> { exp.DataType.build(DType.VARCHAR), kind });
                        map_type.set("nested", true);
                        break;
                    }
                }

            _set_type(expression, map_type);
            return expression;
        }

        // py: sqlglot/optimizer/annotate_types.py::TypeAnnotator._annotate_extract
        public exp.Expr _annotate_extract(exp.Expr expression)
        {
            var part = expression.name;
            if (part == "TIME") _set_type(expression, DType.TIME);
            else if (part == "DATE") _set_type(expression, DType.DATE);
            else if (annotate_types_module.BIGINT_EXTRACT_DATE_PARTS.Contains(part)) _set_type(expression, DType.BIGINT);
            else _set_type(expression, DType.INT);
            return expression;
        }

        // py: sqlglot/optimizer/annotate_types.py::TypeAnnotator._annotate_within_group
        public exp.Expr _annotate_within_group(exp.Expr expression)
        {
            if (expression.args.get("this") is exp.PercentileDisc)
            {
                var order = expression.args.get("expression") as exp.Expr;
                var order_expressions = order?.expressions;
                object? sort_type = order_expressions is { Count: > 0 }
                    ? ((exp.Expr)((exp.Expr)order_expressions[0]!).args["this"]!).type : DType.UNKNOWN;
                _set_type(expression, sort_type);
                return expression;
            }

            return _annotate_by_args(expression, args: ["this"]);
        }

        // py: sqlglot/optimizer/annotate_types.py::TypeAnnotator._annotate_by_array_element
        public exp.Expr _annotate_by_array_element(exp.Expr expression)
        {
            var array_arg = (exp.Expr)expression.args["this"]!;
            if (array_arg.type!.is_type(DType.ARRAY))
            {
                var element_type = array_arg.type.expressions.Count > 0 ? array_arg.type.expressions[0] : null;
                _set_type(expression, element_type ?? DType.UNKNOWN);
            }
            else _set_type(expression, DType.UNKNOWN);

            return expression;
        }
    }
