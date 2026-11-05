namespace SqlGlotSharp;

// Builder methods of sqlglot/expressions/query.py, in upstream order.
// Hosting (AGENTS.md section 4.4): Query trait members that Python types as `self: Q -> Q` and that no subclass
// overrides (limit, offset, order_by, where, with_) are generic extension methods `Q f<Q>(this Q self, ...)`, so a
// chain keeps its concrete type (`select(...).from_(...).where(...).group_by(...)`). `select`, overridden per class,
// is a virtual member of Expr with covariant overrides. The `params object?[]` overloads carry no anchor.
public static partial class exp
{
    // py: sqlglot/expressions/query.py::_apply_cte_builder #1e4da61922
    public static Expr _apply_cte_builder(Expr instance, object? alias, object? as_, bool? recursive = null,
        bool? materialized = null, bool append = true, object? dialect = null, bool copy = true, bool? scalar = null)
    {
        var alias_expression = maybe_parse(alias, dialect: dialect, into: typeof(TableAlias));
        var as_expression = maybe_parse(as_, dialect: dialect, copy: copy);
        if (scalar == true && as_expression is not Subquery)
            // scalar CTE must be wrapped in a subquery
            as_expression = new Subquery(@this: as_expression);
        var cte = new CTE(@this: as_expression, alias: alias_expression, materialized: materialized, scalar: scalar);
        return _apply_child_list_builder(
            [cte],
            instance: instance,
            arg: "with_",
            append: append,
            copy: copy,
            into: typeof(With),
            properties: py.truthy(recursive) ? new PyDict<string, object?> { ["recursive"] = recursive } : new PyDict<string, object?>());
    }

    /// <summary>
    /// Set the LIMIT expression.
    ///
    /// Example:
    ///     >>> Select().from_("tbl").select("x").limit(10).sql()
    ///     'SELECT x FROM tbl LIMIT 10'
    /// </summary>
    // py: sqlglot/expressions/query.py::Query.limit #5a886e6914
    public static Q limit<Q>(this Q self, object? expression, object? dialect = null, bool copy = true) where Q : Expr
    {
        py.require_trait<Query>(self);
        return (Q)_apply_builder(
            expression: expression,
            instance: self,
            arg: "limit",
            into: typeof(Limit),
            prefix: "LIMIT",
            dialect: dialect,
            copy: copy,
            into_arg: "expression");
    }

    /// <summary>
    /// Set the OFFSET expression.
    ///
    /// Example:
    ///     >>> Select().from_("tbl").select("x").offset(10).sql()
    ///     'SELECT x FROM tbl OFFSET 10'
    /// </summary>
    // py: sqlglot/expressions/query.py::Query.offset #cc2312f935
    public static Q offset<Q>(this Q self, object? expression, object? dialect = null, bool copy = true) where Q : Expr
    {
        py.require_trait<Query>(self);
        return (Q)_apply_builder(
            expression: expression,
            instance: self,
            arg: "offset",
            into: typeof(Offset),
            prefix: "OFFSET",
            dialect: dialect,
            copy: copy,
            into_arg: "expression");
    }

    /// <summary>
    /// Set the ORDER BY expression.
    ///
    /// Example:
    ///     >>> Select().from_("tbl").select("x").order_by("x DESC").sql()
    ///     'SELECT x FROM tbl ORDER BY x DESC'
    /// </summary>
    // py: sqlglot/expressions/query.py::Query.order_by #526ee4591c
    public static Q order_by<Q>(this Q self, object?[] expressions, bool append = true, object? dialect = null, bool copy = true) where Q : Expr
    {
        py.require_trait<Query>(self);
        return (Q)_apply_child_list_builder(
            expressions,
            instance: self,
            arg: "order",
            append: append,
            copy: copy,
            prefix: "ORDER BY",
            into: typeof(Order),
            dialect: dialect);
    }

    public static Q order_by<Q>(this Q self, params object?[] expressions) where Q : Expr =>
        self.order_by(expressions, true, null, true);

