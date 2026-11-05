using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace SqlGlotSharp;

public static partial class helper
{
    // py: sqlglot/helper.py::to_int #5d13eafa1d
    public static long? to_int(object value, int max_digits = 4300)
    {
        var raw = value switch
        {
            string text => text,
            long or int or decimal => ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture),
            _ => throw new ArgumentException("to_int requires a string, integer or decimal", nameof(value))
        };
        var normalized = new StringBuilder();
        foreach (var rune in raw.Trim().EnumerateRunes())
        {
            if (rune.Value == '_') continue; // Decimal accepts underscores anywhere in the input.
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.DecimalDigitNumber)
                normalized.Append((char)('0' + (int)Rune.GetNumericValue(rune)));
            else normalized.Append(rune.ToString());
        }
        var match = DecimalSyntax.Match(normalized.ToString());
        if (!match.Success) return null;
        var whole = match.Groups["whole"].Value;
        var fraction = match.Groups["fraction"].Value;
        var digits = (whole + fraction).TrimStart('0');
        var exponentText = match.Groups["exponent"].Value;
        if (!long.TryParse(exponentText.Length == 0 ? "0" : exponentText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var exponent))
            return null;
        // BigInteger here only holds a bounded-size exponent calculation, never the significand.
        var scale = (BigInteger)exponent - fraction.Length;
        var adjusted = digits.Length == 0 ? scale : scale + digits.Length - 1;
        if (adjusted >= max_digits) return null;
        if (digits.Length == 0) return 0;
        if (scale < 0)
        {
            var remove = -scale;
            if (remove > digits.Length) return null;
            var count = (int)remove;
            if (digits.AsSpan(digits.Length - count).ContainsAnyExcept('0')) return null;
            digits = digits[..(digits.Length - count)];
            scale = 0;
        }
        if (digits.Length + scale > 19) throw new OverflowException("Python integer exceeds the port's Int64 range");
        var integer = digits + new string('0', (int)scale);
        if (match.Groups["sign"].Value == "-") integer = "-" + integer;
        return long.Parse(integer, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    private static readonly Regex DecimalSyntax = new(
        @"\A(?<sign>[+-]?)(?:(?<whole>[0-9]+)(?:\.(?<fraction>[0-9]*))?|\.(?<fraction>[0-9]+))(?:[eE](?<exponent>[+-]?[0-9]+))?\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
}
