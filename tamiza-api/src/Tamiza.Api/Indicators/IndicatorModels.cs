using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Tamiza.Api.CleanData;

namespace Tamiza.Api.Indicators;

/// <summary>The kinds of widget, as stored in <c>dashboard_widgets.chart_type</c> and sent as <c>chartType</c>.</summary>
public static class ChartTypes
{
    public const string Stat = "stat";
    public const string Bar = "bar";
    public const string Column = "column";
    public const string Line = "line";
    public const string Histogram = "histogram";
    public const string Pie = "pie";
    public const string Map = "map";
    public const string Heatmap = "heatmap";
    public const string Pyramid = "pyramid";
    public const string Likert = "likert";
    public const string Crosstab = "crosstab";
    public const string Boxplot = "boxplot";

    public static readonly IReadOnlyList<string> All = [Stat, Bar, Column, Line, Histogram, Pie, Map, Heatmap, Pyramid, Likert, Crosstab, Boxplot];

    public static bool IsKnown(string? chartType) => chartType is not null && All.Contains(chartType);

    /// <summary>The charts that group their rows: they need a column to group by. A stat tile, a histogram and a box plot may plot the rows as they are.</summary>
    public static bool NeedsGroup(string chartType) => chartType is Bar or Column or Line or Pie or Map or Heatmap or Pyramid or Likert or Crosstab;

    /// <summary>The charts that may split each group by a series column.</summary>
    public static bool TakesSeries(string chartType) => chartType is Bar or Column or Line;

    /// <summary>The charts that are made of a series column: they cannot be saved without one.</summary>
    public static bool NeedsSeries(string chartType) => chartType is Heatmap or Pyramid or Likert or Crosstab;

    /// <summary>The charts whose query has a series column when it names one.</summary>
    public static bool HasSeries(string chartType) => TakesSeries(chartType) || NeedsSeries(chartType);

    /// <summary>
    /// The charts that may leave out the values their indicator declares as meaning no data (<see cref="WidgetConfig.MissingFooter"/>,
    /// design D6 of add-small-sample-warnings): those with a group column that a mark stands for. Twin of <c>takesMissingFooter</c>.
    /// </summary>
    public static bool TakesMissingFooter(string chartType) => chartType is Bar or Column or Line or Pie or Heatmap or Pyramid or Likert or Crosstab;
}

/// <summary>A widget's size and position on the 12-column grid, in grid units.</summary>
public sealed record WidgetLayout(int X, int Y, int W, int H);

/// <summary>The ratio of the sums of two numeric columns, such as a rate: <c>sum(numerator) / sum(denominator)</c>.</summary>
public sealed record WidgetRatio(string Numerator, string Denominator);

/// <summary>The values of <see cref="WidgetSort"/>, stored as strings: the endpoints' JSON options have no enum converter.</summary>
public static class SortValues
{
    public const string ByValue = "value";
    public const string ByColumn = "column";
    public const string Text = "text";
    public const string Number = "number";
    public const string Asc = "asc";
    public const string Desc = "desc";
}

/// <summary>The layouts of a bar or column widget with a series (<see cref="WidgetConfig.Layout"/>), stored as strings.</summary>
public static class SeriesLayouts
{
    public const string Grouped = "grouped";
    public const string Stacked = "stacked";
    public const string Percent = "percent";

    public static readonly IReadOnlyList<string> All = [Grouped, Stacked, Percent];

    /// <summary>The layouts that add the series up inside a bar: they plot a measure, never a ratio.</summary>
    public static bool Adds(string? layout) => layout is Stacked or Percent;
}

/// <summary>The orientations of a box plot (<see cref="WidgetDisplay.Orientation"/>), stored as strings. Vertical is what a widget with none is drawn as.</summary>
public static class BoxOrientations
{
    public const string Vertical = "vertical";
    public const string Horizontal = "horizontal";

    public static readonly IReadOnlyList<string> All = [Vertical, Horizontal];

    public static bool IsKnown(string? orientation) => orientation is not null && All.Contains(orientation);
}

/// <summary>The kinds of a reference line (<see cref="WidgetReferenceLine.Kind"/>), stored as strings.</summary>
public static class ReferenceLineKinds
{
    public const string Mean = "mean";
    public const string Median = "median";
    public const string Overall = "overall";
    public const string Value = "value";

    public static readonly IReadOnlyList<string> All = [Mean, Median, Overall, Value];

    public static bool IsKnown(string? kind) => kind is not null && All.Contains(kind);
}

/// <summary>How a line chart joins its points (<see cref="WidgetDisplay.Interpolation"/>), stored as strings. Linear is what a widget with none is drawn as.</summary>
public static class LineInterpolations
{
    public const string Linear = "linear";
    public const string Step = "step";

    public static readonly IReadOnlyList<string> All = [Linear, Step];

    public static bool IsKnown(string? interpolation) => interpolation is not null && All.Contains(interpolation);
}

/// <summary>
/// The statistics a stat tile can take of its matching rows (<see cref="WidgetConfig.Statistic"/>, design D2 of improve-stat-cards), stored
/// as strings: the median, the first and third quartiles (25th and 75th percentiles) and the mean.
/// </summary>
public static class StatStatistics
{
    public const string Median = "median";
    public const string P25 = "p25";
    public const string P75 = "p75";
    public const string Mean = "mean";

    public static readonly IReadOnlyList<string> All = [Median, P25, P75, Mean];

    public static bool IsKnown(string? statistic) => statistic is not null && All.Contains(statistic);
}

/// <summary>The secondary lines a stat tile can show under what it plots (<see cref="WidgetDisplay.Secondary"/>, design D3 of improve-stat-cards), stored as strings.</summary>
public static class StatSecondaryLines
{
    public const string N = "n";
    public const string Unfiltered = "unfiltered";

    public static readonly IReadOnlyList<string> All = [N, Unfiltered];

    public static bool IsKnown(string? line) => line is not null && All.Contains(line);
}

/// <summary>The side of a Likert scale's value (<see cref="LikertStep.Side"/>), stored as strings.</summary>
public static class LikertSides
{
    public const string Negative = "negative";
    public const string Neutral = "neutral";
    public const string Positive = "positive";

    public static readonly IReadOnlyList<string> All = [Negative, Neutral, Positive];

    /// <summary>The position of a side along the scale: negatives, then the neutral value, then positives.</summary>
    public static int Rank(string? side) => side switch { Negative => 0, Neutral => 1, Positive => 2, _ => -1 };
}

/// <summary>The two values of a pyramid's series column: the one drawn to the left of the axis and the one drawn to the right.</summary>
public sealed record WidgetPyramid(string? Left, string? Right);

