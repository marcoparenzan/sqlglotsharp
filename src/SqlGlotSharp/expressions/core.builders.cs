namespace SqlGlotSharp;

public static partial class exp
{
    // py: sqlglot/expressions/builders.py::column
    /// <summary>Builds a Column node, e.g. <c>exp.column("a", table: "t")</c> is <c>t.a</c>.</summary>
    public static Column column(object col, object? table = null, object? db = null, object? catalog = null,
        bool? quoted = null)
    {
        if (col is Star star)
            return new Column(@this: star, table: to_identifier(table, quoted), db: to_identifier(db, quoted), catalog: to_identifier(catalog, quoted));

        return new Column(@this: to_identifier(col, quoted), table: to_identifier(table, quoted),
            db: to_identifier(db, quoted), catalog: to_identifier(catalog, quoted));
    }

    public abstract partial class Expr
    {
        // py: sqlglot/expressions/core.py::Expr.sql
        /// <summary>Generates SQL for this node in the given dialect (default: the base dialect).</summary>
        public string sql(object? dialect = null, bool pretty = false) =>
            dialects.dialect.Dialect.get_or_raise(dialect).generator(pretty).generate(this);
    }
}
