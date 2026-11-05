using System.Numerics;
using System.Text.RegularExpressions;
using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

namespace SqlGlotSharp;

// Small literal, locking, array and conditional handlers of sqlglot/generator.py.
public static partial class generator
{
    public partial class Generator
    {
        private static readonly Regex ESCAPED_UNICODE_RE = new(@"\\(\d+)", RegexOptions.Compiled);

        // py: sqlglot/generator.py::Generator.token_sql #0000000000
        public virtual string token_sql(TokenType token_type) =>
            TOKEN_MAPPING.TryGetValue(token_type.ToString(), out var mapped) && mapped is string text ? text : token_type.ToString();

        // py: sqlglot/generator.py::Generator.autoincrementcolumnconstraint_sql #0000000000
        public virtual string autoincrementcolumnconstraint_sql(exp.AutoIncrementColumnConstraint _) =>
            token_sql(TokenType.AUTO_INCREMENT);

        // py: sqlglot/generator.py::Generator.compresscolumnconstraint_sql #0000000000
        public virtual string compresscolumnconstraint_sql(exp.CompressColumnConstraint expression)
        {
            var @this = expression.@this is System.Collections.IList
                ? wrap(expressions(expression, key: "this", flat: true))
                : sql(expression, "this");

            return $"COMPRESS {@this}";
        }

        // py: sqlglot/generator.py::Generator.hexstring_sql #0000000000
        public virtual string hexstring_sql(exp.HexString expression, string? binary_function_repr = null)
        {
            var @this = sql(expression, "this");
            var is_integer_type = expression.args.get("is_integer");

            if ((py.truthy(is_integer_type) && !dialect.HEX_STRING_IS_INTEGER_TYPE)
                || (!py.truthy(dialect.HEX_START) && binary_function_repr is null))
            {
                // Integer representation will be returned if:
                // - The read dialect treats the hex value as integer literal but not the write
                // - The transpilation is not supported (write dialect has not set HEX_START or the param flag)
                return BigInteger.Parse("0" + @this, System.Globalization.NumberStyles.AllowHexSpecifier).ToString();
            }

            if (!py.truthy(is_integer_type))
            {
                // Read dialect treats the hex value as BINARY/BLOB
                if (binary_function_repr is not null)
                    // The write dialect supports the transpilation to its equivalent BINARY/BLOB
                    return func(binary_function_repr, exp.Literal.@string(@this));
                if (dialect.HEX_STRING_IS_INTEGER_TYPE)
                    // The write dialect does not support the transpilation, it will treat the hex value as INTEGER
                    unsupported("Unsupported transpilation from BINARY/BLOB hex string");
            }

            return $"{dialect.HEX_START}{@this}{dialect.HEX_END}";
        }

        // py: sqlglot/generator.py::Generator.unicodestring_sql #0000000000
        public virtual string unicodestring_sql(exp.UnicodeString expression)
        {
            var @this = sql(expression, "this");
            var escape = expression.args.get("escape") as exp.Expr;
            var unicode_start = dialect.UNICODE_START;

            string escape_substitute, left_quote, right_quote;
            if (py.truthy(unicode_start))
            {
                escape_substitute = @"\$1";
                (left_quote, right_quote) = (unicode_start!, dialect.UNICODE_END ?? "");
            }
            else
            {
                escape_substitute = @"\u$1";
                (left_quote, right_quote) = (dialect.QUOTE_START, dialect.QUOTE_END);
            }

            Regex escape_pattern;
            string escape_sql;
            if (escape is not null)
            {
                escape_pattern = new Regex($@"{Regex.Escape(escape.name)}(\d+)");
                escape_sql = SUPPORTS_UESCAPE ? $" UESCAPE {sql(escape)}" : "";
            }
            else
            {
                escape_pattern = ESCAPED_UNICODE_RE;
                escape_sql = "";
            }

            if (!py.truthy(unicode_start) || (escape is not null && !SUPPORTS_UESCAPE))
            {
                var substitute = UNICODE_SUBSTITUTE as string ?? escape_substitute;
                @this = escape_pattern.Replace(@this, substitute);
            }

            if (py.truthy(unicode_start))
                // A Unicode literal only escapes its delimiter by doubling it; the escape character
                // introduces a code point, so the dialect's ordinary string escapes do not apply here
                @this = _replace_line_breaks(@this).Replace(right_quote, right_quote + right_quote);
            else @this = escape_str(@this, escape_backslash: false);

            return $"{left_quote}{@this}{right_quote}{escape_sql}";
        }

