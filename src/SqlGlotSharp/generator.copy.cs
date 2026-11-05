namespace SqlGlotSharp;

// COPY statement (generator.py).
public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.copyparameter_sql #0000000000
        public virtual string copyparameter_sql(exp.CopyParameter expression)
        {
            var option = sql(expression, "this");

            if (py.truthy(expression.expressions))
            {
                var upper = option.ToUpperInvariant();

                // Snowflake FILE_FORMAT options are separated by whitespace
                var sep = upper == "FILE_FORMAT" ? " " : ", ";

                // Databricks copy/format options do not set their list of values with EQ
                var op = upper is "COPY_OPTIONS" or "FORMAT_OPTIONS" ? " " : " = ";
                var values = expressions(expression, flat: true, sep: sep);
                return $"{option}{op}({values})";
            }

            var value = sql(expression, "expression");

            if (value.Length == 0) return option;

            var op_ = COPY_PARAMS_EQ_REQUIRED ? " = " : " ";

            return $"{option}{op_}{value}";
        }

        // py: sqlglot/generator.py::Generator.credentials_sql #0000000000
        public virtual string credentials_sql(exp.Credentials expression)
        {
            var cred_expr = expression.args.get("credentials");
            string credentials;
            if (cred_expr is exp.Literal)
            {
                // Redshift case: CREDENTIALS <string>
                credentials = sql(expression, "credentials");
                credentials = credentials.Length != 0 ? $"CREDENTIALS {credentials}" : "";
            }
            else
            {
                // Snowflake case: CREDENTIALS = (...)
                credentials = expressions(expression, key: "credentials", flat: true, sep: " ");
                credentials = cred_expr is not null ? $"CREDENTIALS = ({credentials})" : "";
            }

            var storage = sql(expression, "storage");
            storage = storage.Length != 0 ? $"STORAGE_INTEGRATION = {storage}" : "";

            var encryption = expressions(expression, key: "encryption", flat: true, sep: " ");
            encryption = encryption.Length != 0 ? $" ENCRYPTION = ({encryption})" : "";

            var iam_role = sql(expression, "iam_role");
            iam_role = iam_role.Length != 0 ? $"IAM_ROLE {iam_role}" : "";

            var region = sql(expression, "region");
            region = region.Length != 0 ? $" REGION {region}" : "";

            return $"{credentials}{storage}{encryption}{iam_role}{region}";
        }

        // py: sqlglot/generator.py::Generator.copy_sql #0000000000
        public virtual string copy_sql(exp.Copy expression)
        {
            var @this = sql(expression, "this");
            @this = COPY_HAS_INTO_KEYWORD ? $" INTO {@this}" : $" {@this}";

            var credentials = sql(expression, "credentials");
            credentials = credentials.Length != 0 ? seg(credentials) : "";
            var files = expressions(expression, key: "files", flat: true);
            var kind = files.Length != 0 ? seg(py.truthy(expression.args.get("kind")) ? "FROM" : "TO") : "";

            var sep = dialect.COPY_PARAMS_ARE_CSV ? ", " : " ";
            var @params = expressions(expression, key: "params", sep: sep, new_line: true,
                skip_last: true, skip_first: true, indent: COPY_PARAMS_ARE_WRAPPED);

            if (@params.Length != 0)
            {
                if (COPY_PARAMS_ARE_WRAPPED) @params = $" WITH ({@params})";
                else if (!py.truthy(pretty) && (files.Length != 0 || credentials.Length != 0)) @params = $" {@params}";
            }

            return $"COPY{@this}{kind} {files}{credentials}{@params}";
        }
    }
}