/// <summary>One value of a Likert scale and the side of the neutral line it is drawn on.</summary>
public sealed record LikertStep(string? Value, string? Side);

/// <summary>
/// The order of a bar or column widget. <paramref name="By"/> is <c>value</c> (the plotted value) or <c>column</c>;
/// <paramref name="CompareAs"/> is <c>text</c> or <c>number</c> for a text, integer or decimal column, and null otherwise.
/// </summary>
public sealed record WidgetSort(string? By, string? Column, string? CompareAs, string? Direction)
{
    public WidgetSort Normalize() => this with
    {
        Column = string.IsNullOrEmpty(Column) ? null : Column,
        CompareAs = string.IsNullOrEmpty(CompareAs) ? null : CompareAs,
    };
}

/// <summary>The values of <see cref="WidgetTooltipItem.Format"/>, stored as strings: the endpoints' JSON options have no enum converter for the stored config.</summary>
public static class TooltipFormats
{
    public const string Count = "count";
    public const string Decimal = "decimal";
    public const string Percent = "percent";

    public static readonly IReadOnlyList<string> All = [Count, Decimal, Percent];
}

/// <summary>The basemap kinds a map widget can save (<see cref="WidgetConfig.Basemap"/>); a kind, not a provider.</summary>
public static class BasemapKinds
{
    public const string Streets = "streets";
    public const string Satellite = "satellite";

    public static readonly IReadOnlyList<string> All = [Streets, Satellite];
}

/// <summary>
/// A column a map's tooltip shows. <paramref name="Label"/> replaces the column name when set; <paramref name="Format"/> is one of
/// <see cref="TooltipFormats"/> for a numeric column, or null.
/// </summary>
public sealed record WidgetTooltipItem(string Column, string? Label = null, string? Format = null)
{
    public WidgetTooltipItem Normalize() => this with
    {
        Label = string.IsNullOrWhiteSpace(Label) ? null : Label.Trim(),
        Format = string.IsNullOrWhiteSpace(Format) ? null : Format,
    };
}

/// <summary>
/// The line that names what a widget plots (design D1 of the widget context): its own <paramref name="Text"/> in place of the
/// indicator's name, or <paramref name="Hidden"/> to take the line out of sight. Both left out is the default line, and a text with
/// <c>hidden: true</c> is refused. Each member is left out of the JSON while it is not set.
/// </summary>
/// <param name="Text">1 to <see cref="WidgetDisplay.MaxCaptionLength"/> characters once trimmed, with no line break.</param>
/// <param name="Hidden"><c>true</c> takes the line out of sight; <c>false</c> keeps it in sight under the widget's subtitle (design D2 of
/// improve-widget-text-hierarchy); left out, the line is in sight unless a bar, column, line or pie widget has a subtitle.</param>
public sealed record WidgetCaption(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("The caption's own text, 1 to 200 characters with no line break; it replaces the indicator's name wherever the widget names what it plots.")] string? Text = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("true takes the caption line out of sight, false keeps it in sight under the widget's subtitle; left out, a bar, column, line or pie widget with a subtitle shows its own caption text in its foot instead of above the chart. Tooltips and tables still name what is plotted. true is not taken together with text.")] bool? Hidden = null)
{
    /// <summary>The caption as saved: a text is trimmed and a blank one is none, <c>hidden</c> is kept as given (<c>false</c> included), and a caption with neither is none (null).</summary>
    public WidgetCaption? Normalize()
    {
        var text = WidgetDisplay.NormalizeText(Text);
        return text is null && Hidden is null ? null : new WidgetCaption(text, Hidden);
    }

    /// <summary>
    /// The caption's own text when a subtitle takes it out of sight and the widget's foot shows it instead (design D2 of
    /// improve-widget-text-hierarchy), else null. That is a bar, column or line chart with no series, or a pie, that has a subtitle and an own
    /// caption text and leaves <c>hidden</c> unset: <c>true</c> hides the line and <c>false</c> keeps it in sight. Twin of the UI's
    /// <c>captionInFoot</c>; both are tested against <c>testing/caption-in-foot.json</c>.
    /// </summary>
    public static string? TextInFoot(string chartType, WidgetConfig config)
    {
        var onChart = chartType is ChartTypes.Pie || (ChartTypes.TakesSeries(chartType) && string.IsNullOrEmpty(config.SeriesBy));
        var display = config.Display;
        if (!onChart || display?.Caption is not { Hidden: null } caption || WidgetDisplay.NormalizeText(display.Subtitle) is null)
        {
            return null;
        }

        return WidgetDisplay.NormalizeText(caption.Text);
    }

    /// <summary>Whether <see cref="TextInFoot"/> has a text for the widget.</summary>
    public static bool InFoot(string chartType, WidgetConfig config) => TextInFoot(chartType, config) is not null;
}

/// <summary>
/// One reference line of a bar, column or line widget (design D1 of the chart annotations). Both optional members are left out of the
/// JSON while they are not set, so a mean line is stored as <c>{"kind":"mean"}</c>.
/// </summary>
/// <param name="Kind">One of <see cref="ReferenceLineKinds"/>.</param>
/// <param name="Value">A <c>value</c> line only: the number the line is drawn at, in the indicator's units (0.5 is 50% for a percent indicator).</param>
/// <param name="Label">1 to <see cref="WidgetDisplay.MaxReferenceLineLabel"/> characters once trimmed, with no line break; left out, the kind's default label.</param>
public sealed record WidgetReferenceLine(
    [property: Description("mean or median (of the bars or points drawn; not with a seriesBy), overall (the ratio's value over all the widget's rows; needs a ratio) or value (a fixed number), matched exactly in lower case.")] string? Kind = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("A value line only, and then required: the number to draw it at, in the indicator's units (0.5 for 50% of a percent indicator; in a percent layout a share from 0 to 1). No other kind takes a value.")] double? Value = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("The text written at the line, 1 to 40 characters with no line break; left out, the kind's default label (Mean, Median, Overall, Reference).")] string? Label = null);

