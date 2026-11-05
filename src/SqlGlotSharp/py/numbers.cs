using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SqlGlotSharp;

public static partial class py
{
    // Python integers map to Int64 in this port; arbitrary precision remains unsupported.
    public static long @int(object value) => value switch
    {
        bool b => b ? 1 : 0,
        byte n => n,
        sbyte n => n,
        short n => n,
        ushort n => n,
        int n => n,
        uint n => n,
        long n => n,
        ulong n => checked((long)n),
        double n => checked((long)Math.Truncate(n)),
        float n => checked((long)Math.Truncate(n)),
        decimal n => checked((long)decimal.Truncate(n)),
        string text => ParseInteger(text),
        _ => throw new ArgumentException("int() requires a number or string", nameof(value))
    };

    public static double @float(object value) => value switch
    {
        bool b => b ? 1 : 0,
        byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal =>
            Convert.ToDouble(value, CultureInfo.InvariantCulture),
        string text => ParseFloat(text),
        _ => throw new ArgumentException("float() requires a number or string", nameof(value))
    };

    private static string NormalizeDigits(string text)
    {
        var result = new StringBuilder();
        foreach (var rune in text.Trim().EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.DecimalDigitNumber)
                result.Append((char)('0' + (int)Rune.GetNumericValue(rune)));
            else result.Append(rune.ToString());
        }
        return result.ToString();
    }

    private static long ParseInteger(string text)
    {
        var normalized = NormalizeDigits(text);
        if (!Regex.IsMatch(normalized, @"\A[+-]?[0-9](?:_?[0-9])*\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            throw new FormatException("invalid literal for int() with base 10");
        return long.Parse(normalized.Replace("_", ""), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    private static double ParseFloat(string text)
    {
        var normalized = NormalizeDigits(text);
        var unsigned = normalized.TrimStart('+', '-').ToLowerInvariant();
        // Only one leading sign is valid.
        if (unsigned is "nan" or "inf" or "infinity" &&
            Regex.IsMatch(normalized, @"\A[+-]?(?:nan|inf|infinity)\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            return unsigned == "nan" ? double.NaN : normalized.StartsWith('-') ? double.NegativeInfinity : double.PositiveInfinity;
        const string digits = @"[0-9](?:_?[0-9])*";
        if (!Regex.IsMatch(normalized, @"\A[+-]?(?:" + digits + @"(?:\.(?:" + digits + @")?)?|\." + digits + @")(?:[eE][+-]?" + digits + @")?\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            throw new FormatException("could not convert string to float");
        return double.Parse(normalized.Replace("_", ""), NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
