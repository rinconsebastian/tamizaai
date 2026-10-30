using System.Diagnostics;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Tamiza.Api.Auth;
using Tamiza.Api.Data;

namespace Tamiza.Api.Mcp;

/// <summary>
/// Wraps every tool call (design D12): the person's limits, an error result for whatever the tool did not catch, and the record of the
/// call. Tools report refusals through <see cref="McpCallScope"/>; whatever else goes wrong is a failure.
/// </summary>
public static class McpCallFilter
{
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Create(McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (context, ct) =>
        {
            var services = context.Services ?? throw new InvalidOperationException("The MCP request has no service provider.");
            var user = services.GetRequiredService<ICurrentUser>();
            var call = services.GetRequiredService<McpCallScope>();
            var time = services.GetRequiredService<TimeProvider>();
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(McpCallFilter));
            call.Tool = Cut(context.Params?.Name ?? "");
            var startedAt = time.GetUtcNow();
            var timer = Stopwatch.StartNew();

            try
            {
                var limiter = services.GetRequiredService<McpLimiter>();
                using var slot = limiter.TryEnter(user.Id, out var retryAfter);
                if (slot is null)
                {
                    var refusal = McpRefusal.RateLimited(retryAfter);
                    call.Refused(refusal);
                    call.SkipRecord = !limiter.ShouldRecordRefusal(user.Id);
                    return McpOutput.Error(refusal, call);
                }

                CallToolResult result;
                try
                {
                    result = await next(context, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    call.Failed("mcp.cancelled");
                    throw;
                }
                catch (Exception exception) when (!call.BodyStarted && ArgumentsRefusal(exception) is { } refusal)
                {
                    // The request was wrong before any tool code ran: an unknown tool, a missing or malformed argument. Say which, so the client can correct it.
                    call.Refused(refusal);
                    return McpOutput.Error(refusal, call);
                }
                catch (Exception exception)
                {
                    // Never the exception's text: it could quote a value. The log has it, without the arguments.
                    logger.LogError(exception, "The MCP tool {Tool} failed.", call.Tool);
                    call.Failed(McpRefusal.FailedCode);
                    return McpOutput.Error(McpRefusal.Failed("The tool failed. Try again; if it keeps failing, tell the operator."), call);
                }

                if (result.IsError == true && call.Status == McpCallStatus.Succeeded)
                {
                    // The SDK turned an exception into an error result before this filter saw it.
                    call.Failed(McpRefusal.FailedCode);
                }

                call.ResultChars ??= result.Content.OfType<TextContentBlock>().Sum(t => t.Text.Length);
                return result;
            }
            finally
            {
                timer.Stop();
                if (!call.SkipRecord)
                {
                    await services.GetRequiredService<McpLedger>().RecordAsync(user.Id, call, timer.Elapsed, startedAt);
                }
            }
        };

    /// <summary>The longest tool name the record keeps (the column's length).</summary>
    private const int MaxToolNameLength = 64;

    private static string Cut(string name) => name.Length <= MaxToolNameLength ? name : name[..MaxToolNameLength];

    /// <summary>
    /// What to tell a client whose request the SDK could not bind to a tool: the SDK's messages for these cases name the tool or the argument,
    /// and a value in them is the client's own. Null for any other exception, which is a failure.
    /// </summary>
    private static McpRefusal? ArgumentsRefusal(Exception exception) => exception switch
    {
        McpProtocolException { Message: var message } when message.StartsWith("Unknown tool", StringComparison.Ordinal) =>
            McpRefusal.NotFound("The tool", "Call tools/list for the tools that exist."),
        ArgumentException or System.Text.Json.JsonException or FormatException =>
            McpRefusal.Validation(new Dictionary<string, string[]> { ["arguments"] = [exception.Message] }),
        _ => null,
    };
}