        // py: sqlglot/generator.py::Generator.rand_sql #0000000000
        public virtual string rand_sql(exp.Rand expression)
        {
            var lower = sql(expression, "lower");
            var upper = sql(expression, "upper");

            if (lower.Length != 0 && upper.Length != 0)
                return $"({upper} - {lower}) * {func("RAND", expression.@this)} + {lower}";
            return func("RAND", expression.@this);
        }

        // py: sqlglot/generator.py::Generator.slice_sql #0000000000
        public virtual string slice_sql(exp.Slice expression)
        {
            var step = sql(expression, "step");
            // An omitted end is written as `-` before a step, since `::` is the cast operator
            var end = sql(expression.expression);
            if (end.Length == 0) end = step.Length != 0 ? "-" : "";
            var begin = sql(expression.@this);

            var text = step.Length != 0 ? $"{end}:{step}" : end;
            return text.Length != 0 ? $"{begin}:{text}" : $"{begin}:";
        }

        // py: sqlglot/generator.py::Generator.lock_sql #0000000000
        public virtual string lock_sql(exp.Lock expression)
        {
            if (!LOCKING_READS_SUPPORTED)
            {
                unsupported("Locking reads using 'FOR UPDATE/SHARE' are not supported");
                return "";
            }

            var update = expression.args["update"];
            var key = expression.args.get("key");
            string lock_type;
            if (py.truthy(update)) lock_type = py.truthy(key) ? "FOR NO KEY UPDATE" : "FOR UPDATE";
            else lock_type = py.truthy(key) ? "FOR KEY SHARE" : "FOR SHARE";
            var exprs = expressions(expression, flat: true);
            exprs = exprs.Length != 0 ? $" OF {exprs}" : "";
            var wait = expression.args.get("wait");

            string wait_sql = "";
            if (wait is not null)
            {
                if (wait is exp.Literal) wait_sql = $" WAIT {sql(wait)}";
                else wait_sql = py.truthy(wait) ? " NOWAIT" : " SKIP LOCKED";
            }

            return $"{lock_type}{exprs}{wait_sql}";
        }

        // py: sqlglot/generator.py::Generator.nvl2_sql #0000000000
        public virtual string nvl2_sql(exp.Nvl2 expression)
        {
            if (NVL2_SUPPORTED) return function_fallback_sql(expression);

            var condition = new exp.Not(@this: new exp.Is(@this: expression.@this, expression: new exp.Null()));
            var @case = new exp.Case(ifs: new List<object?> { new exp.If(@this: condition, @true: expression.args["true"]) });
            var else_cond = expression.args.get("false");
            if (py.truthy(else_cond)) @case.set("default", else_cond);

            return sql(@case);
        }

        // py: sqlglot/generator.py::Generator.arraysize_sql #0000000000
        public virtual string arraysize_sql(exp.ArraySize expression)
        {
            var dim = expression.args.get("expression") as exp.Expr;

            // For dialects that do not support the dimension arg, we can safely transpile its default value (1st dimension)
            if (dim is not null && ARRAY_SIZE_DIM_REQUIRED is null)
            {
                if (!(dim.is_int && dim.name == "1")) unsupported("Cannot transpile dimension argument for ARRAY_LENGTH");
                dim = null;
            }

            // If dimension is required but not specified, default initialize it
            if (ARRAY_SIZE_DIM_REQUIRED == true && dim is null) dim = exp.Literal.number(1);

            return func(ARRAY_SIZE_NAME, expression.@this, dim);
        }

        // py: sqlglot/generator.py::Generator.arrayany_sql #0000000000
        public virtual string arrayany_sql(exp.ArrayAny expression)
        {
            if (CAN_IMPLEMENT_ARRAY_ANY)
            {
                var filtered = new exp.ArrayFilter(@this: expression.@this, expression: expression.expression);
                var filtered_not_empty = new exp.NEQ(@this: new exp.ArraySize(@this: filtered), expression: exp.Literal.number(0));
                var original_is_empty = new exp.EQ(@this: new exp.ArraySize(@this: expression.@this), expression: exp.Literal.number(0));
                return sql(new exp.Paren(@this: new exp.Or(@this: original_is_empty, expression: filtered_not_empty)));
            }

            // SQLGlot's executor supports ARRAY_ANY, so we do not want to warn for the SQLGlot dialect
            if (dialect.GetType() != typeof(dialects.dialect.Dialect)) unsupported("ARRAY_ANY is unsupported");

            return function_fallback_sql(expression);
        }