/// <summary>
/// A widget's own order of values (design D1 and D7 of add-declared-category-order): the groups and the series it lists, as full text, in
/// the order to draw them. Each member is left out of the JSON while it is not set. Values the list does not name follow in the chart's
/// order; the lists are never checked against the table, since a script can change its values later. Null items are kept by
/// <see cref="Normalize"/> so validation can name them, as tooltip items are, and a value is never trimmed: it is exact text.
/// </summary>
/// <param name="Group">Up to <see cref="CategoryOrder.MaxValues"/> group values; left out, the indicator's order of the group column, else the chart's.</param>
/// <param name="Series">Up to <see cref="CategoryOrder.MaxValues"/> series values; left out, the indicator's order of the series column, else the chart's.</param>
public sealed record WidgetCategoryOrder(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("Group values as full text, in the order to draw them (up to 100, none twice); the other groups follow in the chart's order. Not with a sort or, for a boxplot, an order by median.")] IReadOnlyList<string?>? Group = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("Series values as full text, in the order to draw them (up to 100, none twice); the other series follow in the chart's order. Needs seriesBy; not for a pyramid or a likert.")] IReadOnlyList<string?>? Series = null)
{
    /// <summary>The order as saved: an empty list is none, and an order that lists nothing is none (null).</summary>
    public WidgetCategoryOrder? Normalize()
    {
        var group = Group is { Count: 0 } ? null : Group;
        var series = Series is { Count: 0 } ? null : Series;
        return group is null && series is null ? null : new WidgetCategoryOrder(group, series);
    }
}

/// <summary>
/// How a widget is drawn, apart from its query (design D3 of dashboard palettes): a change here never changes the rows. Each field is
/// left out of the JSON while it is not set, so a config saved without it reads and writes exactly as before. The description and range on
/// the fields are what the MCP tools' input schema shows of them; nothing is validated with them, since
/// <see cref="IndicatorValidation"/> is the rule, and a test keeps them with <see cref="Palettes"/>. The scale names are in the
/// description and not in an enum: the fields are nullable, and a schema whose enum leaves out null would refuse the null that the
/// server accepts for "not set".
/// </summary>
/// <param name="Color">Bar, column and line with no series, histogram and box plot only: the number, 1 to <see cref="Palettes.ColorCount"/>, of the dashboard palette's color the widget is drawn in; left out, color 1.</param>
/// <param name="Ramp">Map and heat map only: one of <see cref="Palettes.Scales"/>; left out, the dashboard palette's own scale.</param>
/// <param name="Orientation">Box plot only: one of <see cref="BoxOrientations"/>; left out, vertical. A saved <c>vertical</c> is kept as it is, so a form saved unchanged stays unchanged.</param>
/// <param name="Labels">
/// Every kind: the name shown for up to <see cref="ColumnLabels.MaxWidgetLabels"/> of the widget's columns, by column name, in place of
/// its indicator's label and of the column's own name (design D5 of the indicator column metadata). Never queried, never checked
/// against the table, and the order of the map carries no meaning: it is kept sorted by column name.
/// </param>
/// <param name="Subtitle">Every kind: the question the chart answers, 1 to <see cref="WidgetDisplay.MaxSubtitleLength"/> characters (design D1 of the widget context).</param>
/// <param name="Footnote">Every kind: a note at the widget's foot, 1 to <see cref="WidgetDisplay.MaxFootnoteLength"/> characters.</param>
/// <param name="Caption">Every kind: the line that names what the widget plots, with its own text, hidden, or kept in sight under a subtitle; left out, the default line.</param>
/// <param name="Unit">Every kind: replaces the indicator's unit for this widget, 1 to <see cref="ScriptEndpoints.MaxUnitLength"/> characters.</param>
/// <param name="ShowDescription">Every kind: shows a button that opens the indicator's description. Only <c>true</c> is stored.</param>
/// <param name="ReferenceLines">Bar, column and line only: up to <see cref="WidgetDisplay.MaxReferenceLines"/> lines to compare the bars with, in the order given (design D1 of the chart annotations).</param>
/// <param name="Interpolation">Line only: one of <see cref="LineInterpolations"/>; left out, linear. A saved <c>linear</c> is kept as it is, so a form saved unchanged stays unchanged.</param>
/// <param name="DataLabels">Bar, column (no stacked or 100% layout), line and pie only: writes each value on its mark; left out, off. A saved <c>false</c> is kept as it is.</param>
/// <param name="Order">The widget's own order of groups and of series (design D1 of add-declared-category-order); left out, the indicator's order, else the chart's.</param>
/// <param name="MinN">Every kind (design D7 of add-small-sample-warnings): the widget's own small-base threshold, 0 (off for this widget) or 1 to <see cref="WidgetDisplay.MaxMinN"/>; left out, the dashboard's. A saved <c>0</c> is kept as it is.</param>
/// <param name="ShowN">Every kind: shows (<c>true</c>) or hides (<c>false</c>) the widget's base n line whatever the dashboard says; left out, the dashboard's. A saved <c>false</c> is kept as it is.</param>
/// <param name="Secondary">Stat tile only (design D3 of improve-stat-cards): the lines under what it plots, a list of <see cref="StatSecondaryLines"/> each at most once; left out, none. The order is kept as given.</param>
public sealed record WidgetDisplay(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("A place in the dashboard's palette, 1 to 8, never a color code. For a bar, column or line chart with no seriesBy, for a histogram and for a boxplot; left out, color 1."),
     Range(1, Palettes.ColorCount)] int? Color = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("A sequential scale: blue, teal, green, orange, red, purple or gray, matched exactly in lower case. For a map or heatmap only; left out, the palette's own scale.")] string? Ramp = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("The direction a boxplot is drawn in: vertical or horizontal, matched exactly in lower case. For a boxplot only; left out, vertical.")] string? Orientation = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("Up to 10 of the widget's columns named for people: { column: name }, keyed by the column's name in lower case, each name 1 to 80 characters with no line break. For every kind; left out, a column takes its indicator's label (save_script columns), else its own name.")] IReadOnlyDictionary<string, string>? Labels = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("The question the chart answers, shown under the title: 1 to 200 characters with no line break. For every kind; left out, no subtitle.")] string? Subtitle = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("A note at the widget's foot, such as how to read it: 1 to 500 characters with no line break. For every kind; left out, no footnote.")] string? Footnote = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("The line that names what the widget plots: { text } replaces the indicator's name there and in tooltips and tables, { hidden: true } leaves the line out of sight, { hidden: false } keeps it in sight under the widget's subtitle (tooltips and tables still name what is plotted). A text is not taken with hidden: true. For every kind; left out, the default line.")] WidgetCaption? Caption = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("The unit of this widget's values, 1 to 32 characters with no line break; it replaces the indicator's unit for this widget only. For every kind; left out, the indicator's unit.")] string? Unit = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("true shows a button on the widget that opens the indicator's description. For every kind; only true is stored, and left out means no button.")] bool? ShowDescription = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("Up to 4 lines to compare the bars with: [{ kind, value, label }], kind mean, median, overall or value. For a bar, column or line; a mean or median needs no seriesBy, an overall needs a ratio, a stacked layout takes only value lines, and a percent layout only value lines from 0 to 1. Left out, no lines.")] IReadOnlyList<WidgetReferenceLine?>? ReferenceLines = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("How a line chart joins its points: linear (straight segments) or step (each value holds until the next point), matched exactly in lower case. For a line only; left out, linear.")] string? Interpolation = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("true writes each value on its bar, column, point or slice. For a bar or column with no seriesBy or a grouped layout, and for a line or pie; left out, no data labels.")] bool? DataLabels = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("The order to draw the widget's values in: { group, series }, each a list of up to 100 values as full text; the others follow in the chart's order. Left out, the indicator's order of the column (save_script columns), else the chart's own.")] WidgetCategoryOrder? Order = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("The small-base threshold of this widget: 0 turns it off, 1 to 10000 dims the marks whose base n is below it. For every kind; left out, the dashboard's threshold.")] int? MinN = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("true shows the widget's base n line under it and false hides it, whatever the dashboard says. For every kind; left out, the dashboard's setting.")] bool? ShowN = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("The lines a stat tile shows under what it plots: a list of n (the base n) and unfiltered (the value without the dashboard filters), each at most once, in lower case. For a stat tile only; left out, no lines.")] IReadOnlyList<string>? Secondary = null)
{
    /// <summary>The greatest small-base threshold of a widget or a dashboard (design D7 of add-small-sample-warnings).</summary>
    public const int MaxMinN = 10000;

    /// <summary>The longest subtitle, footnote and caption text, counted as a title is: UTF-16 units once trimmed (design D2 of the widget context).</summary>
    public const int MaxSubtitleLength = 200;

    public const int MaxFootnoteLength = 500;

    public const int MaxCaptionLength = 200;

    /// <summary>The most reference lines a widget has, and the longest label of one, counted as a title is (design D1 of the chart annotations).</summary>
    public const int MaxReferenceLines = 4;

    public const int MaxReferenceLineLabel = 40;

    /// <summary>
    /// The display as saved: an empty scale or orientation is none, a label is trimmed and a null or empty one is none, an empty set of
    /// labels is none, the subtitle, footnote and unit are trimmed and a blank one is none, <c>showDescription: false</c> is none, a
    /// caption that sets nothing is none, an empty list of reference lines is none and a line's label is trimmed (a blank one is none),
    /// and a display that sets nothing is no display (null). The texts have no default value a form could store: their default is to be
    /// absent. A saved <c>linear</c> interpolation and <c>dataLabels: false</c> are kept as given, as a box plot's <c>vertical</c> is.
    /// A null line, and a line with no kind, are kept so validation can name them.
    /// An order that lists nothing is none, so a display that holds only an empty order is no display.
    /// A saved <c>minN</c> of 0 and <c>showN: false</c> are kept as given, so a form saved unchanged stays unchanged (design D7 of add-small-sample-warnings).
    /// An empty list of secondary lines is none, and a list keeps the order it was given in (design D3 of improve-stat-cards).
    /// </summary>
    public WidgetDisplay? Normalize()
    {
        var ramp = string.IsNullOrEmpty(Ramp) ? null : Ramp;
        var orientation = string.IsNullOrEmpty(Orientation) ? null : Orientation;
        var labels = NormalizeLabels(Labels);
        var subtitle = NormalizeText(Subtitle);
        var footnote = NormalizeText(Footnote);
        var caption = Caption?.Normalize();
        var unit = NormalizeText(Unit);
        var showDescription = ShowDescription is true ? true : (bool?)null;
        var referenceLines = NormalizeReferenceLines(ReferenceLines);
        var interpolation = string.IsNullOrEmpty(Interpolation) ? null : Interpolation;
        var order = Order?.Normalize();
        var secondary = Secondary is { Count: 0 } ? null : Secondary;
        return Color is null && ramp is null && orientation is null && labels is null
            && subtitle is null && footnote is null && caption is null && unit is null && showDescription is null
            && referenceLines is null && interpolation is null && DataLabels is null && order is null && MinN is null && ShowN is null && secondary is null
            ? null
            : this with
            {
                Ramp = ramp, Orientation = orientation, Labels = labels, Subtitle = subtitle, Footnote = footnote, Caption = caption,
                Unit = unit, ShowDescription = showDescription, ReferenceLines = referenceLines, Interpolation = interpolation, Order = order,
                Secondary = secondary,
            };
    }

    private static IReadOnlyList<WidgetReferenceLine?>? NormalizeReferenceLines(IReadOnlyList<WidgetReferenceLine?>? lines) =>
        lines is null or { Count: 0 }
            ? null
            : lines.Select(line => line is null ? null : line with { Label = NormalizeText(line.Label) }).ToList();

    /// <summary>
    /// A text as saved: its surrounding spaces are trimmed and a blank one is none. A control character at either end stays, since it is
    /// not a space: <see cref="IndicatorValidation"/> refuses it, as it refuses one inside the text, instead of the save quietly dropping a
    /// line break.
    /// </summary>
    internal static string? NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var start = 0;
        var end = text.Length;
        while (start < end && char.IsWhiteSpace(text[start]) && !char.IsControl(text[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(text[end - 1]) && !char.IsControl(text[end - 1]))
        {
            end--;
        }

        return text[start..end];
    }

    // The map is sorted by column name, so two configs that label the same columns serialize alike whatever order they were sent in:
    // the update compares configs as text, and jsonb gives the keys back in its own order.
    private static SortedDictionary<string, string>? NormalizeLabels(IReadOnlyDictionary<string, string>? labels)
    {
        if (labels is null)
        {
            return null;
        }

        var kept = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (column, label) in labels)
        {
            if (!string.IsNullOrWhiteSpace(label))
            {
                kept[column] = label.Trim();
            }
        }

        return kept.Count == 0 ? null : kept;
    }
}

