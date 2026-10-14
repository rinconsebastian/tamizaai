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

    /// <summary>Stat tiles, pies and maps plot exactly one indicator; the other charts plot one or more (design D9).</summary>
    public static bool NeedsExactlyOneIndicator(string chartType) => chartType is Stat or Pie or Map;
}

/// <summary>The fields a widget can plot.</summary>
public static class ValueFields
{
    public const string Value = "value";
    public const string Numerator = "numerator";
    public const string Denominator = "denominator";

    public static readonly IReadOnlyList<string> All = [Value, Numerator, Denominator];
}

/// <summary>A widget's size and position on the 12-column grid, in grid units.</summary>
public sealed record WidgetLayout(int X, int Y, int W, int H);

/// <summary>
/// A widget's query as a request carries it: anything left out takes its default (<see cref="WidgetConfig"/>). It is the body of
/// the query endpoint and the <c>config</c> of a widget.
/// </summary>
/// <param name="Indicators">Values of the table's <c>indicator</c> column.</param>
/// <param name="GroupBy">The column to group by; <c>disaggregation</c> by default.</param>
/// <param name="ValueField"><c>value</c> (default), <c>numerator</c> or <c>denominator</c>.</param>
/// <param name="Filters">At most 50, built as the clean data dashboard's.</param>
/// <param name="GeometryColumn">The column of WKT text a map plots.</param>
/// <param name="Buckets">Histogram only: the number of equal-width ranges, 10 when left out, at most 100.</param>
public sealed record WidgetConfigInput(
    IReadOnlyList<string>? Indicators = null,
    string? GroupBy = null,
    string? ValueField = null,
    IReadOnlyList<DataFilter>? Filters = null,
    string? GeometryColumn = null,
    int? Buckets = null)
{
    public WidgetConfig Normalize() => new(
        Indicators ?? [],
        string.IsNullOrEmpty(GroupBy) ? WidgetConfig.DefaultGroupBy : GroupBy,
        string.IsNullOrEmpty(ValueField) ? ValueFields.Value : ValueField,
        Filters ?? [],
        string.IsNullOrEmpty(GeometryColumn) ? null : GeometryColumn,
        Buckets);
}

/// <summary>A widget's query with its defaults applied.</summary>
public sealed record WidgetConfig(
    IReadOnlyList<string> Indicators,
    string GroupBy,
    string ValueField,
    IReadOnlyList<DataFilter> Filters,
    string? GeometryColumn,
    int? Buckets)
{
    public const string DefaultGroupBy = "disaggregation";
    public const int DefaultBuckets = 10;
    public const int MaxBuckets = 100;
    public const int MaxIndicators = DataQueryBuilder.MaxInValues;
}

/// <param name="UpdatedAt">When the widget was last changed (ISO 8601).</param>
public sealed record Widget(Guid Id, string Title, string ChartType, WidgetConfig Config, WidgetLayout Layout, DateTimeOffset UpdatedAt);

public sealed record IndicatorLimits(int MaxWidgets, int MaxQueryRows);

/// <param name="Published">False while script 3 has never published the table.</param>
/// <param name="PublishedAt">When the step that published the table finished.</param>
/// <param name="RunId">The run that published it.</param>
/// <param name="Widgets">Ordered by <c>layout.y</c>, <c>layout.x</c>, then creation; listed whether or not the table is published.</param>
public sealed record IndicatorOverview(bool Published, DateTimeOffset? PublishedAt, Guid? RunId, IReadOnlyList<Widget> Widgets, IndicatorLimits Limits);

/// <param name="Columns">Every column of the published table, in table order.</param>
/// <param name="Indicators">The distinct values of the <c>indicator</c> column, sorted, at most 500.</param>
public sealed record IndicatorColumns(bool Published, IReadOnlyList<TableColumn> Columns, IReadOnlyList<string> Indicators);

/// <summary>One plotted point: <see cref="Value"/> is the plotted field summed over the rows that share the indicator and the group.</summary>
/// <param name="Group">The group column's value as text; null for rows with no value in it.</param>
/// <param name="Geometry">The geometry column's text from one row of the group; null when none was requested or the row has none.</param>
public sealed record IndicatorRow(string Indicator, string? Group, double Value, string? Geometry);

/// <summary>What a widget's query names that the published table does not have.</summary>
public sealed record MissingReferences(IReadOnlyList<string> Indicators, IReadOnlyList<string> Columns)
{
    public static readonly MissingReferences None = new([], []);

    public bool Any => Indicators.Count > 0 || Columns.Count > 0;
}

public sealed record IndicatorQueryResponse(bool Published, IReadOnlyList<IndicatorRow> Rows, bool RowLimitReached, MissingReferences Missing);

/// <summary>The outcome of a query: the response, or the validation errors of the request.</summary>
public sealed record IndicatorQueryOutcome(IndicatorQueryResponse? Response, IDictionary<string, string[]>? Errors);

/// <param name="Layout">Left out: the chart type's default size, placed under the existing widgets.</param>
public sealed record CreateWidgetRequest(string? Title, string? ChartType, WidgetConfigInput? Config, WidgetLayout? Layout = null);

/// <summary>Any subset of the widget's fields; what is left out keeps its value.</summary>
public sealed record UpdateWidgetRequest(string? Title = null, string? ChartType = null, WidgetConfigInput? Config = null, WidgetLayout? Layout = null);

/// <param name="Format"><c>csv</c> or <c>xlsx</c>.</param>
public sealed record IndicatorExportRequest(string? Format);
