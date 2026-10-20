using Tamiza.Api.Projects;

namespace Tamiza.Api.Auth;

public static class MeEndpoints
{
    /// <param name="CanCreateProjects">Whether the user is a superadmin or admin of a project, the rule project creation applies.</param>
    public sealed record MeResponse(Guid Id, string Name, string? Email, bool IsSuperAdmin, bool CanCreateProjects);

    public static RouteGroupBuilder MapMeEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me", async (ICurrentUser user, ProjectAccess access, CancellationToken ct) =>
                TypedResults.Ok(new MeResponse(user.Id, user.Name, user.Email, user.IsSuperAdmin, await access.CanCreateProjectsAsync(ct))))
            .WithName("GetCurrentUser")
            .WithTags("Auth");
        return api;
    }
}
