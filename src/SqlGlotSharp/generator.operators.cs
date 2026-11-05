namespace SqlGlotSharp;

// Bounded operator handlers from sqlglot/generator.py, in upstream order.
// Capability-dependent rewrites that need unported AST builders remain explicit failures.
public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.not_sql #001f8821bf
        public virtual string not_sql(exp.Not expression) => $"NOT {sql(expression, "this")}";

        // py: sqlglot/generator.py::Generator.add_sql #f85ec7802b
        public virtual string add_sql(exp.Add expression) => binary(expression, "+");

        // py: sqlglot/generator.py::Generator.and_sql #4f4961a4e0
        public virtual string and_sql(exp.And expression, List<object?>? stack = null) => connector_sql(expression, "AND", stack);

        // py: sqlglot/generator.py::Generator.or_sql #238d8a9298
        public virtual string or_sql(exp.Or expression, List<object?>? stack = null) => connector_sql(expression, "OR", stack);

        // py: sqlglot/generator.py::Generator.xor_sql #dbdf710a90
        public virtual string xor_sql(exp.Xor expression, List<object?>? stack = null) => connector_sql(expression, "XOR", stack);

        // py: sqlglot/generator.py::Generator.connector_sql #9de07f162a
        public virtual string connector_sql(exp.Expr expression, string op, List<object?>? stack = null)
        {
            if (stack is not null)
            {
                // Binary.right/left are direct args indexing upstream (not optional getters).
                stack.Add(expression.args["expression"]);
                if (py.truthy(expression.comments) && comments)
                    op = maybe_comment(op, comments: expression.comments);

                stack.Add(op);
                stack.Add(expression.args["this"]);
                return op;
            }

            stack = [expression];
            var sqls = new List<string>();
            var ops = new HashSet<string>(StringComparer.Ordinal);
            while (stack.Count != 0)
            {
                var node = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                if (node is exp.Connector)
                {
                    // Upstream getattr calls the connector method, not TRANSFORMS. In particular
                    // nested PostgreSQL Xor is deliberately handled differently from root Xor.
                    var method = GetType().GetMethod($"{((exp.Expr)node).key}_sql")!;
                    try { ops.Add((string)method.Invoke(this, [node, stack])!); }
                    catch (System.Reflection.TargetInvocationException error) when (error.InnerException is not null)
                    {
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                        throw;
                    }
                }
                else
                {
                    var sql = this.sql(node);
                    if (sqls.Count != 0 && ops.Contains(sqls[^1])) sqls[^1] += $" {sql}";
                    else sqls.Add(sql);
                }
            }
            var sep = py.truthy(pretty) && too_wide(sqls) ? "\n" : " ";
            return string.Join(sep, sqls);
        }

        // py: sqlglot/generator.py::Generator.bitwiseand_sql #18100b2b09
        public virtual string bitwiseand_sql(exp.BitwiseAnd expression) => binary(expression, "&");
        // py: sqlglot/generator.py::Generator.bitwiseleftshift_sql #298f47e296
        public virtual string bitwiseleftshift_sql(exp.BitwiseLeftShift expression) => binary(expression, "<<");
        // py: sqlglot/generator.py::Generator.bitwisenot_sql #a1f8e8cdec
        public virtual string bitwisenot_sql(exp.BitwiseNot expression) => $"~{sql(expression, "this")}";
        // py: sqlglot/generator.py::Generator.bitwiseor_sql #7c7e7413b8
        public virtual string bitwiseor_sql(exp.BitwiseOr expression) => binary(expression, "|");
        // py: sqlglot/generator.py::Generator.bitwiserightshift_sql #e1f7ea73e2
        public virtual string bitwiserightshift_sql(exp.BitwiseRightShift expression) => binary(expression, ">>");
        // py: sqlglot/generator.py::Generator.bitwisexor_sql #998f40aaf6
        public virtual string bitwisexor_sql(exp.BitwiseXor expression) => binary(expression, "^");

        // py: sqlglot/generator.py::Generator.intdiv_sql #cdcfa2e280
        public virtual string intdiv_sql(exp.IntDiv expression) => _truncated_div_sql((exp.Expr)expression.@this!, (exp.Expr)expression.expression!);

        // py: sqlglot/generator.py::Generator._truncated_div_sql #c16db46326
        public virtual string _truncated_div_sql(exp.Expr @this, exp.Expr divisor)
        {
            exp.Expr div = new exp.Div(@this: @this, expression: divisor);
            if (CAST_TO_INT_ROUNDS) div = new exp.Trunc(@this: div);
            return sql(new exp.Cast(@this: div, to: new exp.DataType(@this: exp.DType.BIGINT)));
        }

        // Partial: safe CONCAT needs flatten, exp.cast and func; never emit unsafe || instead.
        // py: sqlglot/generator.py::Generator.dpipe_sql #0000000000
        public virtual string dpipe_sql(exp.DPipe expression)
        {
            if (dialect.STRICT_STRING_CONCAT && py.truthy(expression.args.get("safe")))
                throw new NotPortedException("sqlglot/generator.py::Generator.dpipe_sql safe CONCAT rewrite (exp.cast/func)");
            return binary(expression, "||");
        }

        // py: sqlglot/generator.py::Generator.div_sql #0000000000
        public virtual string div_sql(exp.Div expression)
        {
            var l = (exp.Expr)expression.args["this"]!;
            var r = (exp.Expr)expression.args["expression"]!;

            if (!dialect.SAFE_DIVISION && py.truthy(expression.args.get("safe")))
                r.replace(new exp.Nullif(@this: r.copy(), expression: exp.Literal.number(0)));

            if (dialect.TYPED_DIVISION && !py.truthy(expression.args.get("typed")))
            {
                if (!l.is_type(exp.DataType.REAL_TYPES_.Cast<object>().ToArray()) &&
                    !r.is_type(exp.DataType.REAL_TYPES_.Cast<object>().ToArray()))
                    l.replace(exp_cast(l.copy(), exp.DType.DOUBLE));
            }
            else if (!dialect.TYPED_DIVISION && py.truthy(expression.args.get("typed")))
            {
                if (l.is_type(exp.DataType.INTEGER_TYPES_.Cast<object>().ToArray()) &&
                    r.is_type(exp.DataType.INTEGER_TYPES_.Cast<object>().ToArray()))
                {
                    // Native integer division depends on the target's operand types, which may differ
                    // from the source's (e.g. FLOOR(int) is DOUBLE in DuckDB), unless the operands are
                    // integer literals or casts
                    if (new[] { l, r }.All(e => e.is_int || e is exp.Cast) && (py.truthy(expression.args.get("safe")) || !SAFE_INT_DIVISION))
                        return sql(new exp.IntDiv(@this: l, expression: r));
                    return _truncated_div_sql(l, r);
                }
            }

            return binary(expression, "/");
        }

        // py: sqlglot/generator.py::Generator.overlaps_sql #f9dec505b2
        public virtual string overlaps_sql(exp.Overlaps expression) => binary(expression, "OVERLAPS");
        // py: sqlglot/generator.py::Generator.distance_sql #9c179512ee
        public virtual string distance_sql(exp.Distance expression) => binary(expression, "<->");
        // py: sqlglot/generator.py::Generator.distancend_sql #2b571b348f
        public virtual string distancend_sql(exp.DistanceNd expression) => binary(expression, "<<->>");
        // py: sqlglot/generator.py::Generator.dot_sql #bb892134f8
        public virtual string dot_sql(exp.Dot expression) => $"{sql(expression, "this")}.{sql(expression, "expression")}";
        // py: sqlglot/generator.py::Generator.eq_sql #c5cb7d0486
        public virtual string eq_sql(exp.EQ expression) => binary(expression, "=");
        // py: sqlglot/generator.py::Generator.propertyeq_sql #7ddb66529a
        public virtual string propertyeq_sql(exp.PropertyEQ expression) => binary(expression, ":=");
        // py: sqlglot/generator.py::Generator.glob_sql #d4f73a2e36
        public virtual string glob_sql(exp.Glob expression) => binary(expression, "GLOB");
        // py: sqlglot/generator.py::Generator.gt_sql #bf2bf85723
        public virtual string gt_sql(exp.GT expression) => binary(expression, ">");
        // py: sqlglot/generator.py::Generator.gte_sql #e32a401a22
        public virtual string gte_sql(exp.GTE expression) => binary(expression, ">=");

        // Partial: IS_BOOL_ALLOWED=False needs exp.not_ (including its wrapping algorithm).
        // py: sqlglot/generator.py::Generator.is_sql #0000000000
        public virtual string is_sql(exp.Is expression)
        {
            var negate = expression.args.get("negate");
            if (!IS_BOOL_ALLOWED && expression.expression is exp.Boolean)
                throw new NotPortedException("sqlglot/generator.py::Generator.is_sql boolean exp.not_ rewrite");
            return binary(expression, py.truthy(negate) ? "IS NOT" : "IS");
        }

        // py: sqlglot/generator.py::Generator.match_sql #b10c2d7b48
        public virtual string match_sql(exp.Match expression)
        {
            if (py.truthy(expression.args.get("kind")) || py.truthy(expression.args.get("analyzer")))
                unsupported("MATCH variants and USING ANALYZER are not supported in this dialect");
            return binary(expression, "MATCH");
        }
        // py: sqlglot/generator.py::Generator.similarto_sql #497816f630
        public virtual string similarto_sql(exp.SimilarTo expression) => binary(expression, "SIMILAR TO");
        // py: sqlglot/generator.py::Generator.lt_sql #201d67d3fe
        public virtual string lt_sql(exp.LT expression) => binary(expression, "<");
        // py: sqlglot/generator.py::Generator.lte_sql #c11282d85d
        public virtual string lte_sql(exp.LTE expression) => binary(expression, "<=");

        // py: sqlglot/generator.py::Generator.mod_sql #8896360f43
        public virtual string mod_sql(exp.Mod expression)
        {
            var @this = this.sql(expression, "this");
            var expr = this.sql(expression, "expression");
            var sql = $"{@this} {maybe_comment(MOD_OPERATOR, comments: expression.comments)} {expr}";
            var parent = expression.parent;
            var types = MOD_PAREN_PARENT_TYPES;
            if (parent is not null && (types.Item1.IsInstanceOfType(parent) || types.Item2.IsInstanceOfType(parent) ||
                types.Item3.IsInstanceOfType(parent) || types.Item4.IsInstanceOfType(parent)) && ReferenceEquals(parent.expression, expression))
                return $"({sql})";
            return sql;
        }
        // py: sqlglot/generator.py::Generator.mul_sql #b16db27133
        public virtual string mul_sql(exp.Mul expression) => binary(expression, "*");
        // py: sqlglot/generator.py::Generator.neq_sql #96344835ac
        public virtual string neq_sql(exp.NEQ expression) => binary(expression, "<>");
        // py: sqlglot/generator.py::Generator.nullsafeeq_sql #02121b68f3
        public virtual string nullsafeeq_sql(exp.NullSafeEQ expression) => binary(expression, "IS NOT DISTINCT FROM");
        // py: sqlglot/generator.py::Generator.nullsafeneq_sql #67b72052ea
        public virtual string nullsafeneq_sql(exp.NullSafeNEQ expression) => binary(expression, "IS DISTINCT FROM");
        // py: sqlglot/generator.py::Generator.sub_sql #bbadede7d3
        public virtual string sub_sql(exp.Sub expression) => binary(expression, "-");

        // py: sqlglot/generator.py::Generator.binary #3b94b28b56
        public virtual string binary(exp.Expr expression, string op)
        {
            var sqls = new List<string>();
            var stack = new List<object?> { expression };
            var binary_type = expression.GetType();
            var is_is = binary_type == typeof(exp.Is) && op is "IS" or "IS NOT";
            var negate = expression.args.get("negate");
            while (stack.Count != 0)
            {
                var node = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                if (node?.GetType() == binary_type && (ReferenceEquals(node, expression) ||
                    is_is && ((exp.Expr)node).expression is not exp.Boolean ||
                    binary__equal_negate(((exp.Expr)node!).args.get("negate"), negate)))
                {
                    var e = (exp.Expr)node;
                    var op_func = e.args.get("operator");
                    if (py.truthy(op_func)) op = $"OPERATOR({sql(op_func)})";
                    else if (is_is) op = py.truthy(e.args.get("negate")) ? "IS NOT" : "IS";
                    stack.Add(e.args.get("expression"));
                    stack.Add($" {maybe_comment(op, comments: e.comments)} ");
                    stack.Add(e.args.get("this"));
                }
                else sqls.Add(sql(node));
            }
            return string.Join("", sqls);
        }

        // Python equality for the negate flag also equates bool and numeric flag values.
        private static bool binary__equal_negate(object? left, object? right)
        {
            static bool Numeric(object? x) => x is bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;
            if (Numeric(left) && Numeric(right))
                return Convert.ToDouble(left, System.Globalization.CultureInfo.InvariantCulture) ==
                    Convert.ToDouble(right, System.Globalization.CultureInfo.InvariantCulture);
            return Equals(left, right);
        }

        // py: sqlglot/generator.py::Generator.too_wide #e14a8e80fb
        public virtual bool too_wide(System.Collections.IEnumerable args)
        {
            long length = 0;
            foreach (var arg in args) length += py.len(arg);
            return length > max_text_width;
        }
    }
}
