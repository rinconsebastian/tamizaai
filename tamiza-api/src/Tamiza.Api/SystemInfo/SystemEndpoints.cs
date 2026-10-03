using System.Reflection;
using Microsoft.Extensions.Options;
using Tamiza.Api.Configuration;

namespace Tamiza.Api.SystemInfo;

public static class SystemEndpoints
{
    public sealed record OidcClientSettings(string Authority, string ClientId, string Scope);

    public sealed record SystemConfigResponse(OidcClientSettings Oidc, string Version);

    private static readonly string Version =
        typeof(SystemEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    public static RouteGroupBuilder MapSystemEndpoints(this RouteGroupBuilder api)
    {
        // Public: the UI needs these settings before it can sign anyone in. Nothing here is secret.
        api.MapGet("/system/config", (IOptions<OidcOptions> oidc) => TypedResults.Ok(new SystemConfigResponse(
                new OidcClientSettings(oidc.Value.Authority, oidc.Value.ClientId, oidc.Value.Scope),
                Version)))
            .AllowAnonymous()
            .WithName("GetSystemConfig")
            .WithTags("System");
        return api;
    }
}
