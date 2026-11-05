namespace SqlGlotSharp.Tests;

public class IsoHelperTests
{
    [Theory]
    [InlineData("2024-02-29", true)]
    [InlineData("20240229", true)]
    [InlineData("2020-W53-7", true)]
    [InlineData("2020W537", true)]
    [InlineData("2024-W01", true)]
    [InlineData("2023-02-29", false)]
    [InlineData("2021-W53-1", false)]
    [InlineData("2024-W01-8", false)]
    [InlineData("0000-01-01", false)]
    [InlineData("9999-W52-7", false)]
    [InlineData("2024-1-1", false)]
    [InlineData("2024-01-01 ", false)]
    public void Date_validation_handles_calendar_and_week_dates(string text, bool expected) => Assert.Equal(expected, helper.is_iso_date(text));

    [Theory]
    [InlineData("2024-01-01", true)]
    [InlineData("2024-01-01T12:34:56.123+02:30", true)]
    [InlineData("20240101T123456Z", true)]
    [InlineData("2024-W01-1T12", true)]
    [InlineData("2024-01-01x12:34", true)]
    [InlineData("2024-01-01😀12:34", true)]
    [InlineData("2024-01-01T12,5", true)]
    [InlineData("2024-01-01T24:00", false)]
    [InlineData("2024-01-01T12:60", false)]
    [InlineData("2024-01-01T12:34:60", false)]
    [InlineData("2024-01-01T12:3456", false)]
    [InlineData("2024-01-01T12:34+24:00", false)]
    [InlineData("2024-01-01T", false)]
    [InlineData("01/02/2024 12:34", false)]
    public void Datetime_validation_uses_iso_syntax_not_local_culture(string text, bool expected) => Assert.Equal(expected, helper.is_iso_datetime(text));
}
