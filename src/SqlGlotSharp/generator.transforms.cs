namespace SqlGlotSharp;

// One-line entries of sqlglot/generator.py::Generator.TRANSFORMS.
public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.TRANSFORMS #0000000000 (single-expression lambdas)
        internal static readonly IReadOnlyDictionary<Type, Func<Generator, exp.Expr, string>> SIMPLE_TRANSFORMS =
            new Dictionary<Type, Func<Generator, exp.Expr, string>>
            {
                [typeof(exp.CaseSpecificColumnConstraint)] = (_, e) => $"{(py.truthy(e.args.get("not_")) ? "NOT " : "")}CASESPECIFIC",
                [typeof(exp.CharacterSetColumnConstraint)] = (self, e) => $"CHARACTER SET {self.sql(e, "this")}",
                [typeof(exp.ClusteredColumnConstraint)] = (self, e) => $"CLUSTERED ({self.expressions(e, "this", indent: false)})",
                [typeof(exp.NonClusteredColumnConstraint)] = (self, e) => $"NONCLUSTERED ({self.expressions(e, "this", indent: false)})",
                [typeof(exp.CollateColumnConstraint)] = (self, e) => $"COLLATE {self.sql(e, "this")}",
                [typeof(exp.CommentColumnConstraint)] = (self, e) => $"COMMENT {self.sql(e, "this")}",
                [typeof(exp.ConnectByRoot)] = (self, e) => $"CONNECT_BY_ROOT {self.sql(e, "this")}",
                [typeof(exp.CurrentCatalog)] = (_, _) => "CURRENT_CATALOG",
                [typeof(exp.SessionUser)] = (_, _) => "SESSION_USER",
                [typeof(exp.DateFormatColumnConstraint)] = (self, e) => $"FORMAT {self.sql(e, "this")}",
                [typeof(exp.EncodeColumnConstraint)] = (self, e) => $"ENCODE {self.sql(e, "this")}",
                [typeof(exp.EphemeralColumnConstraint)] = (self, e) => $"EPHEMERAL{(py.truthy(e.@this) ? " " + self.sql(e, "this") : "")}",
                [typeof(exp.HeapProperty)] = (_, _) => "HEAP",
                [typeof(exp.HybridProperty)] = (_, _) => "HYBRID",
                [typeof(exp.InlineLengthColumnConstraint)] = (self, e) => $"INLINE LENGTH {self.sql(e, "this")}",
                [typeof(exp.InputModelProperty)] = (self, e) => $"INPUT{self.sql(e, "this")}",
                [typeof(exp.OutputModelProperty)] = (self, e) => $"OUTPUT{self.sql(e, "this")}",
                [typeof(exp.NotForReplicationColumnConstraint)] = (_, _) => "NOT FOR REPLICATION",
                [typeof(exp.OnCommitProperty)] = (_, e) => $"ON COMMIT {(py.truthy(e.args.get("delete")) ? "DELETE" : "PRESERVE")} ROWS",
                [typeof(exp.OnProperty)] = (self, e) => $"ON {self.sql(e, "this")}",
                [typeof(exp.OnUpdateColumnConstraint)] = (self, e) => $"ON UPDATE {self.sql(e, "this")}",
                [typeof(exp.PathColumnConstraint)] = (self, e) => $"PATH {self.sql(e, "this")}",
                [typeof(exp.TableColumn)] = (self, e) => self.sql(e.@this),
                [typeof(exp.TitleColumnConstraint)] = (self, e) => $"TITLE {self.sql(e, "this")}",
                [typeof(exp.UppercaseColumnConstraint)] = (_, _) => "UPPERCASE",
                [typeof(exp.UtcDate)] = (self, _) => self.sql(new exp.CurrentDate(@this: exp.Literal.@string("UTC"))),
                [typeof(exp.UtcTime)] = (self, _) => self.sql(new exp.CurrentTime(@this: exp.Literal.@string("UTC"))),
                [typeof(exp.UtcTimestamp)] = (self, _) => self.sql(new exp.CurrentTimestamp(@this: exp.Literal.@string("UTC"))),
                [typeof(exp.AnalyzeColumns)] = (self, e) => self.sql(e, "this"),
                [typeof(exp.AnalyzeWith)] = (self, e) => self.expressions(e, prefix: "WITH ", sep: " "),
                [typeof(exp.UsingData)] = (self, e) => $"USING DATA {self.sql(e, "this")}",
                [typeof(exp.Operator)] = (self, e) => self.binary(e, ""), // The operator is produced in binary
                [typeof(exp.Ceil)] = (self, e) => self.ceil_floor(e),
                [typeof(exp.Floor)] = (self, e) => self.ceil_floor(e),
                [typeof(exp.VarMap)] = (self, e) => self.func("MAP", e.args["keys"], e.args["values"]),
            };
    }
}
