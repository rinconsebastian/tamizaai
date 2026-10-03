namespace Tamiza.Api.Auth;

public static class MeEndpoints
{
    public sealed record MeResponse(Guid Id, string Name, string? Email, bool IsSuperAdmin);

    public static RouteGroupBuilder MapMeEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me", (ICurrentUser user) => TypedResults.Ok(new MeResponse(user.Id, user.Name, user.Email, user.IsSuperAdmin)))
            .WithName("GetCurrentUser")
            .WithTags("Auth");
        return api;
    }
}
