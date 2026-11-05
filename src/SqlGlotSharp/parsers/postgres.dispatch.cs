using TokenType = SqlGlotSharp.tokenizer_core.TokenType;
using Parser = SqlGlotSharp.parser.Parser;

namespace SqlGlotSharp.parsers;

public static partial class postgres
{
    // Callable overlays are intentionally bounded. The generated class supplies
    // the upstream data tables (notably HASH/Xor, CARET/Pow and null ordering).
    // Inherited callable entries retain the base parser's explicit boundaries.
    public partial class PostgresParser
    {
        public PostgresParser(errors.ErrorLevel? error_level = null, int error_message_context = 100,
            int max_errors = 3, int max_nodes = -1, object? dialect = null)
            : base(error_level, error_message_context, max_errors, max_nodes,
                dialect ?? new dialects.postgres.Postgres()) { }

        // py: sqlglot/parsers/postgres.py::PostgresParser.PROPERTY_PARSERS #0000000000
        public override PyDict<string, parser.PropertyParser> PROPERTY_PARSERS { get; } = PropertyParsers();
        private static PyDict<string, parser.PropertyParser> PropertyParsers()
        {
            var table = new PyDict<string, parser.PropertyParser>();
            foreach (var (key, value) in Parser.PROPERTY_PARSERS_) if (key != "INPUT") table[key] = value;
            table["SET"] = new((self, _) => self.expression(new exp.SetConfigProperty(@this: self._parse_set())));
            table["TABLESPACE"] = new((self, _) => self.expression(new exp.TableSpaceProperty(@this: self._parse_id_var())));
            return table;
        }

        // py: sqlglot/parsers/postgres.py::PostgresParser._parse_parameter_mode #0000000000
        /// <summary>
        /// Parse PostgreSQL function parameter mode (IN, OUT, INOUT, VARIADIC). Disambiguates between
        /// mode keywords and identifiers with the same name: "out INT" (identifier) vs "OUT x INT" (mode).
        /// </summary>
        public TokenType? _parse_parameter_mode()
        {
            if (!_match_set(ARG_MODE_TOKENS, advance: false) || !py.truthy(_next)) return null;

            var mode_token = _curr;

            // Pattern 1: MODE TYPE. If the next token parses as a built-in type, the keyword is an identifier.
            var is_followed_by_builtin_type = _try_parse(() =>
            {
                _advance();
                return _parse_types(check_func: false, allow_identifiers: false);
            }, retreat: true);
            if (py.truthy(is_followed_by_builtin_type)) return null;

            // Pattern 2: MODE NAME TYPE. The type can be built-in or user-defined.
            if (!ID_VAR_TOKENS.Contains(_next.token_type)) return null;

            var is_followed_by_any_type = _try_parse(() =>
            {
                _advance(2);
                return _parse_types(check_func: false, allow_identifiers: true);
            }, retreat: true);

            return py.truthy(is_followed_by_any_type) ? mode_token.token_type : null;
        }

        // py: sqlglot/parsers/postgres.py::PostgresParser._create_mode_constraint #0000000000
        public exp.Expr _create_mode_constraint(TokenType param_mode) =>
            expression(Mk<exp.InOutColumnConstraint>(
                ("input_", param_mode is TokenType.IN or TokenType.INOUT),
                ("output", param_mode is TokenType.OUT or TokenType.INOUT),
                ("variadic", param_mode == TokenType.VARIADIC)));

        // py: sqlglot/parsers/postgres.py::PostgresParser._parse_function_parameter #0000000000
        public override exp.Expr? _parse_function_parameter()
        {
            var param_mode = _parse_parameter_mode();

            if (param_mode is not null) _advance();

            // Parse parameter name and type
            var param_name = _parse_id_var();
            var column_def = _parse_column_def(param_name, computed_column: false);

            // Attach mode as constraint
            if (param_mode is not null && column_def is not null)
            {
                var constraint = _create_mode_constraint(param_mode.Value);
                if (!py.truthy(column_def.args.get("constraints"))) column_def.set("constraints", new List<object?>());
                ((List<object?>)column_def.args["constraints"]!).Insert(0, constraint);
            }

            return column_def;
        }

