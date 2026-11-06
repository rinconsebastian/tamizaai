using System.Globalization;
using Tamiza.Api.Reports;

namespace Tamiza.Api.Indicators;

/// <summary>The base settings that decide what a widget shows (design D7 of add-small-sample-warnings): the threshold in force and whether it shows its n line.</summary>
/// <param name="MinN">The small-base threshold in force; null for off.</param>
/// <param name="ShowN">Whether the widget shows its base n line.</param>
public sealed record BaseSettings(int? MinN, bool ShowN)
{
    /// <summary>No small base and no n line: what a widget without base settings draws.</summary>
    public static readonly BaseSettings None = new(null, false);
}

/// <summary>The names under which a widget writes its figures (design D9): the columns of its ratio and the column its n counts. Null where the widget has none.</summary>
/// <param name="Numerator">The numerator column's name.</param>
/// <param name="Denominator">The denominator column's name.</param>
/// <param name="Base">The declared base column's name, else the denominator's: what the n counts.</param>
public sealed record FigureNames(string? Numerator, string? Denominator, string? Base);

/// <summary>The lines a tooltip adds after a value's own line, and the note of a small base (<see cref="BaseFigures.Tooltip"/>).</summary>
/// <param name="Lines">The label and the value of each line.</param>
/// <param name="Footer">The small-base note, or null.</param>
public sealed record FigureTooltip(IReadOnlyList<(string Label, string Value)> Lines, string? Footer);

