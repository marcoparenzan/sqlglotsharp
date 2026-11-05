namespace SqlGlotSharp.dialects;

public static partial class dialect
{
    public partial class Dialect
    {
        /// <summary>Bounded native generator factory. Only exact base, PostgreSQL and Timescale types are routed.</summary>
        public global::SqlGlotSharp.generator.Generator generator(bool pretty = false)
        {
            var result = CreateGenerator(pretty);
            result.dialect = this;
            result.pretty = pretty;
            return result;
        }

        /// <summary>Extension point: the equivalent of Python's <c>Dialect.Generator</c> attribute.</summary>
        public virtual global::SqlGlotSharp.generator.Generator CreateGenerator(bool pretty = false)
        {
            global::SqlGlotSharp.generator.Generator result;
            if (GetType() == typeof(timescale.Timescale))
                result = new global::SqlGlotSharp.generators.timescale.TimescaleGenerator(dialect: this, pretty: pretty);
            else if (GetType() == typeof(postgres.Postgres))
                result = new global::SqlGlotSharp.generators.postgres.PostgresGenerator(dialect: this, pretty: pretty);
            else if (GetType() == typeof(fabric.Fabric))
                result = new global::SqlGlotSharp.generators.fabric.FabricGenerator(dialect: this, pretty: pretty);
            else if (GetType() == typeof(tsql.TSQL))
                result = new global::SqlGlotSharp.generators.tsql.TSQLGenerator(dialect: this, pretty: pretty);
            else if (GetType() == typeof(Dialect))
                result = new global::SqlGlotSharp.generator.Generator(dialect: this, pretty: pretty);
            else
                throw new NotPortedException($"sqlglot/dialects/dialect.py::Dialect.generator ({GetType().FullName})");
            return result;
        }

        /// <summary>Extension point: the equivalent of Python's <c>Dialect.Tokenizer</c> attribute.</summary>
        public virtual global::SqlGlotSharp.tokens.Tokenizer CreateTokenizer() =>
            global::SqlGlotSharp.tokens.Tokenizer.BuiltInTokenizer(this);
    }
}
