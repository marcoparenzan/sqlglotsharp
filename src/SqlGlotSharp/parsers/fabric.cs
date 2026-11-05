namespace SqlGlotSharp.parsers;

// Logic of sqlglot/parsers/fabric.py.
public static partial class fabric
{
    public partial class FabricParser
    {
        public FabricParser(errors.ErrorLevel? error_level = null, int error_message_context = 100,
            int max_errors = 3, int max_nodes = -1, object? dialect = null)
            : base(error_level, error_message_context, max_errors, max_nodes,
                dialect ?? new dialects.fabric.Fabric()) { }

        // py: sqlglot/parsers/fabric.py::FabricParser._parse_create
        public override exp.Expr _parse_create()
        {
            var create = base._parse_create();

            if (create is exp.Create && py.str(create.args.get("kind")).ToUpperInvariant() == "TABLE"
                && create.args.get("this") is exp.Schema schema)
            {
                // Transform VARCHAR/CHAR without precision to VARCHAR(1)/CHAR(1)
                foreach (var column in ((System.Collections.IEnumerable)(schema.args.get("expressions") ?? new List<object?>())).Cast<object?>())
                {
                    if (column is exp.ColumnDef column_def && column_def.args.get("kind") is exp.DataType column_type
                        && column_type.args.get("this") is exp.DType t && t is exp.DType.VARCHAR or exp.DType.CHAR
                        && !(column_type.args.get("expressions") is List<object?> { Count: > 0 }))
                        // Add default precision of 1 to VARCHAR/CHAR without precision
                        // When n isn't specified in a data definition or variable declaration statement, the default length is 1.
                        // https://learn.microsoft.com/en-us/sql/t-sql/data-types/char-and-varchar-transact-sql?view=sql-server-ver17#remarks
                        column_type.set("expressions", new List<object?> { exp.Literal.number("1") });
                }
            }

            return create;
        }
    }
}