/// <summary>
/// The base figures of a widget as the server writes them, for a scheduled email, a widget workbook and the MCP tools (design D7, D9,
/// D11, D13 and D14 of add-small-sample-warnings): the settings in force, the small-base rule, the lines a tooltip adds, the n line, the
/// no-data line and the notes about small bases. Pure, so every surface says the same words. Twin of <c>base-figures.ts</c> in
/// tamiza-ui (<c>indicators/base-figures.ts</c>); both are tested against the cases in
/// <c>indicators/testing/base-figures.json</c>, so change them together. The language of the words is the one of the format's culture
/// (English or Spanish; anything else reads as English), as a scheduled email's are.
/// </summary>
public static class BaseFigures
{
    /// <summary>How many values the no-data line names before it ends with an ellipsis.</summary>
    public const int MaxNoDataNamed = 3;

    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        ["numerator"] = "Numerator",
        ["denominator"] = "Denominator",
        ["nName"] = "n ({0})",
        ["rows"] = "Rows",
        ["smallTooltip"] = "Small base: n = {0} (below {1})",
        ["smallSome"] = "Small base in some series (below {0})",
        ["smallNote"] = "Marks with a base below {0} are dimmed.",
        ["summaryCount"] = ", {0} with a small base",
        ["tableNote"] = "* Base below {0}.",
        ["tableMark"] = "small base",
        ["nColumn"] = "n",
        ["emailNote.one"] = "1 value has a base below {0}, marked *.",
        ["emailNote.other"] = "{0} values have a base below {1}, marked *.",
    };

    private static readonly IReadOnlyDictionary<string, string> Spanish = new Dictionary<string, string>
    {
        ["numerator"] = "Numerador",
        ["denominator"] = "Denominador",
        ["nName"] = "n ({0})",
        ["rows"] = "Filas",
        ["smallTooltip"] = "Base pequeña: n = {0} (menos de {1})",
        ["smallSome"] = "Base pequeña en algunas series (menos de {0})",
        ["smallNote"] = "Las marcas con una base menor que {0} aparecen atenuadas.",
        ["summaryCount"] = ", {0} con base pequeña",
        ["tableNote"] = "* Base menor que {0}.",
        ["tableMark"] = "base pequeña",
        ["nColumn"] = "n",
        ["emailNote.one"] = "1 valor tiene una base menor que {0}, marcado con *.",
        ["emailNote.other"] = "{0} valores tienen una base menor que {1}, marcados con *.",
    };

    /// <summary>The keys of the English and the Spanish words, so a test keeps the two languages in step.</summary>
    public static (IReadOnlyCollection<string> English, IReadOnlyCollection<string> Spanish) Keys => ([.. English.Keys], [.. Spanish.Keys]);

    /// <summary>One of the words, in the language of <paramref name="format"/>'s culture, with its arguments written as the culture writes them.</summary>
    private static string Say(ReportFormat format, string key, params object[] values) =>
        string.Format(format.Culture, (format.Culture.TwoLetterISOLanguageName == "es" ? Spanish : English)[key], values);

    // The settings in force and the small-base rule (design D7)

    /// <summary>
    /// The settings in force for a widget: its own over the dashboard's, or the shown version's. An own threshold of 0 turns it off for the
    /// widget; an absent one follows the dashboard. Twin of <c>settingsInForce</c>.
    /// </summary>
    /// <param name="display">The widget's display, or null.</param>
    /// <param name="dashboardMinN">The dashboard's threshold; null for off.</param>
    /// <param name="dashboardShowN">Whether the dashboard shows each widget's n line.</param>
    public static BaseSettings InForce(WidgetDisplay? display, int? dashboardMinN, bool dashboardShowN)
    {
        var minN = display?.MinN is { } own ? (own >= 1 ? own : (int?)null) : dashboardMinN;
        return new BaseSettings(minN, display?.ShowN ?? dashboardShowN);
    }

    /// <summary>Whether a value has a small base: its n is below the threshold. A value with no n, or with no threshold in force, never has one.</summary>
    public static bool Small(double? n, int? minN) => minN is >= 1 && n is { } value && value < minN;

    // What the indicator declares (design D1, D16)

    /// <summary>The column the indicator declares as its base, or null (at most one is). Twin of <c>baseColumnOf</c>.</summary>
    public static string? BaseColumnOf(IReadOnlyDictionary<string, IndicatorColumnMetadata>? columns) =>
        columns?.FirstOrDefault(entry => entry.Value?.Base is true).Key;

    /// <summary>The columns the indicator declares as answers (<c>categories</c> single or multiple), in the order the map lists them. Twin of <c>answerColumnsOf</c>.</summary>
    public static IReadOnlyList<string> AnswerColumnsOf(IReadOnlyDictionary<string, IndicatorColumnMetadata>? columns) =>
        columns is null
            ? []
            : [.. columns.Where(entry => entry.Value?.Categories is AnswerKinds.Single or AnswerKinds.Multiple).Select(entry => entry.Key)];

    // Names (design D9)

    /// <summary>
    /// The names a widget gives its figures: the columns of its ratio, and the column its n counts, as the dashboard names columns
    /// (<paramref name="name"/> is the widget's own label, else the indicator's, else the column itself).
    /// </summary>
    /// <param name="baseColumn">The column the indicator declares as its base, or null.</param>
    public static FigureNames Names(WidgetRatio? ratio, Func<string, string> name, string? baseColumn) => new(
        ratio is null ? null : name(ratio.Numerator),
        ratio is null ? null : name(ratio.Denominator),
        baseColumn is not null ? name(baseColumn) : ratio is null ? null : name(ratio.Denominator));

    // Tooltips (design D9)

    /// <summary>
    /// What a tooltip says after the value: a ratio's numerator and denominator under their names, and the n under the name of the column
    /// it counts when it is not the denominator shown; then, for a small base, "Small base: n = 16 (below 30)" as the footer. A mark with
    /// no figures adds nothing. A figure is left out when the caller shows it elsewhere (<paramref name="omitNumerator"/>, a map's own
    /// tooltip columns).
    /// </summary>
    public static FigureTooltip Tooltip(
        double? numerator, double? denominator, double? n, FigureNames names, int? minN, ReportFormat format, bool omitNumerator = false, bool omitDenominator = false)
    {
        var lines = new List<(string Label, string Value)>();
        if (numerator is { } top && !omitNumerator)
        {
            lines.Add((names.Numerator ?? Say(format, "numerator"), format.Figure(top)));
        }

        if (denominator is { } bottom && !omitDenominator)
        {
            lines.Add((names.Denominator ?? Say(format, "denominator"), format.Figure(bottom)));
        }

        if (n is { } count && !(denominator is { } same && count == same))
        {
            lines.Add((names.Base is { } name ? Say(format, "nName", name) : "n", format.Figure(count)));
        }

        return new FigureTooltip(lines, SmallTooltip(n, minN, format));
    }

    /// <summary>"Small base: n = 16 (below 30)" for a value with a small base, else null.</summary>
    public static string? SmallTooltip(double? n, int? minN, ReportFormat format) =>
        Small(n, minN) ? Say(format, "smallTooltip", format.Figure(n!.Value), format.Figure(minN!.Value)) : null;

    /// <summary>The value of one series in the tooltip of a group that lists several: its own n only, "50% (n = 16)"; a value with no n is written as it is.</summary>
    public static string SeriesValueWithN(string value, double? n, ReportFormat format) => n is { } count ? $"{value} (n = {format.Figure(count)})" : value;

    /// <summary>The note of a tooltip that lists several series when one of them has a small base.</summary>
    public static string SmallSome(int minN, ReportFormat format) => Say(format, "smallSome", format.Figure(minN));

    /// <summary>A histogram's bar in a tooltip: the label of its count, "Rows". It has no numerator, denominator or n.</summary>
    public static string HistogramRows(ReportFormat format) => Say(format, "rows");

    // Lines and notes (design D11, D13)

    /// <summary>The base n line under a widget: "n = 223 Hogares que asistieron"; null when the widget has no n. <paramref name="name"/> is the column the n counts, if it has a name.</summary>
    public static string? NLine(double? n, string? name, ReportFormat format) =>
        n is { } count ? (string.IsNullOrEmpty(name) ? $"n = {format.Figure(count)}" : $"n = {format.Figure(count)} {name}") : null;

    /// <summary>The note under a chart while a mark is dimmed: "Marks with a base below 30 are dimmed."</summary>
    public static string SmallNote(int minN, ReportFormat format) => Say(format, "smallNote", format.Figure(minN));

    /// <summary>The words a chart's summary adds while marks are dimmed: ", 2 with a small base". Empty while none is.</summary>
    public static string SmallSummary(int count, ReportFormat format) => count > 0 ? Say(format, "summaryCount", count) : "";

    /// <summary>The note under a table that marks small bases: "* Base below 30."</summary>
    public static string SmallTableNote(int minN, ReportFormat format) => Say(format, "tableNote", format.Figure(minN));

    /// <summary>What a visually hidden mark says for a small base: "small base".</summary>
    public static string SmallMark(ReportFormat format) => Say(format, "tableMark");

    /// <summary>The header of the n column of a table: "n".</summary>
    public static string NColumn(ReportFormat format) => Say(format, "nColumn");

    /// <summary>The sentence a scheduled email's notes carry for the values it marks with "*": "2 values have a base below 30, marked *." Null while none is marked.</summary>
    public static string? SmallValuesNote(int count, int minN, ReportFormat format) =>
        count <= 0 ? null : count == 1 ? Say(format, "emailNote.one", format.Figure(minN)) : Say(format, "emailNote.other", count, format.Figure(minN));

    /// <summary>
    /// The line under a widget that left out the values its indicator declares as no data: <c>{values}: {value}</c>, with <c> ({share})</c>
    /// for a measure and <c> · n = {n}</c> while the base n line shows. At most <see cref="MaxNoDataNamed"/> values are named, then "…".
    /// <paramref name="value"/> writes the value as the widget writes values, with its own unit when it has one; the share is a percent
    /// with at most one decimal. Null when nothing was left out.
    /// </summary>
    public static string? NoDataLine(IndicatorNoData? noData, Func<double, string> value, bool showN, ReportFormat format)
    {
        if (noData is not { Values.Count: > 0 })
        {
            return null;
        }

        var named = noData.Values.Count > MaxNoDataNamed
            ? string.Join(", ", noData.Values.Take(MaxNoDataNamed)) + ", …"
            : string.Join(", ", noData.Values);
        var written = noData.Value is { } measured ? $": {value(measured)}" : "";
        var share = noData.Share is { } part ? $" ({format.Share(part)})" : "";
        var n = showN && noData.N is { } count ? $" · n = {format.Figure(count)}" : "";
        return named + written + share + n;
    }
}
