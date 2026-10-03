using Microsoft.EntityFrameworkCore;
using Tamiza.Api.Auth;
using Tamiza.Api.Data;

namespace Tamiza.Api.Projects;

/// <summary>Resolves the caller's role in a project. Superadmins act as admin on every project.</summary>
public sealed class ProjectAccess(TamizaDbContext db, ICurrentUser user)
{
    public async Task<ProjectRole?> GetRoleAsync(Guid projectId, CancellationToken cancellationToken)
    {
        if (!user.IsAuthenticated)
        {
            return null;
        }

        if (user.IsSuperAdmin)
        {
            return await db.Projects.AnyAsync(p => p.Id == projectId, cancellationToken) ? ProjectRole.Admin : null;
        }

        return await db.ProjectMembers
            .Where(m => m.ProjectId == projectId && m.UserId == user.Id)
            .Select(m => (ProjectRole?)m.Role)
            .SingleOrDefaultAsync(cancellationToken);
    }
}

public static class ProjectAuthorization
{
    private const string RoleItemKey = "tamiza.projectRole";

    /// <summary>
    /// Requires at least <paramref name="minimum"/> in the project named by the <c>projectId</c> route value.
    /// Non-members get 404, the same as for a missing project; members with a lower role get 403.
    /// </summary>
    public static TBuilder RequireProjectRole<TBuilder>(this TBuilder builder, ProjectRole minimum)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            ProjectRole? role = null;
            if (Guid.TryParse(http.Request.RouteValues["projectId"]?.ToString(), out var projectId))
            {
                role = await http.RequestServices.GetRequiredService<ProjectAccess>().GetRoleAsync(projectId, http.RequestAborted);
            }

            if (role is null)
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Project not found.");
            }

            if (role < minimum)
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Not allowed.",
                    detail: $"This action requires the {minimum.ToString().ToLowerInvariant()} role in the project.");
            }

            http.Items[RoleItemKey] = role.Value;
            return await next(context);
        });

    /// <summary>The role resolved by <see cref="RequireProjectRole{TBuilder}"/> for this request.</summary>
    public static ProjectRole GetProjectRole(this HttpContext http) => (ProjectRole)http.Items[RoleItemKey]!;
}
