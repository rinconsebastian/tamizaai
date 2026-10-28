using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamiza.Api.Auth;
using Tamiza.Api.CleanData;
using Tamiza.Api.Data;
using Tamiza.Api.Email;

namespace Tamiza.Api.Reports;

/// <summary>
/// Sends one dashboard's email and records how it went (design D9, D10). The workbook is written first, to a file of the export
/// directory the startup sweep cleans, so that the bodies can say whether it is attached; the file is deleted once the email is sent.
/// A failure is recorded with its code and never retried: the next scheduled day, or sending now, tries again.
/// </summary>
public sealed class ReportSender(
    TamizaDbContext db,
    ReportDataBuilder builder,
    EmailSenderSource email,
    SuperAdminRule superAdmins,
    ExportRegistry exports,
    IOptions<ReportOptions> options,
    [FromKeyedServices(ReportsRegistration.ClockKey)] TimeProvider time,
    ILogger<ReportSender> logger)
{
    public const string NothingToSendCode = "reports.nothingToSend";
    public const string FailedCode = "reports.failed";
    public const string AuthorNoAccessCode = "reports.authorNoAccess";

    /// <summary>Sends the email of <paramref name="dashboardId"/>; does nothing when it has none (it was removed meanwhile) or email is off.</summary>
    public async Task SendAsync(Guid dashboardId, CancellationToken ct)
    {
        var report = await db.DashboardReports.AsNoTracking().SingleOrDefaultAsync(r => r.DashboardId == dashboardId && r.RemovedAt == null, ct);
        if (report is null || email.Sender is not { } sender)
        {
            return;
        }

        try
        {
            // The member who set the email up must still manage the project: replies reach them and they alone answer for the
            // addresses. Until another analyst or admin saves it again, which makes it theirs, nothing is sent.
            var author = await db.Users.AsNoTracking().Where(u => u.Id == report.UpdatedBy).Select(u => new { u.Email, u.EmailVerified }).SingleAsync(ct);
            var role = await db.ProjectMembers.AsNoTracking()
                .Where(m => m.UserId == report.UpdatedBy && db.Dashboards.Any(d => d.Id == dashboardId && d.ProjectId == m.ProjectId))
                .Select(m => (ProjectRole?)m.Role)
                .SingleOrDefaultAsync(ct);
            if (role is not (ProjectRole.Analyst or ProjectRole.Admin) && !superAdmins.IsSuperAdmin(author.Email, author.EmailVerified))
            {
                await RecordFailureAsync(dashboardId, AuthorNoAccessCode, null, ct);
                return;
            }

            var model = await builder.BuildAsync(dashboardId, report.Locale, ct);
            if (model is null)
            {
                return;
            }

            if (model.NothingToSend)
            {
                await RecordFailureAsync(dashboardId, NothingToSendCode, null, ct);
                return;
            }

            var settings = options.Value;
            var path = exports.NewFilePath();
            try
            {
                var size = ReportWorkbook.Write(model, path);
                var attached = size <= settings.MaxAttachmentKb * 1024L;
                var (html, shown) = ReportHtml.Write(model, attached, settings);
                var text = ReportPlainText.Write(model, attached, shown, settings);
                var workbook = attached ? (ReportWorkbook.FileName(model), await File.ReadAllBytesAsync(path, ct)) : ((string, byte[])?)null;
                var message = ReportMessage.Build(model, html, text, workbook, report.Recipients, author.Email);

                var result = await sender.SendAsync(message, ct);
                if (result.Succeeded)
                {
                    await RecordSentAsync(dashboardId, ct);
                    logger.LogInformation("Sent the scheduled email of dashboard {DashboardId} to {Count} recipients.", dashboardId, report.Recipients.Count);
                }
                else
                {
                    await RecordFailureAsync(dashboardId, result.Code!, result.RefusedAddress, ct);
                }
            }
            finally
            {
                ExportRegistry.DeleteQuietly(path);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(exception, "The scheduled email of dashboard {DashboardId} could not be prepared or sent.", dashboardId);
            await RecordFailureAsync(dashboardId, FailedCode, null, CancellationToken.None);
        }
    }

    private Task RecordSentAsync(Guid dashboardId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return db.DashboardReports.Where(r => r.DashboardId == dashboardId).ExecuteUpdateAsync(set => set
            .SetProperty(r => r.LastSentAt, now)
            .SetProperty(r => r.LastFailedAt, (DateTimeOffset?)null)
            .SetProperty(r => r.LastFailure, (string?)null)
            .SetProperty(r => r.LastFailureAddress, (string?)null), ct);
    }

    private Task RecordFailureAsync(Guid dashboardId, string code, string? address, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return db.DashboardReports.Where(r => r.DashboardId == dashboardId).ExecuteUpdateAsync(set => set
            .SetProperty(r => r.LastFailedAt, now)
            .SetProperty(r => r.LastFailure, code)
            .SetProperty(r => r.LastFailureAddress, address), ct);
    }
}
