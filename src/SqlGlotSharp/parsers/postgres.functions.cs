using TokenType = SqlGlotSharp.tokenizer_core.TokenType;

using Parser = SqlGlotSharp.parser.Parser;

namespace SqlGlotSharp.parsers;

// Function builders and special parsers of sqlglot/parsers/postgres.py.
public static partial class postgres
{
    public partial class PostgresParser
    {
        private static readonly PyDict<string, string> MAKE_INTERVAL_KWARGS = new()
        {
            ["years"] = "year", ["months"] = "month", ["weeks"] = "week", ["days"] = "day",
            ["hours"] = "hour", ["mins"] = "minute", ["secs"] = "second"
        };

        private static exp.Expr Build(Type type, params (string key, object? value)[] pairs)
        {
            var arguments = new PyDict<string, object?>();
            foreach (var (key, value) in pairs) arguments[key] = value;
            return (exp.Expr)Activator.CreateInstance(type, [arguments])!;
        }

        // py: sqlglot/expressions/builders.py::cast (no dialect, simple types)
        private static exp.Expr CastTo(exp.Expr expr, exp.DType to)
        {
            var data_type = exp.DataType.build(to);
            if (expr is exp.Cast existing)
            {
                var existing_type = (existing.args.get("to") as exp.DataType)?.@this;
                var type_mapping = generator.Generator.TYPE_MAPPING_;
                var equivalent = existing_type is exp.DType e && data_type.@this is exp.DType n
                    && (type_mapping.TryGetValue(e, out var em) ? em : e.ToString()) == (type_mapping.TryGetValue(n, out var nm) ? nm : n.ToString());
                if (expr.is_type(data_type) || equivalent) return expr;
            }

            var result = new exp.Cast(@this: expr, to: data_type);
            result.type = data_type;
            return result;
        }

        // py: sqlglot/expressions/builders.py::to_interval
        private static exp.Interval ToInterval(object interval)
        {
            if (interval is exp.Literal literal)
            {
                if (!literal.is_string) throw new ArgumentException("Invalid interval string.");
                interval = literal.@this!;
            }

            return (exp.Interval)SqlGlot.ParseOne($"INTERVAL {interval}");
        }

        // py: sqlglot/parsers/postgres.py::_build_make_interval
        private static exp.Expr _build_make_interval(IReadOnlyList<object?> args)
        {
            var arguments = new PyDict<string, object?>();
            var keys = exp.MakeInterval.arg_types_.Keys.ToList();
            for (var i = 0; i < Math.Min(keys.Count, args.Count); i++)
            {
                var key = keys[i];
                var value = args[i];
                if (value is exp.Kwarg kwarg)
                    key = MAKE_INTERVAL_KWARGS.TryGetValue(py.lower(((exp.Expr)kwarg.@this!).name), out var mapped) ? mapped : key;
                arguments[key] = value;
            }
            return new exp.MakeInterval(arguments);
        }

        // py: sqlglot/parsers/postgres.py::_build_generate_series
        private static exp.Expr _build_generate_series(IReadOnlyList<object?> args)
        {
            // The goal is to convert step values like '1 day' or INTERVAL '1 day' into INTERVAL '1' day
            // Note: postgres allows calls with just two arguments -- the "step" argument defaults to 1
            var list = args.ToList();
            var step = helper.seq_get(list, 2) as exp.Expr;
            if (step is not null)
            {
                if (step.is_string) list[2] = ToInterval(step.@this!);
                else if (step is exp.Interval && !py.truthy(step.args.get("unit"))) list[2] = ToInterval(((exp.Expr)step.@this!).@this!);
            }

            return new exp.ExplodingGenerateSeries().from_arg_list(list);
        }

        // py: sqlglot/parsers/postgres.py::_build_to_timestamp
        private static exp.Expr _build_to_timestamp(IReadOnlyList<object?> args)
        {
            // TO_TIMESTAMP accepts either a single double argument or (text, text)
            if (args.Count == 1) return new exp.UnixToTime().from_arg_list(args);

            return dialects.dialect.build_formatted_time(typeof(exp.StrToTime))(args, BuilderDialect);
        }

