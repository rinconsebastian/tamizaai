using System.Reflection;
using Microsoft.Extensions.Options;
using Tamiza.Api.Assistant;
using Tamiza.Api.Configuration;

namespace Tamiza.Api.SystemInfo;

public static class SystemEndpoints
{
    public sealed record OidcClientSettings(string Authority, string ClientId, string Scope);

    /// <summary>Whether the AI assistant is configured. Nothing else about it is public: not the provider, the model nor the key.</summary>
    public sealed record AssistantSettings(bool Enabled);

    public sealed record SystemConfigResponse(OidcClientSettings Oidc, string Version, AssistantSettings Assistant);

    private static readonly string Version =
        typeof(SystemEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    public static RouteGroupBuilder MapSystemEndpoints(this RouteGroupBuilder api)
    {
        // Public: the UI needs these settings before it can sign anyone in. Nothing here is secret.
        api.MapGet("/system/config", (IOptions<OidcOptions> oidc, IOptions<AssistantOptions> assistant) => TypedResults.Ok(new SystemConfigResponse(
                new OidcClientSettings(oidc.Value.Authority, oidc.Value.ClientId, oidc.Value.Scope),
                Version,
                new AssistantSettings(assistant.Value.Enabled))))
            .AllowAnonymous()
            .WithName("GetSystemConfig")
            .WithTags("System");
        return api;
    }
}
