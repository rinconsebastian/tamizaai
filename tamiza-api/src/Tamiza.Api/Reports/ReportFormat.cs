using System.Globalization;
using System.Text.RegularExpressions;

namespace Tamiza.Api.Reports;

/// <summary>
/// How a scheduled email writes values, dates and times, in its regional format and the project's time zone (design D8). The values
/// follow the rules of "Indicator formatting", as the UI's <c>valueFormatter</c> (tamiza-ui <c>indicators/format.ts</c>) does: keep
/// them in step. Small differences from the browser's <c>Intl</c> are accepted, such as a currency written with its code.
/// </summary>
public sealed partial class ReportFormat(CultureInfo culture, TimeZoneInfo timeZone)
{
    public const string Count = "count";
    public const string Decimal = "decimal";
    public const string Percent = "percent";
    public const string Currency = "currency";

    public CultureInfo Culture { get; } = culture;

    public TimeZoneInfo TimeZone { get; } = timeZone;

    /// <summary>The project's time zone, or UTC for a name this system does not know.</summary>
    public static TimeZoneInfo ZoneOf(string timeZone) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone) ? zone : TimeZoneInfo.Utc;

    /// <summary>A plotted value with its indicator's format and unit: what a stat tile, a table cell or a tooltip shows.</summary>
    public string Value(double value, string? format, string? unit)
    {
        unit = unit?.Trim() ?? "";
        return format switch
        {
            Count => After(value.ToString("N0", Culture), unit),
            // A unit of "%" would only repeat the sign.
            Percent => After(PercentText(value, 2), unit == "%" ? "" : unit),
            Currency => CurrencyText(value, unit),
            _ => After(value.ToString("N2", Culture), unit),
        };
    }

    /// <summary>A value with the indicator's format but without its unit, as a histogram's ranges are written.</summary>
    public string Plain(double value, string? format) =>
        format == Percent ? PercentText(value, 2) : value.ToString("#,##0.##", Culture);

    /// <summary>A whole number, such as a count of rows.</summary>
    public string Whole(double value) => value.ToString("N0", Culture);

    /// <summary>A share of a total (a pie chart's), with at most one decimal.</summary>
    public string Share(double share) => PercentText(share, 1);

    /// <summary>
    /// A number of a tooltip column: with the column's own format when it has one, otherwise as a whole number for an integer column
    /// and with up to two decimals for a decimal column.
    /// </summary>
    public string TooltipNumber(double value, string? format, bool integer) => format switch
    {
        Count => value.ToString("N0", Culture),
        Decimal => value.ToString("N2", Culture),
        Percent => PercentText(value, 2),
        _ => value.ToString(integer ? "#,##0" : "#,##0.##", Culture),
    };

    /// <summary>The value times 100 with at most <paramref name="decimals"/> decimals and a percent sign, as the UI shows it in every region.</summary>
    private string PercentText(double fraction, int decimals) =>
        (fraction * 100).ToString(decimals == 1 ? "#,##0.#" : "#,##0.##", Culture) + "%";

    /// <summary>A currency code is written before the number; a symbol such as $ sits against it, and a word such as pesos is set apart.</summary>
    private string CurrencyText(double value, string unit)
    {
        var number = value.ToString("N2", Culture);
        if (unit.Length == 0)
        {
            return number;
        }

        return CurrencyCode().IsMatch(unit) || Letter().IsMatch(unit) ? $"{unit} {number}" : unit + number;
    }

    private static string After(string text, string unit) => unit.Length == 0 ? text : $"{text} {unit}";

    /// <summary>A day that names the month: <c>Oct 28, 2026</c> in English, <c>28 oct 2026</c> in Spanish.</summary>
    public string Day(DateOnly day)
    {
        var month = Culture.DateTimeFormat.GetAbbreviatedMonthName(day.Month).TrimEnd('.');
        return Culture.TwoLetterISOLanguageName == "es"
            ? $"{day.Day} {month} {day.Year}"
            : $"{month} {day.Day}, {day.Year}";
    }

    /// <summary>The day of <paramref name="moment"/> in the project's time zone.</summary>
    public DateOnly LocalDay(DateTimeOffset moment) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, TimeZone).DateTime);

    /// <summary>A moment to the minute in the project's time zone, naming the month, followed by the zone's name.</summary>
    public string Moment(DateTimeOffset moment, bool withZone = true)
    {
        var local = TimeZoneInfo.ConvertTime(moment, TimeZone);
        var text = $"{Day(DateOnly.FromDateTime(local.DateTime))}, {local.ToString(Culture.DateTimeFormat.ShortTimePattern, Culture)}";
        return withZone ? $"{text} ({TimeZone.Id})" : text;
    }

    /// <summary>A date column's ISO value, written as a day; text that is not one stays as it is.</summary>
    public string DateText(string iso) =>
        DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? Day(day) : iso;

    /// <summary>A timestamp column's ISO value, written to the minute in the project's time zone; text that is not one stays as it is.</summary>
    public string TimestampText(string iso) =>
        DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var moment) ? Moment(moment, withZone: false) : iso;

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyCode();

    [GeneratedRegex(@"\p{L}")]
    private static partial Regex Letter();
}
