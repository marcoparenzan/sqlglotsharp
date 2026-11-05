namespace SqlGlotSharp;

// CACHE, UNCACHE, LOAD DATA, DESCRIBE, ANALYZE and TABLESAMPLE rendering (generator.py).
public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.uncache_sql #0000000000
        public virtual string uncache_sql(exp.Uncache expression)
        {
            var table = sql(expression, "this");
            var exists_sql = py.truthy(expression.args.get("exists")) ? " IF EXISTS" : "";
            return $"UNCACHE TABLE{exists_sql} {table}";
        }

        // py: sqlglot/generator.py::Generator.cache_sql #0000000000
        public virtual string cache_sql(exp.Cache expression)
        {
            var lazy = py.truthy(expression.args.get("lazy")) ? " LAZY" : "";
            var table = sql(expression, "this");
            var options = expression.args.get("options") as List<object?>;
            var options_sql = options is { Count: > 0 } ? $" OPTIONS({sql(options[0])} = {sql(options[1])})" : "";
            var text = sql(expression, "expression");
            text = text.Length != 0 ? $" AS{sep()}{text}" : "";
            text = $"CACHE{lazy} TABLE {table}{options_sql}{text}";
            return prepend_ctes(expression, text);
        }

        // py: sqlglot/generator.py::Generator.loaddata_sql #0000000000
        public virtual string loaddata_sql(exp.LoadData expression)
        {
            var is_overwrite = py.truthy(expression.args.get("overwrite"));
            var overwrite = is_overwrite ? " OVERWRITE" : "";
            var @this = sql(expression, "this");

            if (expression.args.get("files") is exp.Expr files && py.truthy(files))
            {
                var files_sql = expressions(files, flat: true);
                files_sql = $"FILES{wrap(files_sql)}";
                if (is_overwrite) @this = $" {@this}";
                else if (py.truthy(expression.args.get("temp"))) @this = $" INTO TEMP TABLE {@this}";
                else @this = $" INTO TABLE {@this}";
                return $"LOAD DATA{overwrite}{@this} FROM {files_sql}";
            }

            var local = py.truthy(expression.args.get("local")) ? " LOCAL" : "";
            var inpath = $" INPATH {sql(expression, "inpath")}";
            @this = $" INTO TABLE {@this}";
            var partition = sql(expression, "partition");
            partition = partition.Length != 0 ? $" {partition}" : "";
            var input_format = sql(expression, "input_format");
            input_format = input_format.Length != 0 ? $" INPUTFORMAT {input_format}" : "";
            var serde = sql(expression, "serde");
            serde = serde.Length != 0 ? $" SERDE {serde}" : "";
            return $"LOAD DATA{local}{inpath}{overwrite}{@this}{partition}{input_format}{serde}";
        }

        // py: sqlglot/generator.py::Generator.describe_sql #0000000000
        public virtual string describe_sql(exp.Describe expression)
        {
            var style = expression.args.get("style");
            var style_sql = py.truthy(style) ? $" {py.str(style)}" : "";
            var partition = sql(expression, "partition");
            partition = partition.Length != 0 ? $" {partition}" : "";
            var format = sql(expression, "format");
            format = format.Length != 0 ? $" {format}" : "";
            var as_json = py.truthy(expression.args.get("as_json")) ? " AS JSON" : "";

            return $"DESCRIBE{style_sql}{format} {sql(expression, "this")}{partition}{as_json}";
        }

        // py: sqlglot/generator.py::Generator.analyze_sql #0000000000
        public virtual string analyze_sql(exp.Analyze expression)
        {
            var options = expressions(expression, key: "options", sep: " ");
            options = options.Length != 0 ? $" {options}" : "";
            var kind = sql(expression, "kind");
            kind = kind.Length != 0 ? $" {kind}" : "";
            var tables = expressions(expression, key: "tables", flat: true);
            tables = tables.Length != 0 ? $" {tables}" : "";
            var mode = sql(expression, "mode");
            mode = mode.Length != 0 ? $" {mode}" : "";
            var properties = sql(expression, "properties");
            properties = properties.Length != 0 ? $" {properties}" : "";
            var partition = sql(expression, "partition");
            partition = partition.Length != 0 ? $" {partition}" : "";
            var inner_expression = sql(expression, "expression");
            inner_expression = inner_expression.Length != 0 ? $" {inner_expression}" : "";
            return $"ANALYZE{options}{kind}{tables}{partition}{mode}{inner_expression}{properties}";
        }

        // py: sqlglot/generator.py::Generator.analyzestatistics_sql #0000000000
        public virtual string analyzestatistics_sql(exp.AnalyzeStatistics expression)
        {
            var kind = sql(expression, "kind");
            var option = sql(expression, "option");
            option = option.Length != 0 ? $" {option}" : "";
            var @this = sql(expression, "this");
            @this = @this.Length != 0 ? $" {@this}" : "";
            var columns = expressions(expression);
            columns = columns.Length != 0 ? $" {columns}" : "";
            return $"{kind}{option} STATISTICS{@this}{columns}";
        }

        // py: sqlglot/generator.py::Generator.analyzesample_sql #0000000000
        public virtual string analyzesample_sql(exp.AnalyzeSample expression)
        {
            var kind = sql(expression, "kind");
            var sample = sql(expression, "sample");
            return $"SAMPLE {sample} {kind}";
        }

        // py: sqlglot/generator.py::Generator.analyzehistogram_sql #0000000000
        public virtual string analyzehistogram_sql(exp.AnalyzeHistogram expression)
        {
            var @this = sql(expression, "this");
            var columns = expressions(expression);
            var inner_expression = sql(expression, "expression");
            inner_expression = inner_expression.Length != 0 ? $" {inner_expression}" : "";
            var update_options = sql(expression, "update_options");
            update_options = update_options.Length != 0 ? $" {update_options} UPDATE" : "";
            return $"{@this} HISTOGRAM ON {columns}{inner_expression}{update_options}";
        }

        // py: sqlglot/generator.py::Generator.analyzedelete_sql #0000000000
        public virtual string analyzedelete_sql(exp.AnalyzeDelete expression)
        {
            var kind = sql(expression, "kind");
            kind = kind.Length != 0 ? $" {kind}" : "";
            return $"DELETE{kind} STATISTICS";
        }

        // py: sqlglot/generator.py::Generator.analyzelistchainedrows_sql #0000000000
        public virtual string analyzelistchainedrows_sql(exp.AnalyzeListChainedRows expression) =>
            $"LIST CHAINED ROWS{sql(expression, "expression")}";

        // py: sqlglot/generator.py::Generator.analyzevalidate_sql #0000000000
        public virtual string analyzevalidate_sql(exp.AnalyzeValidate expression)
        {
            var kind = sql(expression, "kind");
            var @this = sql(expression, "this");
            @this = @this.Length != 0 ? $" {@this}" : "";
            var inner_expression = sql(expression, "expression");
            return $"VALIDATE {kind}{@this}{inner_expression}";
        }

        // py: sqlglot/generator.py::Generator.tablesample_sql #0000000000
        public virtual string tablesample_sql(exp.TableSample expression, string? tablesample_keyword = null)
        {
            if (py.truthy(expression.args.get("expressions")))
            {
                unsupported("Multiple sample sets are not supported");
                return "";
            }

            var method = sql(expression, "method");
            method = method.Length != 0 && TABLESAMPLE_WITH_METHOD ? $"{method} " : "";
            var numerator = sql(expression, "bucket_numerator");
            var denominator = sql(expression, "bucket_denominator");
            var field = sql(expression, "bucket_field");
            field = field.Length != 0 ? $" ON {field}" : "";
            var bucket = numerator.Length != 0 ? $"BUCKET {numerator} OUT OF {denominator}{field}" : "";
            var seed = sql(expression, "seed");
            seed = seed.Length != 0 ? $" {TABLESAMPLE_SEED_KEYWORD} ({seed})" : "";

            var size = sql(expression, "size");
            if (size.Length != 0 && TABLESAMPLE_SIZE_IS_ROWS) size = $"{size} ROWS";

            var percent = sql(expression, "percent");
            if (percent.Length != 0 && !dialect.TABLESAMPLE_SIZE_IS_PERCENT) percent = $"{percent} PERCENT";

            var expr = $"{bucket}{percent}{size}";
            if (TABLESAMPLE_REQUIRES_PARENS) expr = $"({expr})";

            return $" {tablesample_keyword ?? TABLESAMPLE_KEYWORDS} {method}{expr}{seed}";
        }
    }
}
