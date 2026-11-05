using Token = SqlGlotSharp.tokenizer_core.Token;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

public static partial class jsonpath
{
    // py: sqlglot/jsonpath.py::parse #75f58f1bd7
    public static exp.JSONPath parse(string path, object? dialect = null)
    {
        var dialect_inst = dialects.dialect.Dialect.get_or_raise(dialect);
        var tokenizer = dialect_inst.jsonpath_tokenizer();
        var tokens = tokenizer.tokenize(path);
        var size = tokens.Count;
        var i = 0;
        TokenType? Curr() => i < size ? tokens[i].token_type : null;
        Token Prev() => tokens[i - 1];
        Token Advance() { i++; return Prev(); }
        string Error(string message) => $"{message} at index {i}: {path}";
        Token? Match(TokenType type, bool raise_unmatched = false)
        {
            if (Curr() == type) return Advance();
            if (raise_unmatched) throw new errors.ParseError(Error($"Expected TokenType.{type}"));
            return null;
        }
        Token? MatchSet(IReadOnlySet<TokenType> types) => Curr() is { } type && types.Contains(type) ? Advance() : null;

        object? ParseLiteral()
        {
            var token = Match(TokenType.STRING) ?? Match(TokenType.IDENTIFIER);
            if (token is not null) return token.text;
            if (Match(TokenType.STAR) is not null) return new exp.JSONPathWildcard();
            if (Match(TokenType.PLACEHOLDER) is not null || Match(TokenType.L_PAREN) is not null)
            {
                var script = Prev().text == "(";
                var start = i;
                while (true)
                {
                    if (Match(TokenType.L_BRACKET) is not null) ParseBracket();
                    if (Curr() is TokenType.R_BRACKET or null) break;
                    Advance();
                }
                var end = i < size ? tokens[i].end : tokens[^1].end;
                var text = py.slice(path, tokens[start].start, end);
                return script ? new exp.JSONPathScript(@this: text) : new exp.JSONPathFilter(@this: text);
            }
            var number = Match(TokenType.DASH) is not null ? "-" : "";
            token = Match(TokenType.NUMBER);
            if (token is not null) number += token.text;
            if (number.Length == 0) return false;
            try { return py.@int(number); }
            catch (OverflowException) { return System.Numerics.BigInteger.Parse(number, System.Globalization.CultureInfo.InvariantCulture); }
        }
        object? ParseSlice()
        {
            var start = ParseLiteral();
            var end = Match(TokenType.COLON) is not null ? ParseLiteral() : null;
            var step = Match(TokenType.COLON) is not null ? ParseLiteral() : null;
            return end is null && step is null ? start : new exp.JSONPathSlice(start: start, end: end, step: step);
        }
        exp.Expr ParseBracket()
        {
            var literal = ParseSlice();
            exp.Expr node;
            if (literal is string || literal is not false)
            {
                var indexes = new List<object?> { literal };
                while (Match(TokenType.COMMA) is not null)
                {
                    literal = ParseSlice();
                    if (literal is string || literal is not false) indexes.Add(literal);
                }
                if (indexes.Count == 1)
                    node = indexes[0] switch
                    {
                        string text => new exp.JSONPathKey(@this: text),
                        exp.JSONPathScript or exp.JSONPathFilter => new exp.JSONPathSelector(@this: indexes[0]),
                        _ => new exp.JSONPathSubscript(@this: indexes[0])
                    };
                else node = new exp.JSONPathUnion(expressions: indexes);
            }
            else throw new errors.ParseError(Error("Cannot have empty segment"));
            Match(TokenType.R_BRACKET, raise_unmatched: true);
            return node;
        }
        string ParseVarText()
        {
            var prev_index = i - 2;
            while (MatchSet(tokenizer.VAR_TOKENS) is not null) { }
            var start = prev_index < 0 ? 0 : tokens[prev_index].end + 1;
            return i >= size ? py.slice(path, start) : py.slice(path, start, tokens[i].start);
        }

        Match(TokenType.DOLLAR);
        var expressions = new List<object?> { new exp.JSONPathRoot() };
        while (Curr() is not null)
        {
            if (Match(TokenType.DOT) is not null || Match(TokenType.COLON) is not null)
            {
                var recursive = Prev().text == "..";
                object? value = MatchSet(tokenizer.VAR_TOKENS) is not null ? ParseVarText() :
                    Match(TokenType.IDENTIFIER) is not null ? Prev().text :
                    Match(TokenType.STAR) is not null ? new exp.JSONPathWildcard() : null;
                if (recursive) expressions.Add(new exp.JSONPathRecursive(@this: value));
                else if (py.truthy(value)) expressions.Add(new exp.JSONPathKey(@this: value));
                else if (!dialect_inst.JSON_PATH_SINGLE_DOT_IS_WILDCARD) throw new errors.ParseError(Error("Expected key name or * after DOT"));
            }
            else if (Match(TokenType.L_BRACKET) is not null) expressions.Add(ParseBracket());
            else if (MatchSet(tokenizer.VAR_TOKENS) is not null) expressions.Add(new exp.JSONPathKey(@this: ParseVarText()));
            else if (Match(TokenType.IDENTIFIER) is not null) expressions.Add(new exp.JSONPathKey(@this: Prev().text));
            else if (Match(TokenType.STAR) is not null) expressions.Add(new exp.JSONPathWildcard());
            else throw new errors.ParseError(Error($"Unexpected TokenType.{tokens[i].token_type}"));
        }
        return new exp.JSONPath(expressions: expressions);
    }
}
