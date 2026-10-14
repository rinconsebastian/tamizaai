using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamiza.Api.Data;
using Tamiza.Api.Projects;

namespace Tamiza.Api.Ingestion;

/// <summary>
/// Fetches the definitions of form versions between synchronizations, so a version that arrived by webhook has its labels
/// within a tick instead of waiting for the next synchronization. It has its own loop and does not use the
/// synchronizations' slots, so long full synchronizations of other projects never delay it. It is enabled and timed by the
/// scheduler's options.
/// </summary>
public sealed class FormVersionWorker(
    IServiceScopeFactory scopes,
    MigrationStatus migrations,
    TimeProvider time,
    IOptions<IngestionOptions> options,
    ILogger<FormVersionWorker> logger) : BackgroundService
{
    /// <summary>Time a pass spends on one project; versions left over are taken by the next tick.</summary>
    private static readonly TimeSpan ProjectBudget = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.SchedulerEnabled)
        {
            logger.LogInformation("The form version worker is disabled.");
            return;
        }

        await migrations.WhenCompleted.WaitAsync(stoppingToken);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunPassAsync(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(exception, "The form version worker could not check for due versions; it will retry.");
                }

                await Task.Delay(settings.Tick, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping.
        }
    }

    /// <summary>One pass: every project that has due versions and no synchronization running, one project at a time.</summary>
    private async Task RunPassAsync(CancellationToken stoppingToken)
    {
        List<Guid> projectIds;
        await using (var scope = scopes.CreateAsyncScope())
        {
            projectIds = await scope.ServiceProvider.GetRequiredService<FormVersionStore>().ProjectsWithDueVersionsAsync(time.GetUtcNow(), stoppingToken);
        }

        foreach (var projectId in projectIds)
        {
            stoppingToken.ThrowIfCancellationRequested();
            try
            {
                await FetchProjectAsync(projectId, stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "The form versions of project {ProjectId} could not be fetched; they stay due.", projectId);
            }
        }
    }

    private async Task FetchProjectAsync(Guid projectId, CancellationToken stoppingToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TamizaDbContext>();
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, stoppingToken);

        // A running synchronization fetches the project's versions itself before it ends. A queued one does not block this:
        // every write is guarded, and a duplicate request costs nothing.
        if (project is null || await db.Syncs.AnyAsync(s => s.ProjectId == projectId && s.Status == SyncStatus.Running, stoppingToken))
        {
            return;
        }

        var token = scope.ServiceProvider.GetRequiredService<SecretProtector>().TryUnprotect(project.EncryptedApiToken, out var unprotected) ? unprotected : null;
        var result = await scope.ServiceProvider.GetRequiredService<FormVersionFetcher>().FetchAsync(project, token, FetchMode.Due, ProjectBudget, stoppingToken);
        if (result.Selected > 0)
        {
            logger.LogInformation("Fetched {Fetched} of {Selected} due form versions of project {ProjectId}.", result.Fetched, result.Selected, projectId);
        }
    }
}