        // py: sqlglot/generator.py::Generator.uuid_sql #0000000000
        public virtual string uuid_sql(exp.Uuid expression)
        {
            var is_string = expression.args.get("is_string");
            var uuid_func_sql = func("UUID");

            if (py.truthy(is_string) && !dialect.UUID_IS_STRING_TYPE)
                return sql(new exp.Cast(@this: uuid_func_sql, to: exp.DataType.build(exp.DType.VARCHAR)));

            return uuid_func_sql;
        }

        // py: sqlglot/generator.py::Generator.median_sql #0000000000
        public virtual string median_sql(exp.Median expression)
        {
            if (!SUPPORTS_MEDIAN)
                return sql(new exp.PercentileCont(@this: expression.@this, expression: exp.Literal.number(0.5)));

            return function_fallback_sql(expression);
        }

        // py: sqlglot/generator.py::Generator.opclass_sql #0000000000
        public virtual string opclass_sql(exp.Opclass expression) =>
            $"{sql(expression, "this")} {sql(expression, "expression")}";

        // py: sqlglot/generator.py::Generator.national_sql #0000000000
        public virtual string national_sql(exp.National expression, string prefix = "N")
        {
            var @string = sql(exp.Literal.@string(expression.name));
            return $"{prefix}{@string}";
        }

        // py: sqlglot/generator.py::Generator.refresh_sql #0000000000
        public virtual string refresh_sql(exp.Refresh expression) =>
            unsupported_args("partition_start", "partition_end", "force", "mode")((self, e) =>
            {
                var @this = self.sql(e, "this");
                var kind = e.@this is exp.Literal ? "" : $"{e.text("kind")} ";
                return $"REFRESH {kind}{@this}";
            })(this, expression);

        // py: sqlglot/generator.py::Generator.into_sql #0000000000
        public virtual string into_sql(exp.Into expression) =>
            unsupported_args("expressions")((self, e) =>
            {
                var temporary = py.truthy(e.args.get("temporary")) ? " TEMPORARY" : "";
                var unlogged = py.truthy(e.args.get("unlogged")) ? " UNLOGGED" : "";
                return $"{self.seg("INTO")}{(temporary.Length != 0 ? temporary : unlogged)} {self.sql(e, "this")}";
            })(this, expression);

        // py: sqlglot/generator.py::Generator.strtotime_sql #0000000000
        public virtual string strtotime_sql(exp.StrToTime expression) =>
            func("STR_TO_TIME", expression.@this, expression.args.get("format"));

        // py: sqlglot/generator.py::Generator.collate_sql #0000000000
        public virtual string collate_sql(exp.Collate expression)
        {
            if (COLLATE_IS_FUNC) return function_fallback_sql(expression);
            return binary(expression, "COLLATE");
        }

        // py: sqlglot/expressions/core.py::alias_ (expression, non-table form)
        internal static exp.Expr alias_(exp.Expr expression, object? alias)
        {
            var node = expression.copy();
            var identifier = exp.to_identifier(alias);

            // The "alias" arg is not set for Window expressions: that would add an IDENTIFIER node
            // representing a named window. An ALIAS node wraps the complete Window instead.
            if (node.arg_types.ContainsKey("alias") && node.GetType().Name != "Window")
            {
                node.set("alias", identifier);
                return node;
            }

            return new exp.Alias(@this: node, alias: identifier);
        }

        // py: sqlglot/generator.py::Generator.struct_sql #0000000000
        public virtual string struct_sql(exp.Struct expression)
        {
            expression.set("expressions", ((System.Collections.IEnumerable)(expression.args.get("expressions") ?? new List<object?>())).Cast<exp.Expr>().Select(e => e is exp.PropertyEQ
                ? alias_((exp.Expr)e.args["expression"]!, ((exp.Expr)e.@this!).is_string ? e.name : e.@this)
                : e).Cast<object?>().ToList());

            return function_fallback_sql(expression);
        }

