using SqlGlotSharp.dialects;

namespace SqlGlotSharp.Tests;

public class DialectDataTests
{
    [Fact]
    public void Generated_dialect_hierarchy_dispatches_runtime_settings()
    {
        dialect.Dialect postgres = new postgres.Postgres();
        Assert.Equal(1, postgres.INDEX_OFFSET);
        Assert.Equal("nulls_are_large", postgres.NULL_ORDERING);
        Assert.Equal("%Y", postgres.TIME_MAPPING["YYYY"]);
        dialect.Dialect sqlServer = new tsql.TSQL();
        Assert.Equal(dialect.NormalizationStrategy.CASE_INSENSITIVE, sqlServer.NORMALIZATION_STRATEGY);
        dialect.Dialect warehouse = new fabric.Fabric();
        Assert.IsAssignableFrom<tsql.TSQL>(warehouse);
        Assert.Equal(dialect.NormalizationStrategy.CASE_SENSITIVE, warehouse.NORMALIZATION_STRATEGY);
    }

    [Fact]
    public void Scoped_registry_and_parser_generator_links_match_python_classes()
    {
        Assert.Equal(4, dialect.DIALECT_REGISTRY__helper.Count);
        Assert.Equal(typeof(postgres.Postgres), dialect.DIALECT_REGISTRY__helper["postgres"]);
        Assert.Equal(typeof(SqlGlotSharp.parsers.tsql.TSQLParser), new tsql.TSQL().Parser);
        Assert.Equal(typeof(SqlGlotSharp.generators.fabric.FabricGenerator), new fabric.Fabric().Generator);
    }

    [Fact]
    public void Nested_enum_alias_preserves_original_enum_identity()
    {
        exp.DType value = exp.DataType.Type.INT;
        Assert.Equal(exp.DType.INT, value);
        Assert.Equal(Enum.GetNames<exp.DType>().Length, typeof(exp.DataType.Type).GetFields().Length);
    }
}
