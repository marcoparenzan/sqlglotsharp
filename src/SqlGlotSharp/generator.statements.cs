namespace SqlGlotSharp;

// GRANT/REVOKE, transactions, USE and MERGE rendering (generator.py).
public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator._grant_or_revoke_sql #0000000000
        internal string _grant_or_revoke_sql(exp.Expr expression, string keyword, string preposition,
            string grant_option_prefix = "", string grant_option_suffix = "")
        {
            var privileges_sql = expressions(expression, key: "privileges", flat: true);

            var kind = sql(expression, "kind");
            kind = kind.Length != 0 ? $" {kind}" : "";

            var securable = sql(expression, "securable");
            securable = securable.Length != 0 ? $" {securable}" : "";

            var principals = expressions(expression, key: "principals", flat: true);

            if (!py.truthy(expression.args.get("grant_option"))) grant_option_prefix = grant_option_suffix = "";

            // cascade for revoke only
            var cascade = sql(expression, "cascade");
            cascade = cascade.Length != 0 ? $" {cascade}" : "";

            return $"{keyword} {grant_option_prefix}{privileges_sql} ON{kind}{securable} {preposition} {principals}{grant_option_suffix}{cascade}";
        }

        // py: sqlglot/generator.py::Generator.grant_sql #0000000000
        public virtual string grant_sql(exp.Grant expression) =>
            _grant_or_revoke_sql(expression, "GRANT", "TO", grant_option_suffix: " WITH GRANT OPTION");

        // py: sqlglot/generator.py::Generator.revoke_sql #0000000000
        public virtual string revoke_sql(exp.Revoke expression) =>
            _grant_or_revoke_sql(expression, "REVOKE", "FROM", grant_option_prefix: "GRANT OPTION FOR ");

        // py: sqlglot/generator.py::Generator.grantprivilege_sql #0000000000
        public virtual string grantprivilege_sql(exp.GrantPrivilege expression)
        {
            var @this = sql(expression, "this");
            var columns = expressions(expression, flat: true);
            columns = columns.Length != 0 ? $"({columns})" : "";

            return $"{@this}{columns}";
        }

        // py: sqlglot/generator.py::Generator.grantprincipal_sql #0000000000
        public virtual string grantprincipal_sql(exp.GrantPrincipal expression)
        {
            var @this = sql(expression, "this");

            var kind = sql(expression, "kind");
            kind = kind.Length != 0 ? $"{kind} " : "";

            return $"{kind}{@this}";
        }

        // py: sqlglot/generator.py::Generator.transaction_sql #0000000000
        public virtual string transaction_sql(exp.Transaction expression)
        {
            var modes = expressions(expression, key: "modes");
            modes = modes.Length != 0 ? $" {modes}" : "";
            return $"BEGIN{modes}";
        }

        // py: sqlglot/generator.py::Generator.commit_sql #0000000000
        public virtual string commit_sql(exp.Commit expression)
        {
            var chain = expression.args.get("chain");
            var chain_sql = chain is null ? "" : py.truthy(chain) ? " AND CHAIN" : " AND NO CHAIN";
            return $"COMMIT{chain_sql}";
        }

        // py: sqlglot/generator.py::Generator.rollback_sql #0000000000
        public virtual string rollback_sql(exp.Rollback expression)
        {
            var savepoint = expression.args.get("savepoint");
            var savepoint_sql = py.truthy(savepoint) ? $" TO {py.str(savepoint is exp.Expr node ? sql(node) : savepoint)}" : "";
            return $"ROLLBACK{savepoint_sql}";
        }

        // py: sqlglot/generator.py::Generator.use_sql #0000000000
        public virtual string use_sql(exp.Use expression)
        {
            var kind = sql(expression, "kind");
            kind = kind.Length != 0 ? $" {kind}" : "";
            var @this = sql(expression, "this");
            if (@this.Length == 0) @this = expressions(expression, flat: true);
            @this = @this.Length != 0 ? $" {@this}" : "";
            return $"USE{kind}{@this}";
        }

        // py: sqlglot/generator.py::Generator.merge_sql #0000000000
        public virtual string merge_sql(exp.Merge expression)
        {
            var table = (exp.Expr)expression.@this!;
            var table_alias = "";

            var hints = table.args.get("hints") as List<object?>;
            if (hints is { Count: > 0 } && py.truthy(table.alias) && hints[0] is exp.WithTableHint)
                // T-SQL syntax is MERGE ... <target_table> [WITH (<merge_hint>)] [[AS] table_alias]
                table_alias = $" AS {sql(((exp.Expr)table.args["alias"]!).pop())}";

            var @this = sql(table);
            var @using = $"USING {sql(expression, "using")}";
            var whens = sql(expression, "whens");

            var on = sql(expression, "on");
            on = on.Length != 0 ? $"ON {on}" : "";

            if (on.Length == 0)
            {
                on = expressions(expression, key: "using_cond");
                on = on.Length != 0 ? $"USING ({on})" : "";
            }

            var returning = sql(expression, "returning");
            if (returning.Length != 0) whens = $"{whens}{returning}";

            var sep_ = sep();

            return prepend_ctes(expression, $"MERGE INTO {@this}{table_alias}{sep_}{@using}{sep_}{on}{sep_}{whens}");
        }

        // py: sqlglot/generator.py::Generator.whens_sql #0000000000
        public virtual string whens_sql(exp.Whens expression) => expressions(expression, sep: " ", indent: false);

        // py: sqlglot/generator.py::Generator.when_sql #0000000000
        public virtual string when_sql(exp.When expression)
        {
            var matched = py.truthy(expression.args["matched"]) ? "MATCHED" : "NOT MATCHED";
            var source = MATCHED_BY_SOURCE && py.truthy(expression.args.get("source")) ? " BY SOURCE" : "";
            var condition = sql(expression, "condition");
            condition = condition.Length != 0 ? $" AND {condition}" : "";

            var then_expression = expression.args.get("then");
            string then;
            if (then_expression is exp.Insert insert)
            {
                var @this = MERGE_ALL_BY_NAME && insert.@this is exp.Star ? "ALL BY NAME" : sql(insert, "this");
                @this = @this.Length != 0 ? $"INSERT {@this}" : "INSERT";
                then = sql(insert, "expression");
                then = then.Length != 0 ? $"{@this} VALUES {then}" : @this;
            }
            else if (then_expression is exp.Update update)
            {
                if (update.args.get("expressions") is exp.Star)
                    then = MERGE_ALL_BY_NAME ? "UPDATE ALL BY NAME" : $"UPDATE {sql(update, "expressions")}";
                else if (MERGE_ALL_BY_NAME && update.expressions.Count == 1 && update.expressions[0] is exp.Star)
                    then = "UPDATE ALL BY NAME";
                else
                {
                    var expressions_sql = expressions(update);
                    then = expressions_sql.Length != 0 ? $"UPDATE SET{sep()}{expressions_sql}" : "UPDATE";
                }
            }
            else then = sql(then_expression);

            if (then_expression is exp.Insert or exp.Update)
            {
                var where = sql(then_expression, "where");
                if (where.Length != 0 && !SUPPORTS_MERGE_WHERE)
                {
                    var kind = then_expression is exp.Insert ? "INSERT" : "UPDATE";
                    unsupported($"WHERE clause in MERGE {kind} is not supported");
                    where = "";
                }
                then = $"{then}{where}";
            }
            return $"WHEN {matched}{source}{condition} THEN {then}";
        }
    }
}