    /// <summary>
    /// Append to or set the WHERE expressions.
    ///
    /// Examples:
    ///     >>> Select().select("x").from_("tbl").where("x = 'a' OR x &lt; 'b'").sql()
    ///     "SELECT x FROM tbl WHERE x = 'a' OR x &lt; 'b'"
    /// </summary>
    // py: sqlglot/expressions/query.py::Query.where #a19b3d7e6e
    public static Q where<Q>(this Q self, object?[] expressions, bool append = true, object? dialect = null, bool copy = true) where Q : Expr
    {
        py.require_trait<Query>(self);
        return (Q)_apply_conjunction_builder(
            expressions.Select(expr => expr is Where where ? where.@this : expr).ToArray(),
            instance: self,
            arg: "where",
            append: append,
            into: typeof(Where),
            dialect: dialect,
            copy: copy);
    }

    public static Q where<Q>(this Q self, params object?[] expressions) where Q : Expr =>
        self.where(expressions, true, null, true);

    /// <summary>
    /// Append to or set the common table expressions.
    ///
    /// Example:
    ///     >>> Select().with_("tbl2", as_="SELECT * FROM tbl").select("x").from_("tbl2").sql()
    ///     'WITH tbl2 AS (SELECT * FROM tbl) SELECT x FROM tbl2'
    /// </summary>
    // py: sqlglot/expressions/query.py::Query.with_ #2ec35dc604
    public static Q with_<Q>(this Q self, object? alias, object? as_, bool? recursive = null, bool? materialized = null,
        bool append = true, object? dialect = null, bool copy = true, bool? scalar = null) where Q : Expr
    {
        py.require_trait<Query>(self);
        return (Q)_apply_cte_builder(
            self,
            alias,
            as_,
            recursive: recursive,
            materialized: materialized,
            append: append,
            dialect: dialect,
            copy: copy,
            scalar: scalar);
    }

    /// <summary>
    /// Initializes a syntax tree from one UNION expression.
    ///
    /// Example:
    ///     >>> union("SELECT * FROM foo", "SELECT * FRO BLA").sql()
    ///     'SELECT * FROM foo UNION SELECT * FROM bla'
    /// </summary>
    // py: sqlglot/expressions/query.py::union #bf14dbcfa9
    public static Union union(object?[] expressions, bool distinct = true, object? dialect = null, bool copy = true)
    {
        if (!(expressions.Length >= 2)) throw new InvalidOperationException("At least two expressions are required by `union`.");
        return (Union)_apply_set_operation(
            expressions, set_operation: typeof(Union), distinct: distinct, dialect: dialect, copy: copy);
    }

    public static Union union(params object?[] expressions) => union(expressions, true, null, true);

    /// <summary>
    /// Initializes a syntax tree from one INTERSECT expression.
    ///
    /// Example:
    ///     >>> intersect("SELECT * FROM foo", "SELECT * FROM bla").sql()
    ///     'SELECT * FROM foo INTERSECT SELECT * FROM bla'
    /// </summary>
    // py: sqlglot/expressions/query.py::intersect #29a093fe23
    public static Intersect intersect(object?[] expressions, bool distinct = true, object? dialect = null, bool copy = true)
    {
        if (!(expressions.Length >= 2)) throw new InvalidOperationException("At least two expressions are required by `intersect`.");
        return (Intersect)_apply_set_operation(
            expressions, set_operation: typeof(Intersect), distinct: distinct, dialect: dialect, copy: copy);
    }

    public static Intersect intersect(params object?[] expressions) => intersect(expressions, true, null, true);

    /// <summary>
    /// Initializes a syntax tree from one EXCEPT expression.
    ///
    /// Example:
    ///     >>> except_("SELECT * FROM foo", "SELECT * FROM bla").sql()
    ///     'SELECT * FROM foo EXCEPT SELECT * FROM bla'
    /// </summary>
    // py: sqlglot/expressions/query.py::except_ #944bc9ab40
    public static Except except_(object?[] expressions, bool distinct = true, object? dialect = null, bool copy = true)
    {
        if (!(expressions.Length >= 2)) throw new InvalidOperationException("At least two expressions are required by `except_`.");
        return (Except)_apply_set_operation(
            expressions, set_operation: typeof(Except), distinct: distinct, dialect: dialect, copy: copy);
    }

