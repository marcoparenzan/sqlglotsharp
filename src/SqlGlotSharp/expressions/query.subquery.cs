namespace SqlGlotSharp;

public static partial class exp
{
    public abstract partial class Expr
    {
        #region trait Query
        /// <summary>
        /// Returns a `Subquery` that wraps around this query.
        ///
        /// Example:
        ///     >>> subquery = Select().select("x").from_("tbl").subquery()
        ///     >>> Select().select("x").from_(subquery).sql()
        ///     'SELECT x FROM (SELECT x FROM tbl)'
        ///
        /// Args:
        ///     alias: an optional alias for the subquery.
        ///     copy: if `False`, modify this expression instance in-place.
        /// </summary>
        // py: sqlglot/expressions/query.py::Query.subquery #13948a663f
        public virtual Subquery subquery(object? alias = null, bool copy = true)
        {
            py.require_trait<Query>(this);
            var instance = maybe_copy(this, copy);
            if (alias is not Expr)
                alias = py.truthy(alias) ? new TableAlias(@this: to_identifier(alias)) : null;

            return new Subquery(@this: instance, alias: alias);
        }
        #endregion
    }

    // Minimal core builder dependencies of Query.subquery, housed here to keep
    // this port isolated. Expr.copy and constructor parent linkage are already ported.
    // py: sqlglot/expressions/core.py::maybe_copy #4ee472e5e6
    public static Expr? maybe_copy(Expr? instance, bool copy = true) =>
        copy && py.truthy(instance) ? instance!.copy() : instance;

    /// <summary>
    /// Builds an identifier.
    ///
    /// Args:
    ///     name: The name to turn into an identifier.
    ///     quoted: Whether to force quote the identifier.
    ///     copy: Whether to copy name if it's an Identifier.
    ///
    /// Returns:
    ///     The identifier ast node.
    /// </summary>
    // py: sqlglot/expressions/core.py::to_identifier #89917279f0
    public static Identifier? to_identifier(object? name, bool? quoted = null, bool copy = true)
    {
        if (name is null)
            return null;

        Identifier identifier;
        if (name is Identifier node)
            identifier = (Identifier)maybe_copy(node, copy)!;
        else if (name is string text)
            identifier = new Identifier(
                @this: text,
                // Existing runtime implements SAFE_IDENTIFIER_RE.match using
                // Python Unicode word characters and the final-newline '$' rule.
                quoted: quoted is null ? !py.safe_identifier(text) : quoted);
        else
            throw new ArgumentException($"Name needs to be a string or an Identifier, got: {name.GetType()}");
        return identifier;
    }
}

public static partial class exp
{
    public partial class Subquery
    {
        // py: sqlglot/expressions/query.py::Subquery.unnest
        /// <summary>Returns the first non subquery.</summary>
        public override Expr unnest()
        {
            Expr expression = this;
            while (expression is Subquery) expression = (Expr)expression.args["this"]!;
            return expression;
        }
    }
}
