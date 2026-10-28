using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamiza.Api.Auth;
using Tamiza.Api.Data;
using Tamiza.Api.Email;
using Tamiza.Api.Http;
using Tamiza.Api.Indicators;
using Tamiza.Api.Projects;

namespace Tamiza.Api.Reports;

/// <summary>A dashboard's scheduled email as members set it: recipients, ISO weekdays (1 = Monday), the local time and the saver's locale.</summary>
public sealed record ReportSettings(IReadOnlyList<string> Recipients, IReadOnlyList<int> Days, string Time, bool Active, string Locale);

/// <summary>The last failed attempt: its code (<c>email.*</c> or <c>reports.*</c>), when it happened, and the refused recipient for <c>email.rejected</c>.</summary>
public sealed record ReportFailure(string Code, DateTimeOffset At, string? Address);

public sealed record ReportAuthor(string Name);

/// <param name="Settings">Null while the dashboard has no scheduled email.</param>
/// <param name="TimeZone">The project's time zone, which the time of day is in.</param>
public sealed record ReportView(
    ReportSettings? Settings, string TimeZone, ReportAuthor? UpdatedBy, DateTimeOffset? UpdatedAt, DateTimeOffset? LastSentAt, ReportFailure? LastFailure);

/// <summary>The body that saves a dashboard's scheduled email.</summary>
public sealed record ReportRequest(IReadOnlyList<string?>? Recipients, IReadOnlyList<int>? Days, string? Time, bool? Active, string? Locale);

/// <summary>A dashboard's scheduled email (design D13): only analysts and admins read, save, remove or send it now.</summary>
public static class ReportEndpoints
{
    public const string UnavailableCode = "reports.unavailable";
    public const string NoneCode = "reports.none";
    public const string TooSoonCode = "reports.tooSoon";

    public const int MaxRecipients = 20;

    public static RouteGroupBuilder MapReportEndpoints(this RouteGroupBuilder api)
    {
        var report = api.MapGroup("/projects/{projectId:guid}/dashboards/{dashboardId:guid}/report").WithTags("Dashboard reports");
        report.MapGet("", GetAsync).RequireProjectRole(ProjectRole.Analyst).WithName("GetDashboardReport");
        report.MapPut("", SaveAsync).RequireProjectRole(ProjectRole.Analyst).WithName("SaveDashboardReport")
            .WithMetadata(new RequestSizeLimitAttribute(64 * 1024));
        report.MapDelete("", RemoveAsync).RequireProjectRole(ProjectRole.Analyst).WithName("RemoveDashboardReport");
        report.MapPost("/send", SendNowAsync).RequireProjectRole(ProjectRole.Analyst).WithName("SendDashboardReport");
        return api;
    }

    private static async Task<Results<Ok<ReportView>, ProblemHttpResult>> GetAsync(
        Guid projectId, Guid dashboardId, TamizaDbContext db, CancellationToken ct)
    {
        var view = await ViewAsync(db, projectId, dashboardId, ct);
        return view is null ? DashboardEndpoints.DashboardNotFound() : TypedResults.Ok(view);
    }

