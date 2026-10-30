using Tamiza.Api.Data;

namespace Tamiza.Api.Mcp;

/// <summary>Writes the record of a tool call (design D13). It holds no argument and no result, and a failure to write it never fails the call.</summary>
public sealed class McpLedger(IServiceScopeFactory scopes, ILogger<McpLedger> logger)
{
    public async Task RecordAsync(Guid userId, McpCallScope call, TimeSpan duration, DateTimeOffset startedAt)
    {
        try
        {
            // A scope of its own and no cancellation: a client that disconnects mid-call is still recorded.
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TamizaDbContext>();
            db.McpCalls.Add(new McpCall
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ProjectId = call.ProjectId,
                Tool = call.Tool.Length > 64 ? call.Tool[..64] : call.Tool,
                Access = call.Access is McpAccess.Aggregates or McpAccess.Rows ? call.Access : null,
                Status = call.Status,
                ErrorCode = call.ErrorCode is { Length: > 64 } code ? code[..64] : call.ErrorCode,
                DurationMs = (int)Math.Min(int.MaxValue, duration.TotalMilliseconds),
                ResultChars = call.ResultChars,
                StartedAt = startedAt,
            });
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not record the MCP call {Tool}.", call.Tool);
        }
    }
}
