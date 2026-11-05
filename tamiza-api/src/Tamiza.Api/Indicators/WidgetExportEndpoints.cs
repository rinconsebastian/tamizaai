using System.ComponentModel;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamiza.Api.Auth;
using Tamiza.Api.CleanData;
using Tamiza.Api.Data;
using Tamiza.Api.Http;
using Tamiza.Api.Pipeline;
using Tamiza.Api.Projects;
using Tamiza.Api.Reports;

namespace Tamiza.Api.Indicators;

/// <summary>A dashboard filter value in force on a member's screen, sent to export a widget (design D12 of add-widget-export).</summary>
/// <param name="Column">The filter's column; it must be a filter of the dashboard, or of the version shown, of the same <paramref name="Kind"/>.</param>
/// <param name="Values">1 to 100 values of a text or true/false filter.</param>
/// <param name="Min">The smallest value of a number or date filter.</param>
/// <param name="Max">The largest value of a number or date filter.</param>
/// <param name="Ignored">The widget ignores this filter: it does not narrow the rows, and the workbook says so.</param>
public sealed record WidgetExportFilter(
    string? Column, string? Kind, IReadOnlyList<string>? Values = null, string? Min = null, string? Max = null, bool Ignored = false);

/// <summary>
/// The body to export a widget: the settings the page is showing and the dashboard filter values in force, so that the file is what
/// the member sees, also for a widget of a version that the dashboard no longer has.
/// </summary>
/// <param name="Format">Must be <c>xlsx</c>.</param>
/// <param name="Title">The widget's title.</param>
/// <param name="ChartType">The widget's chart type.</param>
/// <param name="Config">The widget's settings, as a widget is saved.</param>
/// <param name="Filters">At most 8 filter values in force, one per column.</param>
/// <param name="Locale">The UI's language and region, such as <c>es-CO</c>: the workbook's own words and formats follow it.</param>
/// <param name="Version">The number of the dashboard version being viewed, for an <c>analyst</c> or <c>admin</c>; omitted for the current dashboard.</param>
/// <param name="MinN">The small-base threshold the page applies to the widget, its own over the dashboard's or the shown version's: 0 (off) to 10,000. Omitted, the workbook has no n column for it and marks nothing. It replaces any <c>display.minN</c> of <paramref name="Config"/>.</param>
/// <param name="ShowN">Whether the page shows the widget's base n line, resolved the same way. It replaces any <c>display.showN</c> of <paramref name="Config"/>.</param>
public sealed record WidgetExportRequest(
    string? Format, string? Title, string? ChartType, WidgetConfigInput? Config, IReadOnlyList<WidgetExportFilter?>? Filters, string? Locale, int? Version = null,
    [property: Description("The small-base threshold the page applies to the widget, its own over the dashboard's or the shown version's: 0 turns it off, 1 to 10000 marks the values whose base n is below it. Left out, the workbook marks nothing.")] int? MinN = null,
    [property: Description("Whether the page shows the widget's base n line, its own setting over the dashboard's or the shown version's. Left out, the workbook shows none.")] bool? ShowN = null);

/// <summary>A prepared widget workbook.</summary>
/// <param name="DownloadUrl">The single-use link that downloads the file.</param>
/// <param name="RowCount">The rows the data sheet lists.</param>
/// <param name="PublishedAt">When the widget's indicator was last published, as the file read it; null when it has not been.</param>
public sealed record WidgetExportResult(string DownloadUrl, string FileName, long RowCount, DateTimeOffset? PublishedAt, DateTimeOffset ExpiresAt);

/// <summary>
/// Exports one widget's data as an XLSX workbook (design D12 to D15 of add-widget-export). The workbook is built by the code that builds
/// a scheduled email's, from the query the member's screen sent, so the file, the email and "Show as table" read the same table. It
/// changes nothing, so the route group records no dashboard version for it.
/// </summary>
public static class WidgetExportEndpoints
{
    public const string FiltersChangedCode = "widgetExports.filtersChanged";
    public const string MissingColumnsCode = "widgetExports.missingColumns";

