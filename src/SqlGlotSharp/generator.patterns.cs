namespace SqlGlotSharp;

public static partial class generator
{
    public partial class Generator
    {
        // Partial only at the quantifier expansion dependency (exp.and_/or_/paren).
        // py: sqlglot/generator.py::Generator._like_sql #0000000000
        public virtual string _like_sql(exp.Expr expression, exp.Escape? escape = null)
        {
            var rhs = expression.args.get("expression");
            var op = expression is exp.Like ? "LIKE" : "ILIKE";
            if (py.truthy(expression.args.get("negate"))) op = $"NOT {op}";
            if (rhs is exp.All or exp.Any && !SUPPORTS_LIKE_QUANTIFIERS)
                throw new NotPortedException("sqlglot/generator.py::Generator._like_sql (LIKE quantifier expansion)");
            return binary(expression, op);
        }

        // py: sqlglot/generator.py::Generator.like_sql #0000000000
        public virtual string like_sql(exp.Like expression) => _like_sql(expression);

        // py: sqlglot/generator.py::Generator.ilike_sql #0000000000
        public virtual string ilike_sql(exp.ILike expression) => _like_sql(expression);

        // py: sqlglot/generator.py::Generator.escape_sql #0000000000
        public virtual string escape_sql(exp.Escape expression)
        {
            var this_expression = expression.args.get("this");
            if (this_expression is exp.Like or exp.ILike &&
                ((exp.Expr)this_expression).args.get("expression") is exp.All or exp.Any &&
                !SUPPORTS_LIKE_QUANTIFIERS)
                return _like_sql((exp.Expr)this_expression, escape: expression);
            return binary(expression, "ESCAPE");
        }

        // Pattern quantifier children use the upstream wrappers (not function fallback).
        // py: sqlglot/generator.py::Generator.all_sql #0000000000
        public virtual string all_sql(exp.All expression)
        {
            var this_sql = sql(expression, "this");
            if (expression.args.get("this") is not (exp.Tuple or exp.Paren)) this_sql = wrap(this_sql);
            return $"ALL {this_sql}";
        }

        // py: sqlglot/generator.py::Generator.any_sql #0000000000
        public virtual string any_sql(exp.Any expression)
        {
            var this_sql = sql(expression, "this");
            if (expression.args.get("this") is exp.Select or exp.SetOperation or exp.Paren)
            {
                if (expression.args.get("this") is exp.Select or exp.SetOperation) this_sql = wrap(this_sql);
                return $"ANY{this_sql}";
            }
            return $"ANY {this_sql}";
        }

        // SIMILAR TO already has the faithful binary handler in generator.operators.cs.
    }
}
