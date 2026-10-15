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

    public static readonly IReadOnlyList<string> All = [Stat, Bar, Column, Line, Histogram, Pie, Map];

    public static bool IsKnown(string? chartType) => chartType is not null && All.Contains(chartType);

    /// <summary>The charts that group their rows: they need a column to group by. A stat tile and a histogram may plot the rows as they are.</summary>
    public static bool NeedsGroup(string chartType) => chartType is Bar or Column or Line or Pie or Map;
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
public sealed record WidgetConfigInput(
    string? Indicator = null,
    string? Measure = null,
    WidgetRatio? Ratio = null,
    string? GroupBy = null,
    IReadOnlyList<DataFilter>? Filters = null,
    string? GeometryColumn = null,
    int? Buckets = null,
    WidgetSort? Sort = null)
{
    public WidgetConfig Normalize() => new(
        Indicator ?? "",
        string.IsNullOrEmpty(Measure) ? null : Measure,
        Ratio,
        string.IsNullOrEmpty(GroupBy) ? null : GroupBy,
        Filters ?? [],
        string.IsNullOrEmpty(GeometryColumn) ? null : GeometryColumn,
        Buckets,
        Sort?.Normalize());
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
    WidgetSort? Sort = null)
{
    public const int DefaultBuckets = 10;
    public const int MaxBuckets = 100;
}

/// <param name="UpdatedAt">When the widget was last changed (ISO 8601).</param>
public sealed record Widget(Guid Id, string Title, string ChartType, WidgetConfig Config, WidgetLayout Layout, DateTimeOffset UpdatedAt);

public sealed record IndicatorLimits(int MaxWidgets, int MaxQueryRows);

/// <param name="Published">False while the indicator's table has never been published.</param>
/// <param name="PublishedAt">When the step that published the table finished.</param>
/// <param name="RunId">The run that published it.</param>
/// <param name="Rows">The table's row count at that step.</param>
public sealed record IndicatorEntry(
    string Name, string Label, string? Description, string? Unit, string Format, bool Published, DateTimeOffset? PublishedAt, Guid? RunId, long? Rows);

/// <param name="Indicators">The project's current indicator scripts, ordered by name.</param>
/// <param name="Widgets">Ordered by <c>layout.y</c>, <c>layout.x</c>, then creation; listed whether or not their indicator is published.</param>
public sealed record IndicatorOverview(IReadOnlyList<IndicatorEntry> Indicators, IReadOnlyList<Widget> Widgets, IndicatorLimits Limits);

/// <summary>The measure and group column a new widget over the table starts with.</summary>
public sealed record ColumnDefaults(string? Measure, string? GroupBy);

/// <param name="Columns">Every column of the published table, in table order.</param>
/// <param name="Numeric">The columns a widget can plot as a measure or a ratio.</param>
public sealed record IndicatorColumns(bool Published, IReadOnlyList<TableColumn> Columns, IReadOnlyList<string> Numeric, ColumnDefaults Defaults);

/// <summary>One plotted point: <see cref="Value"/> is the measure summed, or the ratio of sums, over the rows that share the group.</summary>
/// <param name="Group">The group column's value as text; null for rows with no value in it and when the query has no group.</param>
/// <param name="Geometry">The geometry column's text from one row of the group; null when none was requested or the row has none.</param>
public sealed record IndicatorRow(string? Group, double Value, string? Geometry);

/// <summary>What a widget's query names that the published table does not have.</summary>
/// <param name="Indicator">True when the indicator has not published a table.</param>
public sealed record MissingReferences(bool Indicator, IReadOnlyList<string> Columns)
{
    public static readonly MissingReferences None = new(false, []);

    public bool Any => Indicator || Columns.Count > 0;
}

public sealed record IndicatorQueryResponse(bool Published, IReadOnlyList<IndicatorRow> Rows, bool RowLimitReached, MissingReferences Missing);

/// <summary>The outcome of a query: the response, or the validation errors of the request.</summary>
public sealed record IndicatorQueryOutcome(IndicatorQueryResponse? Response, IDictionary<string, string[]>? Errors);

/// <summary>
/// The body of the query endpoint. <paramref name="ChartType"/> is optional: a histogram with no group column plots each row's
/// measure as it is instead of one sum.
/// </summary>
public sealed record IndicatorQueryRequest(WidgetConfigInput? Config, string? ChartType = null);

/// <param name="Layout">Left out: the chart type's default size, placed under the existing widgets.</param>
public sealed record CreateWidgetRequest(string? Title, string? ChartType, WidgetConfigInput? Config, WidgetLayout? Layout = null);

/// <summary>Any subset of the widget's fields; what is left out keeps its value.</summary>
public sealed record UpdateWidgetRequest(string? Title = null, string? ChartType = null, WidgetConfigInput? Config = null, WidgetLayout? Layout = null);

/// <param name="Format"><c>csv</c> or <c>xlsx</c>.</param>
public sealed record IndicatorExportRequest(string? Format);
