using Token = SqlGlotSharp.tokenizer_core.Token;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

// GRANT/REVOKE, COMMENT, transactions, USE and MERGE (parser.py).
public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser._parse_var_from_options (options whose continuations are str or str sequences)
        public exp.Var? _parse_var_from_options(PyDict<string, List<object?>> options, bool raise_unmatched = true) =>
            _parse_var_from_options_core(key => options.TryGetValue(key, out var v)
                ? v.Select(item => item is string s ? new[] { s } : item is System.Runtime.CompilerServices.ITuple tuple
                    ? Enumerable.Range(0, tuple.Length).Select(i => (string)tuple[i]!).ToArray()
                    : ((System.Collections.IEnumerable)item!).Cast<string>().ToArray()).ToList()
                : null, raise_unmatched);

        // py: sqlglot/parser.py::Parser._parse_grant_revoke_common #0000000000
        public (List<exp.Expr>? privileges, string? kind, exp.Expr? securable) _parse_grant_revoke_common()
        {
            var privileges = _parse_csv<exp.Expr>(() => _parse_grant_privilege());

            _match(TokenType.ON);
            var kind = _match_set(CREATABLES) ? py.upper(_prev.text) : null;

            // Attempt to parse the securable e.g. MySQL allows names
            // such as "foo.*", "*.*" which are not easily parseable yet
            var securable = _try_parse(() => _parse_table_parts()!);

            return (privileges, kind, securable);
        }

        // py: sqlglot/parser.py::Parser._parse_grant_privilege #0000000000
        public exp.Expr? _parse_grant_privilege()
        {
            List<string> privilege_parts = [];

            // Keep consuming consecutive keywords until comma (end of this privilege) or ON
            // (end of privilege list) or L_PAREN (start of column list) are met
            while (py.truthy(_curr) && !_match_set(PRIVILEGE_FOLLOW_TOKENS, advance: false))
            {
                privilege_parts.Add(py.upper(_curr.text));
                _advance();
            }

            if (privilege_parts.Count == 0)
            {
                raise_error("Expected privilege");
                return null;
            }

            var @this = new exp.Var(@this: string.Join(" ", privilege_parts));
            var expressions = _match(TokenType.L_PAREN, advance: false)
                ? _parse_wrapped_csv(() => _parse_column()).Cast<object?>().ToList() : null;

            return expression(Mk<exp.GrantPrivilege>(("this", @this), ("expressions", expressions)));
        }

        // py: sqlglot/parser.py::Parser._parse_grant_principal #0000000000
        public exp.Expr? _parse_grant_principal()
        {
            object? kind = _match_texts(["ROLE", "GROUP"]) ? py.upper(_prev.text) : false;
            var principal = _parse_id_var();

            if (principal is null) return null;

            return expression(Mk<exp.GrantPrincipal>(("this", principal), ("kind", kind)));
        }

        // py: sqlglot/parser.py::Parser._parse_grant #0000000000
        public exp.Expr _parse_grant()
        {
            var start = _prev;

            var (privileges, kind, securable) = _parse_grant_revoke_common();

            if (securable is null || !_match_text_seq(["TO"])) return _parse_as_command(start);

            var principals = _parse_csv<exp.Expr>(() => _parse_grant_principal());

            var grant_option = _match_text_seq(["WITH", "GRANT", "OPTION"]);

            if (py.truthy(_curr)) return _parse_as_command(start);

            return expression(Mk<exp.Grant>(("privileges", privileges?.Cast<object?>().ToList()), ("kind", kind),
                ("securable", securable), ("principals", principals.Cast<object?>().ToList()), ("grant_option", grant_option)));
        }

        // py: sqlglot/parser.py::Parser._parse_revoke #0000000000
        public exp.Expr _parse_revoke()
        {
            var start = _prev;

            var grant_option = _match_text_seq(["GRANT", "OPTION", "FOR"]);

            var (privileges, kind, securable) = _parse_grant_revoke_common();

            if (securable is null || !_match_text_seq(["FROM"])) return _parse_as_command(start);

            var principals = _parse_csv<exp.Expr>(() => _parse_grant_principal());

            string? cascade = null;
            if (_match_texts(["CASCADE", "RESTRICT"])) cascade = py.upper(_prev.text);

            if (py.truthy(_curr)) return _parse_as_command(start);

            return expression(Mk<exp.Revoke>(("privileges", privileges?.Cast<object?>().ToList()), ("kind", kind),
                ("securable", securable), ("principals", principals.Cast<object?>().ToList()),
                ("grant_option", grant_option), ("cascade", cascade)));
        }

        // py: sqlglot/parser.py::Parser._parse_comment #0000000000
        public exp.Expr _parse_comment(bool allow_exists = true)
        {
            var start = _prev;
            object? exists = allow_exists ? _parse_exists() : null;

            _match(TokenType.ON);

            var materialized = _match_text_seq(["MATERIALIZED"]);
            var kind = _match_set(CREATABLES) ? _prev : null;
            if (kind is null) return _parse_as_command(start);

            exp.Expr? @this;
            if (kind.token_type is TokenType.FUNCTION or TokenType.PROCEDURE)
                @this = _parse_user_defined_function(kind.token_type);
            else if (kind.token_type == TokenType.TABLE)
                @this = _parse_table(alias_tokens: COMMENT_TABLE_ALIAS_TOKENS);
            else if (kind.token_type == TokenType.COLUMN)
                @this = _parse_column();
            else
                @this = _parse_table_parts(schema: true);

            _match(TokenType.IS);

            return expression(Mk<exp.Comment>(("this", @this), ("kind", kind.text), ("expression", _parse_string()),
                ("exists", exists), ("materialized", materialized)));
        }

        // py: sqlglot/parser.py::Parser._parse_transaction #0000000000
        public virtual exp.Expr _parse_transaction()
        {
            string? @this = null;
            if (_match_texts(TRANSACTION_KIND)) @this = _prev.text;

            _match_texts(["TRANSACTION", "WORK"]);

            List<object?> modes = [];
            while (true)
            {
                List<string> mode = [];
                while (_match(TokenType.VAR) || _match(TokenType.NOT)) mode.Add(_prev.text);

                if (mode.Count > 0) modes.Add(string.Join(" ", mode));
                if (!_match(TokenType.COMMA)) break;
            }

            return expression(Mk<exp.Transaction>(("this", @this), ("modes", modes)));
        }

        // py: sqlglot/parser.py::Parser._parse_commit_or_rollback #0000000000
        public virtual exp.Expr _parse_commit_or_rollback()
        {
            bool? chain = null;
            exp.Expr? savepoint = null;
            var is_rollback = _prev.token_type == TokenType.ROLLBACK;

            _match_texts(["TRANSACTION", "WORK"]);

            if (_match_text_seq(["TO"]))
            {
                _match_text_seq(["SAVEPOINT"]);
                savepoint = _parse_id_var();
            }

            if (_match(TokenType.AND))
            {
                chain = !_match_text_seq(["NO"]);
                _match_text_seq(["CHAIN"]);
            }

            if (is_rollback) return expression(Mk<exp.Rollback>(("savepoint", savepoint)));

            return expression(Mk<exp.Commit>(("chain", chain)));
        }

        // py: sqlglot/parser.py::Parser._parse_use #0000000000
        public exp.Expr _parse_use() =>
            expression(Mk<exp.Use>(("kind", _parse_var_from_options(USABLES, raise_unmatched: false)),
                ("this", _parse_table(schema: false))));

        // py: sqlglot/parser.py::Parser._parse_merge #0000000000
        public exp.Expr _parse_merge()
        {
            _match(TokenType.INTO);
            var target = _parse_table();

            if (target is not null && _match(TokenType.ALIAS, advance: false))
                target.set("alias", _parse_table_alias());

            _match(TokenType.USING);
            var @using = _parse_table();

            return expression(Mk<exp.Merge>(("this", target), ("using", @using),
                ("on", _match(TokenType.ON) ? _parse_disjunction() : false),
                ("using_cond", _match(TokenType.USING) ? _parse_using_identifiers().Cast<object?>().ToList() : false),
                ("whens", _parse_when_matched()), ("returning", _parse_returning())));
        }

        // py: sqlglot/parser.py::Parser._parse_when_matched #0000000000
        public exp.Expr _parse_when_matched()
        {
            List<object?> whens = [];

            while (_match(TokenType.WHEN))
            {
                var matched = !_match(TokenType.NOT);
                _match_text_seq(["MATCHED"]);
                var source = _match_text_seq(["BY", "TARGET"]) ? false : _match_text_seq(["BY", "SOURCE"]);
                var condition = _match(TokenType.AND) ? _parse_disjunction() : null;

                _match(TokenType.THEN);

                exp.Expr? then;
                if (_match(TokenType.INSERT))
                {
                    var @this = _match_text_seq(["ALL", "BY", "NAME"]) ? new exp.Star() : _parse_star();
                    if (py.truthy(@this)) then = expression(Mk<exp.Insert>(("this", @this)));
                    else
                        then = expression(Mk<exp.Insert>(
                            ("this", _match_text_seq(["ROW"]) ? new exp.Var(@this: "ROW") : _parse_value(values: false)),
                            ("expression", _match_text_seq(["VALUES"]) ? _parse_value() : false),
                            ("where", _parse_where())));
                }
                else if (_match(TokenType.UPDATE))
                {
                    var expressions = _parse_star();
                    if (py.truthy(expressions)) then = expression(Mk<exp.Update>(("expressions", expressions)));
                    else if (_match_text_seq(["ALL", "BY", "NAME"]))
                        then = expression(Mk<exp.Update>(("expressions", new List<object?> { new exp.Star() })));
                    else
                        then = expression(Mk<exp.Update>(
                            ("expressions", _match(TokenType.SET)
                                ? _parse_csv<exp.Expr>(() => _parse_update_assignment()).Cast<object?>().ToList() : false),
                            ("where", _parse_where())));
                }
                else if (_match(TokenType.DELETE)) then = expression(Mk<exp.Var>(("this", _prev.text)));
                else then = _parse_var_from_options(CONFLICT_ACTIONS);

                whens.Add(expression(Mk<exp.When>(("matched", matched), ("source", source),
                    ("condition", condition), ("then", then))));
            }
            return expression(Mk<exp.Whens>(("expressions", whens)));
        }
    }
}
