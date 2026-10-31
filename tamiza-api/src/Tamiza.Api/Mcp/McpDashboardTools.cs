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
    IndicatorDashboard indicators, IOptions<IndicatorDashboardOptions> dashboardOptions) : McpToolBase(gate, call, options)
{
    private const string ProjectHelp = "The project's id, from list_projects.";

    private const string ChartTypeHelp =
        "stat, bar, column, line, histogram, pie, map, heatmap (groups as rows, series as columns, cells shaded), pyramid (two series values drawn left and right), " +
        "likert (agreement scale as bars centered on the neutral value) or crosstab (a table of groups by series with totals).";

    private const string SeriesHelp =
        "seriesBy is a second column that splits each group into series. It is optional for bar, column and line and required for heatmap, pyramid, likert and crosstab; " +
        "it must differ from groupBy. For bar and column, layout is grouped (side by side, the default), stacked or percent (each bar's shares of its total); stacked and percent need a measure, not a ratio. " +
        "Past 8 series (30 for heatmap and crosstab) the smallest are combined as Other. A pyramid needs pyramid = { left, right }, two values of the series column. " +
        "A likert needs scale = 2 to 11 values of the series column in order, each with a side of negative, neutral or positive (negatives first, at most one neutral, at most 5 per side) and a measure, not a ratio. " +
        "With a series the result lists series, cells ({ group, series, value, formatted, share? }), totals of every group, series and all rows, and notes; rows are not sent.";
    private const string DashboardHelp = "The dashboard's id, from list_dashboards.";

    // Indicators

    [McpServerTool(Name = "list_indicators", ReadOnly = true, Idempotent = true, OpenWorld = false, Title = "List the indicators"),
     Description("The project's indicators with their label, description, unit, format, whether each has published a table, how many rows it has and when, and the limits of dashboards (widgets per dashboard, rows per widget, dashboards per project).")]
    public Task<CallToolResult> ListIndicators([Description(ProjectHelp)] Guid projectId, CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            await Gate.RequireAsync(projectId, ProjectRole.Viewer, McpNeeds.Summaries, ct);
            return McpResults.Unwrap<IndicatorOverview>(await IndicatorEndpoints.GetOverviewAsync(projectId, indicators, db, dashboardOptions, ct));
        });

    [McpServerTool(Name = "get_indicator_columns", ReadOnly = true, Idempotent = true, OpenWorld = false, Title = "Get the columns of an indicator's table"),
     Description("Every column of one indicator's published table, the numeric columns a widget can plot as a measure, and the defaults a new widget would use (measure and group column). Needs the indicator to have published.")]
    public Task<CallToolResult> GetIndicatorColumns(
        [Description(ProjectHelp)] Guid projectId, [Description("The indicator's name, from list_indicators.")] string name, CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            await Gate.RequireAsync(projectId, ProjectRole.Viewer, McpNeeds.Summaries, ct);
            return McpResults.Unwrap<IndicatorColumns>(await IndicatorEndpoints.GetColumnsAsync(projectId, name, indicators, ct));
        });

    [McpServerTool(Name = "query_indicator", ReadOnly = true, Idempotent = true, OpenWorld = false, Title = "Preview what a widget would plot"),
     Description("Runs a widget query without saving anything and returns what the widget would plot: the measure summed (or the ratio of two columns' sums) per group, formatted with the indicator's format and unit. " +
                 "Use it before create_widget. config = { indicator, measure | ratio: { numerator, denominator }, groupBy, seriesBy, layout, pyramid: { left, right }, scale: [{ value, side }], filters: [{ column, op, value | values | min | max }], geometryColumn, buckets }. " +
                 "A bar, column, line, pie, map, heatmap, pyramid, likert or crosstab needs groupBy; a stat tile and a histogram do not. " + SeriesHelp + " " +
                 "At most the widget row limit of rows come back and the result says when it is reached.")]
    public Task<CallToolResult> QueryIndicator(
        [Description(ProjectHelp)] Guid projectId,
        [Description("The widget's query.")] WidgetConfigInput config,
        [Description("Optional. " + ChartTypeHelp + " It changes what a missing groupBy means and which series a pyramid or a Likert widget keeps.")] string? chartType = null,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            await Gate.RequireAsync(projectId, ProjectRole.Viewer, McpNeeds.Summaries, ct);
            var response = McpResults.Unwrap<IndicatorQueryResponse>(
                await IndicatorEndpoints.QueryAsync(projectId, new IndicatorQueryRequest(config, chartType), indicators, ct));
            var meta = await MetaAsync(projectId, ct);
            var info = meta.GetValueOrDefault(config.Indicator ?? "");
            var series = response.Series is null ? null : SeriesPayload.From(response, chartType ?? ChartTypes.Bar, config.Layout, info);
            return new
            {
                indicator = config.Indicator,
                label = info?.Label ?? config.Indicator,
                format = info?.Format,
                unit = info?.Unit,
                response.Published,
                missing = response.Missing.Any ? response.Missing : null,
                response.RowLimitReached,
                rows = series is null ? response.Rows.Select(r => Row(r, info)).ToList() : null,
                series = series?.Series,
                cells = series?.Cells,
                totals = series?.Totals,
                notes = series?.Notes,
            };
        }, hint: "Add a filter or group by a column with fewer values.");

    // Dashboards, read

    [McpServerTool(Name = "list_dashboards", ReadOnly = true, Idempotent = true, OpenWorld = false, Title = "List the dashboards"),
     Description("The project's own dashboards in their order (1 to 7), with how many widgets and filters each has. Performance and Clean data are permanent and not listed.")]
    public Task<CallToolResult> ListDashboards([Description(ProjectHelp)] Guid projectId, CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            await Gate.RequireAsync(projectId, ProjectRole.Viewer, McpNeeds.Summaries, ct);
            var rows = await db.Dashboards.AsNoTracking().Where(d => d.ProjectId == projectId).OrderBy(d => d.Position)
                .Select(d => new { d.Id, d.Name, d.Position, d.Filters, Widgets = db.DashboardWidgets.Count(w => w.DashboardId == d.Id) }).ToListAsync(ct);
            return new
            {
                maxDashboards = IndicatorDashboardOptions.MaxDashboards,
                maxWidgetsPerDashboard = dashboardOptions.Value.MaxWidgetsPerDashboard,
                dashboards = rows.Select(d => new { id = d.Id, name = d.Name, position = d.Position + 1, widgets = d.Widgets, filters = DashboardEndpoints.ReadFilters(d.Filters).Count }).ToList(),
            };
        });

    [McpServerTool(Name = "get_dashboard", ReadOnly = true, Idempotent = true, OpenWorld = false, Title = "Get a dashboard"),
     Description("One dashboard with its filters and its widgets in reading order: each widget's id, title, chart type, query (config) and layout on the 12-column grid. Use get_widget_data for what the widgets show.")]
    public Task<CallToolResult> GetDashboard(
        [Description(ProjectHelp)] Guid projectId, [Description(DashboardHelp)] Guid dashboardId, CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            await Gate.RequireAsync(projectId, ProjectRole.Viewer, McpNeeds.Summaries, ct);
            return McpResults.Unwrap<DashboardView>(await DashboardEndpoints.GetDashboardAsync(projectId, dashboardId, db, ct));
        }, hint: "Use get_widget_data with a widgetId for one widget.");

    [McpServerTool(Name = "get_widget_data", ReadOnly = true, Idempotent = true, OpenWorld = false, Title = "Get what the widgets show"),
     Description("What the widgets of a dashboard (or one widget) show, as a member opening the dashboard sees it: for each widget its rows (group and value, with the value formatted as the dashboard formats it), " +
                 "or the message the widget shows when its indicator has not published, a column is gone or there are no rows. A widget with a series (seriesBy) returns series, cells ({ group, series, value, formatted, share? }), totals and notes instead of rows. " +
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
            var dashboard = await db.Dashboards.AsNoTracking().Where(d => d.Id == dashboardId && d.ProjectId == projectId).Select(d => new { d.Name, d.Filters }).SingleOrDefaultAsync(ct)
                ?? throw new McpRefusal(DashboardEndpoints.NotFoundCode, "The project has no such dashboard. Call list_dashboards.");
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
                results.Add(await WidgetDataAsync(projectId, widget, filters, filterValues, meta.GetValueOrDefault(widget.Config.Indicator), ct));
            }

            return new { dashboard = dashboard.Name, filters, widgets = results };
        }, hint: "Ask for one widget with widgetId.");

    // Dashboards, write

    [McpServerTool(Name = "create_dashboard", Title = "Create a dashboard", Idempotent = false, OpenWorld = false, Destructive = false),
     Description("Adds a dashboard of the project's own (at most 7, names unique whatever their case, 1 to 40 characters, never Performance, Clean data or Settings). It starts empty and goes last unless you give a position (1 = first).")]
    public Task<CallToolResult> CreateDashboard(
        [Description(ProjectHelp)] Guid projectId,
        [Description("The dashboard's name.")] string name,
        [Description("Optional. Where to put it, 1 for the first place.")] int? position = null,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            await Gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, ct);
            var created = McpResults.Unwrap<DashboardEntry>(await DashboardEndpoints.AddAsync(projectId, new DashboardNameRequest(name), db, user, time, ct));
            if (position is { } place)
            {
                await MoveAsync(projectId, created.Id, place, ct);
            }

            return new { created.Id, created.Name, position = await PositionOfAsync(projectId, created.Id, ct) };
        });

    [McpServerTool(Name = "update_dashboard", Title = "Rename, move or set the filters of a dashboard", Idempotent = true, OpenWorld = false, Destructive = false),
     Description("Renames a dashboard, moves it to a position (1 = first) or replaces its filters (up to 8; each { column, label, kind: text | boolean | number | date, default? }). Give any of name, position and filters; what you leave out keeps its value. " +
                 "A filter narrows every widget whose indicator table has its column with a type of its kind.")]
    public Task<CallToolResult> UpdateDashboard(
        [Description(ProjectHelp)] Guid projectId,
        [Description(DashboardHelp)] Guid dashboardId,
        [Description("Optional. The new name.")] string? name = null,
        [Description("Optional. The new place, 1 for the first.")] int? position = null,
        [Description("Optional. The whole list of filters, replacing the current one; an empty list removes them all.")] DashboardFilterInput[]? filters = null,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            await Gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, ct);
            if (name is null && position is null && filters is null)
            {
                throw McpRefusal.Validation(new Dictionary<string, string[]> { ["name"] = ["Give a name, a position or filters to change."] });
            }

            if (name is not null)
            {
                McpResults.Unwrap(await DashboardEndpoints.RenameAsync(projectId, dashboardId, new DashboardNameRequest(name), db, user, time, ct));
            }

            if (filters is not null)
            {
                McpResults.Unwrap(await DashboardEndpoints.SaveFiltersAsync(projectId, dashboardId, new DashboardFiltersRequest(filters), db, user, time, ct));
            }

            if (position is { } place)
            {
                await MoveAsync(projectId, dashboardId, place, ct);
            }

            var current = await db.Dashboards.AsNoTracking().Where(d => d.Id == dashboardId && d.ProjectId == projectId).Select(d => new { d.Name, d.Filters }).SingleOrDefaultAsync(ct);
            return new { id = dashboardId, name = current?.Name, position = await PositionOfAsync(projectId, dashboardId, ct), filters = current is null ? null : DashboardEndpoints.ReadFilters(current.Filters) };
        });

    [McpServerTool(Name = "duplicate_dashboard", Title = "Duplicate a dashboard", Idempotent = false, OpenWorld = false, Destructive = false),
     Description("Copies a dashboard with all its widgets and filters right after the original, under a new name. Fails at 7 dashboards.")]
    public Task<CallToolResult> DuplicateDashboard(
        [Description(ProjectHelp)] Guid projectId,
        [Description(DashboardHelp)] Guid dashboardId,
        [Description("The copy's name.")] string name,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            await Gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, ct);
            var copy = McpResults.Unwrap<DashboardEntry>(
                await DashboardEndpoints.DuplicateAsync(projectId, dashboardId, new DashboardNameRequest(name), db, user, dashboardOptions, time, ct));
            return new { copy.Id, copy.Name, position = await PositionOfAsync(projectId, copy.Id, ct) };
        });

    [McpServerTool(Name = "remove_dashboard", Title = "Remove a dashboard", Idempotent = false, OpenWorld = false, Destructive = true),
     Description("Removes a dashboard and all its widgets. This cannot be undone. Pass the dashboard's current name as expectedName so a wrong id cannot remove the wrong dashboard. A project keeps at least one dashboard.")]
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
     Description("Adds a widget: a stat tile, bar, column, line, histogram, pie chart, map, heat map, population pyramid, diverging Likert chart or cross table, with a title and a query over one indicator's table. Check the query with query_indicator first. " +
                 "config = { indicator, measure | ratio: { numerator, denominator }, groupBy, seriesBy, layout, pyramid: { left, right }, scale: [{ value, side }], filters, geometryColumn, buckets, sort, tooltip }. " +
                 "Every kind but stat tile and histogram needs groupBy; a map also needs geometryColumn (WKT in degrees). " + SeriesHelp + " " +
                 "It goes under the dashboard's widgets unless you give layout = { x, y, w, h } on the 12-column grid. A dashboard holds a limited number of widgets.")]
    public Task<CallToolResult> CreateWidget(
        [Description(ProjectHelp)] Guid projectId,
        [Description(DashboardHelp)] Guid dashboardId,
        [Description("The widget's title, up to 200 characters.")] string title,
        [Description(ChartTypeHelp)] string chartType,
        [Description("The widget's query.")] WidgetConfigInput config,
        [Description("Optional. Position and size on the grid.")] WidgetLayout? layout = null,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            await Gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, ct);
            return McpResults.Unwrap<Widget>(await DashboardEndpoints.CreateWidgetAsync(
                projectId, dashboardId, new CreateWidgetRequest(title, chartType, config, layout), indicators, db, user, dashboardOptions, time, ct));
        });

    [McpServerTool(Name = "update_widget", Title = "Change or move a widget", Idempotent = true, OpenWorld = false, Destructive = false),
     Description("Changes a widget's title, chart type, query or layout, or moves it to another dashboard of the project with moveToDashboardId (it lands under that dashboard's widgets). What you leave out keeps its value; a new config replaces the old one whole, including its series settings.")]
    public Task<CallToolResult> UpdateWidget(
        [Description(ProjectHelp)] Guid projectId,
        [Description(DashboardHelp)] Guid dashboardId,
        [Description("The widget's id, from get_dashboard.")] Guid widgetId,
        [Description("Optional. The new title.")] string? title = null,
        [Description("Optional. The new chart type: " + ChartTypeHelp)] string? chartType = null,
        [Description("Optional. The new query, replacing the old one.")] WidgetConfigInput? config = null,
        [Description("Optional. The new position and size on the grid.")] WidgetLayout? layout = null,
        [Description("Optional. Another dashboard of the project to move the widget to.")] Guid? moveToDashboardId = null,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            await Gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, ct);
            return McpResults.Unwrap<Widget>(await DashboardEndpoints.UpdateWidgetAsync(
                projectId, dashboardId, widgetId, new UpdateWidgetRequest(title, chartType, config, layout, moveToDashboardId), indicators, db, user, dashboardOptions, time, ct));
        });

    [McpServerTool(Name = "remove_widget", Title = "Remove a widget", Idempotent = false, OpenWorld = false, Destructive = true),
     Description("Removes a widget. This cannot be undone. Pass the widget's current title as expectedTitle so a wrong id cannot remove the wrong widget.")]
    public Task<CallToolResult> RemoveWidget(
        [Description(ProjectHelp)] Guid projectId,
        [Description(DashboardHelp)] Guid dashboardId,
        [Description("The widget's id, from get_dashboard.")] Guid widgetId,
        [Description("The widget's current title (case and surrounding spaces are ignored).")] string expectedTitle,
        CancellationToken ct = default) =>
        RunAsync(async () =>
        {
            await Gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, ct);
            var current = await db.DashboardWidgets.AsNoTracking().Where(w => w.Id == widgetId && w.DashboardId == dashboardId && w.ProjectId == projectId).Select(w => w.Title).SingleOrDefaultAsync(ct);
            if (current is not null)
            {
                RequireName(current, expectedTitle);
            }

            McpResults.Unwrap(await DashboardEndpoints.DeleteWidgetAsync(projectId, dashboardId, widgetId, db, ct));
            return new { removed = true, title = current };
        });

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

    private async Task<int?> PositionOfAsync(Guid projectId, Guid dashboardId, CancellationToken ct)
    {
        var order = await db.Dashboards.AsNoTracking().Where(d => d.ProjectId == projectId).OrderBy(d => d.Position).Select(d => d.Id).ToListAsync(ct);
        var index = order.IndexOf(dashboardId);
        return index < 0 ? null : index + 1;
    }

    internal sealed record IndicatorInfo(string Label, string? Unit, string Format);

    private async Task<Dictionary<string, IndicatorInfo>> MetaAsync(Guid projectId, CancellationToken ct) =>
        (await ScriptEndpoints.CurrentIndicatorsAsync(db, projectId, ct))
            .Where(v => v.Name is not null)
            .ToDictionary(v => v.Name!, v => new IndicatorInfo(v.Label ?? v.Name!, v.Unit, v.Format ?? ReportFormat.Decimal), StringComparer.Ordinal);

    private static readonly ReportFormat English = new(CultureInfo.GetCultureInfo("en-US"), TimeZoneInfo.Utc);

    private static object Row(IndicatorRow row, IndicatorInfo? info) => new
    {
        group = row.Group,
        value = row.Value,
        formatted = English.Value(row.Value, info?.Format, info?.Unit),
        row.Rows,
    };

    /// <summary>One widget's data as the dashboard page asks for it: its own query, with the dashboard filters in force and the widget's defaults.</summary>
    private async Task<object> WidgetDataAsync(
        Guid projectId, Widget widget, IReadOnlyList<DashboardFilter> filters, AppliedDashboardFilter[]? given, IndicatorInfo? info, CancellationToken ct)
    {
        var applied = Applied(filters, widget.Config, given);
        object Header(string status, string? message = null, object? rows = null) =>
            new { widgetId = widget.Id, widget.Title, widget.ChartType, indicator = widget.Config.Indicator, label = info?.Label ?? widget.Config.Indicator, status, message, rows };

        IndicatorQueryOutcome outcome;
        try
        {
            outcome = await indicators.QueryAsync(projectId, WidgetQuery.For(widget.ChartType, widget.Config), widget.ChartType, ct, applied);
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

        var notNarrowed = WidgetQuery.NotNarrowedBy(filters, widget.Config, response.NotFiltered);
        var notes = new List<string>();
        if (notNarrowed.Count > 0)
        {
            notes.Add($"Not narrowed by the filters: {string.Join(", ", notNarrowed)}.");
        }

        if (response.RowLimitReached)
        {
            notes.Add($"The row limit was reached ({response.Rows.Count} rows).");
        }

        if (response.Rows.Count == 0)
        {
            return Header("noRows", "No rows to show.");
        }

        var series = response.Series is null ? null : SeriesPayload.From(response, widget.ChartType, widget.Config.Layout, info);
        notes.AddRange(series?.Notes ?? []);
        return new
        {
            widgetId = widget.Id,
            widget.Title,
            widget.ChartType,
            indicator = widget.Config.Indicator,
            label = info?.Label ?? widget.Config.Indicator,
            status = "shown",
            message = (string?)null,
            notes = notes.Count > 0 ? notes : null,
            rows = series is null ? response.Rows.Select(r => Row(r, info)).ToList() : null,
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

/// <summary>What a tool says of a widget with a series: its series, one entry per cell with a value, the totals and the notes about what was set aside.</summary>
internal sealed record SeriesPayload(IReadOnlyList<object> Series, IReadOnlyList<object> Cells, object Totals, IReadOnlyList<string> Notes)
{
    private static readonly ReportFormat English = new(CultureInfo.GetCultureInfo("en-US"), TimeZoneInfo.Utc);

    public static SeriesPayload From(IndicatorQueryResponse response, string chartType, string? layout, McpDashboardTools.IndicatorInfo? info)
    {
        var table = SeriesTable.From(response, chartType, layout);
        string? Shown(double? value) => value is { } number ? English.Value(number, info?.Format, info?.Unit) : null;
        string? KindOf(string kind) => kind == SeriesKinds.Value ? null : kind;

        var cells = new List<object>();
        foreach (var row in table.Rows)
        {
            for (var i = 0; i < table.Columns.Count; i++)
            {
                if (row.Cells[i] is { } value)
                {
                    cells.Add(new
                    {
                        group = row.Group,
                        series = table.Columns[i].Series,
                        kind = KindOf(table.Columns[i].Kind),
                        value,
                        formatted = Shown(value),
                        share = row.Shares?[i],
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
                groups = table.Rows.Select(r => new { group = r.Group, value = r.Total, formatted = Shown(r.Total), r.Rows }).ToList(),
                series = table.Columns.Select(c => new { series = c.Series, kind = KindOf(c.Kind), value = c.Total, formatted = Shown(c.Total), c.Rows }).ToList(),
                all = new { value = table.GrandTotal, formatted = Shown(table.GrandTotal), rows = table.GrandRows },
            },
            notes);
    }
}