        // py: sqlglot/expressions/builders.py::cast (the generator's own dialect decides type equivalence)
        internal exp.Expr exp_cast(exp.Expr expr, exp.DType to) => exp_cast(expr, exp.DataType.build(to));

        internal exp.Expr exp_cast(exp.Expr expr, exp.DataType data_type)
        {

            // dont re-cast if the expression is already a cast to the correct type
            if (expr is exp.Cast existing)
            {
                var existing_type = (existing.args.get("to") as exp.DataType)?.@this;
                // `this` is only a plain type enum for simple types; complex ones such as
                // INTERVAL nest another expression there, so the equivalence check is skipped.
                var equivalent = existing_type is exp.DType e && data_type.@this is exp.DType n
                    && (TYPE_MAPPING.TryGetValue(e, out var em) ? em : e.ToString()) == (TYPE_MAPPING.TryGetValue(n, out var nm) ? nm : n.ToString());
                if (expr.is_type(data_type) || equivalent) return expr;
            }

            var result = new exp.Cast(@this: expr, to: data_type);
            result.type = data_type;
            return result;
        }

        // py: sqlglot/generator.py::Generator.decodecase_sql #0000000000
        public virtual string decodecase_sql(exp.DecodeCase expression)
        {
            var all = ((System.Collections.IEnumerable)expression.args["expressions"]!).Cast<exp.Expr>().ToList();
            if (SUPPORTS_DECODE_CASE) return func("DECODE", all.Cast<object?>().ToArray());

            var decode_expr = all[0];
            var expressions_ = all.Skip(1).ToList();

            List<object?> ifs = [];
            for (var i = 0; i + 1 < expressions_.Count; i += 2)
            {
                var search = expressions_[i];
                var result = expressions_[i + 1];
                if (search is exp.Literal)
                    ifs.Add(new exp.If(@this: new exp.EQ(@this: decode_expr, expression: search), @true: result));
                else if (search is exp.Null)
                    ifs.Add(new exp.If(@this: new exp.Is(@this: decode_expr, expression: new exp.Null()), @true: result));
                else
                {
                    if (search is exp.Binary) search = new exp.Paren(@this: search);

                    var cond = new exp.Or(
                        @this: new exp.EQ(@this: decode_expr, expression: search),
                        expression: new exp.And(@this: new exp.Is(@this: decode_expr, expression: new exp.Null()),
                            expression: new exp.Is(@this: search, expression: new exp.Null())));
                    ifs.Add(new exp.If(@this: cond, @true: result));
                }
            }

            var @case = new exp.Case(ifs: ifs, @default: expressions_.Count % 2 == 1 ? expressions_[^1] : null);
            return sql(@case);
        }

        // py: sqlglot/generator.py::Generator.xmlelement_sql #0000000000
        public virtual string xmlelement_sql(exp.XMLElement expression)
        {
            var prefix = py.truthy(expression.args.get("evalname")) ? "EVALNAME" : "NAME";
            var name = $"{prefix} {sql(expression, "this")}";
            var args = new List<object?> { name };
            if (expression.args.get("expressions") is System.Collections.IEnumerable items) args.AddRange(items.Cast<object?>());
            return func("XMLELEMENT", args.ToArray());
        }

        // py: sqlglot/generator.py::Generator.xmltable_sql #0000000000
        public virtual string xmltable_sql(exp.XMLTable expression)
        {
            var @this = sql(expression, "this");
            var namespaces = expressions(expression, key: "namespaces");
            namespaces = namespaces.Length != 0 ? $"XMLNAMESPACES({namespaces}), " : "";
            var passing = expressions(expression, key: "passing");
            passing = passing.Length != 0 ? $"{sep()}PASSING{seg(passing)}" : "";
            var columns = expressions(expression, key: "columns");
            columns = columns.Length != 0 ? $"{sep()}COLUMNS{seg(columns)}" : "";
            var by_ref = py.truthy(expression.args.get("by_ref")) ? $"{sep()}RETURNING SEQUENCE BY REF" : "";
            return $"XMLTABLE({sep("")}{indent(namespaces + @this + passing + by_ref + columns)}{seg(")", sep: "")}";
        }

        // py: sqlglot/generator.py::Generator.xmlnamespace_sql #0000000000
        public virtual string xmlnamespace_sql(exp.XMLNamespace expression)
        {
            var @this = sql(expression, "this");
            return expression.@this is exp.Alias ? @this : $"DEFAULT {@this}";
        }