    public static Except except_(params object?[] expressions) => except_(expressions, true, null, true);

    public abstract partial class Expr
    {
        #region trait Query
        // py: sqlglot/expressions/query.py::Query.ctes #1ec5fe6056
        public virtual List<object?> ctes
        {
            get
            {
                py.require_trait<Query>(this);
                var with_ = args.get("with_") as Expr;
                return py.truthy(with_) ? with_!.expressions : [];
            }
        }

        /// <summary>
        /// Append to or set the SELECT expressions.
        ///
        /// Example:
        ///     >>> Select().select("x", "y").sql()
        ///     'SELECT x, y'
        /// </summary>
        // py: sqlglot/expressions/query.py::Query.select #ca8e3c152c
        public virtual Expr select(object?[] expressions, bool append = true, object? dialect = null, bool copy = true)
        {
            py.require_trait<Query>(this);
            throw new NotImplementedException("Query objects must implement `select`");
        }

        public Expr select(params object?[] expressions) => select(expressions, append: true, dialect: null, copy: true);

        /// <summary>
        /// Builds a UNION expression.
        ///
        /// Example:
        ///     >>> import sqlglot
        ///     >>> sqlglot.parse_one("SELECT * FROM foo").union("SELECT * FROM bla").sql()
        ///     'SELECT * FROM foo UNION SELECT * FROM bla'
        /// </summary>
        // py: sqlglot/expressions/query.py::Query.union #587fbf4c44
        public Union union(object?[] expressions, bool distinct = true, object? dialect = null, bool copy = true)
        {
            py.require_trait<Query>(this);
            return exp.union([this, .. expressions], distinct: distinct, dialect: dialect, copy: copy);
        }

        public Union union(params object?[] expressions) => union(expressions, true, null, true);

        /// <summary>
        /// Builds an INTERSECT expression.
        /// </summary>
        // py: sqlglot/expressions/query.py::Query.intersect #f22a71cf87
        public Intersect intersect(object?[] expressions, bool distinct = true, object? dialect = null, bool copy = true)
        {
            py.require_trait<Query>(this);
            return exp.intersect([this, .. expressions], distinct: distinct, dialect: dialect, copy: copy);
        }

        public Intersect intersect(params object?[] expressions) => intersect(expressions, true, null, true);

        /// <summary>
        /// Builds an EXCEPT expression.
        /// </summary>
        // py: sqlglot/expressions/query.py::Query.except_ #dc37aec9f9
        public Except except_(object?[] expressions, bool distinct = true, object? dialect = null, bool copy = true)
        {
            py.require_trait<Query>(this);
            return exp.except_([this, .. expressions], distinct: distinct, dialect: dialect, copy: copy);
        }

        public Except except_(params object?[] expressions) => except_(expressions, true, null, true);
        #endregion
    }

    public partial class SetOperation
    {
        // py: sqlglot/expressions/query.py::SetOperation.select #95a2322d41
        public override SetOperation select(object?[] expressions, bool append = true, object? dialect = null, bool copy = true)
        {
            var @this = (SetOperation)maybe_copy(this, copy)!;
            ((Expr)@this.@this!).unnest().select(expressions, append: append, dialect: dialect, copy: false);
            ((Expr)@this.expression!).unnest().select(
                expressions, append: append, dialect: dialect, copy: false);
            return @this;
        }

        public new SetOperation select(params object?[] expressions) => select(expressions, append: true, dialect: null, copy: true);
    }

    public partial class Join
    {
        // py: sqlglot/expressions/query.py::Join.method #2083e06bf7
        public string method => text("method").ToUpperInvariant();

