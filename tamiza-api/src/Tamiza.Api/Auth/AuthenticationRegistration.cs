using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Tamiza.Api.Configuration;
using Tamiza.Api.Mcp;

namespace Tamiza.Api.Auth;

public static class AuthenticationRegistration
{
    public static IServiceCollection AddTamizaAuthentication(this IServiceCollection services)
    {
        // The default scheme forwards each request to the scheme of its path: the MCP endpoint has its own audience (design D3).
        services.AddAuthentication(McpAuthentication.SelectorScheme)
            .AddPolicyScheme(McpAuthentication.SelectorScheme, McpAuthentication.SelectorScheme,
                policy => policy.ForwardDefaultSelector = McpAuthentication.SelectScheme)
            .AddJwtBearer()
            .AddJwtBearer(McpAuthentication.Scheme, jwt => jwt.Events = new JwtBearerEvents { OnChallenge = McpAuthentication.Challenge });
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<OidcOptions>>((jwt, oidcOptions) =>
            {
                McpAuthentication.Configure(jwt, oidcOptions.Value, oidcOptions.Value.Audience);
            });
        services.AddOptions<JwtBearerOptions>(McpAuthentication.Scheme)
            .Configure<IOptions<OidcOptions>, IOptions<McpOptions>>((jwt, oidcOptions, mcpOptions) =>
            {
                McpAuthentication.Configure(jwt, oidcOptions.Value, mcpOptions.Value.Audience);
            });

        // Every endpoint requires a signed-in user unless it opts out with AllowAnonymous.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddMemoryCache();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<SuperAdminRule>();
        services.AddScoped<UserProvisioner>();
        services.AddScoped<CurrentUser>();
        services.AddScoped<ICurrentUser>(provider => provider.GetRequiredService<CurrentUser>());
        return services;
    }

    /// <summary>Resolves the local user for authenticated requests. Runs between authentication and authorization.</summary>
    public static IApplicationBuilder UseUserProvisioning(this IApplicationBuilder app) =>
        app.UseMiddleware<UserProvisioningMiddleware>();
}