        // py: sqlglot/parsers/postgres.py::PostgresParser._parse_user_defined_type #020d001c1d
        public override exp.Expr? _parse_user_defined_type(exp.Identifier identifier)
        {
            exp.Expr udt_type = identifier;

            while (_match(TokenType.DOT))
            {
                var part = _parse_id_var();
                if (py.truthy(part))
                    udt_type = new exp.Dot(@this: udt_type, expression: part);
            }

            return exp.DataType.build(udt_type, udt: true);
        }

        private static NotPortedException Missing(string member) =>
            new($"sqlglot/parsers/postgres.py::PostgresParser.{member}");

        // py: sqlglot/parsers/postgres.py::PostgresParser.PLACEHOLDER_PARSERS #0000000000
        public override PyDict<TokenType, Func<Parser, exp.Expr?>> PLACEHOLDER_PARSERS { get; } = Placeholders();
        private static PyDict<TokenType, Func<Parser, exp.Expr?>> Placeholders()
        {
            var table = new PyDict<TokenType, Func<Parser, exp.Expr?>>();
            foreach (var (key, value) in Parser.PLACEHOLDER_PARSERS_) table[key] = value;
            table[TokenType.PLACEHOLDER] = self => self.expression(new exp.Placeholder(jdbc: true));
            table[TokenType.MOD] = self => ((PostgresParser)self)._parse_query_parameter();
            return table;
        }

        // Do not use base/registry constructors for PostgreSQL custom builders.
        // py: sqlglot/parsers/postgres.py::PostgresParser.FUNCTIONS #0000000000
        public override PyDict<string, Func<IReadOnlyList<object?>, exp.Expr>> FUNCTIONS { get; } = Functions();
        private static PyDict<string, Func<IReadOnlyList<object?>, exp.Expr>> Functions()
        {
            var table = new PyDict<string, Func<IReadOnlyList<object?>, exp.Expr>>();
            foreach (var (key, value) in Parser.FUNCTIONS_) table[key] = value;
            foreach (var (name, builder) in PostgresBuilders()) table[name] = builder;
            table["NOW"] = new exp.CurrentTimestamp().from_arg_list;
            table["DATE_TRUNC"] = dialects.dialect.build_timestamp_trunc;
            return table;
        }

        // py: sqlglot/parsers/postgres.py::PostgresParser.FUNCTION_PARSERS #0000000000
        public override PyDict<string, Func<Parser, exp.Expr?>> FUNCTION_PARSERS { get; } = FunctionParsers();
        private static PyDict<string, Func<Parser, exp.Expr?>> FunctionParsers()
        {
            var table = new PyDict<string, Func<Parser, exp.Expr?>>();
            foreach (var (key, value) in Parser.FUNCTION_PARSERS_) table[key] = value;
            table["DATE_PART"] = self => ((PostgresParser)self)._parse_date_part();
            table["JSON_AGG"] = self => self.expression(new exp.JSONArrayAgg(@this: self._parse_lambda(), order: self._parse_order()));
            table["JSONB_EXISTS"] = self => ((PostgresParser)self)._parse_jsonb_exists();
            table["JSON_VALUE"] = self => self._parse_json_value(json_path: false);
            return table;
        }

        // py: sqlglot/parsers/postgres.py::PostgresParser.NO_PAREN_FUNCTION_PARSERS #0000000000
        public override PyDict<string, Func<Parser, exp.Expr?>> NO_PAREN_FUNCTION_PARSERS { get; } = NoParen();
        private static PyDict<string, Func<Parser, exp.Expr?>> NoParen()
        {
            var table = new PyDict<string, Func<Parser, exp.Expr?>>();
            foreach (var (key, value) in Parser.NO_PAREN_FUNCTION_PARSERS_) table[key] = value;
            table["VARIADIC"] = self => self.expression(new exp.Variadic(@this: self._parse_bitwise()));
            return table;
        }

