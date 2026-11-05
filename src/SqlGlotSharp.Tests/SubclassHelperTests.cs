namespace SqlGlotSharp.Tests;

public class SubclassHelperTests
{
    [Theory]
    [InlineData(typeof(exp.Expr), 1067)]
    [InlineData(typeof(exp.Func), 571)]
    [InlineData(typeof(exp.Binary), 65)]
    public void Expression_module_counts_match_python(Type baseType, int expected)
    {
        var classes = helper.subclasses("sqlglot.expressions", baseType);
        Assert.Equal(expected, classes.Count);
        Assert.All(classes, type => Assert.True(baseType.IsAssignableFrom(type) || baseType == typeof(exp.Expr) && type.IsInterface));
        Assert.Equal(classes.OrderBy(type => type.Name, StringComparer.Ordinal), classes);
    }

    [Fact]
    public void Exclusions_and_multiple_base_types_work()
    {
        var excluded = new HashSet<Type> { typeof(exp.Func), typeof(exp.AggFunc), typeof(exp.Anonymous) };
        var classes = helper.subclasses("sqlglot.expressions", typeof(exp.Func), excluded);
        Assert.All(excluded, type => Assert.DoesNotContain(type, classes));
        var combined = helper.subclasses("sqlglot.expressions", new[] { typeof(exp.Binary), typeof(exp.Func) });
        Assert.All(combined, type => Assert.True(typeof(exp.Binary).IsAssignableFrom(type) || typeof(exp.Func).IsAssignableFrom(type)));
        Assert.Throws<NotPortedException>(() => helper.subclasses("unknown", typeof(exp.Expr)));
    }
}
