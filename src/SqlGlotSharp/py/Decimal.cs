using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace SqlGlotSharp;

/// <summary>Exact decimal literal value. Preserves coefficient, exponent and signed zero without expanding huge exponents.</summary>
public sealed class PyDecimal : IPyRepresentation, IPyString, IPyImmutable, IPyTruthiness
{
    public BigInteger Coefficient { get; }
    public long Exponent { get; }
    public bool Negative { get; }
    private readonly string? special;

    public PyDecimal(string text)
    {
        var normalized = new StringBuilder();
        foreach (var rune in text.Trim().EnumerateRunes())
        {
            if (rune.Value == '_') continue;
            normalized.Append(Rune.GetUnicodeCategory(rune) == UnicodeCategory.DecimalDigitNumber
                ? ((char)('0' + (int)Rune.GetNumericValue(rune))).ToString() : rune.ToString());
        }
        var value = normalized.ToString();
        Negative = value.StartsWith('-');
        var unsigned = value.TrimStart('+', '-');
        if (SpecialSyntax.IsMatch(value))
        {
            if (unsigned.StartsWith("inf", StringComparison.OrdinalIgnoreCase)) special = "Infinity";
            else
            {
                var signaling = unsigned.StartsWith('s') || unsigned.StartsWith('S');
                var payload = unsigned[(signaling ? 4 : 3)..].TrimStart('0');
                special = (signaling ? "sNaN" : "NaN") + payload;
            }
            return;
        }
        var match = Syntax.Match(value);
        if (!match.Success) throw new FormatException("Invalid decimal literal");
        var fraction = match.Groups["fraction"].Value;
        var digits = match.Groups["whole"].Value + fraction;
        Coefficient = BigInteger.Parse(digits, CultureInfo.InvariantCulture);
        var exponent = match.Groups["exponent"].Value;
        Exponent = checked((exponent.Length == 0 ? 0 : long.Parse(exponent, CultureInfo.InvariantCulture)) - fraction.Length);
    }
    private PyDecimal(PyDecimal other, bool negative)
    { Coefficient = other.Coefficient; Exponent = other.Exponent; special = other.special; Negative = negative; }

    public bool __bool__() => special is not null || !Coefficient.IsZero;

    public bool IsLessThanZero()
    {
        if (special?.Contains("NaN", StringComparison.Ordinal) == true) throw new ArithmeticException("Invalid decimal comparison");
        return Negative && (special == "Infinity" || !Coefficient.IsZero);
    }
    public PyDecimal Abs() => new(this, false);
    public PyDecimal Negate() => new(this, !Negative);
    public string __str__()
    {
        var sign = Negative ? "-" : "";
        if (special is not null) return sign + special;
        var digits = Coefficient.ToString(CultureInfo.InvariantCulture);
        var adjusted = (BigInteger)Exponent + digits.Length - 1;
        if (Exponent <= 0 && adjusted >= -6)
        {
            var point = checked((int)(digits.Length + Exponent));
            if (Exponent == 0) return sign + digits;
            return sign + (point > 0 ? digits.Insert(point, ".") : "0." + new string('0', -point) + digits);
        }
        var mantissa = digits.Length == 1 ? digits : digits.Insert(1, ".");
        return sign + mantissa + "E" + (adjusted >= 0 ? "+" : "") + adjusted.ToString(CultureInfo.InvariantCulture);
    }
    public string __repr__() => "Decimal('" + __str__() + "')";
    public override string ToString() => __str__();
    private static readonly Regex Syntax = new(@"\A[+-]?(?:(?<whole>[0-9]+)(?:\.(?<fraction>[0-9]*))?|\.(?<fraction>[0-9]+))(?:[eE](?<exponent>[+-]?[0-9]+))?\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex SpecialSyntax = new(@"\A[+-]?(?:inf(?:inity)?|s?nan[0-9]*)\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
}
