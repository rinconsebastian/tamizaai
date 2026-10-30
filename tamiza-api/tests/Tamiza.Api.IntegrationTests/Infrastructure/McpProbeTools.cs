using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Tamiza.Api.Data;
using Tamiza.Api.Mcp;
using Tamiza.Api.Projects;

namespace Tamiza.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Tools that exist only in tests, to exercise what every real tool shares: the access gate, the result budget, the call filter and
/// the limits. They are registered by <see cref="McpHarness"/> through the same builder the server uses.
/// </summary>
[McpServerToolType]
public sealed class McpProbeTools(McpAccessGate gate, McpCallScope call, IOptions<McpOptions> options) : McpToolBase(gate, call, options)
{
    public static void Register(IServiceCollection services) => services.AddMcpServer().WithTools<McpProbeTools>();

    /// <summary>Counts the calls that are running, to show the limits run before the tool.</summary>
    public static int Started;

    [McpServerTool(Name = "probe_viewer", ReadOnly = true), Description("Needs the viewer role and summaries.")]
    public Task<CallToolResult> Viewer(Guid projectId) =>
        RunAsync(async () =>
        {
            var project = await Gate.RequireAsync(projectId, ProjectRole.Viewer, McpNeeds.Summaries, CancellationToken.None);
            return new { project.Role, project.Level };
        });

    [McpServerTool(Name = "probe_analyst"), Description("Needs the analyst role and summaries.")]
    public Task<CallToolResult> Analyst(Guid projectId) =>
        RunAsync(async () =>
        {
            var project = await Gate.RequireAsync(projectId, ProjectRole.Analyst, McpNeeds.Summaries, CancellationToken.None);
            return new { project.Role, project.Level };
        });

    [McpServerTool(Name = "probe_rows", ReadOnly = true), Description("Needs the viewer role and rows.")]
    public Task<CallToolResult> Rows(Guid projectId) =>
        RunAsync(async () =>
        {
            var project = await Gate.RequireAsync(projectId, ProjectRole.Viewer, McpNeeds.Rows, CancellationToken.None);
            return new { project.Role, project.Level };
        });

    [McpServerTool(Name = "probe_ok", ReadOnly = true), Description("Needs nothing.")]
    public Task<CallToolResult> Ok() => RunAsync(() => Task.FromResult<object?>(new { fine = true }));

    [McpServerTool(Name = "probe_big", ReadOnly = true), Description("A long list.")]
    public Task<CallToolResult> Big(int items) =>
        RunAsync(() => Task.FromResult<object?>(new { name = "big", items = Enumerable.Range(0, items).Select(i => new { id = i, label = $"item number {i} with some text" }).ToList() }),
            hint: "page");

    [McpServerTool(Name = "probe_fail", ReadOnly = true), Description("Throws.")]
    public Task<CallToolResult> Fail() => RunAsync(() => throw new InvalidOperationException("secret value in the exception"));

    [McpServerTool(Name = "probe_slow", ReadOnly = true), Description("Takes a while.")]
    public Task<CallToolResult> Slow(int milliseconds, CancellationToken ct) =>
        RunAsync(async () =>
        {
            Interlocked.Increment(ref Started);
            await Task.Delay(milliseconds, ct);
            return new { slept = milliseconds };
        });
}
