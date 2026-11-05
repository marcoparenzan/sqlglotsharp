namespace SqlGlotSharp.Tests;

public class CopyHelperTests
{
    public class State
    {
        public List<object> items = [];
        public PyKwargs options { get; set; } = new();
        public string computed => "not instance state";
        public static int shared = 7;
    }
    public class CustomCopy
    {
        public object copy() => "copied";
    }
    public class BrokenCopy
    {
        public object copy() => throw new InvalidOperationException("copy failure");
    }

    [Fact]
    public void Object_dict_copies_state_shallowly_and_overrides_with_original_kwargs_values()
    {
        var child = new object();
        var state = new State { items = [child], options = new PyKwargs { ["child"] = child } };
        var result = helper.object_to_dict(state);
        var items = Assert.IsType<List<object>>(result["items"]);
        Assert.NotSame(state.items, items);
        Assert.Same(child, items[0]);
        var options = Assert.IsType<PyDict<string, object?>>(result["options"]);
        Assert.NotSame(state.options, options);
        Assert.Same(child, options["child"]);
        Assert.False(result.ContainsKey("computed"));
        Assert.False(result.ContainsKey("shared"));
        Assert.Same(state.items, helper.object_to_dict(state, new PyKwargs { ["items"] = state.items })["items"]);
    }

    [Fact]
    public void Copy_methods_have_priority_and_errors_are_not_wrapped()
    {
        Assert.Equal("copied", py.copy(new CustomCopy()));
        Assert.Throws<InvalidOperationException>(() => py.copy(new BrokenCopy()));
        Assert.Null(py.copy(null));
        Assert.Equal("text", py.copy("text"));
    }

    [Fact]
    public void Sets_arrays_and_objects_are_independent_shallow_copies()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a" };
        var setCopy = Assert.IsType<HashSet<string>>(py.copy(set));
        setCopy.Add("b");
        Assert.DoesNotContain("b", set);
        Assert.Contains("A", setCopy, setCopy.Comparer);
        var child = new object();
        object[] array = [child];
        var arrayCopy = Assert.IsType<object[]>(py.copy(array));
        Assert.NotSame(array, arrayCopy);
        Assert.Same(child, arrayCopy[0]);
        var state = new State { items = [child] };
        var stateCopy = Assert.IsType<State>(py.copy(state));
        Assert.NotSame(state, stateCopy);
        Assert.Same(state.items, stateCopy.items);
    }
}
