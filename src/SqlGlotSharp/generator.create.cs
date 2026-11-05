namespace SqlGlotSharp;

public static partial class generator
{
    public partial class Generator
    {
        // Bounded CREATE TABLE closure: reject every active argument outside this port,
        // including false-valued clustered (which has SQL meaning upstream).
        internal static void create_guard(exp.Expr expression, params string[] supported)
        {
            foreach (var (key, value) in expression.args)
                if (!supported.Contains(key, StringComparer.Ordinal) && value is not null)
                    throw new NotPortedException($"sqlglot/generator.py::Generator.{expression.key}_sql argument {key}");
        }

        // py: sqlglot/generator.py::Generator.columndef_sql #0000000000
        public virtual string columndef_sql(exp.ColumnDef expression, string sep = " ")
        {
            var column = sql(expression, "this");
            var kind = sql(expression, "kind");
            var constraints = expressions(expression, key: "constraints", sep: " ", flat: true);
            var exists = py.truthy(expression.args.get("exists")) ? "IF NOT EXISTS " : "";
            kind = kind.Length != 0 ? $"{sep}{kind}" : "";
            constraints = constraints.Length != 0 ? $" {constraints}" : "";
            var position = sql(expression, "position");
            position = position.Length != 0 ? $" {position}" : "";

            if (expression.find<exp.ComputedColumnConstraint>() is not null && !COMPUTED_COLUMN_WITH_TYPE) kind = "";

            return $"{exists}{column}{kind}{constraints}{position}";
        }

        // py: sqlglot/generator.py::Generator.columnconstraint_sql #0000000000
        public virtual string columnconstraint_sql(exp.ColumnConstraint expression)
        {
            var name = sql(expression, "this");
            var kind = sql(expression, "kind").Trim();
            return name.Length != 0 ? $"CONSTRAINT {name} {kind}" : kind;
        }

        // Body of the exp.DefaultColumnConstraint lambda in sqlglot/generator.py::Generator.TRANSFORMS
        // (not an anchor: TRANSFORMS is one Python member; see generator.transforms.cs).
        public virtual string defaultcolumnconstraint_sql(exp.DefaultColumnConstraint expression)
        {
            create_guard(expression, "this");
            return $"DEFAULT {sql(expression, "this")}";
        }

        // py: sqlglot/generator.py::Generator.notnullcolumnconstraint_sql #0000000000
        public virtual string notnullcolumnconstraint_sql(exp.NotNullColumnConstraint expression)
        {
            create_guard(expression, "allow_null");
            return $"{(py.truthy(expression.args.get("allow_null")) ? "" : "NOT ")}NULL";
        }

        // py: sqlglot/generator.py::Generator.inoutcolumnconstraint_sql #0000000000
        public virtual string inoutcolumnconstraint_sql(exp.InOutColumnConstraint expression)
        {
            create_guard(expression, "input_", "output", "variadic");
            if (py.truthy(expression.args.get("variadic"))) return "VARIADIC";
            var input = py.truthy(expression.args.get("input_"));
            var output = py.truthy(expression.args.get("output"));
            return input && output ? $"IN{INOUT_SEPARATOR}OUT" : input ? "IN" : output ? "OUT" : "";
        }
    }
}
