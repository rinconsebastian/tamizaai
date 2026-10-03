using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Tamiza.Api.Data;

namespace Tamiza.Api.Ingestion;

/// <summary>Wakes the scheduler before its next tick, after a manual request or a project creation.</summary>
public sealed class IngestionSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Wake() => _channel.Writer.TryWrite(true);

    public async Task WaitAsync(CancellationToken cancellationToken) => await _channel.Reader.ReadAsync(cancellationToken);
}

/// <summary>
/// Queues due synchronizations and reconciliations and runs queued ones, a few at a time across projects. There is no
/// queue service: the <c>syncs</c> table is the queue (see <see cref="SyncQueue"/>).
/// </summary>
public sealed class IngestionScheduler(
    IServiceScopeFactory scopes,
    IngestionSignal signal,
    MigrationStatus migrations,
    TimeProvider time,
    IOptions<IngestionOptions> options,
    ILogger<IngestionScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.SchedulerEnabled)
        {
            logger.LogInformation("The ingestion scheduler is disabled.");
            return;
        }

        // In production the host starts after migrations; under test hosts it may not, so wait for the schema.
        await migrations.WhenCompleted.WaitAsync(stoppingToken);
        await WithQueueAsync(async queue =>
        {
            var interrupted = await queue.FailInterruptedAsync(time.GetUtcNow(), stoppingToken);
            if (interrupted > 0)
            {
                logger.LogWarning("Marked {Count} synchronizations interrupted by the last shutdown as failed.", interrupted);
            }
        });

        var running = new List<Task>();
        Task? wake = null, tick = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                running.RemoveAll(task => task.IsCompleted);
                await WithQueueAsync(queue => queue.EnqueueDueAsync(time.GetUtcNow(), stoppingToken));
                while (running.Count < settings.MaxConcurrentRuns)
                {
                    ClaimedSync? claimed = null;
                    await WithQueueAsync(async queue => claimed = await queue.ClaimNextAsync(time.GetUtcNow(), stoppingToken));
                    if (claimed is null)
                    {
                        break;
                    }

                    running.Add(Task.Run(() => RunAsync(claimed, stoppingToken), CancellationToken.None));
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "The ingestion scheduler could not check for due synchronizations; it will retry.");
            }

            // The wake and tick tasks survive iterations, so a signal is never consumed by a wait nobody observes.
            wake ??= signal.WaitAsync(stoppingToken);
            tick ??= Task.Delay(settings.Tick, time, stoppingToken);
            await Task.WhenAny([wake, tick, .. running]);
            wake = wake.IsCompleted ? null : wake;
            tick = tick.IsCompleted ? null : tick;
        }

        // Runs observe the token, record themselves as interrupted and end.
        await Task.WhenAll(running);
    }

    private async Task RunAsync(ClaimedSync claimed, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<SyncRunner>().RunAsync(claimed, stoppingToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Synchronization {SyncId} could not be recorded as finished.", claimed.Id);
        }
    }

    private async Task WithQueueAsync(Func<SyncQueue, Task> action)
    {
        await using var scope = scopes.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<SyncQueue>());
    }
}
