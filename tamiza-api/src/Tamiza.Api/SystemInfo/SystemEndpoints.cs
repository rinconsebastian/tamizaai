using System.Reflection;
using Microsoft.Extensions.Options;
using Tamiza.Api.Accounts;
using Tamiza.Api.Assistant;
using Tamiza.Api.Configuration;
using Tamiza.Api.Email;
using Tamiza.Api.Mcp;

namespace Tamiza.Api.SystemInfo;

public static class SystemEndpoints
{
    public sealed record OidcClientSettings(string Authority, string ClientId, string Scope);

    /// <summary>Whether the AI assistant is configured. Nothing else about it is public: not the provider, the model nor the key.</summary>
    public sealed record AssistantSettings(bool Enabled);

    /// <summary>Whether the identity provider can send account emails (invitation, resend, password reset). Nothing else about it is public.</summary>
    public sealed record AccountEmailsSettings(bool Enabled);

    /// <summary>Whether Tamiza can send email itself (scheduled dashboard emails). Nothing else about it is public: not the server nor the sender.</summary>
    public sealed record EmailSettings(bool Enabled);

    /// <summary>Whether the MCP server for AI clients is enabled. Nothing else about it is public here: the connect page asks for the rest once signed in.</summary>
    public sealed record McpSettings(bool Enabled);

    public sealed record SystemConfigResponse(
        OidcClientSettings Oidc, string Version, AssistantSettings Assistant, AccountEmailsSettings AccountEmails, EmailSettings Email, McpSettings Mcp);

    private static readonly string Version =
        typeof(SystemEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    public static RouteGroupBuilder MapSystemEndpoints(this RouteGroupBuilder api)
    {
        // Public: the UI needs these settings before it can sign anyone in. Nothing here is secret.
        api.MapGet("/system/config", (IOptions<OidcOptions> oidc, IOptions<AssistantOptions> assistant, IAccountProviderSource accounts, IOptions<EmailOptions> email, IOptions<McpOptions> mcp) =>
                TypedResults.Ok(new SystemConfigResponse(
                    new OidcClientSettings(oidc.Value.Authority, oidc.Value.ClientId, oidc.Value.Scope),
                    Version,
                    new AssistantSettings(assistant.Value.Enabled),
                    new AccountEmailsSettings(accounts.Provider is not null),
                    new EmailSettings(email.Value.Enabled),
                    new McpSettings(mcp.Value.Enabled))))
            .AllowAnonymous()
            .WithName("GetSystemConfig")
            .WithTags("System");
        return api;
    }
}
