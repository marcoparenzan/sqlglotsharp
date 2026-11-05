using System.Globalization;
using System.Text.RegularExpressions;

namespace SqlGlotSharp;

public static partial class helper
{
    // py: sqlglot/helper.py::is_iso_date #cab7ba3d79
    public static bool is_iso_date(string text) => ParseIsoDate(text);

    // py: sqlglot/helper.py::is_iso_datetime #e1174a9dc8
    public static bool is_iso_datetime(string text)
    {
        if (ParseIsoDate(text)) return true;
        var match = IsoDateTimeSyntax.Match(text);
        if (!match.Success || !ParseIsoDate(match.Groups["date"].Value)) return false;
        if (!ValidTime(match.Groups["time"].Value, false)) return false;
        var offset = match.Groups["offset"].Value;
        return offset.Length == 0 || offset == "Z" || ValidTime(offset[1..], true);
    }

    internal static string? IsoTimeComponent(string text)
    {
        if (!is_iso_datetime(text)) return null;
        var match = IsoDateTimeSyntax.Match(text);
        return match.Success ? match.Groups["time"].Value : "";
    }

    private static bool ParseIsoDate(string text)
    {
        if (DateOnly.TryParseExact(text, new[] { "yyyy-MM-dd", "yyyyMMdd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return true;
        var match = IsoWeekSyntax.Match(text);
        if (!match.Success) return false;
        var year = int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);
        var week = int.Parse(match.Groups["week"].Value, CultureInfo.InvariantCulture);
        var dayText = match.Groups["day"].Value;
        var day = dayText.Length == 0 ? 1 : int.Parse(dayText, CultureInfo.InvariantCulture);
        if (year is < 1 or > 9999 || week < 1 || week > ISOWeek.GetWeeksInYear(year) || day is < 1 or > 7) return false;
        try { _ = ISOWeek.ToDateTime(year, week, day == 7 ? DayOfWeek.Sunday : (DayOfWeek)day); return true; }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    private static bool ValidTime(string text, bool offset)
    {
        var match = IsoTimeSyntax.Match(text);
        if (!match.Success) return false;
        var hour = int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture);
        var minutes = match.Groups["minute"].Value;
        var seconds = match.Groups["second"].Value;
        var minute = minutes.Length == 0 ? 0 : int.Parse(minutes, CultureInfo.InvariantCulture);
        var second = seconds.Length == 0 ? 0 : int.Parse(seconds, CultureInfo.InvariantCulture);
        if (offset) return hour * 3600 + minute * 60 + second < 24 * 3600;
        return hour < 24 && minute < 60 && second < 60;
    }

    private static readonly Regex IsoWeekSyntax = new(
        @"\A(?<year>[0-9]{4})(?:-W(?<week>[0-9]{2})(?:-(?<day>[0-9]))?|W(?<week>[0-9]{2})(?<day>[0-9])?)\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex IsoDateTimeSyntax = new(
        @"\A(?<date>[0-9]{4}-[0-9]{2}-[0-9]{2}|[0-9]{8}|[0-9]{4}-W[0-9]{2}(?:-[0-9])?|[0-9]{4}W[0-9]{2}[0-9]?)(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|.)(?<time>[0-9:.,]+)(?<offset>Z|[+-][0-9:.,]+)?\z",
        RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromSeconds(1));
    private static readonly Regex IsoTimeSyntax = new(
        @"\A(?<hour>[0-9]{2})(?:(?<sep>:?)(?<minute>[0-9]{2})(?:\k<sep>(?<second>[0-9]{2}))?)?(?:[.,][0-9]+)?\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
}
