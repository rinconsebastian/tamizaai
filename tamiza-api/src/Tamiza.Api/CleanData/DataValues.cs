using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Tamiza.Api.CleanData;

/// <summary>
/// Turns what PostgreSQL returns as text into the values the dashboard shows (design D3). Dates, timestamps and numbers are
/// selected as text so PostgreSQL does the work of its own zones and ranges, and .NET never has to represent a value it cannot.
/// </summary>
public static partial class DataValues
{
    /// <summary>Longest text a page shows; longer text is cut and marked.</summary>
    public const int MaxDisplayLength = 500;

    /// <summary>What the page query selects for a text column, so a very long value never reaches the API whole.</summary>
    public const int FetchLength = MaxDisplayLength + 1;

    [GeneratedRegex(@"^(?<date>\d{4,}-\d{2}-\d{2}) (?<time>\d{2}:\d{2}:\d{2}(?:\.\d+)?)(?:(?<sign>[+-])(?<h>\d{2})(?::(?<m>\d{2}))?(?::\d{2})?)?$")]
    private static partial Regex Timestamp();

    /// <summary>
    /// <c>2026-09-01 21:30:00-05</c> as <c>2026-09-01T21:30:00-05:00</c>. A timestamp without an offset keeps none, and
    /// <c>infinity</c> or a date with an era (<c>BC</c>) is returned as PostgreSQL wrote it.
    /// </summary>
    public static string IsoTimestamp(string postgres)
    {
        var match = Timestamp().Match(postgres);
        if (!match.Success)
        {
            return postgres;
        }

        var value = $"{match.Groups["date"].Value}T{match.Groups["time"].Value}";
        if (!match.Groups["sign"].Success)
        {
            return value;
        }

        var minutes = match.Groups["m"].Success ? match.Groups["m"].Value : "00";
        return $"{value}{match.Groups["sign"].Value}{match.Groups["h"].Value}:{minutes}";
    }

    /// <summary>
    /// A number selected as text, as a <see cref="decimal"/> when it fits one and otherwise as the text PostgreSQL wrote
    /// (<c>NaN</c>, <c>Infinity</c>, or a value beyond 28 digits).
    /// </summary>
    public static object Number(string postgres) =>
        decimal.TryParse(postgres, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : postgres;

    /// <summary>The text to show: the value as it is when it has at most 500 characters, otherwise its first 500, and whether it was cut.</summary>
    public static (string Text, bool Cut) Display(string fetched) =>
        Length(fetched) > MaxDisplayLength ? (Truncate(fetched, MaxDisplayLength), true) : (fetched, false);

    /// <summary>The characters of <paramref name="text"/> counted as PostgreSQL counts them: code points.</summary>
    public static int Length(string text) => text.EnumerateRunes().Count();

    /// <summary>At most <paramref name="max"/> code points of <paramref name="text"/>.</summary>
    public static string Truncate(string text, int max)
    {
        if (text.Length <= max)
        {
            return text;
        }

        var builder = new StringBuilder();
        var count = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (count++ == max)
            {
                break;
            }

            builder.Append(rune.ToString());
        }

        return builder.ToString();
    }
}
