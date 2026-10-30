using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamiza.Api.Data;
using Tamiza.Api.Projects;

namespace Tamiza.Api.Mcp;

/// <summary>What a tool needs from the data of a project.</summary>
public enum McpNeeds
{
    /// <summary>Summaries: the form, shapes, profiles, scripts, dashboards and what indicators and widgets show.</summary>
    Summaries,

    /// <summary>Rows of tables and the values previews and <c>ctx.show</c> hold.</summary>
    Rows,
}

/// <summary>What a call may do in the project it names: the person's role there and the level of data access it got.</summary>
public sealed record McpProject(Guid Id, ProjectRole Role, McpAccess Level)
{
    public bool CanSeeRows => Level >= McpAccess.Rows;
}

/// <summary>
/// The two locks and the role (design D4, D5). The access level of a call is the lower of the project's level, which its admin sets, and
/// the deployment's maximum, which the operator sets; the person's role in the project is checked first, with the answers of the REST API.
/// </summary>
public sealed class McpAccessGate(ProjectAccess access, TamizaDbContext db, IOptions<McpOptions> options, McpCallScope call)
{
    /// <summary>The lower (stricter) of two levels.</summary>
    public static McpAccess Effective(McpAccess project, McpAccess maximum) => project < maximum ? project : maximum;

    /// <summary>
    /// Resolves the call's project: a person who is not a member gets <see cref="McpRefusal.ProjectNotFound"/>, a member with a lower role
    /// than <paramref name="minimum"/> <see cref="McpRefusal.Forbidden"/>, then a project that is off <see cref="McpRefusal.ProjectOff"/>
    /// and a call that needs rows at a level without them <see cref="McpRefusal.RowsNotAllowed"/>.
    /// </summary>
    public async Task<McpProject> RequireAsync(Guid projectId, ProjectRole minimum, McpNeeds needs, CancellationToken ct)
    {
        var role = await access.GetRoleAsync(projectId, ct) ?? throw McpRefusal.ProjectNotFound();
        call.ProjectId = projectId;
        if (role < minimum)
        {
            throw McpRefusal.Forbidden(minimum);
        }

        var stored = await db.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(p => p.McpAccess).SingleAsync(ct);
        var level = Effective(stored, options.Value.MaxAccess);
        if (level == McpAccess.Off)
        {
            throw McpRefusal.ProjectOff();
        }

        if (needs == McpNeeds.Rows && level < McpAccess.Rows)
        {
            throw McpRefusal.RowsNotAllowed();
        }

        call.Access = level;
        return new McpProject(projectId, role, level);
    }
}
