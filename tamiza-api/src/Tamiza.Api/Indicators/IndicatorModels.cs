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

    public static readonly IReadOnlyList<string> All = [Stat, Bar, Column, Line, Histogram, Pie, Map, Heatmap, Pyramid, Likert, Crosstab];

    public static bool IsKnown(string? chartType) => chartType is not null && All.Contains(chartType);

    /// <summary>The charts that group their rows: they need a column to group by. A stat tile and a histogram may plot the rows as they are.</summary>
    public static bool NeedsGroup(string chartType) => chartType is Bar or Column or Line or Pie or Map or Heatmap or Pyramid or Likert or Crosstab;

    /// <summary>The charts that may split each group by a series column.</summary>
    public static bool TakesSeries(string chartType) => chartType is Bar or Column or Line;

    /// <summary>The charts that are made of a series column: they cannot be saved without one.</summary>
    public static bool NeedsSeries(string chartType) => chartType is Heatmap or Pyramid or Likert or Crosstab;

    /// <summary>The charts whose query has a series column when it names one.</summary>
    public static bool HasSeries(string chartType) => TakesSeries(chartType) || NeedsSeries(chartType);
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
/// How a widget is drawn, apart from its query (design D3 of dashboard palettes): a change here never changes the rows. Each field is
/// left out of the JSON while it is not set, so a config saved without it reads and writes exactly as before. The description and range on
/// the fields are what the MCP tools' input schema shows of them; nothing is validated with them, since
/// <see cref="IndicatorValidation"/> is the rule, and a test keeps them with <see cref="Palettes"/>. The scale names are in the
/// description and not in an enum: the fields are nullable, and a schema whose enum leaves out null would refuse the null that the
/// server accepts for "not set".
/// </summary>
/// <param name="Color">Bar, column and line with no series, and histogram only: the number, 1 to <see cref="Palettes.ColorCount"/>, of the dashboard palette's color the widget is drawn in; left out, color 1.</param>
/// <param name="Ramp">Map and heat map only: one of <see cref="Palettes.Scales"/>; left out, the dashboard palette's own scale.</param>
public sealed record WidgetDisplay(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("A place in the dashboard's palette, 1 to 8, never a color code. For a bar, column or line chart with no seriesBy and for a histogram; left out, color 1."),
     Range(1, Palettes.ColorCount)] int? Color = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull),
     Description("A sequential scale: blue, teal, green, orange, red, purple or gray, matched exactly in lower case. For a map or heatmap only; left out, the palette's own scale.")] string? Ramp = null)
{
    /// <summary>The display as saved: an empty scale is none, and a display that sets nothing is no display (null).</summary>
    public WidgetDisplay? Normalize()
    {
        var ramp = string.IsNullOrEmpty(Ramp) ? null : Ramp;
        return Color is null && ramp is null ? null : this with { Ramp = ramp };
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
     Description("How the widget is drawn: its color or its scale. It never changes the rows.")] WidgetDisplay? Display = null)
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
        Display?.Normalize());
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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WidgetDisplay? Display = null)
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
public sealed record IndicatorEntry(
    string Name, string Label, string? Description, string? Unit, string Format, bool Published, DateTimeOffset? PublishedAt, Guid? RunId, long? Rows);

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
public sealed record DashboardView(
    Guid Id, string Name, IReadOnlyList<DashboardFilter> Filters, string Palette, IReadOnlyList<ValueColor> ValueColors,
    IReadOnlyList<Widget> Widgets, int? Version);

/// <summary>One of the project's own dashboards, without its widgets.</summary>
public sealed record DashboardEntry(Guid Id, string Name);

/// <summary>The body that adds, renames or duplicates a dashboard.</summary>
/// <param name="Name">1 to 40 characters once trimmed, stored trimmed.</param>
public sealed record DashboardNameRequest(string? Name);

/// <summary>A new order of the project's dashboards. The API knows nothing of left and right: the page sends the whole list.</summary>
/// <param name="ExpectedIds">The order the member's page shows. When it is not the project's current order, nothing changes.</param>
/// <param name="DashboardIds">The order asked for: the same dashboards, once each.</param>
public sealed record ReorderDashboardsRequest(IReadOnlyList<Guid>? ExpectedIds, IReadOnlyList<Guid>? DashboardIds);

/// <summary>The measure and group column a new widget over the table starts with.</summary>
public sealed record ColumnDefaults(string? Measure, string? GroupBy);

/// <param name="Columns">Every column of the published table, in table order.</param>
/// <param name="Numeric">The columns a widget can plot as a measure or a ratio.</param>
public sealed record IndicatorColumns(bool Published, IReadOnlyList<TableColumn> Columns, IReadOnlyList<string> Numeric, ColumnDefaults Defaults);

/// <summary>One plotted point: <see cref="Value"/> is the measure summed, or the ratio of sums, over the rows that share the group.</summary>
/// <param name="Group">The group column's value as text; null for rows with no value in it and when the query has no group.</param>
/// <param name="Geometry">The geometry column's text from one row of the group; null when none was requested or the row has none.</param>
/// <param name="Rows">Map only: how many rows of the table the group combines (those its query keeps); left out for every other widget.</param>
/// <param name="Tooltip">Map only: one value per tooltip column, in order, as <see cref="IndicatorQueryResponse.TooltipTypes"/> types them: a number, text, true/false, or null for no value.</param>
/// <param name="Series">A widget with a series only: the series value of the cell (<see cref="Rows"/> then counts the table rows it sums). Left out for the rows with no series value and for the folded series.</param>
/// <param name="SeriesKind">A widget with a series only: <see cref="SeriesKinds.Value"/>, <see cref="SeriesKinds.None"/> or <see cref="SeriesKinds.Other"/>.</param>
public sealed record IndicatorRow(
    string? Group,
    double Value,
    string? Geometry,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? Rows = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<object?>? Tooltip = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Series = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SeriesKind = null);

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
/// <param name="Domain">
/// A pie chart only (design D8 of dashboard palettes): the groups its colors and its "Other" slice follow, read from the rows that
/// match the widget's own filters, not the dashboard's. Every group with a total above zero is ranked by total, largest first, the
/// missing group (<c>none</c>) in its place, as the page ranked the slices of a pie before the domain existed. At most
/// <see cref="WidgetConfig.MaxSlices"/> entries: all the groups when there are that many or fewer, otherwise the largest
/// <see cref="WidgetConfig.MaxSlices"/> - 1 followed by <c>other</c>. A group outside it, the missing one included, is part of "Other".
/// Left out for every other widget.
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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<SeriesInfo>? Domain = null);

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
public sealed record IndicatorQueryRequest(
    WidgetConfigInput? Config, string? ChartType = null, IReadOnlyList<AppliedDashboardFilter>? DashboardFilters = null);

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
