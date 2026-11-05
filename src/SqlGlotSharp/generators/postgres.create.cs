namespace SqlGlotSharp.generators;

public static partial class postgres
{
    public partial class PostgresGenerator
    {
        // py: sqlglot/generators/postgres.py::_auto_increment_to_serial
        internal static exp.Expr auto_increment_to_serial(exp.Expr expression)
        {
            var auto = expression.find<exp.AutoIncrementColumnConstraint>();

            if (auto is not null)
            {
                ((List<object?>)expression.args["constraints"]!).Remove(auto.parent);
                var kind = (exp.DataType)expression.args["kind"]!;

                if (Equals(kind.@this, exp.DType.INT)) kind.replace(new exp.DataType(@this: exp.DType.SERIAL));
                else if (Equals(kind.@this, exp.DType.SMALLINT)) kind.replace(new exp.DataType(@this: exp.DType.SMALLSERIAL));
                else if (Equals(kind.@this, exp.DType.BIGINT)) kind.replace(new exp.DataType(@this: exp.DType.BIGSERIAL));
            }

            return expression;
        }

        // py: sqlglot/generators/postgres.py::_serial_to_generated
        internal string create_column_def_sql(exp.ColumnDef expression)
        {
            auto_increment_to_serial(expression);
            if (expression.args.get("kind") is exp.DataType type)
            {
                exp.DType? target = type.@this switch
                {
                    exp.DType.SERIAL => exp.DType.INT,
                    exp.DType.SMALLSERIAL => exp.DType.SMALLINT,
                    exp.DType.BIGSERIAL => exp.DType.BIGINT,
                    _ => null
                };
                if (target is not null)
                {
                    type.replace(new exp.DataType(@this: target.Value));
                    var constraints = (List<object?>)expression.args["constraints"]!;
                    var generated = new exp.ColumnConstraint(kind: new exp.GeneratedAsIdentityColumnConstraint(@this: false));
                    var notnull = new exp.ColumnConstraint(kind: new exp.NotNullColumnConstraint());
                    // Python mutates the raw list: inserted wrappers have no ColumnDef
                    // parent/index. Do not use set/append here or move existing constraints.
                    if (!constraints.Contains(notnull)) constraints.Insert(0, notnull);
                    if (!constraints.Contains(generated)) constraints.Insert(0, generated);
                }
            }
            return columndef_sql(expression);
        }

        // py: sqlglot/generators/postgres.py::PostgresGenerator.commentcolumnconstraint_sql #0000000000
        public virtual string commentcolumnconstraint_sql(exp.CommentColumnConstraint expression)
        {
            unsupported("Column comments are not supported in the CREATE statement");
            return "";
        }

        // py: sqlglot/generators/postgres.py::PostgresGenerator.columndef_sql #0000000000
        public override string columndef_sql(exp.ColumnDef expression, string sep = " ")
        {
            var parameter = expression.find<exp.InOutColumnConstraint>();
            if (parameter is null) return base.columndef_sql(expression, sep);
            var mode = sql(parameter);
            parameter.pop();
            return $"{mode} {base.columndef_sql(expression, sep)}";
        }
    }
}
