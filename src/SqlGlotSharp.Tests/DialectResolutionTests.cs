using Dialect = SqlGlotSharp.dialects.dialect.Dialect;

namespace SqlGlotSharp.Tests;

public class DialectResolutionTests
{
    public class Plugin : Dialect { }

    [Fact]
    public void Resolver_returns_scoped_types_and_preserves_instance_identity()
    {
        var instance = Assert.IsType<dialects.tsql.TSQL>(Dialect.get_or_raise("tsql"));
        Assert.Same(instance, Dialect.get_or_raise(instance));
        Assert.IsType<dialects.postgres.Postgres>(Dialect.get_or_raise(typeof(dialects.postgres.Postgres)));
        Assert.IsType<Dialect>(Dialect.get_or_raise(null));
        Assert.IsType<dialects.fabric.Fabric>(Dialect.get_or_raise("fabric"));
        Assert.Throws<NotPortedException>(() => Dialect.get_or_raise("mysql"));
    }

    [Fact]
    public void Settings_parse_version_normalization_and_validate_unknown_names()
    {
        var instance = Dialect.get_or_raise("postgres, version=15.2, normalization_strategy=case_sensitive");
        Assert.Equal((15L, 2L, 0L), instance.version);
        Assert.Equal(dialects.dialect.NormalizationStrategy.CASE_SENSITIVE, instance.normalization_strategy);
        Assert.Empty(instance.settings);
        Assert.Throws<ArgumentException>(() => Dialect.get_or_raise("tsql, unknown_setting=yes"));
        Assert.Throws<ArgumentException>(() => Dialect.get_or_raise(", unknown_setting=yes"));
        Assert.Throws<ArgumentException>(() => Dialect.get_or_raise("unknowndialect"));
    }

    [Fact]
    public void Tokenization_uses_instance_and_generated_subclass_configuration()
    {
        var dialect = Dialect.get_or_raise("postgres, version=15");
        var tokenizer = dialect.tokenizer();
        Assert.Same(dialect, tokenizer.dialect);
        Assert.Equal("text", dialect.tokenize("$tag$text$tag$")[0].text);
        Assert.Equal(tokenizer_core.TokenType.DOLLAR, dialect.jsonpath_tokenizer().tokenize("$")[0].token_type);
    }

    [Fact]
    public void Plugin_registration_validates_type_and_reuses_inherited_settings_constructor()
    {
        const string name = "test_plugin";
        Dialect.register(name, typeof(Plugin));
        try
        {
            var plugin = Assert.IsType<Plugin>(Dialect.get_or_raise(name + ", version=2.1"));
            Assert.Equal((2L, 1L, 0L), plugin.version);
            Assert.True(plugin.Equals(typeof(Plugin)));
            Assert.True(plugin.Equals(name));
            Assert.Throws<ArgumentException>(() => Dialect.register("bad", typeof(string)));
        }
        finally { dialects.dialect.DIALECT_REGISTRY__helper.Remove(name); }
    }
}