    private static async Task<Results<Ok<ReportView>, ValidationProblem, ProblemHttpResult>> SaveAsync(
        Guid projectId, Guid dashboardId, ReportRequest? request, TamizaDbContext db, ICurrentUser user, IOptions<EmailOptions> email,
        [FromKeyedServices(ReportsRegistration.ClockKey)] TimeProvider time, CancellationToken ct)
    {
        if (!email.Value.Enabled)
        {
            return Unavailable();
        }

        var (settings, errors) = Validate(request);
        if (settings is null)
        {
            return Problems.Validation(errors.ToDictionary());
        }

        var now = time.GetUtcNow();
        var recipients = settings.Recipients.ToArray();
        var days = settings.Days.Select(d => (short)d).ToArray();
        var sendTime = TimeOnly.ParseExact(settings.Time, "HH:mm", CultureInfo.InvariantCulture);
        // Only the latest save counts: one made after today's time marks today as not due (design D4); the day the scheduler took and
        // the last "send now" stay whatever is saved, also over an email removed and saved again.
        var saved = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO tamiza.dashboard_reports (dashboard_id, recipients, days, send_time, active, locale, updated_by, updated_at, not_before)
            SELECT d.id, {recipients}, {days}, {sendTime}, {settings.Active}, {settings.Locale}, {user.Id}, {now},
                   CASE WHEN ({now} AT TIME ZONE p.time_zone)::time >= {sendTime} THEN {now} ELSE NULL END
            FROM tamiza.dashboards d JOIN tamiza.projects p ON p.id = d.project_id
            WHERE d.id = {dashboardId} AND d.project_id = {projectId}
            ON CONFLICT (dashboard_id) DO UPDATE SET
                recipients = EXCLUDED.recipients, days = EXCLUDED.days, send_time = EXCLUDED.send_time, active = EXCLUDED.active,
                locale = EXCLUDED.locale, updated_by = EXCLUDED.updated_by, updated_at = EXCLUDED.updated_at, not_before = EXCLUDED.not_before,
                removed_at = NULL
            """, ct);
        if (saved == 0)
        {
            return DashboardEndpoints.DashboardNotFound();
        }

        return TypedResults.Ok((await ViewAsync(db, projectId, dashboardId, ct))!);
    }

    /// <summary>
    /// Removes the email: its recipients are forgotten and nothing more is sent, but the row stays with the day last sent and the last
    /// "send now", so that removing and saving it again neither sends a day twice nor skips the 10-minute rule.
    /// </summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveAsync(
        Guid projectId, Guid dashboardId, TamizaDbContext db, [FromKeyedServices(ReportsRegistration.ClockKey)] TimeProvider time,
        CancellationToken ct)
    {
        if (!await db.Dashboards.AnyAsync(d => d.Id == dashboardId && d.ProjectId == projectId, ct))
        {
            return DashboardEndpoints.DashboardNotFound();
        }

        var now = time.GetUtcNow();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE tamiza.dashboard_reports
            SET removed_at = {now}, recipients = ARRAY[]::text[], active = false, send_now_pending = false
            WHERE dashboard_id = {dashboardId} AND removed_at IS NULL
            """, ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Accepted, ProblemHttpResult>> SendNowAsync(
        Guid projectId, Guid dashboardId, TamizaDbContext db, IOptions<EmailOptions> email, IOptions<ReportOptions> options,
        ReportSignal signal, [FromKeyedServices(ReportsRegistration.ClockKey)] TimeProvider time, CancellationToken ct)
    {
        if (!email.Value.Enabled)
        {
            return Unavailable();
        }

        var now = time.GetUtcNow();
        var since = now - options.Value.SendNowCooldown;
        var queued = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE tamiza.dashboard_reports r SET send_now_pending = true, send_now_requested_at = {now}
            FROM tamiza.dashboards d
            WHERE d.id = r.dashboard_id AND r.dashboard_id = {dashboardId} AND d.project_id = {projectId} AND r.removed_at IS NULL
              AND (r.send_now_requested_at IS NULL OR r.send_now_requested_at <= {since})
            """, ct);
        if (queued == 0)
        {
            if (!await db.Dashboards.AnyAsync(d => d.Id == dashboardId && d.ProjectId == projectId, ct))
            {
                return DashboardEndpoints.DashboardNotFound();
            }

            return await db.DashboardReports.AnyAsync(r => r.DashboardId == dashboardId && r.RemovedAt == null, ct)
                ? Problems.Conflict(TooSoonCode, $"This email was sent now less than {options.Value.SendNowCooldown.TotalMinutes:0} minutes ago. Try again later.")
                : Problems.Conflict(NoneCode, "This dashboard has no scheduled email. Save its settings first.");
        }

        signal.Wake();
        return TypedResults.Accepted((string?)null);
    }

    private static ProblemHttpResult Unavailable() =>
        Problems.Status(StatusCodes.Status503ServiceUnavailable, UnavailableCode, "Email is not available.",
            "This deployment does not send email: the operator has not configured an SMTP server.");

    /// <summary>The dashboard's email as members see it; null when the project has no such dashboard.</summary>
    private static async Task<ReportView?> ViewAsync(TamizaDbContext db, Guid projectId, Guid dashboardId, CancellationToken ct)
    {
        var dashboard = await db.Dashboards.AsNoTracking()
            .Where(d => d.Id == dashboardId && d.ProjectId == projectId)
            .Select(d => new { TimeZone = db.Projects.Where(p => p.Id == d.ProjectId).Select(p => p.TimeZone).Single() })
            .SingleOrDefaultAsync(ct);
        if (dashboard is null)
        {
            return null;
        }

        var report = await db.DashboardReports.AsNoTracking()
            .Where(r => r.DashboardId == dashboardId && r.RemovedAt == null)
            .Select(r => new { Report = r, Author = db.Users.Where(u => u.Id == r.UpdatedBy).Select(u => u.Name).Single() })
            .SingleOrDefaultAsync(ct);
        if (report is null)
        {
            return new ReportView(null, dashboard.TimeZone, null, null, null, null);
        }

        var r = report.Report;
        return new ReportView(
            new ReportSettings(r.Recipients, r.Days.Select(d => (int)d).ToList(), r.SendTime.ToString("HH:mm", CultureInfo.InvariantCulture), r.Active, r.Locale),
            dashboard.TimeZone,
            new ReportAuthor(report.Author),
            r.UpdatedAt,
            r.LastSentAt,
            r.LastFailure is { } code ? new ReportFailure(code, r.LastFailedAt ?? r.UpdatedAt, r.LastFailureAddress) : null);
    }

    /// <summary>The settings to store, or the field errors that refuse them (spec "Email recipients" and "Sending days and time").</summary>
    public static (ReportSettings? Settings, FieldErrors Errors) Validate(ReportRequest? request)
    {
        var errors = new FieldErrors();
        var recipients = new List<string>();
        var raw = request?.Recipients ?? [];
        // Anything much longer than the limit is refused before any address is looked at.
        if (raw.Count > MaxRecipients * 2)
        {
            errors.Add("recipients", $"A scheduled email can have at most {MaxRecipients} recipients.");
        }
        else
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var invalid = new List<string>();
            foreach (var address in raw.Select(r => r?.Trim() ?? "").Where(a => a.Length > 0))
            {
                if (!EmailAddressRule.IsValid(address))
                {
                    invalid.Add(address.Length > 60 ? address[..60] + "…" : address);
                }
                else if (seen.Add(address))
                {
                    recipients.Add(address);
                }
            }

            // One message naming every refused address: a page shows a field's first message only.
            if (invalid.Count > 0)
            {
                errors.Add("recipients", invalid.Count == 1
                    ? $"\"{invalid[0]}\" is not a valid email address."
                    : $"These are not valid email addresses: {string.Join(", ", invalid.Select(a => $"\"{a}\""))}.");
            }
            else if (recipients.Count == 0)
            {
                errors.Add("recipients", "Add at least one recipient.");
            }
            else if (recipients.Count > MaxRecipients)
            {
                errors.Add("recipients", $"A scheduled email can have at most {MaxRecipients} recipients.");
            }
        }

        var days = (request?.Days ?? []).Distinct().Order().ToList();
        if (days.Count == 0)
        {
            errors.Add("days", "Choose at least one weekday.");
        }
        else if (days.Any(d => d is < 1 or > 7))
        {
            errors.Add("days", "Weekdays go from 1 (Monday) to 7 (Sunday).");
        }

        var time = request?.Time?.Trim() ?? "";
        if (!TimeOnly.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            errors.Add("time", "Enter a time of day as HH:mm, such as 07:00.");
        }

        // A browser region the server has no data for (en-CO, es-DE) falls back to its language's formats.
        var locale = ReportText.Resolve(request?.Locale?.Trim());
        if (locale is null)
        {
            errors.Add("locale", "The language must be Spanish or English, such as es-CO or en-US.");
        }

        return errors.Any
            ? (null, errors)
            : (new ReportSettings(recipients, days, time, request?.Active ?? true, locale!), errors);
    }
}
