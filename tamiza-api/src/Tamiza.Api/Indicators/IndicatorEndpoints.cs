using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamiza.Api.Auth;
using Tamiza.Api.CleanData;
using Tamiza.Api.Data;
using Tamiza.Api.Http;
using Tamiza.Api.Projects;

namespace Tamiza.Api.Indicators;

/// <summary>The indicators dashboard: a canvas of widgets over the table script 3 published.</summary>
public static class IndicatorEndpoints
{
    public const string NotPublishedCode = "indicators.notPublished";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static RouteGroupBuilder MapIndicatorEndpoints(this RouteGroupBuilder api)
    {
        var indicators = api.MapGroup("/projects/{projectId:guid}/indicators").WithTags("Indicators");
        indicators.MapGet("", GetOverviewAsync).RequireProjectRole(ProjectRole.Viewer).WithName("GetIndicatorsDashboard");
        indicators.MapGet("/columns", GetColumnsAsync).RequireProjectRole(ProjectRole.Viewer).WithName("GetIndicatorColumns");
        indicators.MapPost("/query", QueryAsync).RequireProjectRole(ProjectRole.Viewer).WithName("QueryIndicators");
        indicators.MapPost("/widgets", CreateWidgetAsync).RequireProjectRole(ProjectRole.Analyst).WithName("CreateIndicatorWidget");
        indicators.MapPatch("/widgets/{widgetId:guid}", UpdateWidgetAsync).RequireProjectRole(ProjectRole.Analyst).WithName("UpdateIndicatorWidget");
        indicators.MapDelete("/widgets/{widgetId:guid}", DeleteWidgetAsync).RequireProjectRole(ProjectRole.Analyst).WithName("DeleteIndicatorWidget");
        indicators.MapPost("/exports", ExportAsync).RequireProjectRole(ProjectRole.Viewer).WithName("ExportIndicators");
        return api;
    }

    /// <summary>Whether script 3 has published, and the widgets in reading order, listed either way.</summary>
    private static async Task<Results<Ok<IndicatorOverview>, ProblemHttpResult>> GetOverviewAsync(
        Guid projectId, IndicatorDashboard dashboard, TamizaDbContext db, IOptions<IndicatorDashboardOptions> options, CancellationToken ct)
    {
        try
        {
            var publication = await dashboard.GetPublicationAsync(projectId, ct);
            var widgets = (await db.DashboardWidgets.AsNoTracking().Where(w => w.ProjectId == projectId).ToListAsync(ct))
                .Select(w => (Widget: ToWidget(w), w.CreatedAt))
                .OrderBy(w => w.Widget.Layout.Y).ThenBy(w => w.Widget.Layout.X).ThenBy(w => w.CreatedAt)
                .Select(w => w.Widget)
                .ToList();
            return TypedResults.Ok(new IndicatorOverview(
                publication.Published, publication.PublishedAt, publication.RunId, widgets,
                new IndicatorLimits(options.Value.MaxWidgetsPerProject, options.Value.MaxWidgetQueryRows)));
        }
        catch (DataTimeoutException)
        {
            return DataEndpoints.Timeout();
        }
    }

    /// <summary>Every column of the published table and the indicators it holds.</summary>
    private static async Task<Results<Ok<IndicatorColumns>, ProblemHttpResult>> GetColumnsAsync(
        Guid projectId, IndicatorDashboard dashboard, CancellationToken ct)
    {
        try
        {
            return TypedResults.Ok(await dashboard.GetColumnsAsync(projectId, ct));
        }
        catch (DataTimeoutException)
        {
            return DataEndpoints.Timeout();
        }
    }

    /// <summary>One widget's rows: the plotted field summed per indicator and group. Used to draw a saved widget and to preview a draft.</summary>
    private static async Task<Results<Ok<IndicatorQueryResponse>, ValidationProblem, ProblemHttpResult>> QueryAsync(
        Guid projectId, WidgetConfigInput? config, IndicatorDashboard dashboard, CancellationToken ct)
    {
        try
        {
            var outcome = await dashboard.QueryAsync(projectId, config ?? new WidgetConfigInput(), ct);
            return outcome.Errors is { } errors ? Problems.Validation(errors) : TypedResults.Ok(outcome.Response!);
        }
        catch (DataTimeoutException)
        {
            return DataEndpoints.Timeout();
        }
    }

