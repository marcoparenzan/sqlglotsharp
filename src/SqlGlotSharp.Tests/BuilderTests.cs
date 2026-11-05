namespace SqlGlotSharp.Tests;

/// <summary>
/// SQL builders (core.py condition/and_/or_/not_/alias_/convert, query.py Query/Select methods, builders.py select/from_).
/// Every expected string was produced by the Python 3.11 oracle (sqlglot v30.27.0) on the same calls.
/// </summary>
public class BuilderTests
{
    private static readonly (string Expected, Func<object> Build)[] Cases =
    [
        ("x = 1 AND y = 1", () => exp.condition("x=1").and_("y=1")),
        ("SELECT * FROM y WHERE x = 1 AND y = 1", () => exp.select("*").from_("y").where(exp.condition("x=1").and_("y=1"))),
        ("SELECT x FROM z", () => SqlGlot.parse_one("SELECT x FROM y").Let(e => ((exp.Select)e).from_("z"))),
        ("x = 1 AND (y = 1 AND z = 1)", () => exp.and_("x=1", exp.and_("y=1", "z=1"))),
        ("x = 1 OR (y = 1 OR z = 1)", () => exp.or_("x=1", exp.or_("y=1", "z=1"))),
        ("x = 1 XOR (y = 1 XOR z = 1)", () => exp.xor("x=1", exp.xor("y=1", "z=1"))),
        ("NOT this_suit = 'black'", () => exp.not_("this_suit='black'")),
        ("(5 + 3)", () => exp.paren("5 + 3")),
        ("foo AS bar", () => exp.alias_("foo", "bar")),
        ("(SELECT 1, 2) AS bar(a, b)", () => exp.alias_("(select 1, 2)", "bar", table: new[] { "a", "b" })),
        ("SELECT col1, col2 FROM tbl", () => exp.select("col1", "col2").from_("tbl")),
        ("SELECT col1, col2 FROM tbl", () => exp.from_("tbl").select("col1", "col2")),
        ("SELECT x FROM tbl LIMIT 10", () => new exp.Select().from_("tbl").select("x").limit(10)),
        ("SELECT x FROM tbl OFFSET 10", () => new exp.Select().from_("tbl").select("x").offset(10)),
        ("SELECT x FROM tbl ORDER BY x DESC", () => new exp.Select().from_("tbl").select("x").order_by("x DESC")),
        ("SELECT x FROM tbl WHERE x = 'a' OR x < 'b'", () => new exp.Select().select("x").from_("tbl").where("x = 'a' OR x < 'b'")),
        ("WITH tbl2 AS (SELECT * FROM tbl) SELECT x FROM tbl2", () => new exp.Select().with_("tbl2", as_: "SELECT * FROM tbl").select("x").from_("tbl2")),
        ("WITH RECURSIVE tbl2 AS (SELECT * FROM tbl) SELECT x FROM tbl2", () => new exp.Select().with_("tbl2", as_: "SELECT * FROM tbl", recursive: true).select("x").from_("tbl2")),
        ("SELECT * FROM foo UNION SELECT * FROM bla", () => exp.union("SELECT * FROM foo", "SELECT * FROM bla")),
        ("SELECT * FROM foo INTERSECT SELECT * FROM bla", () => exp.intersect("SELECT * FROM foo", "SELECT * FROM bla")),
        ("SELECT * FROM foo EXCEPT SELECT * FROM bla", () => exp.except_("SELECT * FROM foo", "SELECT * FROM bla")),
        ("SELECT * FROM foo UNION SELECT * FROM bla", () => SqlGlot.parse_one("SELECT * FROM foo").union("SELECT * FROM bla")),
        ("SELECT x, COUNT(1) FROM tbl GROUP BY x", () => new exp.Select().from_("tbl").select("x", "COUNT(1)").group_by("x")),
        ("SELECT * FROM tbl JOIN tbl2 ON tbl1.y = tbl2.y", () => new exp.Select().select("*").from_("tbl").join("tbl2", on: "tbl1.y = tbl2.y")),
        ("SELECT 1 FROM a JOIN b USING (x, y, z)", () => new exp.Select().select("1").from_("a").join("b", @using: new[] { "x", "y", "z" })),
        ("SELECT * FROM tbl LEFT OUTER JOIN tbl2 ON tbl1.y = tbl2.y", () => new exp.Select().select("*").from_("tbl").join("tbl2", on: "tbl1.y = tbl2.y", join_type: "left outer")),
        ("SELECT * FROM tbl INNER JOIN (SELECT a FROM t2) AS s", () => new exp.Select().select("*").from_("tbl").join(exp.select("a").from_("t2"), join_alias: "s", join_type: "inner")),
        ("SELECT x, COUNT(y) FROM tbl GROUP BY x HAVING COUNT(y) > 3", () => new exp.Select().select("x", "COUNT(y)").from_("tbl").group_by("x").having("COUNT(y) > 3")),
        ("SELECT DISTINCT x FROM tbl", () => new exp.Select().from_("tbl").select("x").distinct()),
        ("SELECT DISTINCT ON (a, b) x FROM tbl", () => new exp.Select().from_("tbl").select("x").distinct("a", "b")),
        ("SELECT x FROM tbl WHERE x = 'a'", () => new exp.Select().select("x").from_("tbl").where("x = 'a'").@lock()),
        ("SELECT x FROM t WHERE a = 1 AND b = 2 ORDER BY x LIMIT 5 OFFSET 2", () => exp.select("x").from_("t").where("a = 1").where("b = 2").order_by("x").limit(5).offset(2)),
        ("(a = 1 AND b IN (1, 2, 3)) OR c BETWEEN 1 AND 10", () => exp.column("a").eq(1).and_(exp.column("b").isin(1, 2, 3)).or_(exp.column("c").between(1, 10))),
        ("NOT a LIKE 'x%'", () => exp.column("a").like("x%").not_()),
        ("a IS NULL", () => exp.column("a").is_(new exp.Null())),
        ("a AS b", () => exp.column("a").as_("b")),
        ("a DESC", () => exp.column("a").desc()),
        ("a / 2", () => exp.column("a").div(2, typed: true)),
        ("ARRAY(1, 'a', TRUE, NULL, 2.5)", () => exp.convert(new object?[] { 1, "a", true, null, 2.5 })),
        ("MAP(ARRAY('a'), ARRAY(1))", () => exp.convert(new Dictionary<string, object> { ["a"] = 1 })),
        ("DATE_STR_TO_DATE('2021-01-02')", () => exp.convert(new DateOnly(2021, 1, 2))),
        ("TIME_STR_TO_TIME('2021-01-02 03:04:05')", () => exp.convert(new DateTime(2021, 1, 2, 3, 4, 5))),
        ("SELECT a, c FROM t UNION SELECT b, c FROM u", () => exp.select("a").from_("t").union(exp.select("b").from_("u")).select("c")),
        ("(SELECT a, b FROM t)", () => SqlGlot.parse_one("(SELECT a FROM t)").Let(e => ((exp.Expr)e).select("b"))),
        ("a IN (SELECT b FROM t)", () => exp.column("a").isin(new object?[0], query: "SELECT b FROM t")),
        ("0", () => exp.select("a").from_("t").where("x > 1", "y < 2").group_by().ctes.Count),
        ("1", () => exp.select("x").from_("t").with_("t", as_: "SELECT 1 AS x").ctes.Count),
    ];

    public static TheoryData<int> Indexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Cases.Length; i++) data.Add(i);
        return data;
    }

    [Theory]
    [MemberData(nameof(Indexes))]
    public void Builder_output_matches_the_python_oracle(int index)
    {
        var (expected, build) = Cases[index];
        var result = build();
        Assert.Equal(expected, result is exp.Expr node ? node.sql() : Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Union_needs_two_expressions()
    {
        Assert.Throws<InvalidOperationException>(() => exp.union("SELECT 1"));
    }

    [Fact]
    public void Builders_do_not_mutate_their_input_by_default()
    {
        var original = exp.select("a").from_("t");
        var changed = original.where("x = 1");
        Assert.Equal("SELECT a FROM t", original.sql());
        Assert.Equal("SELECT a FROM t WHERE x = 1", changed.sql());
    }
}

internal static class BuilderTestExtensions
{
    public static T2 Let<T1, T2>(this T1 x, Func<T1, T2> f) => f(x);
}
