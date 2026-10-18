using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Tamiza.Api.Auth;
using Tamiza.Api.CleanData;
using Tamiza.Api.Data;
using Tamiza.Api.Http;
using Tamiza.Api.Pipeline;
using Tamiza.Api.Projects;

namespace Tamiza.Api.Indicators;

/// <summary>
/// What every dashboard of a project shares: the indicators, the columns of the tables they published, the widget query and the
/// exports. The dashboards and their widgets are <see cref="DashboardEndpoints"/>'.
/// </summary>
public static class IndicatorEndpoints
{
    public const string NotPublishedCode = "indicators.notPublished";

    public static RouteGroupBuilder MapIndicatorEndpoints(this RouteGroupBuilder api)
    {
        var indicators = api.MapGroup("/projects/{projectId:guid}/indicators").WithTags("Indicators");
        indicators.MapGet("", GetOverviewAsync).RequireProjectRole(ProjectRole.Viewer).WithName("GetIndicatorsDashboard");
        indicators.MapGet("/{name}/columns", GetColumnsAsync).RequireProjectRole(ProjectRole.Viewer).WithName("GetIndicatorColumns");
        indicators.MapPost("/query", QueryAsync).RequireProjectRole(ProjectRole.Viewer).WithName("QueryIndicators");
        indicators.MapPost("/{name}/exports", ExportAsync).RequireProjectRole(ProjectRole.Viewer).WithName("ExportIndicators");
        return api;
    }

    /// <summary>The project's indicators with whether each has published a table, and the limits of its dashboards.</summary>
    private static async Task<Results<Ok<IndicatorOverview>, ProblemHttpResult>> GetOverviewAsync(
        Guid projectId, IndicatorDashboard dashboard, TamizaDbContext db, IOptions<IndicatorDashboardOptions> options, CancellationToken ct)
    {
        try
        {
            var scripts = await ScriptEndpoints.CurrentIndicatorsAsync(db, projectId, ct);
            var publications = await dashboard.GetPublicationsAsync(projectId, ct);
            var indicators = scripts
                .Select(script =>
                {
                    var publication = publications.GetValueOrDefault(script.Name!, IndicatorPublication.None);
                    return new IndicatorEntry(
                        script.Name!, script.Label ?? script.Name!, script.Description, script.Unit, script.Format ?? "decimal",
                        publication.Published, publication.PublishedAt, publication.RunId, publication.Rows);
                })
                .ToList();
            return TypedResults.Ok(new IndicatorOverview(
                indicators,
                new IndicatorLimits(options.Value.MaxWidgetsPerDashboard, options.Value.MaxWidgetQueryRows, IndicatorDashboardOptions.MaxDashboards)));
        }
        catch (DataTimeoutException)
        {
            return DataEndpoints.Timeout();
        }
    }

    /// <summary>Every column of one indicator's published table, those a widget can plot, and the defaults for a new widget.</summary>
    private static async Task<Results<Ok<IndicatorColumns>, ProblemHttpResult>> GetColumnsAsync(
        Guid projectId, string name, IndicatorDashboard dashboard, CancellationToken ct)
    {
        try
        {
            return TypedResults.Ok(await dashboard.GetColumnsAsync(projectId, name, ct));
        }
        catch (DataTimeoutException)
        {
            return DataEndpoints.Timeout();
        }
    }

    /// <summary>One widget's rows: the measure summed, or the ratio of sums, per group. Used to draw a saved widget and to preview a draft.</summary>
    private static async Task<Results<Ok<IndicatorQueryResponse>, ValidationProblem, ProblemHttpResult>> QueryAsync(
        Guid projectId, IndicatorQueryRequest? request, IndicatorDashboard dashboard, CancellationToken ct)
    {
        try
        {
            var outcome = await dashboard.QueryAsync(projectId, request?.Config ?? new WidgetConfigInput(), request?.ChartType, ct);
            return outcome.Errors is { } errors ? Problems.Validation(errors) : TypedResults.Ok(outcome.Response!);
        }
        catch (DataTimeoutException)
        {
            return DataEndpoints.Timeout();
        }
    }

    /// <summary>Prepares a file with the whole published table of one indicator and returns the link that downloads it once.</summary>
    private static async Task<Results<Ok<DataExportResult>, ValidationProblem, ProblemHttpResult>> ExportAsync(
        Guid projectId, string name, IndicatorExportRequest? request, IndicatorDashboard dashboard, ICurrentUser user, CancellationToken ct)
    {
        if (request?.Format is not (DataDashboard.Csv or DataDashboard.Xlsx))
        {
            return Problems.Validation("format", "The format must be 'csv' or 'xlsx'.");
        }

        try
        {
            var result = await dashboard.ExportAsync(projectId, user.Id, name, request.Format, ct);
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

    private static ProblemHttpResult NotPublished() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "The indicator is not published.",
            detail: "The indicator has not published its table yet, or a run removed it.",
            extensions: new Dictionary<string, object?> { ["code"] = NotPublishedCode });
}
