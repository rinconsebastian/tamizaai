using System.Globalization;
using Tamiza.Api.Reports;

namespace Tamiza.Api.UnitTests.Reports;

/// <summary>The rules of "Indicator formatting" in the email's regional format (design D8).</summary>
public sealed class ReportFormatTests
{
    private static ReportFormat Format(string locale, string zone = "UTC") =>
        new(CultureInfo.GetCultureInfo(locale), ReportFormat.ZoneOf(zone));

    [Fact]
    public void Spanish_of_Colombia() => Assert.Equal("12.345,50", Format("es-CO").Value(12345.5, "decimal", null));

    [Fact]
    public void Percentage_in_Spanish_of_Colombia() => Assert.Equal("45,5%", Format("es-CO").Value(0.455, "percent", null));

    [Fact]
    public void Value_formatted() => Assert.Equal("42%", Format("en-US").Value(0.42, "percent", null));

    [Fact]
    public void A_unit_of_percent_is_not_repeated() => Assert.Equal("42%", Format("en-US").Value(0.42, "percent", "%"));

    [Fact]
    public void Count_with_a_unit() => Assert.Equal("12,450 households", Format("en-US").Value(12450, "count", "households"));

    [Fact]
    public void A_count_is_rounded_to_a_whole_number() => Assert.Equal("12.346", Format("es-CO").Value(12345.5, "count", null));

    [Fact]
    public void An_unknown_or_missing_format_is_a_decimal() => Assert.Equal("1,234.50", Format("en-US").Value(1234.5, null, null));

    [Theory]
    [InlineData("COP", "COP 1,234.50")]
    [InlineData("pesos", "pesos 1,234.50")]
    [InlineData("$", "$1,234.50")]
    [InlineData(null, "1,234.50")]
    public void Currency_writes_its_unit_before_the_value(string? unit, string expected) =>
        Assert.Equal(expected, Format("en-US").Value(1234.5, "currency", unit));

    [Fact]
    public void Plain_values_have_at_most_two_decimals_and_no_unit()
    {
        Assert.Equal("10", Format("en-US").Plain(10, "count"));
        Assert.Equal("10.25", Format("en-US").Plain(10.254, "decimal"));
        Assert.Equal("12.5%", Format("en-US").Plain(0.125, "percent"));
    }

    [Fact]
    public void A_share_has_at_most_one_decimal() => Assert.Equal("33,3%", Format("es-CO").Share(1d / 3));

    [Fact]
    public void Subject_date() => Assert.Equal("Oct 28, 2026", Format("en-US").Day(new DateOnly(2026, 10, 28)));

    [Fact]
    public void A_spanish_day_names_the_month_without_a_trailing_dot() => Assert.Equal("28 oct 2026", Format("es-CO").Day(new DateOnly(2026, 10, 28)));

    [Fact]
    public void A_moment_is_written_in_the_project_time_zone_to_the_minute_with_the_zone()
    {
        var text = Format("en-US", "America/Bogota").Moment(new DateTimeOffset(2026, 10, 28, 12, 0, 30, TimeSpan.Zero));

        Assert.StartsWith("Oct 28, 2026, 7:00", text);
        Assert.EndsWith("(America/Bogota)", text);
        Assert.DoesNotContain(":30", text);
    }

    [Fact]
    public void The_local_day_follows_the_project_time_zone() =>
        Assert.Equal(new DateOnly(2026, 10, 27), Format("en-US", "America/Bogota").LocalDay(new DateTimeOffset(2026, 10, 28, 3, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void An_unknown_time_zone_reads_as_utc() => Assert.Equal(TimeZoneInfo.Utc, ReportFormat.ZoneOf("Mars/Olympus"));

    [Fact]
    public void Tooltip_numbers_use_their_own_format()
    {
        var format = Format("en-US");

        Assert.Equal("20", format.TooltipNumber(20, null, integer: true));
        Assert.Equal("2.5", format.TooltipNumber(2.5, null, integer: false));
        Assert.Equal("20.00", format.TooltipNumber(20, "decimal", integer: true));
        Assert.Equal("45%", format.TooltipNumber(0.45, "percent", integer: false));
    }

    [Fact]
    public void Dates_and_timestamps_of_tooltip_columns()
    {
        var format = Format("en-US", "America/Bogota");

        Assert.Equal("Oct 1, 2026", format.DateText("2026-10-01"));
        Assert.Equal("Oct 1, 2026, 7:00 AM", format.TimestampText("2026-10-01T12:00:00Z"));
        Assert.Equal("not a date", format.DateText("not a date"));
    }
}
