using System.Reflection;

namespace SqlGlotSharp.Tests;

public class ExpressionShellTests
{
    [Fact]
    [Trait("Level", "Tooling")]
    public void Generated_hierarchy_preserves_traits()
    {
        Assert.True(typeof(exp.Expr).IsAbstract);
        Assert.True(typeof(exp.Query).IsInterface);
        Assert.IsAssignableFrom<exp.Expr>(new exp.Select());
        Assert.IsAssignableFrom<exp.Query>(new exp.Select());
        Assert.IsAssignableFrom<exp.Func>(new exp.Sum());
        Assert.IsAssignableFrom<exp.AggFunc>(new exp.Sum());
    }

    [Fact]
    [Trait("Level", "Tooling")]
    public void All_generated_expression_declarations_are_present()
    {
        var declarations = typeof(exp).GetNestedTypes(BindingFlags.Public);
        Assert.Equal(17, declarations.Count(type => type.IsInterface));
        // TimeBucket is a deliberate Timescale extension, not an upstream-generated declaration.
        Assert.Equal(1051, declarations.Count(type => typeof(exp.Expr).IsAssignableFrom(type) && type != typeof(exp.TimeBucket)));
        Assert.Contains(typeof(exp.TimeBucket), declarations);
    }
}
