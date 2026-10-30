using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamiza.Api.Data;

namespace Tamiza.Api.Mcp;

/// <summary>Deletes the records of tool calls older than <see cref="McpOptions.CallRetentionDays"/>, once a day (design D13).</summary>
public sealed class McpCallSweeper(
    IServiceScopeFactory scopes, IOptions<McpOptions> options, TimeProvider time, ILogger<McpCallSweeper> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Could not delete the old MCP call records.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Deletes the records older than the retention and returns how many.</summary>
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TamizaDbContext>();
        var before = time.GetUtcNow().AddDays(-options.Value.CallRetentionDays);
        return await db.McpCalls.Where(c => c.StartedAt < before).ExecuteDeleteAsync(ct);
    }
}
