namespace SqlGlotSharp.dialects;

public static partial class dialect
{
    public partial class Dialect
    {
        /// <summary>Tokenizes SQL and parses it into one nullable AST per statement.</summary>
        /// <param name="sql">SQL passed unchanged to the tokenizer and parser.</param>
        /// <param name="error_level">Parser error policy; null uses the parser default (IMMEDIATE).</param>
        /// <remarks>
        /// Bounded port of Python's parse(**opts): only the error_level keyword is exposed.
        /// Other ParserArgs, including a dialect override, are not supported. Only the exact
        /// base Dialect, exact postgres.Postgres and exact timescale.Timescale types are supported; unported grammar fails explicitly.
        /// </remarks>
        /// <exception cref="NotPortedException">The dialect factory or required grammar is not ported.</exception>
        // py: sqlglot/dialects/dialect.py::Dialect.parse #0000000000
        public List<exp.Expr?> parse(string sql, errors.ErrorLevel? error_level = null)
            => parser(error_level).parse(tokenize(sql), sql);

        /// <summary>Parses SQL into the given expression type (or the first type of a list that succeeds).</summary>
        /// <remarks>Bounded like <see cref="parse"/>: only error_level is exposed.</remarks>
        // py: sqlglot/dialects/dialect.py::Dialect.parse_into #3eeff2752f
        public List<exp.Expr?> parse_into(object expression_type, string sql, errors.ErrorLevel? error_level = null)
            => parser(error_level).parse_into(expression_type, tokenize(sql), sql);

        /// <summary>Creates a fresh base, PostgreSQL or identity-only Timescale parser bound to this dialect instance.</summary>
        /// <param name="error_level">Parser error policy; null uses the parser default (IMMEDIATE).</param>
        /// <remarks>
        /// Bounded port of Python's parser(**opts): only error_level is exposed. All other
        /// ParserArgs use constructor defaults; no dialect override is exposed. Only exact base
        /// PostgreSQL and Timescale types are supported. Other subclasses never fall back to a base parser.
        /// </remarks>
        /// <exception cref="NotPortedException">This instance is not the exact base Dialect type.</exception>
        // py: sqlglot/dialects/dialect.py::Dialect.parser #0000000000
        public global::SqlGlotSharp.parser.Parser parser(errors.ErrorLevel? error_level = null) => CreateParser(error_level);

        /// <summary>
        /// Extension point: a dialect defined outside this assembly overrides this (and <see cref="CreateGenerator"/>,
        /// <see cref="CreateTokenizer"/>) to supply its own parser, the equivalent of Python's <c>Dialect.Parser</c> attribute.
        /// </summary>
        public virtual global::SqlGlotSharp.parser.Parser CreateParser(errors.ErrorLevel? error_level = null)
        {
            if (GetType() == typeof(timescale.Timescale))
                return new global::SqlGlotSharp.parsers.timescale.TimescaleParser(error_level: error_level, dialect: this);
            if (GetType() == typeof(postgres.Postgres))
                return new global::SqlGlotSharp.parsers.postgres.PostgresParser(error_level: error_level, dialect: this);
            if (GetType() == typeof(fabric.Fabric))
                return new global::SqlGlotSharp.parsers.fabric.FabricParser(error_level: error_level, dialect: this);
            if (GetType() == typeof(tsql.TSQL))
                return new global::SqlGlotSharp.parsers.tsql.TSQLParser(error_level: error_level, dialect: this);
            if (GetType() != typeof(Dialect))
                throw new NotPortedException($"sqlglot/dialects/dialect.py::Dialect.parser ({GetType().FullName})");
            return new global::SqlGlotSharp.parsers.@base.BaseParser(error_level: error_level, dialect: this);
        }
    }
}
