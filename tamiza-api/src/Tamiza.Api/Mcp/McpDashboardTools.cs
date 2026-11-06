using System.ComponentModel;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Tamiza.Api.Auth;
using Tamiza.Api.CleanData;
using Tamiza.Api.Data;
using Tamiza.Api.Indicators;
using Tamiza.Api.Pipeline;
using Tamiza.Api.Projects;
using Tamiza.Api.Reports;

namespace Tamiza.Api.Mcp;

/// <summary>The indicator tables widgets plot, and the dashboards and widgets that show them. Writes run the same handlers as the dashboard page.</summary>
[McpServerToolType]
public sealed class McpDashboardTools(
    McpAccessGate gate, McpCallScope call, IOptions<McpOptions> options, TamizaDbContext db, ICurrentUser user, TimeProvider time,
    IndicatorDashboard indicators, IOptions<IndicatorDashboardOptions> dashboardOptions, DashboardVersions versions) : McpToolBase(gate, call, options)
{
    private const string ProjectHelp = "The project's id, from list_projects.";

    private const string ChartTypeHelp =
        "stat, bar, column, line, histogram, pie, map, heatmap (groups as rows, series as columns, cells shaded), pyramid (two series values drawn left and right), " +
        "likert (agreement scale as bars centered on the neutral value), crosstab (a table of groups by series with totals) or " +
        "boxplot (the spread of each row's value per group: quartiles, whiskers and outliers).";

    private const string SeriesHelp =
        "seriesBy is a second column that splits each group into series. It is optional for bar, column and line and required for heatmap, pyramid, likert and crosstab; " +
        "it must differ from groupBy. For bar and column, layout is grouped (side by side, the default), stacked or percent (each bar's shares of its total); stacked and percent need a measure, not a ratio. " +
        "Past 8 series (30 for heatmap and crosstab) the smallest are combined as Other. A pyramid needs pyramid = { left, right }, two values of the series column. " +
        "A likert needs scale = 2 to 11 values of the series column in order, each with a side of negative, neutral or positive (negatives first, at most one neutral, at most 5 per side) and a measure, not a ratio. " +
        "With a series the result lists series, cells ({ group, series, value, formatted, share? }), totals of every group, series and all rows, and notes; rows are not sent. " +
        "A boxplot is not a widget with a series: it takes no seriesBy, and its result lists boxes instead (see the boxplot paragraph).";

    private const string BoxplotHelp =
        "A boxplot summarizes the rows of each group as they are: every row is one value (its measure, or its numerator over its own denominator), never summed, so it needs a table with one row per unit, and groupBy is optional (without it there is one box over every row). " +
        "Its result lists boxes: { group, values, min, q1, median, q3, max, mean, lowerWhisker, upperWhisker, outlierCount, formatted }, with Tukey's whiskers (1.5 times the interquartile range) and the quartiles pandas' quantile gives; the outliers' own values are not returned. " +
        "sort = { by: \"value\", direction } orders the boxes by median; the figures of a count indicator have two decimals. config.display.orientation draws it vertical or horizontal.";
    private const string DashboardHelp = "The dashboard's id, from list_dashboards.";

    private const string ColorsHelp =
        "A dashboard draws its widgets in one palette: tamiza (the default), institutional, earth or vivid, each with eight numbered colors (1 to 8) and a sequential scale for maps and heat maps. " +
        "valueColors keeps a color for a value of a column, [{ column, value, color }] with color 1 to 8, so the value has that color in every widget of the dashboard that colors by that column. " +
        "In a column a value and a color are each kept once. Names are matched exactly, as written: the palette is lower case (Vivid is refused), column is a column's name in lower case (a capital letter is refused), " +
        "and value is matched as exact text, case and spaces included, so female does not color Female.";

    private const string DisplayHelp =
        "config.display = { color, ramp, orientation, labels } sets how the widget is drawn, all optional. color is a place in the dashboard's palette, 1 to 8 (not a color code; 1 when left out), " +
        "for a bar, column or line chart with no seriesBy, for a histogram and for a boxplot. ramp is a sequential scale, one of blue, teal, green, orange, red, purple or gray, " +
        "for a map or heatmap only; left out, the palette's own scale. The scale is matched exactly, in lower case: Green is refused. " +
        "orientation is vertical (the default) or horizontal, matched exactly in lower case, for a boxplot only. " +
        "labels = { column: name } gives up to 10 of the widget's columns a name of 1 to 80 characters, on every kind; left out, a column takes its indicator's label (save_script columns), else its own name. " +
        "A widget with a series takes its colors from its series, and no other kind has a color of its own.";

    /// <summary>The widget's texts (design D12 of the widget context): said once, after <see cref="DisplayHelp"/>, whose pinned sentence stays as it is.</summary>
    private const string ContextHelp =
        "config.display also takes the widget's texts, on every chart type: subtitle (1 to 200 characters), footnote (1 to 500), " +
        "caption = { text } (1 to 200; it replaces the indicator's name wherever the widget names what it plots) or { hidden: true } (no caption line; tooltips and tables still name what is plotted), " +
        "unit (1 to 32; it replaces the indicator's unit for this widget) and showDescription: true (a button that shows the indicator's description). None of them may hold a line break.";

    /// <summary>
    /// A stat tile's statistic and secondary lines (design D8 of improve-stat-cards): said once, after <see cref="ContextHelp"/>, in
    /// <c>create_widget</c>, <c>update_widget</c> and <c>query_indicator</c>. <see cref="DisplayHelp"/>'s pinned sentence stays as it is.
    /// </summary>
    private const string StatHelp =
        "A stat tile sums its rows, or takes the ratio of their sums. config.statistic = median, p25, p75 or mean takes each row as one value instead (its measure, or its numerator over its own denominator), " +
        "for a table with one row per unit, and needs chartType stat. config.display.secondary = [\"n\", \"unfiltered\"] adds the base n and the value without the dashboard filters under the figure.";

    /// <summary>The widget's chart annotations (design D11 of add-chart-annotations): said once, after <see cref="ContextHelp"/>; <see cref="DisplayHelp"/>'s pinned sentence stays as it is.</summary>
    private const string AnnotationsHelp =
        "config.display also takes referenceLines, interpolation and dataLabels. " +
        "referenceLines is up to 4 lines on a bar, column or line: [{ kind, value, label }] with kind mean or median (of the bars drawn; not with a seriesBy), " +
        "overall (the ratio's value over all the widget's rows, as a stat tile with the same filters shows; needs a ratio; over the answers of one question it is only the mean of the bars, not a total) " +
        "or value (a number in the indicator's units, 0.5 for 50%; the only kind with a value; in a percent layout a share from 0 to 1); label is optional, 1 to 40 characters. " +
        "interpolation is linear (the default) or step, for a line only. " +
        "dataLabels true writes each value on a bar, column (no seriesBy or layout grouped), line or pie.";

    /// <summary>A widget's own order of values (design D12 of add-declared-category-order): said once, after <see cref="AnnotationsHelp"/>.</summary>
    private const string OrderHelp =
        "config.display.order = { group, series } lists values, as full text, in the order to draw them (up to 100 each); the others follow in the chart's order. " +
        "group is refused with a sort (a boxplot's median order included), series without a series column and on a pyramid or likert.";

    /// <summary>A widget's base settings and its no-data option (design D14 of add-small-sample-warnings): said once, after <see cref="OrderHelp"/>.</summary>
    private const string BasesHelp =
        "config.display also takes minN and showN, on every chart type: minN is the widget's own small-base threshold, 0 (off for this widget) or 1 to 10000, and the marks whose base n is below it are drawn dimmed; " +
        "showN true or false shows or hides the widget's base n line under it; left out, each follows the dashboard's minN and showN (update_dashboard). " +
        "A mark has a base n when it is a ratio (its denominator) or its indicator declares a base column, and the widget has an n, and so an n line, only when its indicator declares a base column or answer columns (save_script columns: base, categories). " +
        "config.missingFooter true leaves out of a bar, column, line, pie, heatmap, pyramid, likert or crosstab the rows whose group or series value its indicator declares as meaning no data (save_script columns: missing) " +
        "and says what they held in noData; it changes the rows, and is refused on another kind.";

    private const string SourceHelp =
        "A dashboard's source says where its data comes from, such as the survey (1 to 300 characters, no line break); an empty text removes it.";

    /// <summary>The dashboard's base settings (design D14 of add-small-sample-warnings).</summary>
    private const string DashboardBasesHelp =
        "minN is the dashboard's small-base threshold, a whole number from 1 to 10000 (0 turns it off): the marks of its widgets whose base n is below it are drawn dimmed and get_widget_data marks them smallBase, unless a widget sets its own display.minN. " +
        "showN true shows each widget's base n line under it, false hides them, unless a widget sets its own display.showN. Set them only when the person asks or their study's specification does.";

    /// <summary>What only a change of an existing widget needs: a new chart type alone keeps the old config, display included.</summary>
    private const string ChartTypeChangeHelp =
        "To change the chart type of a widget that has a color, a scale, an orientation, reference lines, steps, data labels, an order, no-data values left out, a statistic or lines under its figure, also send a config without the display fields the new type does not take: " +
        "a new chartType alone keeps the old config, so it is refused, with an error on config.display.color, config.display.ramp, config.display.orientation, " +
        "config.display.referenceLines, config.display.interpolation, config.display.dataLabels, config.display.order, config.missingFooter, config.statistic or config.display.secondary, when the new type does not take them.";

    /// <summary>The most characters of a request's <c>change</c>, as <c>dashboard_versions.note</c> keeps it (design D1, D10 of dashboard versions).</summary>
    internal const int MaxChangeLength = 120;

    /// <summary>What every dashboard write says of <c>change</c>, the request its versions are grouped by (design D4, D10 of dashboard versions).</summary>
    internal const string ChangeHelp =
        "What the person asked for, in a few words (1 to 120 characters), the same on every write you make for that request, for example \"Add five water indicators\". " +
        "Writes with the same change on a dashboard make one version of it.";

    // Indicators

    [McpServerTool(Name = "list_indicators", ReadOnly = true, Idempotent = true, OpenWorld = false, Title = "List the indicators"),
     Description("The project's indicators with their label, description, unit, format, the labels of their columns (columns), with the base, no-data and answer columns they declare, whether each has published a table, how many rows it has and when, and the limits of dashboards (widgets per dashboard, rows per widget, dashboards per project).")]
    public Task<CallToolResult> ListIndicators([Description(ProjectHelp)] Guid projectId, CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            await Gate.RequireAsync(projectId, ProjectRole.Viewer, McpNeeds.Summaries, ct);
            return McpResults.Unwrap<IndicatorOverview>(await IndicatorEndpoints.GetOverviewAsync(projectId, indicators, db, dashboardOptions, ct));
        });

    [McpServerTool(Name = "get_indicator_columns", ReadOnly = true, Idempotent = true, OpenWorld = false, Title = "Get the columns of an indicator's table"),
     Description("Every column of one indicator's published table, with its label when its indicator gives it one (and its order, last and stripPrefix when it declares them), what it declares about bases (declared: base, missing and categories by column name), the numeric columns a widget can plot as a measure, and the defaults a new widget would use (measure and group column). Needs the indicator to have published.")]
    public Task<CallToolResult> GetIndicatorColumns(
        [Description(ProjectHelp)] Guid projectId, [Description("The indicator's name, from list_indicators.")] string name, CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            await Gate.RequireAsync(projectId, ProjectRole.Viewer, McpNeeds.Summaries, ct);
            return McpResults.Unwrap<IndicatorColumns>(await IndicatorEndpoints.GetColumnsAsync(projectId, name, indicators, db, ct));
        });

    [McpServerTool(Name = "query_indicator", ReadOnly = true, Idempotent = true, OpenWorld = false, Title = "Preview what a widget would plot"),
     Description("Runs a widget query without saving anything and returns what the widget would plot: the measure summed (or the ratio of two columns' sums) per group, or for a boxplot the spread of the rows of each group, formatted with the indicator's format and unit. " +
                 "Use it before create_widget. config = { indicator, measure | ratio: { numerator, denominator }, groupBy, seriesBy, layout, pyramid: { left, right }, scale: [{ value, side }], filters: [{ column, op, value | values | min | max }], geometryColumn, buckets, missingFooter, statistic, sort: { by: \"value\", direction } for a boxplot, display: { labels, referenceLines, order } }. " +
                 "A ratio's rows carry its numerator, denominator and n (the base n: the sum of the indicator's base column over the value's rows, else the denominator; a measure with no base column has none), and so do the totals of a widget with a series, whose cells carry n. " +
                 "n at the top is the widget's own n, given only when its indicator declares a base column or answer columns; with config.missingFooter, noData: { values, value, formatted, share, n } gives the values the widget left out and what they held. " +
                 "The result's labels name the columns the widget would show (display.labels over the indicator's labels), for those that have a name other than their own. " +
                 "With a chartType and display.referenceLines, the result's referenceLines gives each line drawn: { kind, label, value, formatted } (an overall line is the figure a stat tile with the same filters shows). " +
                 "A bar, column, line, pie, map, heatmap, pyramid, likert or crosstab needs groupBy; a stat tile, a histogram and a boxplot do not. " + SeriesHelp + " " + BoxplotHelp + " " + StatHelp + " " +
                 "A stat tile with a statistic returns it as statistic, with its value and its n (the number of values, or the sum of the indicator's base column) and no numerator or denominator; a preview sets no dashboard filter, so it never gives a value without them. " +
                 "A ratio over the answers of one question that the indicator declares (save_script columns: categories) counts a single-choice base once across the answers, and for a multiple-choice question returns combinedCategories, the column, with a message and no rows. " +
                 "At most the widget row limit of rows come back and the result says when it is reached.")]
    public Task<CallToolResult> QueryIndicator(
        [Description(ProjectHelp)] Guid projectId,
        [Description("The widget's query.")] WidgetConfigInput config,
        [Description("Optional. " + ChartTypeHelp + " It changes what a missing groupBy means and which series a pyramid or a Likert widget keeps.")] string? chartType = null,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            await Gate.RequireAsync(projectId, ProjectRole.Viewer, McpNeeds.Summaries, ct);
            // A preview of a config with an overall line asks for the overall value, as a saved widget does (design D11); no tool argument sets it.
            var normalized = config.Normalize();
            var overall = chartType is not null && WidgetQuery.Overall(chartType, normalized);
            var response = McpResults.Unwrap<IndicatorQueryResponse>(
                await IndicatorEndpoints.QueryAsync(projectId, new IndicatorQueryRequest(config, chartType, Overall: overall ? true : null), indicators, ct));
            var meta = await MetaAsync(projectId, ct);
            var info = meta.GetValueOrDefault(config.Indicator ?? "");
            // What the widget would declare for the order of its values (add-declared-category-order, design D12): its own order over the
            // indicator's. Null when it declares nothing, and the result is then built as it always was.
            var orders = CategoryOrder.For(chartType ?? ChartTypes.Bar, normalized, info?.Columns);
            var series = response.Series is null ? null : SeriesPayload.From(response, chartType ?? ChartTypes.Bar, config.Layout, info, orders);
            var boxes = response.Boxes is null ? null : BoxPayload.From(response.Boxes, info, orders);
            // The note of an overall line that is not drawn over answers; a preview with no such line returns the notes it always did.
            var overallNote = chartType is not null && ReferenceLines.OverallLeftOut(chartType, normalized, BaseFigures.AnswerColumnsOf(info?.Columns))
                ? ReportText.For("en-US")["noOverall"]
                : null;
            return new
            {
                indicator = config.Indicator,
                label = info?.Label ?? config.Indicator,
                format = info?.Format,
                unit = info?.Unit,
                labels = LabelsOf(chartType ?? ChartTypes.Bar, normalized, info),
                // A preview that plots nothing draws no line: no rows, no key, as the widget data and the email do.
                referenceLines = chartType is null || response.Rows.Count == 0 ? null : ReferenceLinesOf(chartType, normalized, response, info),
                // A stat tile's statistic, when it has one (design D8 of improve-stat-cards); a preview sets no flag, so it has no unfiltered value.
                statistic = chartType == ChartTypes.Stat ? normalized.Statistic : null,
                response.Published,
                missing = response.Missing.Any ? response.Missing : null,
                response.RowLimitReached,
                // A ratio that would add up several answers of a multiple-choice question: the column and why it has no rows.
                combinedCategories = response.CombinedCategories,
                message = response.CombinedCategories is { } combined ? CombinedMessage(combined) : null,
                // The widget's n and what it left out as no data; a preview has no dashboard, so no threshold (design D14).
                n = response.N,
                noData = NoDataOf(response.NoData, info),
                rows = series is null && boxes is null ? RowsOf(response.Rows, chartType ?? ChartTypes.Bar, orders).Select(r => Row(r, info)).ToList() : null,
                boxes,
                series = series?.Series,
                cells = series?.Cells,
                totals = series?.Totals,
                notes = overallNote is null ? series?.Notes : [.. series?.Notes ?? [], overallNote],
            };
        }, hint: "Add a filter or group by a column with fewer values.");

    // Dashboards, read

    [McpServerTool(Name = "list_dashboards", ReadOnly = true, Idempotent = true, OpenWorld = false, Title = "List the dashboards"),
     Description("The project's own dashboards in their order (1 to 10), with how many widgets and filters each has and the number of its current version. Performance and Clean data are permanent and not listed.")]
    public Task<CallToolResult> ListDashboards([Description(ProjectHelp)] Guid projectId, CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            await Gate.RequireAsync(projectId, ProjectRole.Viewer, McpNeeds.Summaries, ct);
            var rows = await db.Dashboards.AsNoTracking().Where(d => d.ProjectId == projectId).OrderBy(d => d.Position)
                .Select(d => new
                {
                    d.Id,
                    d.Name,
                    d.Position,
                    d.Filters,
                    Widgets = db.DashboardWidgets.Count(w => w.DashboardId == d.Id),
                    Version = db.DashboardVersions.Where(v => v.ProjectId == projectId && v.DashboardId == d.Id).Max(v => (int?)v.Number),
                })
                .ToListAsync(ct);
            return new
            {
                maxDashboards = IndicatorDashboardOptions.MaxDashboards,
                maxWidgetsPerDashboard = dashboardOptions.Value.MaxWidgetsPerDashboard,
                dashboards = rows.Select(d => new
                {
                    id = d.Id, name = d.Name, position = d.Position + 1, widgets = d.Widgets, filters = DashboardEndpoints.ReadFilters(d.Filters).Count, version = d.Version,
                }).ToList(),
            };
        });

    [McpServerTool(Name = "get_dashboard", ReadOnly = true, Idempotent = true, OpenWorld = false, Title = "Get a dashboard"),
     Description("One dashboard with its filters, its palette and the colors it keeps by value (palette, valueColors), its source when it has one, its small-base threshold and base n line setting (minN, showN) when it has set them, its widgets in reading order (each widget's id, title, chart type, query and display (config) and layout on the 12-column grid) and the number of its current version, which restore_dashboard_version needs as baseVersion. Use get_widget_data for what the widgets show.")]
    public Task<CallToolResult> GetDashboard(
        [Description(ProjectHelp)] Guid projectId, [Description(DashboardHelp)] Guid dashboardId, CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            await Gate.RequireAsync(projectId, ProjectRole.Viewer, McpNeeds.Summaries, ct);
            return McpResults.Unwrap<DashboardView>(await DashboardEndpoints.GetDashboardAsync(projectId, dashboardId, db, ct));
        }, hint: "Use get_widget_data with a widgetId for one widget.");

    [McpServerTool(Name = "get_widget_data", ReadOnly = true, Idempotent = true, OpenWorld = false, Title = "Get what the widgets show"),
     Description("What the widgets of a dashboard (or one widget) show, as a member opening the dashboard sees it: for each widget its rows (group and value, with the value formatted as the dashboard formats it), " +
                 "or the message the widget shows when its indicator has not published, a column is gone or there are no rows. A widget with a series (seriesBy) returns series, cells ({ group, series, value, formatted, share? }), totals and notes instead of rows, and a boxplot returns boxes instead of rows. " +
                 "Each widget's labels gives the name shown for each column it names (its measure or ratio, group, series and map tooltip columns) that has a label, its own or its indicator's; a widget whose columns have none has no labels. " +
                 "Each widget's result also gives its subtitle, footnote and own caption text when it has them, and its values are formatted with the widget's own unit when it has one. " +
                 "A bar, column or line with reference lines also gives referenceLines: { kind, label, value, formatted } for each line drawn. " +
                 "A ratio's rows carry its numerator, denominator and n (the base n: the sum of the indicator's base column, else the denominator), the cells of a widget with a series carry n and its totals the three figures, " +
                 "and a row, cell or box whose n is below the small-base threshold in force has smallBase: true, with a note that counts them. " +
                 "Each widget also gives its own n (only when its indicator declares a base column or answer columns; a stat tile with a statistic always has one), the minN and showN in force when set, and, with config.missingFooter, noData: { values, value, formatted, share, n } for the values it left out. " +
                 "A stat tile gives its statistic when it has one and, while a dashboard filter narrows it and it shows the line (config.display.secondary has unfiltered), unfiltered: { value, formatted }, its value without the dashboard filters; no argument sets that line. " +
                 "A widget that would add up several answers of a multiple-choice question the indicator declares has status combinedCategories, a message naming the column and no rows. " +
                 "Dashboard filters are at their defaults unless you pass filterValues: [{ column, values | min | max }].")]
    public Task<CallToolResult> GetWidgetData(
        [Description(ProjectHelp)] Guid projectId,
        [Description(DashboardHelp)] Guid dashboardId,
        [Description("Optional. One widget's id; every widget of the dashboard when omitted.")] Guid? widgetId = null,
        [Description("Optional. Values for the dashboard's filters in place of their defaults.")] AppliedDashboardFilter[]? filterValues = null,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            await Gate.RequireAsync(projectId, ProjectRole.Viewer, McpNeeds.Summaries, ct);
            var dashboard = await db.Dashboards.AsNoTracking().Where(d => d.Id == dashboardId && d.ProjectId == projectId).Select(d => new { d.Name, d.Filters, d.MinN, d.ShowN }).SingleOrDefaultAsync(ct)
                ?? throw McpRefusal.Failed("The project has no such dashboard. Call list_dashboards.", StatusCodes.Status404NotFound, DashboardEndpoints.NotFoundCode);
            var filters = DashboardEndpoints.ReadFilters(dashboard.Filters);
            var widgets = await DashboardEndpoints.ReadWidgetsAsync(db, dashboardId, ct);
            if (widgetId is { } only)
            {
                widgets = widgets.Where(w => w.Id == only).ToList();
                if (widgets.Count == 0)
                {
                    throw McpRefusal.NotFound("The widget", "Call get_dashboard to see the dashboard's widgets.");
                }
            }

            var meta = await MetaAsync(projectId, ct);
            var results = new List<object>();
            foreach (var widget in widgets)
            {
                results.Add(await WidgetDataAsync(projectId, widget, filters, filterValues, meta.GetValueOrDefault(widget.Config.Indicator), dashboard.MinN, dashboard.ShowN, ct));
            }

            return new { dashboard = dashboard.Name, filters, widgets = results };
        }, hint: "Ask for one widget with widgetId.");

    // Dashboards, write

    [McpServerTool(Name = "create_dashboard", Title = "Create a dashboard", Idempotent = false, OpenWorld = false, Destructive = false),
     Description("Adds a dashboard of the project's own (at most 10, names unique whatever their case, 1 to 40 characters, never Performance, Clean data or Settings). It starts empty and goes last unless you give a position (1 = first).")]
    public Task<CallToolResult> CreateDashboard(
        [Description(ProjectHelp)] Guid projectId,
        [Description("The dashboard's name.")] string name,
        [Description(ChangeHelp)] string change,
        [Description("Optional. Where to put it, 1 for the first place.")] int? position = null,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            var request = RequireChange(change);
            await Gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, ct);
            // A move refused after the add, such as dashboards.orderChanged, still leaves the dashboard its version 1 (design D3).
            var (created, recorded) = await RecordAsync(request, async () =>
            {
                var entry = McpResults.Unwrap<DashboardEntry>(await DashboardEndpoints.AddAsync(projectId, new DashboardNameRequest(name), db, versions, user, time, ct));
                if (position is { } place)
                {
                    await MoveAsync(projectId, entry.Id, place, ct);
                }

                return entry;
            });
            return new
            {
                created.Id,
                created.Name,
                position = await PositionOfAsync(projectId, created.Id, ct),
                dashboardVersions = await VersionsOfAsync(projectId, recorded, [created.Id], ct),
            };
        });

    [McpServerTool(Name = "update_dashboard", Title = "Rename, move or set the filters, colors, source or base sizes of a dashboard", Idempotent = true, OpenWorld = false, Destructive = false),
     Description("Renames a dashboard, moves it to a position (1 = first), replaces its filters (up to 8; each { column, label, kind: text | boolean | number | date, default? }), sets its colors: its palette and the colors it keeps by value (up to 64), sets its source, " +
                 "or sets its small-base threshold (minN, 0 turns it off) and whether it shows each widget's base n line (showN). " +
                 "Give any of name, position, filters, palette, valueColors, source, minN and showN; what you leave out keeps its value. " +
                 "A filter narrows every widget whose indicator table has its column with a type of its kind. " + ColorsHelp + " " + SourceHelp + " " + DashboardBasesHelp + " " +
                 "A color, source or base-size change is one more commit of the same request, so a call that renames and sets colors, a source or a threshold makes one version.")]
    public Task<CallToolResult> UpdateDashboard(
        [Description(ProjectHelp)] Guid projectId,
        [Description(DashboardHelp)] Guid dashboardId,
        [Description(ChangeHelp)] string change,
        [Description("Optional. The new name.")] string? name = null,
        [Description("Optional. The new place, 1 for the first.")] int? position = null,
        [Description("Optional. The whole list of filters, replacing the current one; an empty list removes them all.")] DashboardFilterInput[]? filters = null,
        [Description("Optional. The dashboard's palette: tamiza, institutional, earth or vivid, in lower case exactly as written (Vivid is refused). The colors kept by value stay as they are unless you also give valueColors.")] string? palette = null,
        [Description("Optional. The whole list of colors kept by value, [{ column, value, color }] with color 1 to 8, replacing the current one; an empty list removes them all. The palette stays as it is unless you also give palette.")] ValueColorInput[]? valueColors = null,
        [Description("Optional. Where the dashboard's data comes from, such as the survey: 1 to 300 characters with no line break. Left out, the source stays as it is; an empty text removes it.")] string? source = null,
        [Description("Optional. The small-base threshold: a whole number from 1 to 10000, below which a mark's base n is small and drawn dimmed; 0 turns it off. Left out, it stays as it is.")] int? minN = null,
        [Description("Optional. true shows each widget's base n line under it, false hides it. Left out, it stays as it is.")] bool? showN = null,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            var request = RequireChange(change);
            await Gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, ct);
            if (name is null && position is null && filters is null && palette is null && valueColors is null && source is null && minN is null && showN is null)
            {
                throw McpRefusal.Validation(new Dictionary<string, string[]> { ["name"] = ["Give a name, a position, filters, a palette, colors kept by value, a source, a small-base threshold or a base n line to change."] });
            }

            // Up to six commits: a rename that is saved before the filters, the colors, the source or the base settings are refused is still recorded (design D3).
            var recorded = await RecordAsync(request, async () =>
            {
                if (name is not null)
                {
                    McpResults.Unwrap(await DashboardEndpoints.RenameAsync(projectId, dashboardId, new DashboardNameRequest(name), db, versions, user, time, ct));
                }

                if (filters is not null)
                {
                    McpResults.Unwrap(await DashboardEndpoints.SaveFiltersAsync(projectId, dashboardId, new DashboardFiltersRequest(filters), db, versions, user, time, ct));
                }

                if (palette is not null || valueColors is not null)
                {
                    await SaveColorsAsync(projectId, dashboardId, palette, valueColors, ct);
                }

                if (source is not null)
                {
                    McpResults.Unwrap(await DashboardEndpoints.SaveSourceAsync(projectId, dashboardId, new DashboardSourceRequest(source), db, versions, user, time, ct));
                }

                if (minN is not null || showN is not null)
                {
                    await SaveBasesAsync(projectId, dashboardId, minN, showN, ct);
                }

                if (position is { } place)
                {
                    await MoveAsync(projectId, dashboardId, place, ct);
                }
            });

            var current = await db.Dashboards.AsNoTracking().Where(d => d.Id == dashboardId && d.ProjectId == projectId).Select(d => new { d.Name, d.Filters, d.Palette, d.ValueColors, d.Source, d.MinN, d.ShowN }).SingleOrDefaultAsync(ct);
            return new
            {
                id = dashboardId,
                name = current?.Name,
                position = await PositionOfAsync(projectId, dashboardId, ct),
                filters = current is null ? null : DashboardEndpoints.ReadFilters(current.Filters),
                palette = current?.Palette,
                valueColors = current is null ? null : DashboardEndpoints.ReadValueColors(current.ValueColors),
                source = current?.Source,
                // As get_dashboard writes them: the threshold when set, the line only when on.
                minN = current?.MinN,
                showN = current?.ShowN is true ? true : (bool?)null,
                dashboardVersions = await VersionsOfAsync(projectId, recorded, [dashboardId], ct),
            };
        });

    [McpServerTool(Name = "duplicate_dashboard", Title = "Duplicate a dashboard", Idempotent = false, OpenWorld = false, Destructive = false),
     Description("Copies a dashboard with all its widgets, filters, palette, colors kept by value and source right after the original, under a new name. Fails at 10 dashboards.")]
    public Task<CallToolResult> DuplicateDashboard(
        [Description(ProjectHelp)] Guid projectId,
        [Description(DashboardHelp)] Guid dashboardId,
        [Description("The copy's name.")] string name,
        [Description(ChangeHelp)] string change,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            var request = RequireChange(change);
            await Gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, ct);
            var (copy, recorded) = await RecordAsync(request, async () => McpResults.Unwrap<DashboardEntry>(
                await DashboardEndpoints.DuplicateAsync(projectId, dashboardId, new DashboardNameRequest(name), db, versions, user, dashboardOptions, time, ct)));
            return new
            {
                copy.Id,
                copy.Name,
                position = await PositionOfAsync(projectId, copy.Id, ct),
                dashboardVersions = await VersionsOfAsync(projectId, recorded, [copy.Id], ct),
            };
        });

    [McpServerTool(Name = "remove_dashboard", Title = "Remove a dashboard", Idempotent = false, OpenWorld = false, Destructive = true),
     Description("Removes a dashboard and all its widgets. This cannot be undone, and its versions are removed with it. Pass the dashboard's current name as expectedName so a wrong id cannot remove the wrong dashboard. A project keeps at least one dashboard.")]
    public Task<CallToolResult> RemoveDashboard(
        [Description(ProjectHelp)] Guid projectId,
        [Description(DashboardHelp)] Guid dashboardId,
        [Description("The dashboard's current name, exactly as list_dashboards shows it (case and surrounding spaces are ignored).")] string expectedName,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            await Gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, ct);
            var current = await db.Dashboards.AsNoTracking().Where(d => d.Id == dashboardId && d.ProjectId == projectId).Select(d => d.Name).SingleOrDefaultAsync(ct);
            if (current is not null)
            {
                RequireName(current, expectedName);
            }

            McpResults.Unwrap(await DashboardEndpoints.RemoveAsync(projectId, dashboardId, db, ct));
            return new { removed = true, name = current };
        });

    // Widgets

    [McpServerTool(Name = "create_widget", Title = "Add a widget to a dashboard", Idempotent = false, OpenWorld = false, Destructive = false),
     Description("Adds a widget: a stat tile, bar, column, line, histogram, pie chart, map, heat map, population pyramid, diverging Likert chart, cross table or box plot, with a title and a query over one indicator's table. Check the query with query_indicator first. " +
                 "config = { indicator, measure | ratio: { numerator, denominator }, groupBy, seriesBy, layout, pyramid: { left, right }, scale: [{ value, side }], filters, geometryColumn, buckets, sort, tooltip, missingFooter, statistic, display: { color, ramp, orientation, labels, subtitle, footnote, caption, unit, showDescription, referenceLines, interpolation, dataLabels, order, minN, showN, secondary } }. " +
                 "Every kind but stat tile, histogram and box plot needs groupBy; a map also needs geometryColumn (WKT in degrees). sort orders a bar or column, and a boxplot by value (its median) only. " + SeriesHelp + " " + BoxplotHelp + " " + DisplayHelp + " " + ContextHelp + " " + StatHelp + " " + AnnotationsHelp + " " + OrderHelp + " " + BasesHelp + " " +
                 "It goes under the dashboard's widgets unless you give layout = { x, y, w, h } on the 12-column grid. A dashboard holds a limited number of widgets.")]
    public Task<CallToolResult> CreateWidget(
        [Description(ProjectHelp)] Guid projectId,
        [Description(DashboardHelp)] Guid dashboardId,
        [Description("The widget's title, up to 200 characters.")] string title,
        [Description(ChartTypeHelp)] string chartType,
        [Description("The widget's query, and optionally its display (color, scale, orientation, column labels, texts, reference lines, steps, data labels, the order of its values, its small-base threshold and base n line, leaving out no-data values, or a stat tile's statistic and lines).")] WidgetConfigInput config,
        [Description(ChangeHelp)] string change,
        [Description("Optional. Position and size on the grid.")] WidgetLayout? layout = null,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            var request = RequireChange(change);
            await Gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, ct);
            var (widget, recorded) = await RecordAsync(request, async () => McpResults.Unwrap<Widget>(await DashboardEndpoints.CreateWidgetAsync(
                projectId, dashboardId, new CreateWidgetRequest(title, chartType, config, layout), indicators, db, versions, user, dashboardOptions, time, ct)));
            return WithVersions(widget, await VersionsOfAsync(projectId, recorded, [dashboardId], ct));
        });

    [McpServerTool(Name = "update_widget", Title = "Change or move a widget", Idempotent = true, OpenWorld = false, Destructive = false),
     Description("Changes a widget's title, chart type, query or layout, or moves it to another dashboard of the project with moveToDashboardId (it lands under that dashboard's widgets). What you leave out keeps its value; a new config replaces the old one whole, including its series settings and its display. " + DisplayHelp + " " + ContextHelp + " " + StatHelp + " " + AnnotationsHelp + " " + OrderHelp + " " + BasesHelp + " " + ChartTypeChangeHelp)]
    public Task<CallToolResult> UpdateWidget(
        [Description(ProjectHelp)] Guid projectId,
        [Description(DashboardHelp)] Guid dashboardId,
        [Description("The widget's id, from get_dashboard.")] Guid widgetId,
        [Description(ChangeHelp)] string change,
        [Description("Optional. The new title.")] string? title = null,
        [Description("Optional. The new chart type: " + ChartTypeHelp)] string? chartType = null,
        [Description("Optional. The new query and display (color, scale, orientation, column labels, texts, reference lines, steps, data labels, the order of its values, its small-base threshold and base n line, leaving out no-data values, or a stat tile's statistic and lines), replacing the old ones.")] WidgetConfigInput? config = null,
        [Description("Optional. The new position and size on the grid.")] WidgetLayout? layout = null,
        [Description("Optional. Another dashboard of the project to move the widget to.")] Guid? moveToDashboardId = null,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            var request = RequireChange(change);
            await Gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, ct);
            var (widget, recorded) = await RecordAsync(request, async () => McpResults.Unwrap<Widget>(await DashboardEndpoints.UpdateWidgetAsync(
                projectId, dashboardId, widgetId, new UpdateWidgetRequest(title, chartType, config, layout, moveToDashboardId), indicators, db, versions, user, dashboardOptions, time, ct)));
            // A move names both dashboards: the one it left and the one it is on now.
            return WithVersions(widget, await VersionsOfAsync(projectId, recorded, [dashboardId, widget.DashboardId], ct));
        });

    [McpServerTool(Name = "remove_widget", Title = "Remove a widget", Idempotent = false, OpenWorld = false, Destructive = true),
     Description("Removes a widget. Pass the widget's current title as expectedTitle so a wrong id cannot remove the wrong widget. " +
                 "The dashboard's kept versions still hold it. To bring back only this widget, read it with get_dashboard_version and add it again with create_widget; " +
                 "restore_dashboard_version brings back the whole dashboard and also undoes every later change.")]
    public Task<CallToolResult> RemoveWidget(
        [Description(ProjectHelp)] Guid projectId,
        [Description(DashboardHelp)] Guid dashboardId,
        [Description("The widget's id, from get_dashboard.")] Guid widgetId,
        [Description("The widget's current title (case and surrounding spaces are ignored).")] string expectedTitle,
        [Description(ChangeHelp)] string change,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            var request = RequireChange(change);
            await Gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, ct);
            var current = await db.DashboardWidgets.AsNoTracking().Where(w => w.Id == widgetId && w.DashboardId == dashboardId && w.ProjectId == projectId).Select(w => w.Title).SingleOrDefaultAsync(ct);
            if (current is not null)
            {
                RequireName(current, expectedTitle);
            }

            var recorded = await RecordAsync(request, async () =>
            {
                McpResults.Unwrap(await DashboardEndpoints.DeleteWidgetAsync(projectId, dashboardId, widgetId, db, versions, ct));
            });
            return new { removed = true, title = current, dashboardVersions = await VersionsOfAsync(projectId, recorded, [dashboardId], ct) };
        });

    // Versions (design D3, D10 of dashboard versions)

    /// <summary>
    /// The request a dashboard write is made for, trimmed: 1 to <see cref="MaxChangeLength"/> characters (design D10). It is checked before
    /// anything else runs, so a call refused for it changes nothing.
    /// </summary>
    internal static string RequireChange(string? change)
    {
        var request = change?.Trim();
        if (string.IsNullOrEmpty(request) || request.Length > MaxChangeLength)
        {
            throw McpRefusal.Validation(new Dictionary<string, string[]>
            {
                ["change"] = [$"Say in 1 to {MaxChangeLength} characters what the person asked for, the same on every write you make for that request, for example \"Add five water indicators\"."],
            });
        }

        return request;
    }

    /// <summary>What the flush after a write recorded: the version it answered for each dashboard, and the dashboards the write noted.</summary>
    private sealed record RecordedVersions(IReadOnlyList<DashboardVersionNumber> Numbers, IReadOnlyCollection<Guid> Noted);

    /// <summary>
    /// Runs a write's handler calls and then, in a <c>finally</c>, records one version of each dashboard they changed, made through MCP
    /// by the person for the request <paramref name="change"/> (design D3). So a call refused after part of it was saved, such as a
    /// rename followed by filters that are not valid, still records what was saved, as a call that succeeded would. The flush never
    /// throws and never changes the call's answer.
    /// </summary>
    private async Task<(T Value, RecordedVersions Recorded)> RecordAsync<T>(string change, Func<Task<T>> write)
    {
        IReadOnlyCollection<Guid> noted;
        IReadOnlyList<DashboardVersionNumber> numbers;
        T value;
        try
        {
            value = await write();
        }
        finally
        {
            noted = versions.Noted;
            numbers = await versions.FlushAsync(DashboardVersionOrigin.Mcp, user.Id, change);
        }

        return (value, new RecordedVersions(numbers, noted));
    }

    private async Task<RecordedVersions> RecordAsync(string change, Func<Task> write) =>
        (await RecordAsync(change, async () =>
        {
            await write();
            return true;
        })).Recorded;

    /// <summary>
    /// The current version of each of <paramref name="dashboards"/>, as a write's result gives them in <c>dashboardVersions</c> (design
    /// D10): the version the flush recorded for a dashboard the write changed, and the newest of one it left as it was, such as a move
    /// that only changed the order. A dashboard that is gone, has no version, or whose version could not be recorded is left out, so a
    /// number given always holds the write.
    /// </summary>
    private async Task<List<DashboardVersionNumber>> VersionsOfAsync(
        Guid projectId, RecordedVersions recorded, IEnumerable<Guid> dashboards, CancellationToken ct)
    {
        var numbers = new List<DashboardVersionNumber>();
        foreach (var dashboardId in dashboards.Distinct())
        {
            if (recorded.Numbers.FirstOrDefault(n => n.DashboardId == dashboardId) is { } number)
            {
                numbers.Add(number);
            }
            else if (!recorded.Noted.Contains(dashboardId) && await DashboardVersions.CurrentAsync(db, projectId, dashboardId, ct) is { } current)
            {
                numbers.Add(new DashboardVersionNumber(dashboardId, current));
            }
        }

        return numbers;
    }

    /// <summary>A widget as the REST API answers it, with the current version of each dashboard the write changed.</summary>
    private static object WithVersions(Widget widget, IReadOnlyList<DashboardVersionNumber> dashboardVersions) => new
    {
        widget.Id,
        widget.DashboardId,
        widget.Title,
        widget.ChartType,
        widget.Config,
        widget.Layout,
        widget.UpdatedAt,
        dashboardVersions,
    };

    // Helpers

    /// <summary>A destructive call must name what it removes, so a wrong id cannot remove the wrong thing (design D9).</summary>
    private static void RequireName(string current, string expected)
    {
        if (!string.Equals(current.Trim(), expected?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new McpRefusal(
                McpRefusal.NameMismatchCode,
                $"The name does not match: this is \"{current}\", not \"{expected}\". Nothing was removed.",
                new Dictionary<string, object?> { ["current"] = current });
        }
    }

    /// <summary>Moves a dashboard to a 1-based place among the project's dashboards, with the order the writer just read as the expected one.</summary>
    private async Task MoveAsync(Guid projectId, Guid dashboardId, int position, CancellationToken ct)
    {
        var order = await db.Dashboards.AsNoTracking().Where(d => d.ProjectId == projectId).OrderBy(d => d.Position).Select(d => d.Id).ToListAsync(ct);
        if (!order.Contains(dashboardId))
        {
            return;
        }

        var wanted = order.Where(id => id != dashboardId).ToList();
        wanted.Insert(Math.Clamp(position - 1, 0, wanted.Count), dashboardId);
        if (wanted.SequenceEqual(order))
        {
            return;
        }

        McpResults.Unwrap(await DashboardEndpoints.ReorderAsync(projectId, new ReorderDashboardsRequest(order, wanted), db, ct));
    }

    /// <summary>
    /// Saves a dashboard's colors with the handler of the colors endpoint, so the rules and their error keys are the same (design D10 of
    /// dashboard palettes). The endpoint takes the palette and the colors kept by value together, so a call that names only one of them
    /// keeps the other as the dashboard has it. The save is one more commit of the request: it notes <c>colorsChanged</c> and the flush
    /// that follows makes the request's one version.
    /// </summary>
    private async Task SaveColorsAsync(Guid projectId, Guid dashboardId, string? palette, ValueColorInput[]? valueColors, CancellationToken ct)
    {
        IReadOnlyList<ValueColorInput?>? entries = valueColors;
        if (palette is null || valueColors is null)
        {
            var stored = await db.Dashboards.AsNoTracking().Where(d => d.Id == dashboardId && d.ProjectId == projectId)
                .Select(d => new { d.Palette, d.ValueColors }).SingleOrDefaultAsync(ct);
            // A dashboard that is gone is refused as not found by the handler.
            if (stored is not null)
            {
                palette ??= stored.Palette;
                entries ??= DashboardEndpoints.ReadValueColors(stored.ValueColors).Select(c => new ValueColorInput(c.Column, c.Value, c.Color)).ToList();
            }
        }

        McpResults.Unwrap(await DashboardEndpoints.SaveColorsAsync(projectId, dashboardId, new DashboardColorsRequest(palette, entries), db, versions, user, time, ct));
    }

    /// <summary>
    /// Saves a dashboard's base settings with the handler of the bases endpoint, so the rules and the error key are the same (design D8, D14
    /// of add-small-sample-warnings). The endpoint takes both whole, so a call that names one keeps the other as the dashboard has it, and a
    /// <c>minN</c> of 0 turns the threshold off, as the form's empty field does. The save is one more commit of the request: it notes
    /// <c>basesChanged</c> and the flush that follows makes the request's one version.
    /// </summary>
    private async Task SaveBasesAsync(Guid projectId, Guid dashboardId, int? minN, bool? showN, CancellationToken ct)
    {
        var stored = await db.Dashboards.AsNoTracking().Where(d => d.Id == dashboardId && d.ProjectId == projectId)
            .Select(d => new { d.MinN, d.ShowN }).SingleOrDefaultAsync(ct);
        // A dashboard that is gone is refused as not found by the handler.
        var threshold = minN is { } given ? (given == 0 ? null : given) : stored?.MinN;
        McpResults.Unwrap(await DashboardEndpoints.SaveBasesAsync(
            projectId, dashboardId, new DashboardBasesRequest(threshold, showN ?? stored?.ShowN), db, versions, user, time, ct));
    }

    private async Task<int?> PositionOfAsync(Guid projectId, Guid dashboardId, CancellationToken ct)
    {
        var order = await db.Dashboards.AsNoTracking().Where(d => d.ProjectId == projectId).OrderBy(d => d.Position).Select(d => d.Id).ToListAsync(ct);
        var index = order.IndexOf(dashboardId);
        return index < 0 ? null : index + 1;
    }

    /// <param name="Columns">What the indicator's current version says about its columns (their labels); null when it says nothing.</param>
    internal sealed record IndicatorInfo(string Label, string? Unit, string Format, IReadOnlyDictionary<string, IndicatorColumnMetadata>? Columns = null);

    private async Task<Dictionary<string, IndicatorInfo>> MetaAsync(Guid projectId, CancellationToken ct) =>
        (await ScriptEndpoints.CurrentIndicatorsAsync(db, projectId, ct))
            .Where(v => v.Name is not null)
            .ToDictionary(v => v.Name!, v => new IndicatorInfo(v.Label ?? v.Name!, v.Unit, v.Format ?? ReportFormat.Decimal, v.Columns), StringComparer.Ordinal);

    /// <summary>
    /// The name shown for each column a widget names, for those whose name shown differs from the column (design D11): its measure or its
    /// ratio's two columns, the group and series columns its query uses, and the tooltip columns of a map that have no label of their own.
    /// Null when no column has a label, so a widget that labels nothing returns what it returned before.
    /// </summary>
    internal static IReadOnlyDictionary<string, string>? LabelsOf(string chartType, WidgetConfig config, IndicatorInfo? info)
    {
        var names = ColumnLabels.For(info?.Columns, config.Display?.Labels);
        var query = WidgetQuery.For(chartType, config);
        var columns = new List<string?>();
        if (config.Ratio is { } ratio)
        {
            columns.Add(ratio.Numerator);
            columns.Add(ratio.Denominator);
        }
        else
        {
            columns.Add(config.Measure);
        }

        columns.Add(query.GroupBy);
        columns.Add(query.SeriesBy);
        if (chartType == ChartTypes.Map)
        {
            columns.AddRange(config.Tooltip.Where(item => string.IsNullOrWhiteSpace(item.Label)).Select(item => item.Column));
        }

        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var column in columns.Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.Ordinal))
        {
            var shown = names(column!);
            if (!string.Equals(shown, column, StringComparison.Ordinal))
            {
                labels[column!] = shown;
            }
        }

        return labels.Count == 0 ? null : labels;
    }

    private static readonly ReportFormat English = new(CultureInfo.GetCultureInfo("en-US"), TimeZoneInfo.Utc);

    /// <summary>
    /// The plain rows of a result in the order the widget declares for its groups (design D12 of add-declared-category-order): a line's
    /// rows go in natural order first, as the page draws them. A widget that declares no order of groups gets the rows exactly as the
    /// response has them, a line's included. Values stay full text, since a client writes filters from them.
    /// </summary>
    private static IReadOnlyList<IndicatorRow> RowsOf(IReadOnlyList<IndicatorRow> rows, string chartType, CategoryOrders? orders)
    {
        if (orders?.Groups is not { } groups)
        {
            return rows;
        }

        IReadOnlyList<IndicatorRow> basis = chartType == ChartTypes.Line ? rows.Order(NaturalOrder.ByGroup.Instance).ToList() : rows;
        return CategoryOrder.Arrange(basis, row => OrderEntry.OfValue(row.Group), groups);
    }

    /// <summary>
    /// A row as a tool says it. A ratio's numerator, denominator and n, and the n of a measure with a base column, come with every row (design
    /// D14 of add-small-sample-warnings) and are left out when the row has none. <c>smallBase</c> is true when the row's n is below
    /// <paramref name="minN"/>, the threshold in force of a widget of a dashboard (<c>get_widget_data</c>); a preview has none.
    /// </summary>
    private static object Row(IndicatorRow row, IndicatorInfo? info, int? minN = null) => new
    {
        group = row.Group,
        value = row.Value,
        formatted = English.Value(row.Value, info?.Format, info?.Unit),
        row.Rows,
        numerator = row.Numerator,
        denominator = row.Denominator,
        n = row.N,
        smallBase = BaseFigures.Small(row.N, minN) ? true : (bool?)null,
    };

    /// <summary>
    /// What a widget that leaves out the values its indicator declares as no data left out, as <c>{ values, value, formatted, share, n }</c>
    /// (design D14): the value is a ratio's left-out numerator over its denominator, or a measure's left-out sum, written as the widget's
    /// values are; the share is a measure's share of all its rows. Null when nothing was left out.
    /// </summary>
    private static object? NoDataOf(IndicatorNoData? noData, IndicatorInfo? info) => noData is null
        ? null
        : new
        {
            values = noData.Values,
            value = noData.Value,
            formatted = noData.Value is { } value ? English.Value(value, info?.Format, info?.Unit) : null,
            share = noData.Share,
            n = noData.N,
        };

    /// <summary>
    /// The sentence of a result's notes about the marks that have a small base: how many, and under which threshold. It names <c>smallBase</c>,
    /// the member that marks them, since a result has no asterisk or dimming to point at. Null while none has.
    /// </summary>
    internal static string? SmallBaseNote(int count, int minN) => count switch
    {
        <= 0 => null,
        1 => $"1 value has a base below {minN.ToString("#,##0", English.Culture)}, marked smallBase.",
        _ => $"{count} values have a base below {minN.ToString("#,##0", English.Culture)}, marked smallBase.",
    };

    /// <summary>
    /// The reference lines a bar, column or line draws, as { kind, label, value, formatted } (design D11 of add-chart-annotations): the English
    /// default label when a line has none, and the value formatted as the result's own values are, a share for a layout stacked to 100%.
    /// Null when there are none, so a widget without lines returns what it always did.
    /// </summary>
    internal static IReadOnlyList<object>? ReferenceLinesOf(string chartType, WidgetConfig config, IndicatorQueryResponse response, IndicatorInfo? info)
    {
        // An overall line over the indicator's declared answers is left out (design D16 of add-small-sample-warnings); its note is among the notes.
        var lines = ReferenceLines.Of(chartType, config, response, BaseFigures.AnswerColumnsOf(info?.Columns));
        if (lines.Count == 0)
        {
            return null;
        }

        var text = ReportText.For("en-US");
        var shares = config.SeriesBy is not null && SeriesTable.ShowsShares(chartType, config.Layout);
        return [.. lines.Select(line => (object)new
        {
            kind = line.Kind,
            label = line.Label ?? text[ReportText.ReferenceLabelKey(line.Kind)],
            value = line.Value,
            formatted = shares ? English.Share(line.Value) : English.Value(line.Value, info?.Format, info?.Unit),
        })];
    }

    /// <summary>
    /// What a widget that would add up several answers of a multiple-choice question says instead of its figures (design D4b of
    /// improve-stat-cards), in English and naming <paramref name="column"/>.
    /// </summary>
    internal static string CombinedMessage(string column) =>
        $"`{column}` holds the answers of a multiple-choice question. Adding up several of them counts a respondent once for each answer. " +
        $"Keep one answer, group or split the widget by `{column}`, or plot a row the indicator publishes for any of them.";

    /// <summary>One widget's data as the dashboard page asks for it: its own query, with the dashboard filters in force and the widget's defaults.</summary>
    private async Task<object> WidgetDataAsync(
        Guid projectId, Widget widget, IReadOnlyList<DashboardFilter> filters, AppliedDashboardFilter[]? given, IndicatorInfo? info,
        int? dashboardMinN, bool dashboardShowN, CancellationToken ct)
    {
        var applied = Applied(filters, widget.Config, given);
        // The widget's own unit replaces its indicator's for the values, the series cells and the boxes (design D4 of the widget context).
        if (widget.Config.Display?.Unit is { } unit)
        {
            info = info is null ? new IndicatorInfo(widget.Config.Indicator, unit, ReportFormat.Decimal) : info with { Unit = unit };
        }

        // Left out of the result while the widget has none, as every null is, so a widget without these texts returns what it always did.
        var display = widget.Config.Display;
        object Header(string status, string? message = null, object? rows = null) => new
        {
            widgetId = widget.Id,
            widget.Title,
            widget.ChartType,
            indicator = widget.Config.Indicator,
            label = info?.Label ?? widget.Config.Indicator,
            status,
            message,
            subtitle = display?.Subtitle,
            footnote = display?.Footnote,
            caption = display?.Caption?.Text,
            rows,
        };

        IndicatorQueryOutcome outcome;
        try
        {
            outcome = await indicators.QueryAsync(
                projectId, WidgetQuery.For(widget.ChartType, widget.Config), widget.ChartType, ct, applied, WidgetQuery.Overall(widget.ChartType, widget.Config),
                WidgetQuery.Unfiltered(widget.ChartType, widget.Config));
        }
        catch (DataTimeoutException)
        {
            return Header("timedOut", "The query took too long.");
        }

        if (outcome.Response is not { } response)
        {
            return Header("noLongerFits", "The table changed under the saved settings, so the query was refused. Update the widget.");
        }

        if (!response.Published || response.Missing.Indicator)
        {
            return Header("notPublished", $"The indicator '{widget.Config.Indicator}' has not published a table yet. Run the project after saving its script.");
        }

        if (response.Missing.Columns.Count > 0)
        {
            return Header("missingColumns", $"The table no longer has: {string.Join(", ", response.Missing.Columns)}. Update the widget.");
        }

        // A ratio that would add up several answers of a multiple-choice question has no rows (design D4b of improve-stat-cards).
        if (response.CombinedCategories is { } combined)
        {
            return Header("combinedCategories", CombinedMessage(combined));
        }

        var notNarrowed = WidgetQuery.NotNarrowedBy(filters, widget.Config, response.NotFiltered);
        var notes = new List<string>();
        if (notNarrowed.Count > 0)
        {
            notes.Add($"Not narrowed by the filters: {string.Join(", ", notNarrowed)}.");
        }

        if (response.RowLimitReached)
        {
            notes.Add($"The row limit was reached ({response.Rows.Count} {(widget.ChartType == ChartTypes.Boxplot ? "boxes" : "rows")}).");
        }

        if (response.Rows.Count == 0)
        {
            return Header("noRows", "No rows to show.");
        }

        var orders = CategoryOrder.For(widget.ChartType, widget.Config, info?.Columns);
        // The widget's own base settings over the dashboard's (design D7, D14 of add-small-sample-warnings).
        var bases = BaseFigures.InForce(display, dashboardMinN, dashboardShowN);
        var series = response.Series is null ? null : SeriesPayload.From(response, widget.ChartType, widget.Config.Layout, info, orders, bases.MinN);
        var boxes = response.Boxes is null ? null : BoxPayload.From(response.Boxes, info, orders, bases.MinN);
        notes.AddRange(series?.Notes ?? []);
        if (ReferenceLines.OverallLeftOut(widget.ChartType, widget.Config, BaseFigures.AnswerColumnsOf(info?.Columns)))
        {
            notes.Add(ReportText.For("en-US")["noOverall"]);
        }

        // Every value of a plain widget is a row, of a series widget a cell, of a box plot a box.
        var small = series?.Small ?? (response.Boxes is { } counted
            ? counted.Count(box => BaseFigures.Small(box.Values, bases.MinN))
            : response.Rows.Count(row => BaseFigures.Small(row.N, bases.MinN)));
        if (bases.MinN is { } threshold && SmallBaseNote(small, threshold) is { } smallNote)
        {
            notes.Add(smallNote);
        }

        return new
        {
            widgetId = widget.Id,
            widget.Title,
            widget.ChartType,
            indicator = widget.Config.Indicator,
            label = info?.Label ?? widget.Config.Indicator,
            status = "shown",
            message = (string?)null,
            labels = LabelsOf(widget.ChartType, widget.Config, info),
            subtitle = display?.Subtitle,
            footnote = display?.Footnote,
            caption = display?.Caption?.Text,
            referenceLines = ReferenceLinesOf(widget.ChartType, widget.Config, response, info),
            // A stat tile's statistic, and its value without the dashboard filters while a filter narrows it and it shows that line (design D3, D8 of improve-stat-cards).
            statistic = widget.ChartType == ChartTypes.Stat ? widget.Config.Statistic : null,
            unfiltered = response.Unfiltered is { } unfiltered ? new { value = unfiltered, formatted = English.Value(unfiltered, info?.Format, info?.Unit) } : null,
            // The widget's n (only under design D4's rule; a tile with a statistic always has one), the settings in force when set, and what it left out as no data.
            n = response.N,
            minN = bases.MinN,
            showN = bases.ShowN || display?.ShowN is not null ? bases.ShowN : (bool?)null,
            noData = NoDataOf(response.NoData, info),
            notes = notes.Count > 0 ? notes : null,
            rows = series is null && boxes is null ? RowsOf(response.Rows, widget.ChartType, orders).Select(r => Row(r, info, bases.MinN)).ToList() : null,
            boxes,
            series = series?.Series,
            cells = series?.Cells,
            totals = series?.Totals,
        };
    }

    /// <summary>The filters in force for a widget: the values given for a filter, else its default, except for the filters the widget ignores.</summary>
    private static List<AppliedDashboardFilter> Applied(IReadOnlyList<DashboardFilter> filters, WidgetConfig config, AppliedDashboardFilter[]? given)
    {
        var result = new List<AppliedDashboardFilter>();
        foreach (var filter in filters.Where(f => !config.IgnoredDashboardFilters.Contains(f.Column)))
        {
            var value = given?.FirstOrDefault(g => string.Equals(g.Column, filter.Column, StringComparison.Ordinal))?.Value ?? filter.Default;
            if (value is null)
            {
                continue;
            }

            result.Add(DashboardFilterKinds.TakesValues(filter.Kind)
                ? new AppliedDashboardFilter(filter.Column, filter.Kind, Values: [.. value.Values ?? []])
                : new AppliedDashboardFilter(filter.Column, filter.Kind, Min: value.Min, Max: value.Max));
        }

        return result;
    }
}