/// <summary>
/// A widget's query as a request carries it: anything left out takes its default (<see cref="WidgetConfig"/>). It is the
/// <c>config</c> of a widget and of the query endpoint's body.
/// </summary>
/// <param name="Indicator">The indicator whose table is plotted.</param>
/// <param name="Measure">A numeric column to sum. Left out together with <paramref name="Ratio"/>, the table's default measure.</param>
/// <param name="Ratio">Plots the ratio of two columns' sums instead of a measure.</param>
/// <param name="GroupBy">The column to group by; left out, the rows are plotted as one group (a stat tile) or one by one (a histogram).</param>
/// <param name="Filters">At most 50, built as the clean data dashboard's.</param>
/// <param name="GeometryColumn">The column of WKT text a map plots.</param>
/// <param name="Buckets">Histogram only: the number of equal-width ranges, 10 when left out, at most 100.</param>
/// <param name="Sort">Bar and column only: the order of the bars; left out, the group column's own order.</param>
/// <param name="Tooltip">Map only: up to <see cref="WidgetConfig.MaxTooltipItems"/> columns of the table shown in each shape's tooltip, in order.</param>
/// <param name="Basemap">Map only: <c>streets</c>, <c>satellite</c> or null for none. A kind, never checked against what the deployment offers.</param>
/// <param name="SeriesBy">The column that splits each group into series; see <see cref="ChartTypes.TakesSeries"/> and <see cref="ChartTypes.NeedsSeries"/>.</param>
/// <param name="Layout">Bar and column with a series only: one of <see cref="SeriesLayouts"/>; left out, grouped.</param>
/// <param name="Pyramid">Population pyramid only: the series values drawn to the left and to the right.</param>
/// <param name="Scale">Diverging Likert only: the scale's values in order, each with its side.</param>
/// <param name="IgnoredDashboardFilters">
/// The columns of the dashboard filters the widget ignores, at most 8 (design D6). Kept by column, so they hold on any dashboard the
/// widget is moved or copied to, and for a filter removed and defined again. Not a query setting: a query ignores it.
/// </param>
/// <param name="Display">How the widget is drawn (<see cref="WidgetDisplay"/>). Not a query setting: a query ignores it.</param>
/// <param name="MissingFooter">
/// Bar, column, line, pie, heat map, population pyramid, Likert and cross table only (<see cref="ChartTypes.TakesMissingFooter"/>, design D6 of
/// add-small-sample-warnings): leaves out the rows whose group or series value the indicator declares as meaning no data. A query setting,
/// since it changes the rows. Kept as given, a saved <c>false</c> too; left out of the JSON while it is not set.
/// </param>
/// <param name="Statistic">
/// Stat tile only (design D2 of improve-stat-cards): one of <see cref="StatStatistics"/>, which takes each matching row as one value
/// instead of summing the rows. A query setting, since it changes the rows. Left out of the JSON while it is not set; an empty text is none.
/// </param>
public sealed record WidgetConfigInput(
    string? Indicator = null,
    string? Measure = null,
    WidgetRatio? Ratio = null,
    string? GroupBy = null,
    IReadOnlyList<DataFilter>? Filters = null,
    string? GeometryColumn = null,
    int? Buckets = null,
    WidgetSort? Sort = null,
    IReadOnlyList<WidgetTooltipItem>? Tooltip = null,
    string? Basemap = null,
    IReadOnlyList<string>? IgnoredDashboardFilters = null,
    string? SeriesBy = null,
    string? Layout = null,
    WidgetPyramid? Pyramid = null,
    IReadOnlyList<LikertStep>? Scale = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("How the widget is drawn and what it says: its color, its scale, its orientation, the names of its columns, its subtitle, footnote, caption, unit and description button, its reference lines, its steps, its data labels, the order of its values, its small-base threshold, its base n line or, for a stat tile, the lines under its figure (secondary). It never changes the rows.")] WidgetDisplay? Display = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("true leaves out of the widget the rows whose group or series value the indicator declares as meaning no data (save_script columns, missing), and says under the chart what they held. For a bar, column, line, pie, heatmap, pyramid, likert or crosstab; it changes the rows. Left out, nothing is left out.")] bool? MissingFooter = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("The statistic of a stat tile: median, p25, p75 or mean, matched exactly in lower case. It takes each row as one value (its measure, or its numerator over its own denominator) instead of summing the rows, for a table with one row per unit. For a stat tile only; it changes the rows. Left out, the tile sums its rows.")] string? Statistic = null)
{
    public WidgetConfig Normalize() => new(
        Indicator ?? "",
        string.IsNullOrEmpty(Measure) ? null : Measure,
        Ratio,
        string.IsNullOrEmpty(GroupBy) ? null : GroupBy,
        Filters ?? [],
        string.IsNullOrEmpty(GeometryColumn) ? null : GeometryColumn,
        Buckets,
        Sort?.Normalize(),
        // A null item is kept so validation can name it.
        Tooltip?.Select(item => item?.Normalize()!).ToList() ?? [],
        string.IsNullOrEmpty(Basemap) ? null : Basemap,
        IgnoredDashboardFilters ?? [],
        string.IsNullOrEmpty(SeriesBy) ? null : SeriesBy,
        string.IsNullOrEmpty(Layout) ? null : Layout,
        Pyramid,
        // A null step is kept so validation can name it.
        Scale ?? [],
        Display?.Normalize(),
        MissingFooter,
        string.IsNullOrEmpty(Statistic) ? null : Statistic);
}

