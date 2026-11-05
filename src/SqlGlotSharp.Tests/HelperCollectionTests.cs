namespace SqlGlotSharp.Tests;

public class HelperCollectionTests
{
    [Fact]
    public void List_and_collection_normalization_preserve_existing_identity()
    {
        var list = new List<int> { 1 };
        var set = new HashSet<int> { 1 };
        Assert.Same(list, helper.ensure_list(list));
        Assert.Same(set, helper.ensure_collection(set));
        Assert.Equal(new object?[] { 1, "x" }, helper.ensure_list((1, "x")).Cast<object?>());
        Assert.Empty(helper.ensure_list(null).Cast<object?>());
        Assert.Equal(new object?[] { "abc" }, helper.ensure_list("abc").Cast<object?>());
        Assert.IsType<List<object?>>(helper.ensure_collection(new byte[] { 1 }));
        object tuple = (1, 2);
        Assert.Same(tuple, helper.ensure_collection(tuple));
    }

    [Theory]
    [InlineData("camelCase", "CAMEL_CASE")]
    [InlineData("HTTPServer", "H_T_T_P_SERVER")]
    [InlineData("", "")]
    public void Camel_conversion_matches_python_ascii_names(string input, string expected) => Assert.Equal(expected, helper.camel_to_snake_case(input));

    [Fact]
    public void Csv_names_and_padding_match_upstream_rules()
    {
        Assert.Equal("a, b", helper.csv("a", "", null, "b"));
        Assert.Equal("a|b", helper.csv(new[] { "a", "b" }, sep: "|"));
        Assert.Equal("x_4", helper.find_new_name(new[] { "x", "x_2", "x_3" }, "x"));
        Assert.Equal("y", helper.find_new_name(new[] { "x" }, "y"));
        var next = helper.name_sequence("a");
        Assert.Equal(new[] { "a0", "a1", "a2" }, new[] { next(), next(), next() });
        Assert.Equal(new string?[] { null, "db", "table" }, helper.split_num_words("db.table", ".", 3));
        Assert.Equal(new string?[] { "db", "table", null }, helper.split_num_words("db.table", ".", 3, false));
        Assert.Equal(new[] { "db", "", "table" }, helper.split_num_words("db..table", ".", 1));
        Assert.Throws<ArgumentException>(() => helper.split_num_words("x", "", 2));
    }

    [Fact]
    public void Iterable_detection_excludes_sql_nodes_text_and_bytes()
    {
        Assert.True(helper.is_iterable(new List<int>()));
        Assert.True(helper.is_iterable((1, 2)));
        Assert.False(helper.is_iterable("abc"));
        Assert.False(helper.is_iterable(new byte[1]));
        Assert.False(helper.is_iterable(new exp.Literal(@this: "1")));
        Assert.False(helper.is_iterable(null));
    }
}
