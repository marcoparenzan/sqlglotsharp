namespace SqlGlotSharp;

public static partial class generator
{
    public partial class Generator
    {
        // py: sqlglot/generator.py::Generator.root_properties #0000000000
        public virtual string root_properties(exp.Properties properties) => properties.expressions.Count > 0
            ? expressions(properties, indent: false, sep: " ") : "";

        // py: sqlglot/generator.py::Generator.properties #0000000000
        public virtual string properties(exp.Properties properties, string prefix = "", string sep = ", ",
            string suffix = "", bool wrapped = true)
        {
            if (properties.expressions.Count == 0) return "";
            var text = expressions(properties, sep: sep, indent: false);
            if (text.Length == 0) return "";
            if (wrapped) text = wrap(text);
            return $"{prefix}{(prefix.Trim().Length > 0 ? " " : "")}{text}{suffix}";
        }

        // py: sqlglot/generator.py::Generator.with_properties #0000000000
        public virtual string with_properties(exp.Properties properties) =>
            this.properties(properties, prefix: seg(WITH_PROPERTIES_PREFIX, sep: ""));

        // py: sqlglot/generator.py::Generator.property_name #0000000000
        public virtual string property_name(exp.Property expression, bool string_key = false) =>
            expression.@this is exp.Dot ? sql(expression, "this") : string_key
                ? sql(new exp.Literal(@this: expression.name, is_string: true)) : expression.name;
    }
}