/// <summary>A widget's query with its defaults applied. With no <see cref="Measure"/> and no <see cref="Ratio"/>, the table's default measure is plotted.</summary>
public sealed record WidgetConfig(
    string Indicator,
    string? Measure,
    WidgetRatio? Ratio,
    string? GroupBy,
    IReadOnlyList<DataFilter> Filters,
    string? GeometryColumn,
    int? Buckets,
    WidgetSort? Sort = null,
    IReadOnlyList<WidgetTooltipItem>? Tooltip = null,
    string? Basemap = null,
    IReadOnlyList<string>? IgnoredDashboardFilters = null,
    string? SeriesBy = null,
    string? Layout = null,
    WidgetPyramid? Pyramid = null,
    IReadOnlyList<LikertStep>? Scale = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WidgetDisplay? Display = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? MissingFooter = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Statistic = null)
{
    public const int DefaultBuckets = 10;

    /// <summary>The fewest and the most values a Likert scale has, and the most on one side of its neutral value.</summary>
    public const int MinScaleValues = 2;
    public const int MaxScaleValues = 11;
    public const int MaxScaleSide = 5;

    /// <summary>The most series a bar, column or line widget shows before the smallest are combined into "Other", and the most a heat map or a cross table shows.</summary>
    public const int MaxSeries = 8;
    public const int MaxMatrixSeries = 30;

    /// <summary>The most slices a pie colors by value before the smallest are folded into "Other": the palette's colors, "Other" included (design D8 of dashboard palettes).</summary>
    public const int MaxSlices = Palettes.ColorCount;

    public const int MaxBuckets = 100;

    /// <summary>The most outlier values a box plot's box returns and draws; the ones farthest from its median, the rest only counted.</summary>
    public const int MaxBoxOutliers = 100;

    /// <summary>How many interquartile ranges beyond its quartiles a box's whiskers may reach (Tukey's fences).</summary>
    public const double WhiskerRange = 1.5;

    /// <summary>The most tooltip columns a map has.</summary>
    public const int MaxTooltipItems = 5;

    /// <summary>The longest tooltip label, in characters.</summary>
    public const int MaxTooltipLabel = 80;

    /// <summary>The longest tooltip text a query returns; longer text is cut and marked with an ellipsis.</summary>
    public const int MaxTooltipText = 200;

    /// <summary>The tooltip columns; a config stored before the field existed has none.</summary>
    public IReadOnlyList<WidgetTooltipItem> Tooltip { get; init; } = Tooltip ?? [];

    /// <summary>The columns of the dashboard filters the widget ignores; a config stored before the field existed ignores none.</summary>
    public IReadOnlyList<string> IgnoredDashboardFilters { get; init; } = IgnoredDashboardFilters ?? [];

    /// <summary>The Likert scale; a config stored before the field existed has none.</summary>
    public IReadOnlyList<LikertStep> Scale { get; init; } = Scale ?? [];
}

