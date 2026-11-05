using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

public static partial class parser
{
    public partial class Parser
    {
        // py: sqlglot/parser.py::Parser._parse_function_parameter #0000000000
        public virtual exp.Expr? _parse_function_parameter() =>
            _parse_column_def(_parse_id_var(), computed_column: false);

        // Scalar typed columns share this closure with schemas and function parameters.
        // py: sqlglot/parser.py::Parser._parse_column_def #0000000000
        public virtual exp.Expr? _parse_column_def(exp.Expr? @this, bool computed_column = true)
        {
            // column defs are not really columns, they are identifiers
            if (@this is exp.Column) @this = (exp.Expr?)@this.@this;

            if (!computed_column) _match(TokenType.ALIAS);

            var kind = _parse_types(schema: true);

            if (_match_text_seq(["FOR", "ORDINALITY"]))
                return expression(new exp.ColumnDef(new PyDict<string, object?> { ["this"] = @this, ["ordinality"] = true }));

            List<object?> constraints = [];

            if ((kind is null && _match(TokenType.ALIAS)) || _match_texts(["ALIAS", "MATERIALIZED"]))
            {
                // Match storage before _parse_types so STORED is not treated as a data type
                // (needed for typeless columns, e.g. SQLite `b AS (a * 2) STORED`).
                var persisted = py.upper(_prev.text) == "MATERIALIZED";
                var expression_ = _parse_disjunction();
                if (!persisted)
                {
                    if (_match_text_seq(["PERSISTED"])) persisted = true;
                    else if (_match_texts(["STORED", "VIRTUAL"])) persisted = py.upper(_prev.text) == "STORED";
                }
                var constraint_kind = new exp.ComputedColumnConstraint(
                    @this: expression_,
                    persisted: persisted,
                    data_type: _match_text_seq(["AUTO"]) ? new exp.Var(@this: "AUTO") : _parse_types(),
                    not_null: _match_pair(TokenType.NOT, TokenType.NULL));
                constraints.Add(expression(new exp.ColumnConstraint(kind: constraint_kind)));
            }
            else if (kind is null && _match_set([TokenType.IN, TokenType.OUT], advance: false))
            {
                var in_out_constraint = expression(new exp.InOutColumnConstraint(
                    input_: _match(TokenType.IN), output: _match(TokenType.OUT)));
                constraints.Add(in_out_constraint);
                kind = _parse_types();
            }
            else if (kind is not null && _match(TokenType.ALIAS, advance: false)
                && (!WRAPPED_TRANSFORM_COLUMN_CONSTRAINT || _next.token_type == TokenType.L_PAREN))
            {
                _advance();
                constraints.Add(expression(new exp.ColumnConstraint(kind: new exp.ComputedColumnConstraint(
                    @this: _parse_disjunction(),
                    persisted: _match_texts(["STORED", "VIRTUAL"]) && py.upper(_prev.text) == "STORED"))));
            }

            while (true)
            {
                var constraint = _parse_column_constraint();
                if (constraint is null) break;
                constraints.Add(constraint);
            }

            if (kind is null && constraints.Count == 0) return @this;

            exp.Expr? position = null;
            if (_match_texts(["FIRST", "AFTER"]))
            {
                var pos = _prev.text;
                position = expression(new exp.ColumnPosition(@this: _parse_column(), position: pos));
            }

            return expression(new exp.ColumnDef(new PyDict<string, object?>
            {
                ["this"] = @this, ["kind"] = kind, ["constraints"] = constraints, ["position"] = position
            }));
        }

        // py: sqlglot/parser.py::Parser._parse_column_constraint
        public exp.Expr? _parse_column_constraint()
        {
            _constraint_modifier_boundary();
            var @this = _match(TokenType.CONSTRAINT)
                && !_match_texts(RESERVED_CONSTRAINT_KINDS.Cast<string>(), advance: false) ? _parse_id_var() : null;
            var procedureOptionFollows = _match(TokenType.WITH, advance: false)
                && py.truthy(_next) && PROCEDURE_OPTIONS.ContainsKey(_next.text.ToUpperInvariant());
            var index = _index;
            if (!procedureOptionFollows && _match_texts(ConstraintCallbacks))
            {
                var kind = _parse_constraint_callback(_prev.text.ToUpperInvariant());
                if (kind is null) { _retreat(index); return null; }
                return expression(new exp.ColumnConstraint(new PyDict<string, object?> { ["this"] = @this, ["kind"] = kind }));
            }
            if (_match(TokenType.CHARACTER_SET) || _match_pair(TokenType.CHAR, TokenType.SET))
                return expression(new exp.ColumnConstraint(new PyDict<string, object?>
                {
                    ["this"] = @this,
                    ["kind"] = expression(new exp.CharacterSetColumnConstraint(@this: _parse_var_or_string()))
                }));
            return @this;
        }
    }
}