    public static RouteGroupBuilder MapWidgetExportEndpoints(this RouteGroupBuilder dashboards)
    {
        dashboards.MapPost("/{dashboardId:guid}/widget-exports", ExportAsync)
            .RequireProjectRole(ProjectRole.Viewer).WithName("ExportDashboardWidget");
        return dashboards;
    }

    private static async Task<Results<Ok<WidgetExportResult>, ValidationProblem, ProblemHttpResult>> ExportAsync(
        Guid projectId, Guid dashboardId, WidgetExportRequest? request, HttpContext http, TamizaDbContext db, ReportDataBuilder builder,
        IndicatorDashboard indicators, ExportRegistry exports, ICurrentUser user, TimeProvider time, IOptions<DataDashboardOptions> options,
        CancellationToken ct)
    {
        var errors = new FieldErrors();
        if (request?.Format != DataDashboard.Xlsx)
        {
            errors.Add("format", "The format must be 'xlsx'.");
        }

        IndicatorValidation.CheckTitle(request?.Title, errors);
        IndicatorValidation.CheckChartType(request?.ChartType, errors);
        var config = (request?.Config ?? new WidgetConfigInput()).Normalize();
        IndicatorValidation.CheckConfig(config, ChartTypes.IsKnown(request?.ChartType) ? request!.ChartType : null, "config.", errors);
        var sent = (request?.Filters ?? []).ToList();
        var applied = sent.Select(f => new AppliedDashboardFilter(f?.Column, f?.Kind, f?.Values, f?.Min, f?.Max)).ToList();
        var filterErrors = new FieldErrors();
        IndicatorValidation.CheckDashboardFilters(applied, filterErrors);
        foreach (var (field, messages) in filterErrors.ToDictionary())
        {
            foreach (var message in messages)
            {
                errors.Add(field.Replace("dashboardFilters", "filters", StringComparison.Ordinal), message);
            }
        }

        if (request?.MinN is < 0 or > WidgetDisplay.MaxMinN)
        {
            errors.Add("minN", "The small-base threshold must be a whole number from 0 (off) to 10,000.");
        }

        var locale = ReportText.Resolve(request?.Locale);
        if (locale is null)
        {
            errors.Add("locale", "The locale must be a Spanish or English language tag, such as 'es-CO'.");
        }

        if (errors.Any)
        {
            return Problems.Validation(errors.ToDictionary());
        }

        var dashboard = await db.Dashboards.AsNoTracking()
            .Where(d => d.Id == dashboardId && d.ProjectId == projectId)
            .Select(d => new { d.Name, d.Filters, d.Source })
            .SingleOrDefaultAsync(ct);
        if (dashboard is null)
        {
            return DashboardEndpoints.DashboardNotFound();
        }

        // The filters, the name and the source the file is about: the dashboard's, or the version shown, which may not be the dashboard's any more.
        var dashboardName = dashboard.Name;
        var dashboardSource = dashboard.Source;
        var storedFilters = DashboardEndpoints.ReadFilters(dashboard.Filters);
        Person? versionAuthor = null;
        DateTimeOffset versionSavedAt = default;
        if (request!.Version is { } number)
        {
            if (http.GetProjectRole() < ProjectRole.Analyst)
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Not allowed.",
                    detail: "Viewing a version of a dashboard requires the analyst role in the project.");
            }

            if (await DashboardVersions.DetailsAsync(db, projectId, dashboardId, number, ct) is not { } version)
            {
                return DashboardEndpoints.VersionNotFound();
            }

