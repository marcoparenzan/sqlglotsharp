namespace SqlGlotSharp;

// Collection handlers from sqlglot/generator.py, in upstream source order.
// Children use production dispatch, including explicit failures for unported nodes.
public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.tuple_sql #b20000cc37
        public virtual string tuple_sql(exp.Tuple expression) =>
            $"({expressions(expression, dynamic: true, new_line: true, skip_first: true, skip_last: true)})";

        // py: sqlglot/generator.py::Generator.star_sql #945f944ac6
        public virtual string star_sql(exp.Star expression)
        {
            var except_ = expressions(expression, key: "except_", flat: true);
            except_ = except_.Length != 0 ? $"{seg(STAR_EXCEPT)} ({except_})" : "";
            var replace = expressions(expression, key: "replace", flat: true);
            replace = replace.Length != 0 ? $"{seg("REPLACE")} ({replace})" : "";
            var rename = expressions(expression, key: "rename", flat: true);
            rename = rename.Length != 0 ? $"{seg("RENAME")} ({rename})" : "";
            var ilike = sql(expression, "ilike");
            ilike = ilike.Length != 0 ? $"{seg("ILIKE")} {ilike}" : "";
            return $"*{ilike}{except_}{replace}{rename}";
        }
    }
}