/// <param name="DashboardId">The project dashboard the widget is on.</param>
/// <param name="UpdatedAt">When the widget was last changed (ISO 8601).</param>
public sealed record Widget(
    Guid Id, Guid DashboardId, string Title, string ChartType, WidgetConfig Config, WidgetLayout Layout, DateTimeOffset UpdatedAt);

/// <param name="MaxWidgets">The most widgets one dashboard holds.</param>
/// <param name="MaxDashboards">The most dashboards of its own a project has.</param>
public sealed record IndicatorLimits(int MaxWidgets, int MaxQueryRows, int MaxDashboards);

/// <param name="Published">False while the indicator's table has never been published.</param>
/// <param name="PublishedAt">When the step that published the table finished.</param>
/// <param name="RunId">The run that published it.</param>
/// <param name="Rows">The table's row count at that step.</param>
/// <param name="Columns">What the indicator says about the columns of its table, by column name (their labels); null when it says nothing.</param>
public sealed record IndicatorEntry(
    string Name, string Label, string? Description, string? Unit, string Format, bool Published, DateTimeOffset? PublishedAt, Guid? RunId, long? Rows,
    IReadOnlyDictionary<string, IndicatorColumnMetadata>? Columns = null);

/// <summary>What every dashboard of the project shows besides its widgets (<see cref="DashboardView"/>).</summary>
/// <param name="Indicators">The project's current indicator scripts, ordered by name.</param>
public sealed record IndicatorOverview(IReadOnlyList<IndicatorEntry> Indicators, IndicatorLimits Limits);

/// <summary>One of the project's own dashboards with its filters, colors and widgets.</summary>
/// <param name="Filters">The dashboard's filters, in order; none for a dashboard that has not defined any.</param>
/// <param name="Palette">One of <see cref="Palettes.All"/>; <see cref="Palettes.Tamiza"/> for a dashboard that has not chosen one.</param>
/// <param name="ValueColors">The colors the dashboard keeps for values of columns, in the order they were saved; none for a dashboard that keeps none.</param>
/// <param name="Widgets">Ordered by <c>layout.y</c>, <c>layout.x</c>, then creation; listed whether or not their indicator is published.</param>
/// <param name="Version">
/// The number of the dashboard's current version, its newest (design D9 of dashboard versions); null while it has none, which only a
/// version that could not be recorded leaves.
/// </param>
/// <param name="Source">
/// Where the dashboard's data comes from, 1 to <see cref="DashboardValidation.MaxSourceLength"/> characters (design D7 of the widget
/// context). Left out of the JSON while the dashboard has none, so a dashboard without one returns what it returned before sources.
/// </param>
/// <param name="MinN">
/// The dashboard's small-base threshold, 1 to <see cref="WidgetDisplay.MaxMinN"/> (design D8 of add-small-sample-warnings). Left out of the
/// JSON while it is off.
/// </param>
/// <param name="ShowN">
/// Whether each widget's base n shows under it. Written only when true, so a dashboard without base settings returns what it returned before
/// them; a reader takes a missing <c>showN</c> as false.
/// </param>
public sealed record DashboardView(
    Guid Id, string Name, IReadOnlyList<DashboardFilter> Filters, string Palette, IReadOnlyList<ValueColor> ValueColors,
    IReadOnlyList<Widget> Widgets, int? Version,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Source = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? MinN = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool ShowN = false);

/// <summary>One of the project's own dashboards, without its widgets.</summary>
public sealed record DashboardEntry(Guid Id, string Name);

/// <summary>The body that adds, renames or duplicates a dashboard.</summary>
/// <param name="Name">1 to 40 characters once trimmed, stored trimmed.</param>
public sealed record DashboardNameRequest(string? Name);

/// <summary>The body that replaces a dashboard's source (design D7 of the widget context).</summary>
/// <param name="Source">1 to <see cref="DashboardValidation.MaxSourceLength"/> characters once trimmed, with no line break; null or an empty text removes it.</param>
public sealed record DashboardSourceRequest(
    [property: Description("Where the dashboard's data comes from, such as the survey: 1 to 300 characters with no line break. Null or an empty text removes it.")] string? Source);

/// <summary>A dashboard's source as saved, and the answer to saving it; null when the dashboard has none.</summary>
public sealed record DashboardSource(string? Source);

/// <summary>The body that replaces a dashboard's base settings (design D8 of add-small-sample-warnings): both are saved whole.</summary>
/// <param name="MinN">The small-base threshold, a whole number from 1 to 10,000; null turns it off.</param>
/// <param name="ShowN">Whether each widget shows its base n under it; left out, false.</param>
public sealed record DashboardBasesRequest(
    [property: Description("The small-base threshold: a whole number from 1 to 10,000; marks whose base n is below it are drawn dimmed. Null turns it off.")] int? MinN,
    [property: Description("true shows each widget's base n line under it; left out or false, the widgets that do not choose for themselves hide it.")] bool? ShowN);

/// <summary>A dashboard's base settings as saved, and the answer to saving them (design D8 of add-small-sample-warnings).</summary>
public sealed record DashboardBases(int? MinN, bool ShowN);

/// <summary>A new order of the project's dashboards. The API knows nothing of left and right: the page sends the whole list.</summary>
/// <param name="ExpectedIds">The order the member's page shows. When it is not the project's current order, nothing changes.</param>
/// <param name="DashboardIds">The order asked for: the same dashboards, once each.</param>
public sealed record ReorderDashboardsRequest(IReadOnlyList<Guid>? ExpectedIds, IReadOnlyList<Guid>? DashboardIds);

/// <summary>The measure and group column a new widget over the table starts with.</summary>
public sealed record ColumnDefaults(string? Measure, string? GroupBy);