        // py: sqlglot/parsers/postgres.py::_build_regexp_replace
        private static exp.Expr _build_regexp_replace(IReadOnlyList<object?> args)
        {
            // The signature of REGEXP_REPLACE is:
            // regexp_replace(source, pattern, replacement [, start [, N ]] [, flags ])
            //
            // Any one of `start`, `N` and `flags` can be column references, meaning that
            // unless we can statically see that the last argument is a non-integer string
            // (eg. not '0'), then it is not possible to construct the correct AST
            exp.Expr? regexp_replace = null;
            if (args.Count > 3)
            {
                var last = (exp.Expr)args[^1]!;
                if (!helper.is_int(last.name))
                {
                    if (last.type is null || last.is_type(exp.DType.UNKNOWN, exp.DType.NULL))
                        last = optimizer.annotate_types_module.annotate_types(last, dialect: BuilderDialect);

                    if (last.is_type(exp.DataType.TEXT_TYPES_.Cast<object>().ToArray()))
                    {
                        regexp_replace = new exp.RegexpReplace().from_arg_list(args.Take(args.Count - 1).ToList());
                        regexp_replace.set("modifiers", last);
                    }
                }
            }

            regexp_replace ??= new exp.RegexpReplace().from_arg_list(args);
            regexp_replace.set("single_replace", true);
            return regexp_replace;
        }

        // py: sqlglot/parsers/postgres.py::_build_levenshtein_less_equal
        private static exp.Expr _build_levenshtein_less_equal(IReadOnlyList<object?> args)
        {
            // Postgres has two signatures for levenshtein_less_equal function, but in both cases
            // max_dist is the last argument
            // levenshtein_less_equal(source, target, ins_cost, del_cost, sub_cost, max_d)
            // levenshtein_less_equal(source, target, max_d)
            var rest = args.Take(args.Count - 1).ToList();
            return new exp.Levenshtein(@this: helper.seq_get(rest, 0), expression: helper.seq_get(rest, 1),
                ins_cost: helper.seq_get(rest, 2), del_cost: helper.seq_get(rest, 3), sub_cost: helper.seq_get(rest, 4),
                max_dist: args[^1]);
        }

        // py: sqlglot/dialects/dialect.py::build_json_extract_path
        private static Func<IReadOnlyList<object?>, exp.Expr> build_json_extract_path(Type expr_type,
            bool zero_based_indexing = true, bool arrow_req_json_type = false, string? json_type = null) => args =>
        {
            List<object?> segments = [new exp.JSONPathRoot()];
            foreach (var arg in args.Skip(1))
            {
                if (arg is not exp.Literal literal)
                {
                    // We use the fallback parser because we cannot really transpile non-literals safely
                    var rest = args.Skip(2).ToList();
                    return (exp.Expr)Build(expr_type, ("this", helper.seq_get(args, 0)), ("expression", helper.seq_get(args, 1)),
                        ("expressions", rest.Count > 0 ? rest : null));
                }

                var text = literal.name;
                if (helper.is_int(text) && (!arrow_req_json_type || !literal.is_string))
                {
                    var index = System.Numerics.BigInteger.Parse(text.Trim());
                    segments.Add(new exp.JSONPathSubscript(@this: zero_based_indexing ? (object)(long)index : (long)index - 1));
                }
                else segments.Add(new exp.JSONPathKey(@this: text));
            }

            var kwargs = new List<(string, object?)>
            {
                ("this", helper.seq_get(args, 0)),
                ("expression", new exp.JSONPath(expressions: segments)),
            };
            var is_jsonb = expr_type == typeof(exp.JSONBExtract) || expr_type == typeof(exp.JSONBExtractScalar);
            if (!is_jsonb) kwargs.Add(("only_json_types", arrow_req_json_type));
            if (json_type is not null) kwargs.Add(("json_type", json_type));

            return Build(expr_type, kwargs.ToArray());
        };

        // py: sqlglot/parsers/postgres.py::PostgresParser._parse_generated_as_identity
        public override exp.Expr _parse_generated_as_identity()
        {
            var @this = base._parse_generated_as_identity();

            if (_match_text_seq(["STORED"])) @this = expression(new exp.ComputedColumnConstraint(@this: @this.args.get("expression")));

            return @this;
        }

        // py: sqlglot/parsers/postgres.py::PostgresParser._parse_query_parameter
        public exp.Expr? _parse_query_parameter()
        {
            var @this = _match(TokenType.L_PAREN, advance: false) ? _parse_wrapped(() => _parse_id_var()) : null;
            _match_text_seq(["S"]);
            return expression(new exp.Placeholder(@this: @this));
        }