        // py: sqlglot/generator.py::Generator.overlay_sql #0000000000
        public virtual string overlay_sql(exp.Overlay expression)
        {
            var @this = sql(expression, "this");
            var expr = sql(expression, "expression");
            var from_sql = sql(expression, "from_");
            var for_sql = sql(expression, "for_");
            for_sql = for_sql.Length != 0 ? $" FOR {for_sql}" : "";

            return $"OVERLAY({@this} PLACING {expr} FROM {from_sql}{for_sql})";
        }

        // py: sqlglot/generator.py::Generator.initcap_sql #0000000000
        public virtual string initcap_sql(exp.Initcap expression)
        {
            var delimiters = expression.args.get("expression") as exp.Expr;

            if (delimiters is not null)
            {
                // do not generate delimiters arg if we are round-tripping from default delimiters
                if (delimiters.is_string && Equals(delimiters.@this, dialect.INITCAP_DEFAULT_DELIMITER_CHARS)) delimiters = null;
                else if (!dialect.INITCAP_SUPPORTS_CUSTOM_DELIMITERS)
                {
                    unsupported("INITCAP does not support custom delimiters");
                    delimiters = null;
                }
            }

            return func("INITCAP", expression.@this, delimiters);
        }

        // py: sqlglot/generator.py::Generator.trycast_sql #0000000000
        public virtual string trycast_sql(exp.TryCast expression) => cast_sql(expression, safe_prefix: "TRY_");

        // py: sqlglot/generator.py::Generator.scope_resolution #0000000000
        public virtual string scope_resolution(string rhs, string scope_name) =>
            func("SCOPE_RESOLUTION", scope_name.Length != 0 ? scope_name : null, rhs);

        // py: sqlglot/generator.py::Generator.scoperesolution_sql #0000000000
        public virtual string scoperesolution_sql(exp.ScopeResolution expression)
        {
            var @this = sql(expression, "this");
            var expr = expression.args.get("expression");
            string expr_sql;

            if (expr is exp.Func && expr is exp.Expr func_expr)
            {
                // T-SQL's CLR functions are case sensitive
                var args = func_expr.args.get("expressions") is System.Collections.IEnumerable items
                    ? items.Cast<object?>().ToArray() : [];
                expr_sql = $"{sql(func_expr, "this")}({format_args(args)})";
            }
            else expr_sql = sql(expression, "expression");

            return scope_resolution(expr_sql, @this);
        }

        // py: sqlglot/generator.py::Generator.recursivewithsearch_sql #0000000000
        public virtual string recursivewithsearch_sql(exp.RecursiveWithSearch expression)
        {
            var kind = sql(expression, "kind");
            var @this = sql(expression, "this");
            var set = sql(expression, "expression");
            var to = sql(expression, "to");
            to = to.Length != 0 ? $" TO {to}" : "";
            var @default = sql(expression, "default");
            @default = @default.Length != 0 ? $" DEFAULT {@default}" : "";
            var @using = sql(expression, "using");
            @using = @using.Length != 0 ? $" USING {@using}" : "";

            var kind_sql = kind == "CYCLE" ? kind : $"SEARCH {kind} FIRST BY";

            return $"{kind_sql} {@this} SET {set}{to}{@default}{@using}";
        }

        // py: sqlglot/generator.py::Generator.dateadd_sql #0000000000
        public virtual string dateadd_sql(exp.DateAdd expression) =>
            func("DATE_ADD", expression.@this, expression.args.get("expression"), dialects.dialect.unit_to_str(expression));

        // py: sqlglot/generator.py::Generator.parsejson_sql #0000000000
        public virtual string parsejson_sql(exp.ParseJSON expression)
        {
            if (PARSE_JSON_NAME is null) return sql(expression.@this);

            return func(PARSE_JSON_NAME, expression.@this, expression.args.get("expression"));
        }

        // py: sqlglot/generator.py::Generator.json_sql #0000000000
        public virtual string json_sql(exp.JSON expression)
        {
            var @this = sql(expression, "this");
            @this = @this.Length != 0 ? $" {@this}" : "";

            var _with = expression.args.get("with_");

            string with_sql;
            if (_with is null) with_sql = "";
            else if (!py.truthy(_with)) with_sql = " WITHOUT";
            else with_sql = " WITH";

            var unique_sql = py.truthy(expression.args.get("unique")) ? " UNIQUE KEYS" : "";

            return $"JSON{@this}{with_sql}{unique_sql}";
        }

