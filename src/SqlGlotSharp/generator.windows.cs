namespace SqlGlotSharp;

public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.filter_sql #0000000000
        public virtual string filter_sql(exp.Filter expression) =>
            $"{sql(expression, "this")} FILTER({Strip(sql(expression, "expression"))})";

        // py: sqlglot/generator.py::Generator.withingroup_sql #0000000000
        public virtual string withingroup_sql(exp.WithinGroup expression)
        {
            var value = sql(expression, "this");
            var order = sql(expression, "expression");
            return $"{value} WITHIN GROUP ({(order.Length > 0 ? order[1..] : "")})";
        }

        // py: sqlglot/generator.py::Generator.per_sql #0000000000
        public virtual string per_sql(exp.Per expression) =>
            $"{sql(expression, "this")} PER ({expressions(expression, flat: true)})";

        // Bounded by order_sql/ordered_sql and child dispatch: unsupported NULL-ordering
        // simulation and unported child expressions remain explicit NotPortedException paths.
        // The window algorithm itself follows upstream, including named-window parentheses.
        // py: sqlglot/generator.py::Generator.window_sql #0000000000
        public virtual string window_sql(exp.Window expression)
        {
            var @this = sql(expression, "this");
            var partition = partition_by_sql(expression);
            var order_arg = expression.args.get("order");
            var order = py.truthy(order_arg) ? order_sql((exp.Order)order_arg!, flat: true) : "";
            var spec = sql(expression, "spec");
            var alias = sql(expression, "alias");
            var over = sql(expression, "over");
            if (over.Length == 0) over = "OVER";

            @this = $"{@this} {(expression.arg_key == "windows" ? "AS" : over)}";
            var first_arg = expression.args.get("first");
            var first = first_arg is null ? "" : py.truthy(first_arg) ? "FIRST" : "LAST";
            if (partition.Length == 0 && order.Length == 0 && spec.Length == 0 && alias.Length != 0)
                return expression.arg_key == "windows" ? $"{@this} ({alias})" : $"{@this} {alias}";

            var args = format_args(new[] { alias, first, partition, order, spec }
                .Where(arg => arg.Length != 0).Cast<object?>().ToArray(), sep: " ");
            return $"{@this} ({args})";
        }

        // py: sqlglot/generator.py::Generator.partition_by_sql #159dcab779
        public virtual string partition_by_sql(exp.Expr expression)
        {
            var partition = expressions(expression, key: "partition_by", flat: true);
            return partition.Length != 0 ? $"PARTITION BY {partition}" : "";
        }

        // py: sqlglot/generator.py::Generator.partitionbyexcluding_sql #257f99f7ce
        public virtual string partitionbyexcluding_sql(exp.PartitionByExcluding expression) =>
            $"EXCLUDING {expressions(expression, flat: true)}";

        // py: sqlglot/generator.py::Generator.windowspec_sql #4264af5859
        public virtual string windowspec_sql(exp.WindowSpec expression)
        {
            var kind = sql(expression, "kind");
            var start = helper.csv([sql(expression, "start"), sql(expression, "start_side")], sep: " ");
            var end = helper.csv([sql(expression, "end"), sql(expression, "end_side")], sep: " ");
            if (end.Length == 0) end = "CURRENT ROW";
            var window_spec = $"{kind} BETWEEN {start} AND {end}";
            var exclude = sql(expression, "exclude");
            if (exclude.Length != 0)
            {
                if (SUPPORTS_WINDOW_EXCLUDE) window_spec += $" EXCLUDE {exclude}";
                else unsupported("EXCLUDE clause is not supported in the WINDOW clause");
            }
            return window_spec;
        }
    }
}
