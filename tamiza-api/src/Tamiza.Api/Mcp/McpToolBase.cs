using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using Tamiza.Api.CleanData;

namespace Tamiza.Api.Mcp;

/// <summary>
/// What every tool class shares (design D6): the access gate and the one place that turns a tool's value, a refusal or a slow read into a
/// result. A tool body returns the value to send, or throws <see cref="McpRefusal"/>; it never builds a result itself.
/// </summary>
public abstract class McpToolBase(McpAccessGate gate, McpCallScope call, IOptions<McpOptions> options)
{
    protected McpAccessGate Gate { get; } = gate;

    protected McpCallScope Call { get; } = call;

    protected McpOptions Settings => options.Value;

    /// <summary>
    /// Runs <paramref name="body"/> and gives its value as the result, within the size budget; <paramref name="hint"/> names the argument
    /// that asks for less when the value is cut. A refusal becomes an error result with its code and message.
    /// </summary>
    protected async Task<CallToolResult> RunAsync(Func<Task<object?>> body, string? hint = null)
    {
        Call.BodyStarted = true;
        try
        {
            return McpOutput.Ok(await body(), Settings.MaxResultCharacters, hint, Call);
        }
        catch (McpRefusal refusal)
        {
            Call.Refused(refusal);
            return McpOutput.Error(refusal, Call);
        }
        catch (DataTimeoutException)
        {
            var refusal = McpRefusal.Timeout(hint is null ? "Ask for less data." : $"Ask for less: {hint}");
            Call.Refused(refusal);
            return McpOutput.Error(refusal, Call);
        }
    }
}