/// <param name="Columns">Every column of the published table, in table order.</param>
/// <param name="Numeric">The columns a widget can plot as a measure or a ratio.</param>
/// <param name="Declared">
/// What the indicator's current version declares for its columns about bases (design D1 of add-small-sample-warnings): the base column,
/// the values that mean no data and the answer columns, by column name, each entry holding only those properties. Left out while it
/// declares none, so the columns of an indicator without declarations are what they were.
/// </param>
public sealed record IndicatorColumns(
    bool Published, IReadOnlyList<TableColumn> Columns, IReadOnlyList<string> Numeric, ColumnDefaults Defaults,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("What the indicator declares for its columns about bases, by column name: base (the column that counts the units each row stands for), missing (values that mean no data) and categories (single or multiple, the columns that name a question and its answers). Left out when it declares none.")] IReadOnlyDictionary<string, IndicatorColumnMetadata>? Declared = null);

/// <summary>One plotted point: <see cref="Value"/> is the measure summed, or the ratio of sums, over the rows that share the group.</summary>
/// <param name="Group">The group column's value as text; null for rows with no value in it and when the query has no group.</param>
/// <param name="Geometry">The geometry column's text from one row of the group; null when none was requested or the row has none.</param>
/// <param name="Rows">Map only: how many rows of the table the group combines (those its query keeps); left out for every other widget.</param>
/// <param name="Tooltip">Map only: one value per tooltip column, in order, as <see cref="IndicatorQueryResponse.TooltipTypes"/> types them: a number, text, true/false, or null for no value.</param>
/// <param name="Series">A widget with a series only: the series value of the cell (<see cref="Rows"/> then counts the table rows it sums). Left out for the rows with no series value and for the folded series.</param>
/// <param name="SeriesKind">A widget with a series only: <see cref="SeriesKinds.Value"/>, <see cref="SeriesKinds.None"/> or <see cref="SeriesKinds.Other"/>.</param>
/// <param name="Numerator">A ratio only (design D5 of add-small-sample-warnings): the sum of its numerator over the rows of the value. A measure has none, and nor does a box plot or a histogram.</param>
/// <param name="Denominator">A ratio only: the sum of its denominator over the rows of the value.</param>
/// <param name="N">The value's base n (design D2): the sum of the indicator's base column over its rows, else a ratio's <paramref name="Denominator"/>; none for a measure without a base column, for a histogram's bar and for a box.</param>
public sealed record IndicatorRow(
    string? Group,
    double Value,
    string? Geometry,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? Rows = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<object?>? Tooltip = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Series = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SeriesKind = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), Description("A ratio only: the sum of its numerator over the rows of the value.")] double? Numerator = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), Description("A ratio only: the sum of its denominator over the rows of the value.")] double? Denominator = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), Description("The value's base n: the sum of the indicator's base column over its rows, else a ratio's denominator. Left out for a measure without a base column, a histogram's bar and a box.")] double? N = null);

/// <summary>
/// One box of a box plot (design D4 of add-boxplot-widget), computed over every value of the group that the query matches.
/// </summary>
/// <param name="Group">The group column's value as text; null for the rows with no value in it, and for the one box of a widget with no group column.</param>
/// <param name="Values">How many values the box summarizes.</param>
/// <param name="LowerWhisker">The smallest value at or above the first quartile minus <see cref="WidgetConfig.WhiskerRange"/> times the interquartile range.</param>
/// <param name="UpperWhisker">The largest value at or below the third quartile plus that range.</param>
/// <param name="OutlierCount">How many values lie beyond the whiskers.</param>
/// <param name="Outliers">At most <see cref="WidgetConfig.MaxBoxOutliers"/> of them, the ones farthest from the median, in ascending order.</param>
public sealed record IndicatorBox(
    string? Group, long Values, double Min, double Q1, double Median, double Q3, double Max, double Mean, double LowerWhisker, double UpperWhisker,
    long OutlierCount, IReadOnlyList<double> Outliers);

/// <summary>What a widget's query names that the published table does not have.</summary>
/// <param name="Indicator">True when the indicator has not published a table.</param>
public sealed record MissingReferences(bool Indicator, IReadOnlyList<string> Columns)
{
    public static readonly MissingReferences None = new(false, []);

    public bool Any => Indicator || Columns.Count > 0;
}

/// <param name="TooltipTypes">Map only: the type of each tooltip column, so the page formats the values without the catalog; left out for every other widget.</param>
/// <param name="NotFiltered">
/// The columns of the request's dashboard filters that did not narrow the rows: the table has no such column, or has it with a type
/// of another kind. Left out when the request had no dashboard filters, and when nothing was drawn.
/// </param>
/// <param name="Series">A widget with a series only: its series in display order. <see cref="Rows"/> then holds one <see cref="SeriesCell"/> per group and series value that has a value.</param>
/// <param name="Totals">A widget with a series only: the totals of its groups, its series and all its rows.</param>
/// <param name="LeftOut">A population pyramid or a Likert widget only: the rows it left out because their group or series value does not belong.</param>
/// <param name="Boxes">A box plot only: its boxes in display order. <see cref="Rows"/> then holds one row per box, its value the box's median and its <c>rows</c> its number of values.</param>
/// <param name="Domain">
/// A pie chart only (design D8 of dashboard palettes): the groups its colors and its "Other" slice follow, read from the rows that
/// match the widget's own filters, not the dashboard's. Every group with a total above zero is ranked by total, largest first, the
/// missing group (<c>none</c>) in its place, as the page ranked the slices of a pie before the domain existed. At most
/// <see cref="WidgetConfig.MaxSlices"/> entries: all the groups when there are that many or fewer, otherwise the largest
/// <see cref="WidgetConfig.MaxSlices"/> - 1 followed by <c>other</c>. A group outside it, the missing one included, is part of "Other".
/// Left out for every other widget.
/// </param>
/// <param name="Overall">
/// A plain grouped query that asked for it only (<see cref="IndicatorQueryRequest.Overall"/>): the widget's value over every row its query
/// matches, with the widget's and the dashboard's filters, whatever groups are returned: the figure a stat tile with the same indicator,
/// ratio and filters shows. Left out when none was asked for, and when there is no row to give it (the denominators sum to zero).
/// </param>
/// <param name="N">
/// The widget's base n (design D4 of add-small-sample-warnings), counted over every row it plots, once across answers when the indicator
/// declares answer columns. Left out unless its marks have an n and the indicator declares a base column or an answer column.
/// </param>
/// <param name="NoData">
/// A widget that leaves out the values that mean no data, whose indicator declares some for its group or series column (design D6 of
/// add-small-sample-warnings): what it left out. Left out of the JSON when the widget does not ask, when nothing is declared, and when no
/// row was left out.
/// </param>
/// <param name="Unfiltered">
/// A stat tile that asked for it only (<see cref="IndicatorQueryRequest.Unfiltered"/>), while the dashboard filters it was given narrowed it
/// (design D3 of improve-stat-cards): the tile's value over the same rows without the dashboard filters. Left out otherwise, and when no
/// row gave it a value.
/// </param>
/// <param name="CombinedCategories">
/// A ratio over an indicator that declares a column as the answers of a multiple-choice question, when a value the widget plots would add
/// up the rows of several of those answers (design D4b of improve-stat-cards): the name of that column, in which the widget has no rows
/// and its message says to keep one answer, group or split by the column, or plot a row the indicator publishes for any of them. Left
/// out otherwise.
/// </param>
public sealed record IndicatorQueryResponse(
    bool Published,
    IReadOnlyList<IndicatorRow> Rows,
    bool RowLimitReached,
    MissingReferences Missing,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<DataColumnType>? TooltipTypes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? NotFiltered = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SeriesInfo>? Series = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SeriesTotals? Totals = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? LeftOut = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SeriesInfo>? Domain = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<IndicatorBox>? Boxes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("Only on a query that asked for it with the request's overall flag: the value over all the rows the query matches, as a stat tile with the same indicator and filters shows it.")] double? Overall = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("The widget's base n (design D4 of add-small-sample-warnings): the n over every row it plots, each unit counted once across the answers of one question. Only when its marks have an n and its indicator declares a base column or answer columns; left out for a histogram and a box plot.")] double? N = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("What a widget that leaves out the values that mean no data (config.missingFooter) left out, combined as one value. Left out when the widget does not ask for it or no row was left out.")] IndicatorNoData? NoData = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("Only on a query that asked for it with the request's unfiltered flag, and only while a dashboard filter narrowed the stat tile: its value over the same rows without the dashboard filters. Left out otherwise, and when no row gave it a value.")] double? Unfiltered = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("Only when the widget plots a ratio over an indicator that declares a column as the answers of a multiple-choice question and a value it plots would add up several of those answers, which counts a respondent once for each: the name of that column. The widget then has no rows. Keep one answer, group or split the widget by that column, or plot a row the indicator publishes for any of them. Left out otherwise.")] string? CombinedCategories = null);

