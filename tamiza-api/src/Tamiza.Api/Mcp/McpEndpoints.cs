using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamiza.Api.Configuration;
using Tamiza.Api.Data;
using Tamiza.Api.Projects;

namespace Tamiza.Api.Mcp;

/// <summary>What a person needs to connect an AI client to this installation.</summary>
/// <param name="Url">The MCP address clients enter.</param>
/// <param name="ClientId">The public client registered in the identity provider for AI clients.</param>
/// <param name="CallbackPort">The loopback port registered for Claude Code.</param>
/// <param name="MaxDataAccess">The most any project may allow: <c>aggregates</c> or <c>rows</c>.</param>
/// <param name="AccountUrl">The identity provider's account page, where a person revokes the connection of an AI client.</param>
public sealed record McpConnection(string Url, string ClientId, int CallbackPort, McpAccess MaxDataAccess, string AccountUrl);

/// <summary>One recorded call of an AI client in a project: never its arguments or its result.</summary>
public sealed record McpCallItem(Guid Id, DateTimeOffset StartedAt, string UserName, string Tool, McpCallStatus Status, string? ErrorCode, McpAccess? Access);

public sealed record McpCallList(IReadOnlyList<McpCallItem> Items);

/// <summary>The REST side of the MCP server: the connection details for the connect page and the activity of a project (design D15).</summary>
public static class McpEndpoints
{
    /// <summary>The calls the activity list shows.</summary>
    public const int RecentCalls = 50;

    public static RouteGroupBuilder MapMcpEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/mcp", GetConnection).WithName("GetMcpConnection").WithTags("MCP");
        api.MapGet("/projects/{projectId:guid}/mcp-calls", ListCallsAsync)
            .RequireProjectRole(ProjectRole.Analyst).WithName("ListMcpCalls").WithTags("MCP");
        return api;
    }

    /// <summary>The address, the client ID, the port and the maximum access; not found while the server is disabled.</summary>
    private static Results<Ok<McpConnection>, NotFound> GetConnection(IOptions<McpOptions> mcp, IOptions<TamizaOptions> tamiza, IOptions<OidcOptions> oidc)
    {
        if (!mcp.Value.Enabled)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new McpConnection(
            McpAuthentication.ResourceUrl(tamiza.Value),
            mcp.Value.ClientId,
            mcp.Value.CallbackPort,
            mcp.Value.MaxAccess,
            oidc.Value.Authority.TrimEnd('/') + "/account/#/applications"));
    }

    /// <summary>The project's 50 most recent calls, newest first.</summary>
    private static async Task<Ok<McpCallList>> ListCallsAsync(Guid projectId, TamizaDbContext db, CancellationToken ct)
    {
        var items = await db.McpCalls.AsNoTracking()
            .Where(c => c.ProjectId == projectId)
            .OrderByDescending(c => c.StartedAt)
            .ThenByDescending(c => c.Id)
            .Take(RecentCalls)
            .Join(db.Users, c => c.UserId, u => u.Id, (c, u) => new McpCallItem(c.Id, c.StartedAt, u.Name, c.Tool, c.Status, c.ErrorCode, c.Access))
            .ToListAsync(ct);
        return TypedResults.Ok(new McpCallList(items));
    }
}