        // py: sqlglot/expressions/query.py::Join.kind #a51dfa0f89
        public string kind => text("kind").ToUpperInvariant();

        // py: sqlglot/expressions/query.py::Join.side #9ae5703c19
        public string side => text("side").ToUpperInvariant();
    }

    public partial class Select
    {
        /// <summary>
        /// Set the FROM expression.
        ///
        /// Example:
        ///     >>> Select().from_("tbl").select("x").sql()
        ///     'SELECT x FROM tbl'
        /// </summary>
        // py: sqlglot/expressions/query.py::Select.from_ #893c1b5144
        public Select from_(object? expression, object? dialect = null, bool copy = true) =>
            (Select)_apply_builder(
                expression: expression,
                instance: this,
                arg: "from_",
                into: typeof(From),
                prefix: "FROM",
                dialect: dialect,
                copy: copy);

        /// <summary>
        /// Set the GROUP BY expression.
        ///
        /// Example:
        ///     >>> Select().from_("tbl").select("x", "COUNT(1)").group_by("x").sql()
        ///     'SELECT x, COUNT(1) FROM tbl GROUP BY x'
        /// </summary>
        // py: sqlglot/expressions/query.py::Select.group_by #fbab67c256
        public Select group_by(object?[] expressions, bool append = true, object? dialect = null, bool copy = true)
        {
            if (expressions.Length == 0)
                return !copy ? this : (Select)this.copy();

            return (Select)_apply_child_list_builder(
                expressions,
                instance: this,
                arg: "group",
                append: append,
                copy: copy,
                prefix: "GROUP BY",
                into: typeof(Group),
                dialect: dialect);
        }

        public Select group_by(params object?[] expressions) => group_by(expressions, true, null, true);

        /// <summary>
        /// Set the SORT BY expression.
        /// </summary>
        // py: sqlglot/expressions/query.py::Select.sort_by #2e9d430857
        public Select sort_by(object?[] expressions, bool append = true, object? dialect = null, bool copy = true) =>
            (Select)_apply_child_list_builder(
                expressions,
                instance: this,
                arg: "sort",
                append: append,
                copy: copy,
                prefix: "SORT BY",
                into: typeof(Sort),
                dialect: dialect);

        public Select sort_by(params object?[] expressions) => sort_by(expressions, true, null, true);

        /// <summary>
        /// Set the CLUSTER BY expression.
        /// </summary>
        // py: sqlglot/expressions/query.py::Select.cluster_by #93ef34112b
        public Select cluster_by(object?[] expressions, bool append = true, object? dialect = null, bool copy = true) =>
            (Select)_apply_child_list_builder(
                expressions,
                instance: this,
                arg: "cluster",
                append: append,
                copy: copy,
                prefix: "CLUSTER BY",
                into: typeof(Cluster),
                dialect: dialect);

        public Select cluster_by(params object?[] expressions) => cluster_by(expressions, true, null, true);

        // py: sqlglot/expressions/query.py::Select.select #4c310f2840
        public override Select select(object?[] expressions, bool append = true, object? dialect = null, bool copy = true) =>
            (Select)_apply_list_builder(
                expressions,
                instance: this,
                arg: "expressions",
                append: append,
                dialect: dialect,
                into: typeof(Expr),
                copy: copy);

        public new Select select(params object?[] expressions) => select(expressions, append: true, dialect: null, copy: true);

        /// <summary>
        /// Append to or set the LATERAL expressions.
        /// </summary>
        // py: sqlglot/expressions/query.py::Select.lateral #605150b5ce
        public Select lateral(object?[] expressions, bool append = true, object? dialect = null, bool copy = true) =>
            (Select)_apply_list_builder(
                expressions,
                instance: this,
                arg: "laterals",
                append: append,
                into: typeof(Lateral),
                prefix: "LATERAL VIEW",
                dialect: dialect,
                copy: copy);

        public Select lateral(params object?[] expressions) => lateral(expressions, true, null, true);