            dashboardName = version.Name;
            dashboardSource = version.Source;
            storedFilters = [.. version.Filters];
            versionAuthor = version.Author;
            versionSavedAt = version.SavedAt;
        }

        // A filter the screen sent must still be one of the dashboard's: the member's page may be older than a change to its filters.
        if (sent.Any(f => !storedFilters.Any(s => s.Column == f!.Column && s.Kind == f.Kind)))
        {
            return Problems.Conflict(
                FiltersChangedCode, "The dashboard's filters changed since the page was opened. Reload the dashboard and try again.");
        }

        var project = await db.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(p => new { p.Name, p.TimeZone }).SingleAsync(ct);
        var text = ReportText.For(locale!);
        var format = new ReportFormat(text.Culture, ReportFormat.ZoneOf(project.TimeZone));

        var title = request.Title!.Trim();
        // The filters in force, in the filter bar's order and named as the dashboard (or the version) stores them.
        var inForce = storedFilters
            .Select(stored => (Stored: stored, Sent: sent.SingleOrDefault(f => f!.Column == stored.Column)))
            .Where(f => f.Sent is not null)
            .Select(f => (f.Stored, Sent: f.Sent!, Ignored: f.Sent!.Ignored || config.IgnoredDashboardFilters.Contains(f.Stored.Column)))
            .ToList();
        var narrowing = inForce.Where(f => !f.Ignored)
            .Select(f => new AppliedDashboardFilter(f.Sent.Column, f.Sent.Kind, f.Sent.Values, f.Sent.Min, f.Sent.Max))
            .ToList();
        var inForceLabels = inForce.Select(f => new FilterInForce(f.Stored.Column, f.Stored.Label, f.Ignored)).ToList();
        var widget = new Widget(Guid.Empty, dashboardId, title, request.ChartType!, config, new WidgetLayout(0, 0, 1, 1), time.GetUtcNow());

        WidgetExportContent content;
        IReadOnlyDictionary<string, IndicatorPublication> publications;
        IReadOnlyList<string> notNarrowed = [];
        try
        {
            content = await builder.BuildWidgetExportAsync(
                projectId, widget, narrowing, notFiltered => notNarrowed = WidgetQuery.NotNarrowedBy(inForceLabels, notFiltered), text, format,
                new BaseSettings(request.MinN is >= 1 ? request.MinN : null, request.ShowN ?? false), ct);
            publications = await indicators.GetPublicationsAsync(projectId, ct);
        }
        catch (DataTimeoutException)
        {
            return DataEndpoints.Timeout();
        }

        switch (content.Widget.Outcome)
        {
            case WidgetOutcome.NotPublished:
                return IndicatorEndpoints.NotPublished();
            case WidgetOutcome.MissingColumns:
                return DataEndpoints.Unprocessable(MissingColumnsCode, content.Widget.Reason!);
            case WidgetOutcome.NoLongerFits:
                return Problems.Validation("config", content.Widget.Reason!);
            case WidgetOutcome.TimedOut:
                return DataEndpoints.Timeout();
        }

        // Every filter of the dashboard, with its value in force or "All".
        var filterLines = storedFilters.Select(stored =>
        {
            var sentValue = inForce.SingleOrDefault(f => f.Stored.Column == stored.Column).Sent;
            return new ReportFilterLine(
                stored.Label,
                sentValue is null ? text["allValues"] : ReportDataBuilder.Describe(stored, new DashboardFilterValue(sentValue.Values, sentValue.Min, sentValue.Max), text));
        }).ToList();
        var now = time.GetUtcNow();
        var model = new WidgetWorkbookModel(
            project.Name,
            dashboardName,
            versionAuthor is null ? null : text.Format("versionLine", request.Version, versionAuthor.Name, format.Moment(versionSavedAt, withZone: false)),
            content.Widget,
            content.IndicatorLabel,
            config.Indicator,
            filterLines,
            notNarrowed,
            publications.GetValueOrDefault(config.Indicator)?.PublishedAt,
            now,
            format,
            text,
            dashboardSource);

        var fileName = ExportFileName.ForWidget(project.Name, dashboardName, title, request.Version, format.LocalDay(now), DataDashboard.Xlsx);
        ExportLink? link;
        try
        {
            link = exports.Register(projectId, user.Id, fileName, DataDashboard.XlsxContentType, path =>
            {
                WidgetWorkbook.Write(model, path, options.Value.MaxExportFileSize);
                return true;
            });
        }
        catch (ExportTooLargeException ex)
        {
            return DataEndpoints.Unprocessable(
                DataDashboard.ExportTooLargeCode,
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"The export is larger than the limit of {ex.Limit:N0} bytes. Add a filter to export fewer rows."));
        }

        return TypedResults.Ok(new WidgetExportResult(
            $"/api/v1/data-exports/{link!.Token}", fileName, WidgetWorkbook.RowCountOf(content.Widget), model.PublishedAt, link.ExpiresAt));
    }
}
