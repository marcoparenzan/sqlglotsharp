using Token = SqlGlotSharp.tokenizer_core.Token;
using ErrorLevel = SqlGlotSharp.errors.ErrorLevel;
using ParseError = SqlGlotSharp.errors.ParseError;

namespace SqlGlotSharp;

// Expression/error infrastructure from parser.py, in upstream method order.
// Statement parsing and callable parser tables remain unported; these helpers do
// not provide a SQL parser. All AST validation/positions/comments use shared code.
public static partial class parser
{
    // py: sqlglot/parser.py::logger #12092c5f48
    public static py.logging.Logger logger = py.logging.getLogger("sqlglot");

    public partial class Parser
    {

        // py: sqlglot/parser.py::Parser._find_sql #8bb1a58b61
        public string _find_sql(Token start, Token end) => py.slice(sql, start.start, end.end + 1);

        // py: sqlglot/parser.py::Parser.raise_error #04dba4ce45
        public void raise_error(string message, Token? token = null)
        {
            token = py.truthy(token) ? token! : py.truthy(_curr) ? _curr : py.truthy(_prev) ? _prev : Token.@string("");
            var (formatted_sql, start_context, highlight, end_context) = SqlGlotSharp.errors.highlight_sql(
                sql, [(token.start, token.end)], error_message_context);
            var formatted_message = $"{message}. Line {token.line}, Col: {token.col}.\n  {formatted_sql}";
            var error = ParseError.@new(formatted_message, description: message, line: token.line, col: token.col,
                start_context: start_context, highlight: highlight, end_context: end_context);
            if (error_level == ErrorLevel.IMMEDIATE) throw error;
            errors.Add(error);
        }

        // py: sqlglot/parser.py::Parser.validate_expression #e7444ddc75
        public E validate_expression<E>(E expression, IReadOnlyList<object?>? args = null) where E : exp.Expr
        {
            if (max_nodes > -1)
            {
                _node_count++;
                if (_node_count > max_nodes) raise_error($"Maximum number of AST nodes ({max_nodes}) exceeded");
            }
            if (error_level != ErrorLevel.IGNORE)
                foreach (var error_message in expression.error_messages(args)) raise_error(error_message);
            return expression;
        }

        // py: sqlglot/parser.py::Parser._try_parse #f7803fa6b5
        public T? _try_parse<T>(Func<T> parse_method, bool retreat = false) where T : class
        {
            var index = _index;
            var error_level = this.error_level;
            T? @this = default;
            this.error_level = ErrorLevel.IMMEDIATE;
            try { @this = parse_method(); }
            catch (ParseError) { @this = default; }
            finally
            {
                if (!py.truthy(@this) || retreat) _retreat(index);
                this.error_level = error_level;
            }
            return @this;
        }

        // py: sqlglot/parser.py::Parser.check_errors #2c1bcf3a29
        public void check_errors()
        {
            if (error_level == ErrorLevel.WARN)
                foreach (var error in errors) logger.error(error.ToString());
            else if (error_level == ErrorLevel.RAISE && errors.Count != 0)
                throw new ParseError(SqlGlotSharp.errors.concat_messages(errors, max_errors),
                    errors: SqlGlotSharp.errors.merge_errors(errors));
        }

        // py: sqlglot/parser.py::Parser.expression #3ddcc8e538
        public E expression<E>(E instance, Token? token = null, List<string>? comments = null) where E : exp.Expr
        {
            if (py.truthy(token)) instance.update_positions(token);
            if (py.truthy(comments)) instance.add_comments(comments);
            else _add_comments(instance);
            if (!instance.is_primitive) instance = validate_expression(instance);
            return instance;
        }
    }
}