        /// <summary>
        /// Append to or set the JOIN expressions.
        ///
        /// Example:
        ///     >>> Select().select("*").from_("tbl").join("tbl2", on="tbl1.y = tbl2.y").sql()
        ///     'SELECT * FROM tbl JOIN tbl2 ON tbl1.y = tbl2.y'
        ///
        ///     >>> Select().select("1").from_("a").join("b", using=["x", "y", "z"]).sql()
        ///     'SELECT 1 FROM a JOIN b USING (x, y, z)'
        ///
        ///     Use `join_type` to change the type of join:
        ///
        ///     >>> Select().select("*").from_("tbl").join("tbl2", on="tbl1.y = tbl2.y", join_type="left outer").sql()
        ///     'SELECT * FROM tbl LEFT OUTER JOIN tbl2 ON tbl1.y = tbl2.y'
        /// </summary>
        // py: sqlglot/expressions/query.py::Select.join #cd3b49ca96
        public Select join(object? expression, object? on = null, object? @using = null, bool append = true,
            string? join_type = null, object? join_alias = null, object? dialect = null, bool copy = true)
        {
            try
            {
                expression = maybe_parse(expression, into: typeof(Join), prefix: "JOIN", dialect: dialect);
            }
            catch (errors.ParseError)
            {
                expression = maybe_parse(expression, into: new[] { typeof(Join), typeof(Expr) }, dialect: dialect);
            }

            var join = expression is Join node ? node : new Join(@this: expression);

            if (join.@this is Select select)
                select.replace(select.subquery());

            if (py.truthy(join_type))
            {
                var new_join = maybe_parse($"FROM _ {join_type} JOIN _", dialect: dialect).find<Join>()!;
                var method = new_join.method;
                var side = new_join.side;
                var kind = new_join.kind;

                if (py.truthy(method))
                    join.set("method", method);
                if (py.truthy(side))
                    join.set("side", side);
                if (py.truthy(kind))
                    join.set("kind", kind);
            }

            if (py.truthy(on))
            {
                var on_exprs = helper.ensure_list(on);
                on = exp.and_(on_exprs.Cast<object?>().ToArray(), dialect: dialect, copy: copy);
                join.set("on", on);
            }

            if (py.truthy(@using))
            {
                var using_exprs = helper.ensure_list(@using);
                join = (Join)_apply_list_builder(
                    using_exprs.Cast<object?>().ToArray(),
                    instance: join,
                    arg: "using",
                    append: append,
                    copy: copy,
                    into: typeof(Identifier));
            }

            if (py.truthy(join_alias))
                join.set("this", alias_(join.@this, join_alias, table: true));

            return (Select)_apply_list_builder(
                [join],
                instance: this,
                arg: "joins",
                append: append,
                copy: copy);
        }

        /// <summary>
        /// Append to or set the HAVING expressions.
        ///
        /// Example:
        ///     >>> Select().select("x", "COUNT(y)").from_("tbl").group_by("x").having("COUNT(y) > 3").sql()
        ///     'SELECT x, COUNT(y) FROM tbl GROUP BY x HAVING COUNT(y) > 3'
        /// </summary>
        // py: sqlglot/expressions/query.py::Select.having #9b2423ca76
        public Select having(object?[] expressions, bool append = true, object? dialect = null, bool copy = true) =>
            (Select)_apply_conjunction_builder(
                expressions,
                instance: this,
                arg: "having",
                append: append,
                into: typeof(Having),
                dialect: dialect,
                copy: copy);

        public Select having(params object?[] expressions) => having(expressions, true, null, true);

        // py: sqlglot/expressions/query.py::Select.window #92b292805e
        public Select window(object?[] expressions, bool append = true, object? dialect = null, bool copy = true) =>
            (Select)_apply_list_builder(
                expressions,
                instance: this,
                arg: "windows",
                append: append,
                into: typeof(Window),
                dialect: dialect,
                copy: copy);

        public Select window(params object?[] expressions) => window(expressions, true, null, true);

