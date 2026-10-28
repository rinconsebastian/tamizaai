using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Tamiza.Api.Data;
using Tamiza.Api.Email;

namespace Tamiza.Api.Reports;

/// <summary>Wakes the report scheduler before its next tick, after a "send now" request.</summary>
public sealed class ReportSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Wake() => _channel.Writer.TryWrite(true);

    public async Task WaitAsync(CancellationToken cancellationToken) => await _channel.Reader.ReadAsync(cancellationToken);
}

/// <summary>
/// Sends scheduled dashboard emails, one at a time (design D6). Each step of a pass takes a pending "send now" request first, and only
/// when there is none one due report, so a request made during a busy hour waits for the email being prepared, not for the whole pass.
/// While email is off nothing is taken, so the days that pass are not used up.
/// </summary>
public sealed class ReportScheduler(
    IServiceScopeFactory scopes,
    ReportSignal signal,
    MigrationStatus migrations,
    IOptions<EmailOptions> email,
    IOptions<ReportOptions> options,
    [FromKeyedServices(ReportsRegistration.ClockKey)] TimeProvider time,
    ILogger<ReportScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!email.Value.Enabled)
        {
            logger.LogInformation("Scheduled dashboard emails are off: email is not configured.");
            return;
        }

        var settings = options.Value;
        if (!settings.SchedulerEnabled)
        {
            logger.LogInformation("The report scheduler is disabled.");
            return;
        }

        // In production the host starts after migrations; under test hosts it may not, so wait for the schema.
        await migrations.WhenCompleted.WaitAsync(stoppingToken);
        Task? wake = null, tick = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPassAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "The report scheduler could not check for due emails; it will retry.");
            }

            // The wake and tick tasks survive passes, so a signal is never consumed by a wait nobody observes.
            wake ??= signal.WaitAsync(stoppingToken);
            tick ??= Task.Delay(settings.Tick, TimeProvider.System, stoppingToken);
            await Task.WhenAny(wake, tick);
            wake = wake.IsCompleted ? null : wake;
            tick = tick.IsCompleted ? null : tick;
        }
    }

    /// <summary>Sends every email due now and every pending "send now", one after another. Returns how many were taken.</summary>
    public async Task<int> RunPassAsync(CancellationToken ct)
    {
        if (!email.Value.Enabled)
        {
            return 0;
        }

        var taken = 0;
        while (!ct.IsCancellationRequested)
        {
            await using var scope = scopes.CreateAsyncScope();
            var queue = scope.ServiceProvider.GetRequiredService<ReportQueue>();
            var dashboardId = await queue.ClaimSendNowAsync(ct) ?? await queue.ClaimDueAsync(time.GetUtcNow(), ct);
            if (dashboardId is not { } id)
            {
                break;
            }

            // The claim is made: a restart from here on skips this send rather than repeating it.
            taken++;
            await scope.ServiceProvider.GetRequiredService<ReportSender>().SendAsync(id, ct);
        }

        return taken;
    }
}
