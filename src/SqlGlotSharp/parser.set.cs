using Token = SqlGlotSharp.tokenizer_core.Token;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

// SET / UNSET statements (parser.py).
public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser.SET_PARSERS #0000000000
        public static readonly PyDict<string, Func<Parser, exp.Expr?>> SET_PARSERS_ = new()
        {
            ["GLOBAL"] = self => self._parse_set_item_assignment("GLOBAL"),
            ["LOCAL"] = self => self._parse_set_item_assignment("LOCAL"),
            ["SESSION"] = self => self._parse_set_item_assignment("SESSION"),
            ["TRANSACTION"] = self => self._parse_set_transaction(),
        };
        public virtual PyDict<string, Func<Parser, exp.Expr?>> SET_PARSERS => SET_PARSERS_;

        // py: sqlglot/parser.py::Parser._find_parser #0000000000
        public Func<Parser, exp.Expr?>? _find_parser(PyDict<string, Func<Parser, exp.Expr?>> parsers, PyDict<object, object?> trie)
        {
            if (!py.truthy(_curr)) return null;

            var index = _index;
            List<string> @this = [];
            while (true)
            {
                // The current token might be multiple words
                var curr = py.upper(_curr.text);
                var key = curr.Split(' ').Cast<object>().ToList();
                @this.Add(curr);

                _advance();
                var (result, subtrie) = global::SqlGlotSharp.trie.in_trie(trie, key);
                trie = subtrie;
                if (result == global::SqlGlotSharp.trie.TrieResult.FAILED) break;

                if (result == global::SqlGlotSharp.trie.TrieResult.EXISTS) return parsers[string.Join(" ", @this)];
            }

            _retreat(index);
            return null;
        }

        // py: sqlglot/parser.py::Parser._parse_set_item_assignment #0000000000
        public exp.Expr? _parse_set_item_assignment(string? kind = null)
        {
            var index = _index;

            if ((kind == "GLOBAL" || kind == "SESSION") && _match_text_seq(["TRANSACTION"]))
                return _parse_set_transaction(global_: kind == "GLOBAL");

            var left = _parse_primary() ?? _parse_column();
            var assignment_delimiter = _match_texts(SET_ASSIGNMENT_DELIMITERS);

            if (left is null || (SET_REQUIRES_ASSIGNMENT_DELIMITER && !assignment_delimiter))
            {
                _retreat(index);
                return null;
            }

            var right = _parse_statement() ?? _parse_id_var();
            if (right is exp.Column or exp.Identifier) right = new exp.Var(@this: right.name);

            var @this = expression(new exp.EQ(@this: left, expression: right));
            return expression(new exp.SetItem(@this: @this, kind: kind));
        }

        // py: sqlglot/parser.py::Parser._parse_set_transaction #0000000000
        public exp.Expr _parse_set_transaction(bool global_ = false)
        {
            _match_text_seq(["TRANSACTION"]);
            var characteristics = _parse_csv<exp.Var>(() => _parse_var_from_options(TRANSACTION_CHARACTERISTICS));
            return expression(new exp.SetItem(expressions: characteristics.Cast<object?>().ToList(), kind: "TRANSACTION", global_: global_));
        }

        // py: sqlglot/parser.py::Parser._parse_set_item #0000000000
        public exp.Expr? _parse_set_item()
        {
            var parser = _find_parser(SET_PARSERS, SET_TRIE);
            return parser is not null ? parser(this) : _parse_set_item_assignment(kind: null);
        }

        // py: sqlglot/parser.py::Parser._parse_set #0000000000
        public exp.Expr _parse_set(bool unset = false, bool tag = false)
        {
            var index = _index;
            var set_ = expression(new exp.Set(expressions: _parse_csv<exp.Expr>(_parse_set_item).Cast<object?>().ToList(), unset: unset, tag: tag));

            if (py.truthy(_curr))
            {
                _retreat(index);
                return _parse_as_command(_prev);
            }

            return set_;
        }
    }
}
