using System.Text;
using System.Text.RegularExpressions;

namespace SqlGlotSharp.dialects;

public static partial class dialect
{
    public partial class Dialect
    {
        // py: sqlglot/dialects/dialect.py::Dialect.normalize_identifier #acfa58e725
        public T normalize_identifier<T>(T expression) where T : exp.Expr
        {
            if (expression is exp.Identifier identifier && normalization_strategy != NormalizationStrategy.CASE_SENSITIVE &&
                (!identifier.quoted || normalization_strategy is NormalizationStrategy.CASE_INSENSITIVE or NormalizationStrategy.CASE_INSENSITIVE_UPPERCASE))
            {
                var upper = normalization_strategy is NormalizationStrategy.UPPERCASE or NormalizationStrategy.CASE_INSENSITIVE_UPPERCASE;
                var text = (string)identifier.@this!;
                if (ASCII_ONLY_NORMALIZATION)
                {
                    var output = new StringBuilder();
                    foreach (var c in text)
                        output.Append(upper && c is >= 'a' and <= 'z' ? (char)(c - 32) : !upper && c is >= 'A' and <= 'Z' ? (char)(c + 32) : c);
                    identifier.set("this", output.ToString());
                }
                else identifier.set("this", upper ? py.upper(text) : py.lower(text));
            }
            return expression;
        }
        // py: sqlglot/dialects/dialect.py::Dialect.case_sensitive #6c82c9939b
        public bool case_sensitive(string text)
        {
            if (normalization_strategy == NormalizationStrategy.CASE_INSENSITIVE) return false;
            var lowercaseIsUnsafe = normalization_strategy == NormalizationStrategy.UPPERCASE;
            return text.EnumerateRunes().Any(r => lowercaseIsUnsafe ? py.islower(r.ToString()) : py.isupper(r.ToString()));
        }
        // py: sqlglot/dialects/dialect.py::Dialect.can_quote #0a7af120da
        public bool can_quote(exp.Identifier identifier, [System.Runtime.InteropServices.Optional] object? identify)
        {
            if (identify is System.Reflection.Missing) identify = "safe";
            if (identifier.quoted) return true;
            if (!py.truthy(identify)) return false;
            if (identifier.parent is exp.Func) return false;
            if (identify is true) return true;
            var text = (string)identifier.@this!;
            var safe = !case_sensitive(text) && py.safe_identifier(text);
            return identify switch
            {
                "safe" => safe,
                "unsafe" => !safe,
                _ => throw new ArgumentException($"Unexpected argument for identify: '{identify}'")
            };
        }
        // py: sqlglot/dialects/dialect.py::Dialect.quote_identifier #b6469f1fee
        public T quote_identifier<T>(T expression, bool identify = true) where T : exp.Expr
        {
            if (expression is exp.Identifier identifier) identifier.set("quoted", can_quote(identifier, identify ? true : "unsafe"));
            return expression;
        }
    }
}