/// <summary>
/// What a tool says of a widget with a series: its series, one entry per cell with a value, the totals and the notes about what was set aside.
/// <paramref name="Small"/> is how many cells have a small base under the threshold the result was built with (design D14 of add-small-sample-warnings).
/// </summary>
internal sealed record SeriesPayload(IReadOnlyList<object> Series, IReadOnlyList<object> Cells, object Totals, IReadOnlyList<string> Notes, int Small = 0)
{
    private static readonly ReportFormat English = new(CultureInfo.GetCultureInfo("en-US"), TimeZoneInfo.Utc);

    /// <param name="minN">The small-base threshold in force, or null: a cell is marked <c>smallBase</c> when its n is below it.</param>
    public static SeriesPayload From(
        IndicatorQueryResponse response, string chartType, string? layout, McpDashboardTools.IndicatorInfo? info, CategoryOrders? orders = null, int? minN = null)
    {
        // The groups and the series in the order the widget declares, with their full text (a client writes filters from them).
        var table = CategoryOrder.ArrangeTable(SeriesTable.From(response, chartType, layout), orders);
        string? Shown(double? value) => value is { } number ? English.Value(number, info?.Format, info?.Unit) : null;
        string? KindOf(string kind) => kind == SeriesKinds.Value ? null : kind;

        var cells = new List<object>();
        var small = 0;
        foreach (var row in table.Rows)
        {
            for (var i = 0; i < table.Columns.Count; i++)
            {
                if (row.Cells[i] is { } value)
                {
                    var n = row.Figures?[i]?.N;
                    var smallBase = BaseFigures.Small(n, minN);
                    small += smallBase ? 1 : 0;
                    cells.Add(new
                    {
                        group = row.Group,
                        series = table.Columns[i].Series,
                        kind = KindOf(table.Columns[i].Kind),
                        value,
                        formatted = Shown(value),
                        share = row.Shares?[i],
                        n,
                        smallBase = smallBase ? true : (bool?)null,
                    });
                }
            }
        }

        var notes = new List<string>();
        if (table.LeftOut > 0)
        {
            notes.Add($"{table.LeftOut} rows were left out: their group or value is not one the chart shows.");
        }

        if (table.NegativeLeftOut > 0)
        {
            notes.Add($"{table.NegativeLeftOut} negative values were left out of the shares.");
        }

        return new SeriesPayload(
            table.Columns.Select(c => (object)new { value = c.Series, kind = KindOf(c.Kind) }).ToList(),
            cells,
            new
            {
                groups = table.Rows.Select(r =>
                {
                    var total = response.Totals?.Groups.FirstOrDefault(g => g.Group == r.Group);
                    return new { group = r.Group, value = r.Total, formatted = Shown(r.Total), r.Rows, numerator = total?.Numerator, denominator = total?.Denominator, n = total?.N };
                }).ToList(),
                series = table.Columns.Select(c =>
                {
                    var total = response.Totals?.Series.FirstOrDefault(t => t.Kind == c.Kind && t.Series == c.Series);
                    return new { series = c.Series, kind = KindOf(c.Kind), value = c.Total, formatted = Shown(c.Total), c.Rows, numerator = total?.Numerator, denominator = total?.Denominator, n = total?.N };
                }).ToList(),
                all = new
                {
                    value = table.GrandTotal, formatted = Shown(table.GrandTotal), rows = table.GrandRows,
                    numerator = response.Totals?.All.Numerator, denominator = response.Totals?.All.Denominator, n = response.Totals?.All.N,
                },
            },
            notes,
            small);
    }
}

