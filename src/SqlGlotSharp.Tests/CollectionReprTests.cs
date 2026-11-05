namespace SqlGlotSharp.Tests;

public class CollectionReprTests
{
    [Fact]
    public void Mappings_preserve_order_null_and_cycles()
    {
        var mapping = new PyKwargs { ["a"] = 1, ["b"] = null };
        Assert.Equal("{'a': 1, 'b': None}", py.repr(mapping));
        mapping["a"] = 2;
        Assert.Equal("{'a': 2, 'b': None}", py.str(mapping));
        mapping["self"] = mapping;
        Assert.Equal("{'a': 2, 'b': None, 'self': {...}}", py.repr(mapping));
        Assert.Equal("{}", py.repr(new Dictionary<string, int>()));
        Assert.Equal("{'x': 3}", py.repr(new Dictionary<string, int> { ["x"] = 3 }));
    }

    [Fact]
    public void Tuples_and_sets_use_python_delimiters()
    {
        Assert.Equal("()", py.repr(ValueTuple.Create()));
        Assert.Equal("('x',)", py.repr(ValueTuple.Create("x")));
        Assert.Equal("(1, 'x')", py.repr((1, "x")));
        Assert.Equal("set()", py.repr(new HashSet<int>()));
        Assert.Equal("{1}", py.repr(new HashSet<int> { 1 }));
        var set = new HashSet<int> { 2, 1 };
        Assert.Equal("{" + string.Join(", ", set) + "}", py.repr(set));
    }

    [Fact]
    public void Shared_noncyclic_children_are_rendered_each_time()
    {
        var child = new PyKwargs { ["a"] = true };
        Assert.Equal("[{'a': True}, {'a': True}]", py.repr(new List<object> { child, child }));
        var list = new List<object>();
        var mapping = new PyKwargs { ["list"] = list };
        list.Add(mapping);
        Assert.Equal("{'list': [{...}]}", py.repr(mapping));
    }
}
