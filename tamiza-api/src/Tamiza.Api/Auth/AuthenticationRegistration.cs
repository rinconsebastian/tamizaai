using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Tamiza.Api.Configuration;

namespace Tamiza.Api.Auth;

public static class AuthenticationRegistration
{
    public static IServiceCollection AddTamizaAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<OidcOptions>>((jwt, oidcOptions) =>
            {
                var oidc = oidcOptions.Value;
                jwt.Authority = oidc.Authority;
                jwt.Audience = oidc.Audience;
                if (!string.IsNullOrWhiteSpace(oidc.MetadataAddress))
                {
                    // Discovery and JWKS from an internal URL; the issuer stays the public authority.
                    jwt.MetadataAddress = oidc.MetadataAddress;
                }

                jwt.RequireHttpsMetadata = oidc.RequireHttpsMetadata;
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters.ValidIssuer = oidc.Authority.TrimEnd('/');
                jwt.TokenValidationParameters.ValidAudience = oidc.Audience;
                jwt.TokenValidationParameters.ValidateIssuer = true;
                jwt.TokenValidationParameters.ValidateAudience = true;
                jwt.TokenValidationParameters.ValidateLifetime = true;
                jwt.TokenValidationParameters.ValidateIssuerSigningKey = true;
                jwt.TokenValidationParameters.NameClaimType = TokenIdentity.NameClaim;
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
