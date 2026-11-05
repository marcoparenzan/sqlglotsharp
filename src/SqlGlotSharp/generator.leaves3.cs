using System.Numerics;

namespace SqlGlotSharp;

// More small handlers of sqlglot/generator.py (statements, hex/bit literals, blocks, joins, ...).
public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/expressions/core.py::_wrap
        internal static exp.Expr wrap_expr(exp.Expr expression, Type kind) =>
            kind.IsInstanceOfType(expression) ? new exp.Paren(@this: expression) : expression;

        // py: sqlglot/expressions/core.py::Expr._binop (operator overloading: neither side is already the target class)
        internal static exp.Expr binop(Type klass, exp.Expr left, exp.Expr right)
        {
            var @this = left.copy();
            var other = right.copy();
            if (!klass.IsInstanceOfType(@this) && !klass.IsInstanceOfType(other))
            {
                @this = wrap_expr(@this, typeof(exp.Binary));
                other = wrap_expr(other, typeof(exp.Binary));
            }
            return (exp.Expr)Activator.CreateInstance(klass, [new PyDict<string, object?> { ["this"] = @this, ["expression"] = other }])!;
        }

        // py: sqlglot/generator.py::Generator.directory_sql #0000000000
        public virtual string directory_sql(exp.Directory expression)
        {
            var local = py.truthy(expression.args.get("local")) ? "LOCAL " : "";
            var row_format = sql(expression, "row_format");
            row_format = row_format.Length != 0 ? $" {row_format}" : "";
            return $"{local}DIRECTORY {sql(expression, "this")}{row_format}";
        }

        // py: sqlglot/generator.py::Generator.chr_sql #0000000000
        public virtual string chr_sql(exp.Chr expression, string name = "CHR")
        {
            var @this = expressions(expression);
            var charset = sql(expression, "charset");
            var @using = charset.Length != 0 ? $" USING {charset}" : "";
            return func(name, @this + @using);
        }

        // py: sqlglot/generator.py::Generator.tag_sql #0000000000
        public virtual string tag_sql(exp.Tag expression) =>
            $"{py.str(expression.args.get("prefix"))}{sql(expression.@this)}{py.str(expression.args.get("postfix"))}";

        // py: sqlglot/generator.py::Generator.matchagainst_sql #0000000000
        public virtual string matchagainst_sql(exp.MatchAgainst expression)
        {
            var items = ((System.Collections.IEnumerable)(expression.args.get("expressions") ?? new List<object?>())).Cast<object?>().ToList();
            List<object?> exprs = [];
            if (py.truthy(MATCH_AGAINST_TABLE_PREFIX))
            {
                foreach (var expr in items)
                    exprs.Add(expr is exp.Table ? $"TABLE {sql(expr)}" : expr);
            }
            else exprs = items;

            var modifier = expression.args.get("modifier");
            var modifier_sql = py.truthy(modifier) ? $" {py.str(modifier)}" : "";
            return $"{func("MATCH", exprs.ToArray())} AGAINST({sql(expression, "this")}{modifier_sql})";
        }

        // py: sqlglot/generator.py::Generator.sort_sql #0000000000
        public virtual string sort_sql(exp.Sort expression) => op_expressions("SORT BY", expression);

        // py: sqlglot/generator.py::Generator.cluster_sql #0000000000
        public virtual string cluster_sql(exp.Cluster expression) => op_expressions("CLUSTER BY", expression);

        // py: sqlglot/generator.py::Generator.safedivide_sql #0000000000
        public virtual string safedivide_sql(exp.SafeDivide expression)
        {
            var n = wrap_expr((exp.Expr)expression.args["this"]!, typeof(exp.Binary));
            var d = wrap_expr((exp.Expr)expression.args["expression"]!, typeof(exp.Binary));
            return sql(new exp.If(@this: binop(typeof(exp.NEQ), d, exp.Literal.number(0)),
                @true: binop(typeof(exp.Div), n, d), @false: new exp.Null()));
        }

        // py: sqlglot/generator.py::Generator.lowerhex_sql #0000000000
        public virtual string lowerhex_sql(exp.LowerHex expression)
        {
            var text = func(HEX_FUNC, sql(expression, "this"));
            if (!dialect.HEX_LOWERCASE) text = func("LOWER", text);
            return text;
        }

        // py: sqlglot/generator.py::Generator.hex_sql #0000000000
        public virtual string hex_sql(exp.Hex expression)
        {
            var text = func(HEX_FUNC, sql(expression, "this"));
            if (dialect.HEX_LOWERCASE) text = func("LOWER", text);
            return text;
        }

        // py: sqlglot/generator.py::Generator.datefromunixdate_sql #0000000000
        public virtual string datefromunixdate_sql(exp.DateFromUnixDate expression) =>
            sql(new exp.DateAdd(@this: exp_cast(exp.Literal.@string("1970-01-01"), exp.DType.DATE),
                expression: expression.@this, unit: new exp.Var(@this: "DAY")));

        // py: sqlglot/generator.py::Generator.connect_sql #0000000000
        public virtual string connect_sql(exp.Connect expression)
        {
            var start = sql(expression, "start");
            start = start.Length != 0 ? seg($"START WITH {start}") : "";
            var nocycle = py.truthy(expression.args.get("nocycle")) ? " NOCYCLE" : "";
            var connect = sql(expression, "connect");
            connect = seg($"CONNECT BY{nocycle} {connect}");
            return start + connect;
        }

        // py: sqlglot/generator.py::Generator.computedcolumnconstraint_sql #0000000000
        public virtual string computedcolumnconstraint_sql(exp.ComputedColumnConstraint expression)
        {
            var @this = sql(expression, "this");
            string persisted;
            if (py.truthy(expression.args.get("not_null"))) persisted = " PERSISTED NOT NULL";
            else if (py.truthy(expression.args.get("persisted"))) persisted = " PERSISTED";
            else persisted = "";

            return $"AS {@this}{persisted}";
        }

        // py: sqlglot/generator.py::Generator.columnposition_sql #0000000000
        public virtual string columnposition_sql(exp.ColumnPosition expression)
        {
            var @this = sql(expression, "this");
            @this = @this.Length != 0 ? $" {@this}" : "";
            var position = sql(expression, "position");
            return $"{position}{@this}";
        }

        // py: sqlglot/generator.py::Generator.bitstring_sql #0000000000
        public virtual string bitstring_sql(exp.BitString expression)
        {
            var @this = sql(expression, "this");
            if (py.truthy(dialect.BIT_START)) return $"{dialect.BIT_START}{@this}{dialect.BIT_END}";
            return BigInteger.Parse("0" + @this.Aggregate(BigInteger.Zero, (acc, c) => acc * 2 + (c - '0')).ToString()).ToString();
        }

        // py: sqlglot/generator.py::Generator.anyvalue_sql #0000000000
        public virtual string anyvalue_sql(exp.AnyValue expression)
        {
            var @this = sql(expression, "this");
            var having = sql(expression, "having");

            if (having.Length != 0) @this = $"{@this} HAVING {(py.truthy(expression.args.get("max")) ? "MAX" : "MIN")} {having}";

            return func("ANY_VALUE", @this);
        }

        // py: sqlglot/generator.py::Generator.withtablehint_sql #0000000000
        public virtual string withtablehint_sql(exp.WithTableHint expression) => $"WITH ({expressions(expression, flat: true)})";

        // py: sqlglot/generator.py::Generator.tochar_sql #0000000000
        public virtual string tochar_sql(exp.ToChar expression) =>
            sql(exp_cast((exp.Expr)expression.args["this"]!, exp.DType.TEXT));

        // py: sqlglot/generator.py::Generator._tonumber_cast_sql #0000000000
        public virtual string _tonumber_cast_sql(exp.ToNumber expression)
        {
            if (py.truthy(expression.args.get("safe")))
                return sql(new exp.TryCast(@this: expression.@this, to: exp.DataType.build("DOUBLE")));
            return sql(exp_cast((exp.Expr)expression.args["this"]!, exp.DType.DOUBLE));
        }

        // py: sqlglot/generator.py::Generator.tonumber_sql #0000000000
        public virtual string tonumber_sql(exp.ToNumber expression)
        {
            if (!SUPPORTS_TO_NUMBER)
            {
                unsupported("Unsupported TO_NUMBER function");
                return _tonumber_cast_sql(expression);
            }

            var fmt = expression.args.get("format");
            if (!py.truthy(fmt))
            {
                unsupported("Conversion format is required for TO_NUMBER");
                return _tonumber_cast_sql(expression);
            }

            return func("TO_NUMBER", expression.@this, fmt);
        }

        // py: sqlglot/generator.py::Generator.strtodate_sql #0000000000
        public virtual string strtodate_sql(exp.StrToDate expression) =>
            func("STR_TO_DATE", expression.@this, expression.args.get("format"));

        // py: sqlglot/generator.py::Generator.semicolon_sql #0000000000
        public virtual string semicolon_sql(exp.Semicolon expression) => "";

        // py: sqlglot/generator.py::Generator.prewhere_sql #0000000000
        public virtual string prewhere_sql(exp.PreWhere expression) => "";

        // py: sqlglot/generator.py::Generator.nthvalue_sql #0000000000
        public virtual string nthvalue_sql(exp.NthValue expression)
        {
            if (Equals(expression.args.get("from_first"), false)) unsupported("NTH_VALUE FROM LAST is not supported");

            return function_fallback_sql(expression);
        }

        // py: sqlglot/generator.py::Generator.nearest_sql #0000000000
        public virtual string nearest_sql(exp.Nearest expression)
        {
            if (!SUPPORTS_NEAREST_JOIN) unsupported("NEAREST joins are not supported");

            var approximate = py.truthy(expression.args.get("approximate")) ? "APPROX " : "";
            var metric = py.truthy(expression.args.get("similarity")) ? "SIMILARITY" : "DISTANCE";
            return $"{approximate}NEAREST {sql(expression, "count")} BY {metric} {sql(expression, "this")}";
        }

        // py: sqlglot/generator.py::Generator.lockingproperty_sql #0000000000
        public virtual string lockingproperty_sql(exp.LockingProperty expression)
        {
            var kind = expression.args.get("kind");
            var @this = py.truthy(expression.@this) ? $" {sql(expression, "this")}" : "";
            var for_or_in = expression.args.get("for_or_in");
            var for_or_in_sql = py.truthy(for_or_in) ? $" {py.str(for_or_in)}" : "";
            var lock_type = expression.args.get("lock_type");
            var @override = py.truthy(expression.args.get("override")) ? " OVERRIDE" : "";
            return $"LOCKING {py.str(kind)}{@this}{for_or_in_sql} {py.str(lock_type)}{@override}";
        }

        // py: sqlglot/generator.py::Generator.fromtimezone_sql #0000000000
        public virtual string fromtimezone_sql(exp.FromTimeZone expression)
        {
            var @this = sql(expression, "this");
            var zone = sql(expression, "zone");
            return $"{@this} AT TIME ZONE {zone} AT TIME ZONE 'UTC'";
        }

        // py: sqlglot/generator.py::Generator.forclause_sql #0000000000
        public virtual string forclause_sql(exp.ForClause expression)
        {
            var kind = expression.args["kind"];
            if (Equals(kind, "BROWSE")) return $"{sep()}FOR BROWSE";
            // FOR XML/JSON always carry at least AUTO/PATH. An empty rendering means
            // the target dialect does not support QueryOption, so we drop the clause.
            var options = expressions(expression, key: "expressions");
            if (options.Length == 0) return "";
            return $"{sep()}FOR {py.str(kind)}{seg(options)}";
        }

        // py: sqlglot/generator.py::Generator.converttimezone_sql #0000000000
        public virtual string converttimezone_sql(exp.ConvertTimezone expression)
        {
            if (SUPPORTS_CONVERT_TIMEZONE) return function_fallback_sql(expression);

            var source_tz = expression.args.get("source_tz");
            var target_tz = expression.args.get("target_tz");
            var timestamp = expression.args.get("timestamp");

            if (py.truthy(source_tz) && py.truthy(timestamp))
                timestamp = new exp.AtTimeZone(@this: exp_cast((exp.Expr)timestamp!, exp.DType.TIMESTAMPNTZ), zone: source_tz);

            var expr = new exp.AtTimeZone(@this: timestamp, zone: target_tz);

            return sql(expr);
        }

        // py: sqlglot/generator.py::Generator.ceil_floor #0000000000
        public virtual string ceil_floor(exp.Expr expression)
        {
            var to_clause = sql(expression, "to");
            if (to_clause.Length != 0) return $"{expression.sql_name()}({sql(expression, "this")} TO {to_clause})";

            return function_fallback_sql(expression);
        }

        // py: sqlglot/generator.py::Generator.repeatblock_sql / loopblock_sql / leave_sql / iterate_sql
        public virtual string repeatblock_sql(exp.RepeatBlock expression) { unsupported("Unsupported Repeat block syntax"); return ""; }
        public virtual string loopblock_sql(exp.LoopBlock expression) { unsupported("Unsupported Loop block syntax"); return ""; }
        public virtual string leave_sql(exp.Leave expression) { unsupported("Unsupported Leave syntax"); return ""; }
        public virtual string iterate_sql(exp.Iterate expression) { unsupported("Unsupported Iterate syntax"); return ""; }

        // py: sqlglot/generator.py::Generator.generatedasrowcolumnconstraint_sql #0000000000
        public virtual string generatedasrowcolumnconstraint_sql(exp.GeneratedAsRowColumnConstraint expression)
        {
            var start = py.truthy(expression.args.get("start")) ? "START" : "END";
            var hidden = py.truthy(expression.args.get("hidden")) ? " HIDDEN" : "";
            return $"GENERATED ALWAYS AS ROW {start}{hidden}";
        }

        // py: sqlglot/generator.py::Generator.periodforsystemtimeconstraint_sql #0000000000
        public virtual string periodforsystemtimeconstraint_sql(exp.PeriodForSystemTimeConstraint expression) =>
            $"PERIOD FOR SYSTEM_TIME ({sql(expression, "this")}, {sql(expression, "expression")})";

        // py: sqlglot/generator.py::Generator.lockingstatement_sql #0000000000
        public virtual string lockingstatement_sql(exp.LockingStatement expression)
        {
            unsupported("LOCKING request modifier is not supported");
            return sql(expression, "expression");
        }
    }
}
