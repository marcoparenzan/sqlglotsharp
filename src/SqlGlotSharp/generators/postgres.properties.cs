namespace SqlGlotSharp.generators;

public static partial class postgres
{
    public partial class PostgresGenerator
    {
        // py: sqlglot/generators/postgres.py::PostgresGenerator.with_properties #ae2b502d7f
        public override string with_properties(exp.Properties properties)
        {
            List<object?> with_properties = [];
            List<object?> post_with = [];
            foreach (var prop in properties.expressions)
            {
                if (prop is exp.OnCommitProperty or exp.TableSpaceProperty) post_with.Add(prop);
                else with_properties.Add(prop);
            }

            // list.sort is stable: TableSpaceProperty (True) goes after the other post-WITH properties.
            post_with = post_with.OrderBy(prop => prop is exp.TableSpaceProperty).ToList();
            var sql = base.with_properties(new exp.Properties(expressions: with_properties));
            var post_sql = expressions(sqls: post_with, sep: " ", indent: false);
            return post_sql.Length != 0 ? sql + seg(post_sql, sep: sql.Length != 0 ? " " : "") : sql;
        }
    }
}
