using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tamiza.Api.Data;

namespace Tamiza.Api.Assistant;

/// <summary>
/// The records of assistant requests and what is enforced from them (design D9): the tokens a user has used today, and the rule
/// of one request in progress per user, which a partial unique index on the running rows keeps.
/// </summary>
public sealed class AssistantLedger(TamizaDbContext db)
{
    /// <summary>The start of the UTC day that contains <paramref name="now"/>.</summary>
    public static DateTimeOffset DayStart(DateTimeOffset now) => new(now.UtcDateTime.Date, TimeSpan.Zero);

    /// <summary>
    /// The tokens the user's requests have used since the start of the UTC day, across projects. Per row it is the larger of the
    /// total and the sum of prompt and completion, because some providers count reasoning tokens only in the total.
    /// Estimates are included.
    /// </summary>
    public Task<long> TokensTodayAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var start = DayStart(now);
        return db.AssistantCalls.AsNoTracking()
            .Where(c => c.UserId == userId && c.StartedAt >= start)
            .SumAsync(c => (long)Math.Max(c.TotalTokens ?? 0, (c.PromptTokens ?? 0) + (c.CompletionTokens ?? 0)), ct);
    }

    /// <summary>Stores the running record of a request. False when the user already has a request in progress.</summary>
    public async Task<bool> BeginAsync(AssistantCall call, CancellationToken ct)
    {
        // The common refusal is answered without an insert, which would log the unique violation as an error. The partial
        // unique index still decides when two requests arrive at the same moment.
        if (await db.AssistantCalls.AsNoTracking().AnyAsync(c => c.UserId == call.UserId && c.Status == AssistantCallStatus.Running, ct))
        {
            return false;
        }

        db.AssistantCalls.Add(call);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(call).State = EntityState.Detached;
            return false;
        }
    }

    /// <summary>
    /// Ends the record of a request with how it ended. Only a call that may have used the provider's resources keeps tokens: the
    /// provider's own counts when it reported any, else an estimate from the size of what was sent and received. A refusal costs nothing.
    /// </summary>
    public async Task FinishAsync(AssistantCall call, AssistantResult? result, bool cancelled, DateTimeOffset now, CancellationToken ct)
    {
        switch (result)
        {
            case { Answer: { } answer }:
                call.Status = AssistantCallStatus.Succeeded;
                if (answer.Tokens is not null)
                {
                    SetTokens(call, answer.Usage);
                }
                else
                {
                    Estimate(call, result);
                }

                break;
            case { Failure: { } failure }:
                call.Status = failure.Kind == AssistantFailureKind.Cancelled ? AssistantCallStatus.Cancelled : AssistantCallStatus.Failed;
                call.ErrorCode = failure.Code;
                if (failure.ProviderMayHaveWorked)
                {
                    if (failure.Usage?.Tokens is not null)
                    {
                        SetTokens(call, failure.Usage);
                    }
                    else
                    {
                        Estimate(call, result);
                    }
                }

                break;
            default:
                // The call threw before it gave a result: the caller went away, or something broke in the api.
                call.Status = cancelled ? AssistantCallStatus.Cancelled : AssistantCallStatus.Failed;
                call.ErrorCode = cancelled ? null : AssistantErrorCodes.Unavailable;
                break;
        }

        call.FinishedAt = now;
        await db.SaveChangesAsync(ct);
    }

    private static void SetTokens(AssistantCall call, AssistantUsage usage)
    {
        call.PromptTokens = usage.PromptTokens;
        call.CompletionTokens = usage.CompletionTokens;
        call.TotalTokens = usage.TotalTokens;
    }

    private static void Estimate(AssistantCall call, AssistantResult result)
    {
        call.TotalTokens = result.EstimatedTokens;
        call.TokensEstimated = true;
    }

    /// <summary>
    /// Marks requests still <c>running</c> that started before <paramref name="before"/> as failed with
    /// <c>assistant.interrupted</c>: the api that was making them stopped. Assumes a single api instance, as the ingestion queue does.
    /// </summary>
    public Task<int> FailInterruptedAsync(DateTimeOffset before, DateTimeOffset now, CancellationToken ct) =>
        db.AssistantCalls.Where(c => c.Status == AssistantCallStatus.Running && c.StartedAt < before)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, AssistantCallStatus.Failed)
                .SetProperty(c => c.ErrorCode, AssistantErrorCodes.Interrupted)
                .SetProperty(c => c.FinishedAt, now), ct);
}

/// <summary>
/// At startup, ends the requests a previous api process left <c>running</c>, so their owners are not refused as busy for good. It
/// leaves requests that started after this process did.
/// </summary>
internal sealed class AssistantCallRecovery(
    IServiceScopeFactory scopes, MigrationStatus migrations, TimeProvider time, ILogger<AssistantCallRecovery> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var startedAt = time.GetUtcNow();
        try
        {
            // In production the host starts after migrations; under test hosts it may not, so wait for the schema.
            await migrations.WhenCompleted.WaitAsync(stoppingToken);
            await using var scope = scopes.CreateAsyncScope();
            var interrupted = await scope.ServiceProvider.GetRequiredService<AssistantLedger>()
                .FailInterruptedAsync(startedAt, time.GetUtcNow(), stoppingToken);
            if (interrupted > 0)
            {
                logger.LogWarning("Marked {Count} AI assistant requests interrupted by the last shutdown as failed.", interrupted);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The AI assistant requests left running by the last shutdown could not be closed.");
        }
    }
}
