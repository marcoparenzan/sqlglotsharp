using Dialect = SqlGlotSharp.dialects.dialect.Dialect;

namespace SqlGlotSharp.Tests;

public class DialectIdentifierTests
{
    [Fact]
    public void Normalization_preserves_quoted_case_sensitive_names()
    {
        var postgres = Dialect.get_or_raise("postgres");
        var name = new exp.Identifier(@this: "FoO");
        Assert.Same(name, postgres.normalize_identifier(name));
        Assert.Equal("foo", name.name);
        var quoted = new exp.Identifier(@this: "FoO", quoted: true);
        postgres.normalize_identifier(quoted);
        Assert.Equal("FoO", quoted.name);
        var sensitive = Dialect.get_or_raise("postgres, normalization_strategy=case_sensitive");
        sensitive.normalize_identifier(name.set_kwargs(new PyKwargs { ["this"] = "FoO" }));
        Assert.Equal("FoO", name.name);
    }

    [Fact]
    public void Quoting_policy_distinguishes_safe_unsafe_and_explicit_null()
    {
        var dialect = Dialect.get_or_raise("postgres");
        var safe = new exp.Identifier(@this: "name");
        Assert.True(dialect.can_quote(safe));
        Assert.False(dialect.can_quote(safe, "unsafe"));
        Assert.False(dialect.can_quote(safe, null));
        var unsafeName = new exp.Identifier(@this: "MixedCase");
        dialect.quote_identifier(unsafeName, identify: false);
        Assert.True(unsafeName.quoted);
        Assert.True(dialect.can_quote(new exp.Identifier(@this: "aé")));
        Assert.Throws<ArgumentException>(() => dialect.can_quote(safe, "unexpected"));
    }

    [Fact]
    public void Function_arguments_are_not_quoted_by_can_quote()
    {
        var name = new exp.Identifier(@this: "name");
        _ = new exp.Anonymous(@this: name);
        Assert.False(Dialect.get_or_raise("").can_quote(name, true));
        name.set("quoted", true);
        Assert.True(Dialect.get_or_raise("").can_quote(name, false));
    }
}