        // py: sqlglot/generator.py::Generator.tsordstotime_sql #0000000000
        public virtual string tsordstotime_sql(exp.TsOrDsToTime expression)
        {
            var @this = (exp.Expr)expression.args["this"]!;
            var time_format = format_time(expression);

            if (!string.IsNullOrEmpty(time_format))
                return sql(exp_cast(new exp.StrToTime(@this: @this, format: expression.args["format"]), exp.DType.TIME));

            if (@this is exp.TsOrDsToTime || @this.is_type(exp.DType.TIME)) return sql(@this);

            return sql(exp_cast(@this, exp.DType.TIME));
        }

        // py: sqlglot/generator.py::Generator.tsordstodate_sql #0000000000
        public virtual string tsordstodate_sql(exp.TsOrDsToDate expression)
        {
            var @this = (exp.Expr)expression.args["this"]!;
            var time_format = format_time(expression);
            var safe = expression.args.get("safe");
            if (!string.IsNullOrEmpty(time_format) && !Equals(time_format, dialect.TIME_FORMAT) && !Equals(time_format, dialect.DATE_FORMAT))
                return sql(exp_cast(new exp.StrToTime(@this: @this, format: expression.args["format"], safe: safe), exp.DType.DATE));

            if (@this is exp.TsOrDsToDate || @this.is_type(exp.DType.DATE)) return sql(@this);

            if (py.truthy(safe)) return sql(new exp.TryCast(@this: @this, to: new exp.DataType(@this: exp.DType.DATE)));

            return sql(exp_cast(@this, exp.DType.DATE));
        }

        // py: sqlglot/generator.py::Generator.tsordstotimestamp_sql #0000000000
        public virtual string tsordstotimestamp_sql(exp.TsOrDsToTimestamp expression)
        {
            var @this = (exp.Expr)expression.args["this"]!;
            if (@this is exp.TsOrDsToTimestamp || @this.is_type(exp.DType.TIMESTAMP)) return sql(@this);

            return sql(exp_cast(@this, exp.DType.TIMESTAMP));
        }

        // py: sqlglot/generator.py::Generator.kill_sql #0000000000
        public virtual string kill_sql(exp.Kill expression)
        {
            var kind = sql(expression, "kind");
            kind = kind.Length != 0 ? $" {kind}" : "";
            var @this = sql(expression, "this");
            @this = @this.Length != 0 ? $" {@this}" : "";
            return $"KILL{kind}{@this}";
        }

        // py: sqlglot/generator.py::Generator.multitableinserts_sql #0000000000
        public virtual string multitableinserts_sql(exp.MultitableInserts expression)
        {
            var kind = sql(expression, "kind");
            var source = sql(expression, "source");

            string text;
            if (kind.Length != 0)
            {
                var exprs = seg(expressions(expression, sep: " "));
                text = $"INSERT {kind}{exprs}{seg(source)}";
            }
            else
            {
                // If kind is not present, we have Hive's FROM-first form:
                // FROM x INSERT ... SELECT ... [INSERT ... SELECT ...]
                var items = ((System.Collections.IEnumerable)(expression.args.get("expressions") ?? new List<object?>())).Cast<object?>();
                var inserts = string.Join(sep(), items.Select(e => sql(e)));
                text = $"FROM {source}{seg(inserts)}";
            }

            return prepend_ctes(expression, text);
        }

        // py: sqlglot/generator.py::Generator.conditionalinsert_sql #0000000000
        public virtual string conditionalinsert_sql(exp.ConditionalInsert expression)
        {
            var else_ = py.truthy(expression.args.get("else_")) ? "ELSE " : "";
            var condition = sql(expression, "expression");
            condition = condition.Length != 0 ? $"WHEN {condition} THEN " : else_;
            var insert = sql(expression, "this")["INSERT".Length..].Trim();
            return $"{condition}{insert}";
        }

        // py: sqlglot/generator.py::Generator.aliases_sql #0000000000
        public virtual string aliases_sql(exp.Aliases expression) =>
            $"{sql(expression, "this")} AS ({expressions(expression, flat: true)})";
    }
}
