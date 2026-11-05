namespace SqlGlotSharp;

public static partial class generator
{
    public partial class Generator
    {
        // AST-only dependency closure. The VALUES-to-SELECT branch uses local
        // equivalents of alias_ and union builders, never a parser or SQL fallback.
        // String-valued cells requiring maybe_parse remain explicitly unported.
        // py: sqlglot/generator.py::Generator.values_sql #0000000000
        public virtual string values_sql(exp.Values expression, bool values_as_table = true)
        {
            values_as_table = values_as_table && VALUES_AS_TABLE;
            if (values_as_table || expression.find_ancestor(typeof(exp.From), typeof(exp.Join)) is null)
            {
                var args = expressions(expression);
                var alias = sql(expression, "alias");
                var values = $"VALUES{seg("")}{args}";
                if (WRAP_DERIVED_VALUES && (alias.Length != 0 || expression.parent is exp.From or exp.Table))
                    values = $"({values})";
                values = query_modifiers(expression, values);
                return alias.Length != 0 ? $"{values} AS {alias}" : values;
            }

            var alias_node = expression.args.get("alias") as exp.Expr;
            var column_names = alias_node?.args.get("columns") as List<object?>;
            var selects = new List<exp.Expr>();
            foreach (var tuple in expression.expressions)
            {
                if (tuple is not exp.Expr row_node)
                    throw new NotPortedException("sqlglot/generator.py::Generator.values_sql (non-expression row)");
                var row = row_node.expressions;
                if (selects.Count == 0 && column_names is { Count: > 0 })
                    row = row.Zip(column_names, values_sql__alias).Cast<object?>().ToList();
                // Constructor linkage intentionally reparents the unaliased cells,
                // matching upstream's copy=False SELECT/UNION construction.
                selects.Add(new exp.Select(expressions: row));
            }

            if (py.truthy(pretty))
            {
                if (selects.Count == 0)
                    throw new InvalidOperationException("reduce() of empty iterable with no initial value");
                var query = selects[0];
                foreach (var select in selects.Skip(1))
                    query = new exp.Union(@this: query, expression: select, distinct: false);
                return subquery_sql(query.subquery(alias_node?.@this, copy: false));
            }

            var alias_sql = alias_node is not null ? $" AS {sql(alias_node, "this")}" : "";
            var unions = string.Join(" UNION ALL ", selects.Select(select => sql(select)));
            return $"({unions}){alias_sql}";
        }

        private static exp.Expr values_sql__alias(object? value, object? column)
        {
            if (value is not exp.Expr cell)
                throw new NotPortedException("sqlglot/expressions/core.py::alias_ (maybe_parse dependency)");
            var copy = cell.copy();
            var alias = exp.to_identifier(column);
            if (copy.arg_types.ContainsKey("alias") && copy is not exp.Window)
            {
                copy.set("alias", alias);
                return copy;
            }
            return new exp.Alias(@this: copy, alias: alias);
        }
    }
}