/// <summary>
/// What a tool says of a box plot (design D9 of add-boxplot-widget): one entry per box with its figures and the same figures as the
/// indicator formats them. The outliers' own values are not sent, so a box is a fixed-size summary; the items hold no lists, so the
/// result budget cuts the boxes as it cuts any list of rows.
/// </summary>
internal static class BoxPayload
{
    private static readonly ReportFormat English = new(CultureInfo.GetCultureInfo("en-US"), TimeZoneInfo.Utc);

    /// <param name="minN">The small-base threshold in force, or null: a box is marked <c>smallBase</c> when its number of values is below it (design D14).</param>
    public static List<object> From(IReadOnlyList<IndicatorBox> boxes, McpDashboardTools.IndicatorInfo? info, CategoryOrders? orders = null, int? minN = null)
    {
        boxes = CategoryOrder.Arrange(boxes, box => OrderEntry.OfValue(box.Group), orders?.Groups);
        // A count's figures have two decimals, as on the dashboard: quartiles and means fall between whole numbers.
        var format = ReportDataBuilder.BoxFormat(info?.Format);
        string Shown(double value) => English.Value(value, format, info?.Unit);
        return boxes.Select(box => (object)new
        {
            group = box.Group,
            values = box.Values,
            min = box.Min,
            q1 = box.Q1,
            median = box.Median,
            q3 = box.Q3,
            max = box.Max,
            mean = box.Mean,
            lowerWhisker = box.LowerWhisker,
            upperWhisker = box.UpperWhisker,
            outlierCount = box.OutlierCount,
            formatted = new
            {
                min = Shown(box.Min), q1 = Shown(box.Q1), median = Shown(box.Median), q3 = Shown(box.Q3), max = Shown(box.Max), mean = Shown(box.Mean),
                lowerWhisker = Shown(box.LowerWhisker), upperWhisker = Shown(box.UpperWhisker),
            },
            smallBase = BaseFigures.Small(box.Values, minN) ? true : (bool?)null,
        }).ToList();
    }
}
