using SqlGlotSharp.dialects;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;
using Parser = SqlGlotSharp.parser.Parser;

namespace SqlGlotSharp.Extensions.Layer3;

/// <summary>
/// Layer 3 expression dialect: the base dialect plus the multi-word keyword <c>MEMBER OF</c>,
/// parsed as <see cref="exp.JSONArrayContains"/>. An extension, not part of upstream SQLGlot: it is built only
/// on the public extension points of SqlGlotSharp (<c>CreateTokenizer/CreateParser/CreateGenerator</c>,
/// <c>Dialect.register</c>). Generation is the base generator's, so <c>x MEMBER OF y</c> prints as
/// <c>JSON_ARRAY_CONTAINS(x, y)</c>.
/// </summary>
public class Layer3 : dialect.Dialect
{
    public const string Name = "layer3";

    /// <summary>Registers the dialect under <see cref="Name"/>, so <c>read: "layer3"</c> works. Safe to call more than once.</summary>
    public static void Register() => dialect.Dialect.register(Name, typeof(Layer3));

    public override Parser CreateParser(errors.ErrorLevel? error_level = null) =>
        new Layer3Parser(error_level: error_level, dialect: this);

    public override generator.Generator CreateGenerator(bool pretty = false) =>
        new Layer3Generator(dialect: this, pretty: pretty);

    public override tokens.Tokenizer CreateTokenizer() => new Layer3Tokenizer(this);
}

/// <summary>Base tokenizer plus the keyword <c>MEMBER OF</c>.</summary>
public class Layer3Tokenizer : tokens.Tokenizer
{
    public Layer3Tokenizer(object? dialect = null) : base(dialect) { }

    // py equivalent: KEYWORDS = {**Tokenizer.KEYWORDS, "MEMBER OF": TokenType.MEMBER_OF}
    public static readonly PyDict<string, TokenType> KEYWORDS_ = BuildKeywords();
    public override PyDict<string, TokenType> KEYWORDS => KEYWORDS_;

    // The multi-word keyword needs its prefix in the keyword trie.
    public static readonly PyDict<object, object?> _KEYWORD_TRIE_ = BuildTrie();
    public override PyDict<object, object?> _KEYWORD_TRIE => _KEYWORD_TRIE_;

    private static PyDict<string, TokenType> BuildKeywords()
    {
        var keywords = new PyDict<string, TokenType>();
        foreach (var (key, value) in global::SqlGlotSharp.tokens.Tokenizer.KEYWORDS_) keywords[key] = value;
        keywords["MEMBER OF"] = TokenType.MEMBER_OF;
        return keywords;
    }

    private static PyDict<object, object?> BuildTrie() =>
        trie.new_trie(["MEMBER OF"], trie.copy(global::SqlGlotSharp.tokens.Tokenizer._KEYWORD_TRIE_));
}

/// <summary>Base parser plus the <c>MEMBER OF</c> range parser.</summary>
public class Layer3Parser : parsers.@base.BaseParser
{
    public Layer3Parser(errors.ErrorLevel? error_level = null, int error_message_context = 100,
        int max_errors = 3, int max_nodes = -1, object? dialect = null)
        : base(error_level, error_message_context, max_errors, max_nodes, dialect ?? new Layer3()) { }

    public override PyDict<TokenType, Func<Parser, exp.Expr?, exp.Expr?>> RANGE_PARSERS { get; } = Ranges();

    private static PyDict<TokenType, Func<Parser, exp.Expr?, exp.Expr?>> Ranges()
    {
        var table = new PyDict<TokenType, Func<Parser, exp.Expr?, exp.Expr?>>();
        foreach (var (key, value) in Parser.RANGE_PARSERS_) table[key] = value;
        table[TokenType.MEMBER_OF] = (self, @this) =>
            self.expression(new exp.JSONArrayContains(@this: @this, expression: self._parse_column()));
        return table;
    }
}

/// <summary>The base generator; kept as its own type so the dialect owns its output contract.</summary>
public class Layer3Generator : generator.Generator
{
    public Layer3Generator(object? dialect = null, bool pretty = false) : base(dialect: dialect, pretty: pretty) { }
}
