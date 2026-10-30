using System.Diagnostics;
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
            call.Tool = context.Params?.Name ?? "";
            var startedAt = time.GetUtcNow();
            var timer = Stopwatch.StartNew();

            try
            {
                using var slot = services.GetRequiredService<McpLimiter>().TryEnter(user.Id, out var retryAfter);
                if (slot is null)
                {
                    var refusal = McpRefusal.RateLimited(retryAfter);
                    call.Refused(refusal);
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
                await services.GetRequiredService<McpLedger>().RecordAsync(user.Id, call, timer.Elapsed, startedAt);
            }
        };
}