/// <summary>
/// What a widget left out as no data (design D6 of add-small-sample-warnings): the rows whose group or series value its indicator
/// declares as meaning no data, combined as one value over the widget's own filters and the dashboard's. Each figure is left out of the
/// JSON when it does not exist: a ratio has a numerator, a denominator and no share, a measure has a share and no numerator or denominator.
/// </summary>
/// <param name="Values">The left-out values that had rows, in natural order.</param>
/// <param name="Value">A ratio's left-out numerator over its left-out denominator; a measure's left-out sum. Left out when the denominators sum to zero.</param>
/// <param name="Numerator">A ratio only: the sum of the numerator over the left-out rows.</param>
/// <param name="Denominator">A ratio only: the sum of the denominator over the left-out rows.</param>
/// <param name="N">The base n of the left-out rows (the largest per-answer sum when the indicator declares answer columns); none for a measure without a base column.</param>
/// <param name="Share">A measure only: the left-out sum over the sum of every row the query matches, left out rows included.</param>
public sealed record IndicatorNoData(
    IReadOnlyList<string> Values,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Value = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Numerator = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Denominator = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? N = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Share = null);

/// <summary>The outcome of a query: the response, or the validation errors of the request.</summary>
public sealed record IndicatorQueryOutcome(IndicatorQueryResponse? Response, IDictionary<string, string[]>? Errors);

/// <summary>
/// The body of the query endpoint. <paramref name="ChartType"/> is optional: a histogram with no group column plots each row's
/// measure as it is instead of one sum.
/// </summary>
/// <param name="DashboardFilters">
/// At most 8 dashboard filter values in force, one per column, that narrow the rows too, on top of the widget's own filters. One whose
/// column the table does not have, or has with another kind of type, is listed in the response's <c>notFiltered</c> instead.
/// </param>
/// <param name="Overall">
/// Asks a plain grouped query (a bar, column or line with no series) for its overall value too (design D4 of add-chart-annotations). The
/// page and the server derive it from the widget they draw and no widget stores it; left out of the JSON unless set.
/// </param>
/// <param name="Unfiltered">
/// Asks a stat tile for its value without the dashboard filters too (design D3 of improve-stat-cards), when a dashboard filter narrows it.
/// The page and the server derive it from the widget they draw (<see cref="WidgetQuery.Unfiltered"/>) and no widget stores it; it is not
/// validated, and where it cannot apply it does nothing. Left out of the JSON unless set.
/// </param>
public sealed record IndicatorQueryRequest(
    WidgetConfigInput? Config, string? ChartType = null, IReadOnlyList<AppliedDashboardFilter>? DashboardFilters = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("The page and the server set this to also get the overall value of a bar, column or line with no series: the value over all the widget's rows, as a stat tile with the same indicator and filters shows it. Derived from the widget; no widget stores it.")] bool? Overall = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("The page and the server set this to also get the value of a stat tile without the dashboard filters, while a dashboard filter narrows it. Derived from the widget; no widget stores it, and it does nothing for another kind of widget.")] bool? Unfiltered = null);

/// <param name="Layout">Left out: the chart type's default size, placed under the existing widgets.</param>
public sealed record CreateWidgetRequest(string? Title, string? ChartType, WidgetConfigInput? Config, WidgetLayout? Layout = null);

/// <summary>Any subset of the widget's fields; what is left out keeps its value.</summary>
/// <param name="DashboardId">
/// Another dashboard of the project to move the widget to. The widget is placed below that dashboard's widgets, so a
/// <paramref name="Layout"/> sent with a move is ignored. The widget's own dashboard, or null, moves nothing.
/// </param>
public sealed record UpdateWidgetRequest(
    string? Title = null, string? ChartType = null, WidgetConfigInput? Config = null, WidgetLayout? Layout = null, Guid? DashboardId = null);

/// <summary>
/// The position and size of several widgets of one dashboard, saved at once: what one drag that pushes other widgets leaves (design D8
/// of dashboard versions).
/// </summary>
/// <param name="Widgets">1 to the widget limit of entries, each naming a different widget of the dashboard.</param>
public sealed record DashboardLayoutRequest(IReadOnlyList<WidgetLayoutItem?>? Widgets);

/// <summary>One widget's new position and size, checked as the widget's <c>PATCH</c> checks its <c>layout</c>.</summary>
public sealed record WidgetLayoutItem(Guid? Id, WidgetLayout? Layout);

/// <param name="Format"><c>csv</c> or <c>xlsx</c>.</param>
public sealed record IndicatorExportRequest(string? Format);
