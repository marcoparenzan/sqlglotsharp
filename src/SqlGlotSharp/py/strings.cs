using System.Collections;
using System.Globalization;
using System.Text;

namespace SqlGlotSharp;

public static partial class py
{
    public static string str(object? value) => value is string text ? text : value is IPyString custom ? custom.__str__() : repr(value);
    public static string repr(object? value) => Repr(value, new HashSet<object>(ReferenceEqualityComparer.Instance));

    private static string Repr(object? value, HashSet<object> active)
    {
        switch (value)
        {
            case null: return "None";
            case IPyRepresentation custom: return custom.__repr__();
            case bool b: return b ? "True" : "False";
            case string text: return Quote(text);
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                return ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture);
            case System.Numerics.BigInteger integer: return integer.ToString(CultureInfo.InvariantCulture);
            case double number: return FloatString(number);
            case float number: return FloatString(number);
            case IPyMapping mapping:
                return MappingRepr(value, mapping.repr_items(), active);
            case IDictionary dictionary:
                return MappingRepr(value, DictionaryItems(dictionary), active);
            case System.Runtime.CompilerServices.ITuple tuple:
                if (!active.Add(value)) return "(...)";
                try
                {
                    var items = Enumerable.Range(0, tuple.Length).Select(i => Repr(tuple[i], active));
                    return "(" + string.Join(", ", items) + (tuple.Length == 1 ? "," : "") + ")";
                }
                finally { active.Remove(value); }
            case IEnumerable set when value.GetType().GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISet<>)):
                if (!active.Add(value)) return "set(...)";
                try
                {
                    var items = set.Cast<object?>().Select(item => Repr(item, active)).ToArray();
                    return items.Length == 0 ? "set()" : "{" + string.Join(", ", items) + "}";
                }
                finally { active.Remove(value); }
            case IList list:
                if (!active.Add(list)) return "[...]";
                try { return "[" + string.Join(", ", list.Cast<object?>().Select(item => Repr(item, active))) + "]"; }
                finally { active.Remove(list); }
            default:
                throw new NotPortedException($"py.repr:{value.GetType().Name}");
        }
    }

    private static IEnumerable<(object? Key, object? Value)> DictionaryItems(IDictionary dictionary)
    {
        var iterator = dictionary.GetEnumerator();
        try { while (iterator.MoveNext()) yield return (iterator.Key, iterator.Value); }
        finally { (iterator as IDisposable)?.Dispose(); }
    }

    private static string MappingRepr(object mapping, IEnumerable<(object? Key, object? Value)> items, HashSet<object> active)
    {
        if (!active.Add(mapping)) return "{...}";
        try { return "{" + string.Join(", ", items.Select(p => Repr(p.Key, active) + ": " + Repr(p.Value, active))) + "}"; }
        finally { active.Remove(mapping); }
    }

    private static string Quote(string text)
    {
        var quote = text.Contains('\'') && !text.Contains('"') ? '"' : '\'';
        var output = new StringBuilder().Append(quote);
        foreach (var rune in text.EnumerateRunes())
        {
            var n = rune.Value;
            if (n == quote || n == '\\') output.Append('\\').Append((char)n);
            else if (n == '\n') output.Append(@"\n");
            else if (n == '\r') output.Append(@"\r");
            else if (n == '\t') output.Append(@"\t");
            else if (n != 32 && Rune.GetUnicodeCategory(rune) is
                UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate or
                UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned or UnicodeCategory.SpaceSeparator or
                UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            {
                output.Append(n <= 255 ? @"\x" : n <= 65535 ? @"\u" : @"\U");
                output.Append(n.ToString(n <= 255 ? "x2" : n <= 65535 ? "x4" : "x8", CultureInfo.InvariantCulture));
            }
            else output.Append(rune.ToString());
        }
        return output.Append(quote).ToString();
    }

    private static string FloatString(double value)
    {
        if (double.IsNaN(value)) return "nan";
        if (double.IsPositiveInfinity(value)) return "inf";
        if (double.IsNegativeInfinity(value)) return "-inf";
        if (value == 0) return double.IsNegative(value) ? "-0.0" : "0.0";
        var sign = value < 0 ? "-" : "";
        var raw = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture).ToLowerInvariant();
        var e = raw.IndexOf('e');
        var exponent = e < 0 ? 0 : int.Parse(raw[(e + 1)..], CultureInfo.InvariantCulture);
        var mantissa = e < 0 ? raw : raw[..e];
        var dot = mantissa.IndexOf('.');
        var point = (dot < 0 ? mantissa.Length : dot) + exponent;
        var digits = mantissa.Replace(".", "");
        while (digits.Length > 1 && digits[0] == '0') { digits = digits[1..]; point--; }
        var scientificExponent = point - 1;
        if (scientificExponent is >= -4 and < 16)
        {
            var fixedValue = point <= 0 ? "0." + new string('0', -point) + digits :
                point >= digits.Length ? digits + new string('0', point - digits.Length) + ".0" : digits.Insert(point, ".");
            return sign + fixedValue;
        }
        digits = digits.TrimEnd('0');
        var scientific = digits.Length == 1 ? digits : digits.Insert(1, ".");
        return sign + scientific + "e" + (scientificExponent < 0 ? "-" : "+") +
            Math.Abs(scientificExponent).ToString("D2", CultureInfo.InvariantCulture);
    }
}