        // py: sqlglot/parsers/postgres.py::PostgresParser._parse_date_part
        public exp.Expr _parse_date_part()
        {
            var part = _parse_type();
            _match(TokenType.COMMA);
            var value = _parse_bitwise();

            if (part is exp.Column or exp.Literal) part = new exp.Var(@this: py.upper(part.name));

            return expression(new exp.Extract(@this: part, expression: value));
        }

        // py: sqlglot/parsers/postgres.py::PostgresParser._parse_jsonb_exists
        public exp.Expr _parse_jsonb_exists() =>
            expression(new exp.JSONBExists(@this: _parse_bitwise(),
                path: _match(TokenType.COMMA) ? dialect.to_json_path(_parse_bitwise()) : false));

        private static Func<IReadOnlyList<object?>, exp.Expr> FromArgList(Type type) =>
            args => ((exp.Expr)Activator.CreateInstance(type, [new PyDict<string, object?>()])!).from_arg_list(args);

        private static PyDict<string, Func<IReadOnlyList<object?>, exp.Expr>> PostgresBuilders() => new()
        {
            ["ARRAY_PREPEND"] = args => new exp.ArrayPrepend(@this: helper.seq_get(args, 1), expression: helper.seq_get(args, 0)),
            ["BIT_AND"] = FromArgList(typeof(exp.BitwiseAndAgg)),
            ["BIT_OR"] = FromArgList(typeof(exp.BitwiseOrAgg)),
            ["BIT_XOR"] = FromArgList(typeof(exp.BitwiseXorAgg)),
            ["BTRIM"] = FromArgList(typeof(exp.Trim)),
            ["VERSION"] = FromArgList(typeof(exp.CurrentVersion)),
            ["DIV"] = args => CastTo(dialects.dialect.binary_from_function(typeof(exp.IntDiv))(args), exp.DType.DECIMAL),
            ["GENERATE_SERIES"] = _build_generate_series,
            ["GET_BIT"] = args => new exp.Getbit(@this: helper.seq_get(args, 0), expression: helper.seq_get(args, 1), zero_is_msb: true),
            ["JSON_EXTRACT_PATH"] = build_json_extract_path(typeof(exp.JSONExtract)),
            ["JSON_EXTRACT_PATH_TEXT"] = build_json_extract_path(typeof(exp.JSONExtractScalar)),
            ["LENGTH"] = args => new exp.Length(@this: helper.seq_get(args, 0), encoding: helper.seq_get(args, 1)),
            ["MAKE_INTERVAL"] = _build_make_interval,
            ["MAKE_TIME"] = FromArgList(typeof(exp.TimeFromParts)),
            ["MAKE_TIMESTAMP"] = FromArgList(typeof(exp.TimestampFromParts)),
            ["REGEXP_REPLACE"] = _build_regexp_replace,
            ["TO_CHAR"] = args => dialects.dialect.build_formatted_time(typeof(exp.TimeToStr))(args, BuilderDialect),
            ["TO_DATE"] = args => dialects.dialect.build_formatted_time(typeof(exp.StrToDate))(args, BuilderDialect),
            ["TO_TIMESTAMP"] = _build_to_timestamp,
            ["UNNEST"] = FromArgList(typeof(exp.Explode)),
            ["SHA256"] = args => new exp.SHA2Digest(@this: helper.seq_get(args, 0), length: exp.Literal.number(256)),
            ["SHA384"] = args => new exp.SHA2Digest(@this: helper.seq_get(args, 0), length: exp.Literal.number(384)),
            ["SHA512"] = args => new exp.SHA2Digest(@this: helper.seq_get(args, 0), length: exp.Literal.number(512)),
            ["LEVENSHTEIN_LESS_EQUAL"] = _build_levenshtein_less_equal,
            ["JSON_OBJECT_AGG"] = args => new exp.JSONObjectAgg(expressions: args.ToList()),
            ["JSONB_OBJECT_AGG"] = FromArgList(typeof(exp.JSONBObjectAgg)),
            ["WIDTH_BUCKET"] = args => args.Count == 2
                ? new exp.WidthBucket(@this: helper.seq_get(args, 0), threshold: helper.seq_get(args, 1))
                : new exp.WidthBucket().from_arg_list(args),
            ["UUID"] = args => args.Count > 0 ? CastTo((exp.Expr)args[0]!, exp.DType.UUID) : new exp.Uuid(),
        };
    }
}