        // py: sqlglot/expressions/query.py::Select.qualify #49ec5862f1
        public Select qualify(object?[] expressions, bool append = true, object? dialect = null, bool copy = true) =>
            (Select)_apply_conjunction_builder(
                expressions,
                instance: this,
                arg: "qualify",
                append: append,
                into: typeof(Qualify),
                dialect: dialect,
                copy: copy);

        public Select qualify(params object?[] expressions) => qualify(expressions, true, null, true);

        /// <summary>
        /// Set the OFFSET expression.
        ///
        /// Example:
        ///     >>> Select().from_("tbl").select("x").distinct().sql()
        ///     'SELECT DISTINCT x FROM tbl'
        /// </summary>
        // py: sqlglot/expressions/query.py::Select.distinct #9cdeeb0f0d
        public Select distinct(object?[] ons, bool distinct = true, bool copy = true)
        {
            var instance = (Select)maybe_copy(this, copy)!;
            var on = ons.Length > 0
                ? new Tuple(expressions: ons.Where(on => py.truthy(on)).Select(on => (object?)maybe_parse(on, copy: copy)).ToList())
                : null;
            instance.set("distinct", distinct ? new Distinct(on: on) : null);
            return instance;
        }

        public Select distinct(params object?[] ons) => distinct(ons, true, true);

        /// <summary>
        /// Set the locking read mode for this expression.
        ///
        /// Examples:
        ///     >>> Select().select("x").from_("tbl").where("x = 'a'").lock().sql("mysql")
        ///     "SELECT x FROM tbl WHERE x = 'a' FOR UPDATE"
        /// </summary>
        // py: sqlglot/expressions/query.py::Select.lock #8aa6976cd7
        public Select @lock(bool update = true, bool copy = true)
        {
            var inst = (Select)maybe_copy(this, copy)!;
            inst.set("locks", new List<object?> { new Lock(update: update) });
            return inst;
        }

        /// <summary>
        /// Set hints for this expression.
        /// </summary>
        // py: sqlglot/expressions/query.py::Select.hint #e7845122e0
        public Select hint(object?[] hints, object? dialect = null, bool copy = true)
        {
            var inst = (Select)maybe_copy(this, copy)!;
            inst.set(
                "hint", new Hint(expressions: hints.Select(h => (object?)maybe_parse(h, copy: copy, dialect: dialect)).ToList()));
            return inst;
        }

        public Select hint(params object?[] hints) => hint(hints, null, true);
    }

    public partial class Subquery
    {
        // py: sqlglot/expressions/query.py::Subquery.select #da3c9a447b
        public override Subquery select(object?[] expressions, bool append = true, object? dialect = null, bool copy = true)
        {
            var @this = (Subquery)maybe_copy(this, copy)!;
            var inner = @this.unnest();
            // hasattr(inner, "select"): only Query types define it
            if (inner is Query)
                inner.select(expressions, append: append, dialect: dialect, copy: false);
            return @this;
        }

        public new Subquery select(params object?[] expressions) => select(expressions, append: true, dialect: null, copy: true);
    }

    /// <summary>
    /// Initializes a syntax tree from one or multiple SELECT expressions.
    ///
    /// Example:
    ///     >>> select("col1", "col2").from_("tbl").sql()
    ///     'SELECT col1, col2 FROM tbl'
    /// </summary>
    // py: sqlglot/expressions/builders.py::select #056d031536
    public static Select select(object?[] expressions, object? dialect = null, bool copy = true) =>
        new Select().select(expressions, dialect: dialect, copy: copy);

    public static Select select(params object?[] expressions) => select(expressions, null, true);

    /// <summary>
    /// Initializes a syntax tree from a FROM expression.
    ///
    /// Example:
    ///     >>> from_("tbl").select("col1", "col2").sql()
    ///     'SELECT col1, col2 FROM tbl'
    /// </summary>
    // py: sqlglot/expressions/builders.py::from_ #a0c1a0833f
    public static Select from_(object? expression, object? dialect = null, bool copy = true) =>
        new Select().from_(expression, dialect: dialect, copy: copy);
}