    private static async Task<Results<Created<Widget>, ValidationProblem, ProblemHttpResult>> CreateWidgetAsync(
        Guid projectId, CreateWidgetRequest? request, IndicatorDashboard dashboard, TamizaDbContext db, ICurrentUser user,
        IOptions<IndicatorDashboardOptions> options, TimeProvider time, CancellationToken ct)
    {
        var errors = new FieldErrors();
        var title = request?.Title?.Trim();
        var chartType = request?.ChartType;
        var config = (request?.Config ?? new WidgetConfigInput()).Normalize();
        IndicatorValidation.CheckTitle(title, errors);
        IndicatorValidation.CheckChartType(chartType, errors);
        if (request?.Layout is not null)
        {
            IndicatorValidation.CheckLayout(request.Layout, errors);
        }

        IndicatorValidation.CheckConfig(config, ChartTypes.IsKnown(chartType) ? chartType : null, "config.", errors);
        if (!errors.Any && !await TryCheckAgainstTableAsync(dashboard, projectId, config, errors, ct))
        {
            return DataEndpoints.Timeout();
        }

        if (errors.Any)
        {
            return Problems.Validation(errors.ToDictionary());
        }

        // Counting and inserting are one step per project: without the lock, requests that arrive together each count below the limit.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var lockKey = projectId.ToString();
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);
        var existing = await db.DashboardWidgets.AsNoTracking().Where(w => w.ProjectId == projectId).Select(w => w.Layout).ToListAsync(ct);
        var max = options.Value.MaxWidgetsPerProject;
        if (existing.Count >= max)
        {
            return Problems.Validation("widgets", $"A project can have at most {max} widgets. Remove one before adding another.");
        }