        // py: sqlglot/parsers/postgres.py::PostgresParser.UNARY_PARSERS #0000000000
        public override PyDict<TokenType, Func<Parser, exp.Expr?>> UNARY_PARSERS { get; } = Unary();
        private static PyDict<TokenType, Func<Parser, exp.Expr?>> Unary()
        {
            var table = new PyDict<TokenType, Func<Parser, exp.Expr?>>();
            foreach (var (key, value) in Parser.UNARY_PARSERS_) table[key] = value;
            table[TokenType.RLIKE] = self => self.expression(new exp.BitwiseNot(@this: self._parse_unary()));
            return table;
        }

        // py: sqlglot/parsers/postgres.py::PostgresParser.RANGE_PARSERS #0000000000
        public override PyDict<TokenType, Func<Parser, exp.Expr?, exp.Expr?>> RANGE_PARSERS { get; } = Ranges();
        private static PyDict<TokenType, Func<Parser, exp.Expr?, exp.Expr?>> Ranges()
        {
            var table = new PyDict<TokenType, Func<Parser, exp.Expr?, exp.Expr?>>();
            foreach (var (key, value) in Parser.RANGE_PARSERS_) table[key] = value;
            table[TokenType.DAMP] = parser.binary_range_parser(typeof(exp.ArrayOverlaps));
            table[TokenType.DAT] = (self, @this) => self.expression(new exp.MatchAgainst(@this: self._parse_bitwise(),
                expressions: new List<object?> { @this }));
            return table;
        }

        // py: sqlglot/parsers/postgres.py::PostgresParser.COLUMN_OPERATORS #0000000000
        public override PyDict<TokenType, Func<Parser, exp.Expr?, exp.Expr?, exp.Expr?>?> COLUMN_OPERATORS { get; } = Columns();
        private static PyDict<TokenType, Func<Parser, exp.Expr?, exp.Expr?, exp.Expr?>?> Columns()
        {
            var table = new PyDict<TokenType, Func<Parser, exp.Expr?, exp.Expr?, exp.Expr?>?>();
            foreach (var (key, value) in Parser.COLUMN_OPERATORS_)
                if (key is not (TokenType.ARROW or TokenType.DARROW or TokenType.HASH_ARROW or TokenType.DHASH_ARROW or TokenType.PLACEHOLDER))
                    table[key] = value;
            return table;
        }

        // py: sqlglot/parsers/postgres.py::PostgresParser.JSON_OPERATORS #0000000000
        public override PyDict<TokenType, Func<Parser, exp.Expr?, exp.Expr?, exp.Expr?>> JSON_OPERATORS { get; } = JsonOperators();
        private static PyDict<TokenType, Func<Parser, exp.Expr?, exp.Expr?, exp.Expr?>> JsonOperators()
        {
            var table = new PyDict<TokenType, Func<Parser, exp.Expr?, exp.Expr?, exp.Expr?>>();
            table[TokenType.ARROW] = (self, node, path) => self._build_postgres_json_extract(node, path, scalar: false);
            table[TokenType.DARROW] = (self, node, path) => self._build_postgres_json_extract(node, path, scalar: true);
            table[TokenType.HASH_ARROW] = parser.build_jsonb_extract;
            table[TokenType.DHASH_ARROW] = parser.build_jsonb_extract_scalar;
            table[TokenType.PLACEHOLDER] = (self, node, key) => self.expression(new exp.JSONBContainsTopKey(@this: node, expression: key));
            return table;
        }

        // py: sqlglot/parsers/postgres.py::PostgresParser.STATEMENT_PARSERS #0000000000
        public override PyDict<TokenType, Func<Parser, exp.Expr>> STATEMENT_PARSERS { get; } = Statements();
        private static PyDict<TokenType, Func<Parser, exp.Expr>> Statements()
        {
            var table = new PyDict<TokenType, Func<Parser, exp.Expr>>();
            foreach (var (key, value) in Parser.STATEMENT_PARSERS_) table[key] = value;
            table[TokenType.END] = self => self._parse_commit_or_rollback();
            return table;
        }
    }
}
