using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp.parsers;

public static class @base
{
    // BaseParser's data is implemented; its inherited grammar remains bounded.
    // Zero anchors intentionally do not claim a complete parser dependency closure.
    public class BaseParser : global::SqlGlotSharp.parser.Parser
    {
        public BaseParser(errors.ErrorLevel? error_level = null, int error_message_context = 100,
            int max_errors = 3, int max_nodes = -1, object? dialect = null)
            : base(error_level, error_message_context, max_errors, max_nodes, dialect) { }

        // py: sqlglot/parsers/base.py::BaseParser.NO_PAREN_FUNCTIONS #0000000000
        public new static readonly PyDict<TokenType, Type> NO_PAREN_FUNCTIONS_ = CreateNoParenFunctions();
        public override PyDict<TokenType, Type> NO_PAREN_FUNCTIONS => NO_PAREN_FUNCTIONS_;

        private static PyDict<TokenType, Type> CreateNoParenFunctions()
        {
            var functions = new PyDict<TokenType, Type>();
            foreach (var (token, type) in global::SqlGlotSharp.parser.Parser.NO_PAREN_FUNCTIONS_)
                functions[token] = type;
            functions[TokenType.LOCALTIME] = typeof(exp.Localtime);
            functions[TokenType.LOCALTIMESTAMP] = typeof(exp.Localtimestamp);
            functions[TokenType.CURRENT_CATALOG] = typeof(exp.CurrentCatalog);
            functions[TokenType.SESSION_USER] = typeof(exp.SessionUser);
            return functions;
        }

        // py: sqlglot/parsers/base.py::BaseParser.ID_VAR_TOKENS #0000000000
        public new static readonly HashSet<TokenType> ID_VAR_TOKENS_ =
            [.. global::SqlGlotSharp.parser.Parser.ID_VAR_TOKENS_.Where(t => t != TokenType.STRAIGHT_JOIN)];
        public override HashSet<TokenType> ID_VAR_TOKENS => ID_VAR_TOKENS_;

        // py: sqlglot/parsers/base.py::BaseParser.TABLE_ALIAS_TOKENS #0000000000
        public new static readonly HashSet<TokenType> TABLE_ALIAS_TOKENS_ =
            [.. global::SqlGlotSharp.parser.Parser.TABLE_ALIAS_TOKENS_.Where(t => t != TokenType.STRAIGHT_JOIN)];
        public override HashSet<TokenType> TABLE_ALIAS_TOKENS => TABLE_ALIAS_TOKENS_;
    }
}