        var layout = request!.Layout ?? PlaceBelow(chartType!, existing.Select(ReadLayout));
        var now = Now(time);
        var widget = new DashboardWidget
        {
            Id = Guid.CreateVersion7(now),
            ProjectId = projectId,
            ChartType = chartType!,
            Title = title!,
            Config = JsonSerializer.Serialize(config, Json),
            Layout = JsonSerializer.Serialize(layout, Json),
            CreatedBy = user.Id,
            CreatedAt = now,
            UpdatedBy = user.Id,
            UpdatedAt = now,
        };
        db.DashboardWidgets.Add(widget);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.Created($"/api/v1/projects/{projectId}/indicators/widgets/{widget.Id}", ToWidget(widget));
    }

    /// <summary>Changes any of a widget's title, chart type, query and layout. A move or a rename leaves the query as it is, even if the table no longer has what it names.</summary>
    private static async Task<Results<Ok<Widget>, ValidationProblem, ProblemHttpResult>> UpdateWidgetAsync(
        Guid projectId, Guid widgetId, UpdateWidgetRequest? request, IndicatorDashboard dashboard, TamizaDbContext db, ICurrentUser user,
        TimeProvider time, CancellationToken ct)
    {
        var widget = await db.DashboardWidgets.SingleOrDefaultAsync(w => w.Id == widgetId && w.ProjectId == projectId, ct);
        if (widget is null)
        {
            return WidgetNotFound();
        }

        request ??= new UpdateWidgetRequest();
        var errors = new FieldErrors();
        var title = request.Title is null ? widget.Title : request.Title.Trim();
        var chartType = request.ChartType ?? widget.ChartType;
        var config = request.Config is null ? ReadConfig(widget) : request.Config.Normalize();
        var layout = request.Layout ?? ReadLayout(widget.Layout);

        if (request.Title is not null)
        {
            IndicatorValidation.CheckTitle(title, errors);
        }

        IndicatorValidation.CheckChartType(chartType, errors);
        if (request.Layout is not null)
        {
            IndicatorValidation.CheckLayout(layout, errors);
        }

        var configChanged = request.Config is not null || request.ChartType is not null;
        if (configChanged)
        {
            IndicatorValidation.CheckConfig(config, ChartTypes.IsKnown(chartType) ? chartType : null, "config.", errors);
        }

        if (!errors.Any && request.Config is not null && !await TryCheckAgainstTableAsync(dashboard, projectId, config, errors, ct))
        {
            return DataEndpoints.Timeout();
        }

        if (errors.Any)
        {
            return Problems.Validation(errors.ToDictionary());
        }

        widget.Title = title;
        widget.ChartType = chartType;
        widget.Config = JsonSerializer.Serialize(config, Json);
        widget.Layout = JsonSerializer.Serialize(layout, Json);
        widget.UpdatedBy = user.Id;
        widget.UpdatedAt = Now(time);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToWidget(widget));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteWidgetAsync(
        Guid projectId, Guid widgetId, TamizaDbContext db, CancellationToken ct)
    {
        var widget = await db.DashboardWidgets.SingleOrDefaultAsync(w => w.Id == widgetId && w.ProjectId == projectId, ct);
        if (widget is null)
        {
            return WidgetNotFound();
        }

        db.DashboardWidgets.Remove(widget);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>Prepares a file with the whole published table and returns the link that downloads it once.</summary>
    private static async Task<Results<Ok<DataExportResult>, ValidationProblem, ProblemHttpResult>> ExportAsync(
        Guid projectId, IndicatorExportRequest? request, IndicatorDashboard dashboard, ICurrentUser user, CancellationToken ct)
    {
        if (request?.Format is not (DataDashboard.Csv or DataDashboard.Xlsx))
        {
            return Problems.Validation("format", "The format must be 'csv' or 'xlsx'.");
        }

        try
        {
            var result = await dashboard.ExportAsync(projectId, user.Id, request.Format, ct);
            return result?.Export is { } export ? TypedResults.Ok(export) : NotPublished();
        }
        catch (DataTimeoutException)
        {
            return DataEndpoints.Timeout();
        }
        catch (DataExportBusyException)
        {
            return Problems.Unavailable(DataExportBusyException.Code, "Other exports are being prepared. Try again in a moment.");
        }
        catch (DataXlsxTooManyRowsException ex)
        {
            return DataEndpoints.Unprocessable(DataXlsxTooManyRowsException.Code,
                string.Create(CultureInfo.InvariantCulture, $"The table has {ex.Rows:N0} rows, more than the {ex.Limit:N0} that fit an XLSX sheet. Export to CSV."));
        }
        catch (ExportTooLargeException ex)
        {
            return DataEndpoints.Unprocessable(DataDashboard.ExportTooLargeCode,
                string.Create(CultureInfo.InvariantCulture, $"The export is larger than the limit of {ex.Limit:N0} bytes."));
        }
    }

    /// <summary>Checks the query against the published table's columns. False when the read timed out.</summary>
    private static async Task<bool> TryCheckAgainstTableAsync(
        IndicatorDashboard dashboard, Guid projectId, WidgetConfig config, FieldErrors errors, CancellationToken ct)
    {
        try
        {
            await dashboard.CheckConfigAgainstTableAsync(projectId, config, "config.", errors, ct);
            return true;
        }
        catch (DataTimeoutException)
        {
            return false;
        }
    }

    /// <summary>The chart type's default size at the left edge, under the lowest widget.</summary>
    private static WidgetLayout PlaceBelow(string chartType, IEnumerable<WidgetLayout> existing)
    {
        var (w, h) = IndicatorValidation.DefaultSize(chartType);
        return new WidgetLayout(0, existing.Select(l => l.Y + l.H).DefaultIfEmpty(0).Max(), w, h);
    }

    /// <summary>The time to the microsecond, which is what PostgreSQL keeps, so the response says what was stored.</summary>
    private static DateTimeOffset Now(TimeProvider time)
    {
        var now = time.GetUtcNow();
        return now.AddTicks(-(now.Ticks % 10));
    }

    private static Widget ToWidget(DashboardWidget widget) =>
        new(widget.Id, widget.Title, widget.ChartType, ReadConfig(widget), ReadLayout(widget.Layout), widget.UpdatedAt);

    private static WidgetConfig ReadConfig(DashboardWidget widget) =>
        (JsonSerializer.Deserialize<WidgetConfigInput>(widget.Config, Json) ?? new WidgetConfigInput()).Normalize();

    private static WidgetLayout ReadLayout(string json) => JsonSerializer.Deserialize<WidgetLayout>(json, Json) ?? new WidgetLayout(0, 0, 1, 1);

    private static ProblemHttpResult WidgetNotFound() =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Widget not found.");

    private static ProblemHttpResult NotPublished() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "The indicators are not published.",
            detail: "Script 3 has not published the indicator table. A run may have replaced it.",
            extensions: new Dictionary<string, object?> { ["code"] = NotPublishedCode });
}
