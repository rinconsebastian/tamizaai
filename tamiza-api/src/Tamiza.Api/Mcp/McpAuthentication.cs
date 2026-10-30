using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Tamiza.Api.Configuration;

namespace Tamiza.Api.Mcp;

/// <summary>
/// How the MCP endpoint authenticates (design D3). Requests under <c>/mcp</c> are validated by their own JWT bearer scheme, which
/// requires the MCP audience, and every other request by the API's scheme, which requires the API audience, so a token for one is
/// refused by the other. The realm, the signature and the lifetime are checked the same way for both.
/// </summary>
public static class McpAuthentication
{
    /// <summary>The JWT bearer scheme that validates tokens for the MCP audience.</summary>
    public const string Scheme = "Mcp";

    /// <summary>The default scheme: forwards each request to the scheme of its path.</summary>
    public const string SelectorScheme = "TamizaBearer";

    /// <summary>The scheme that validates <paramref name="context"/>: <see cref="Scheme"/> under <c>/mcp</c>, the API's otherwise.</summary>
    public static string SelectScheme(HttpContext context) =>
        context.Request.Path.StartsWithSegments(McpOptions.Path) ? Scheme : JwtBearerDefaults.AuthenticationScheme;

    /// <summary>Configures <paramref name="jwt"/> from the realm, requiring <paramref name="audience"/> and issued by the same issuer as the API's tokens.</summary>
    internal static void Configure(JwtBearerOptions jwt, OidcOptions oidc, string audience)
    {
        jwt.Authority = oidc.Authority;
        jwt.Audience = audience;
        if (!string.IsNullOrWhiteSpace(oidc.MetadataAddress))
        {
            // Discovery and JWKS from an internal URL; the issuer stays the public authority.
            jwt.MetadataAddress = oidc.MetadataAddress;
        }

        jwt.RequireHttpsMetadata = oidc.RequireHttpsMetadata;
        jwt.MapInboundClaims = false;
        jwt.TokenValidationParameters.ValidIssuer = oidc.Authority.TrimEnd('/');
        jwt.TokenValidationParameters.ValidAudience = audience;
        jwt.TokenValidationParameters.ValidateIssuer = true;
        jwt.TokenValidationParameters.ValidateAudience = true;
        jwt.TokenValidationParameters.ValidateLifetime = true;
        jwt.TokenValidationParameters.ValidateIssuerSigningKey = true;
        jwt.TokenValidationParameters.NameClaimType = Auth.TokenIdentity.NameClaim;
    }

    /// <summary>
    /// Answers a missing, invalid or expired token with 401 and the pointer to the protected resource metadata that clients follow to
    /// find the realm (RFC 9728), plus the scope to request. Clients start signing in only from this header on a 401.
    /// </summary>
    internal static Task Challenge(JwtBearerChallengeContext context)
    {
        var tamiza = context.HttpContext.RequestServices.GetRequiredService<IOptions<TamizaOptions>>().Value;
        var parts = new List<string>();
        if (context.AuthenticateFailure is not null)
        {
            parts.Add("error=\"invalid_token\"");
        }

        parts.Add($"resource_metadata=\"{PublicBase(tamiza)}{McpOptions.ResourceMetadataPath}\"");
        parts.Add($"scope=\"{McpOptions.Scope}\"");

        context.HandleResponse();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer " + string.Join(", ", parts);
        return Task.CompletedTask;
    }

    /// <summary>The installation's public address without a trailing slash. Every URL the server publishes starts from it, never from the request's <c>Host</c>.</summary>
    internal static string PublicBase(TamizaOptions tamiza) => tamiza.PublicUrl.TrimEnd('/');

    /// <summary>The address clients enter to reach the server, and the <c>resource</c> of its metadata.</summary>
    internal static string ResourceUrl(TamizaOptions tamiza) => PublicBase(tamiza) + McpOptions.Path;

    /// <summary>
    /// The origin of <paramref name="publicUrl"/> as a browser sends it in <c>Origin</c>: scheme, host and a port that is not the scheme's
    /// default, in lower case.
    /// </summary>
    internal static string OriginOf(string publicUrl) =>
        Uri.TryCreate(publicUrl, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant() : "";

    /// <summary>
    /// Refuses with 403 a request to the MCP endpoint whose <c>Origin</c> header names another origin than the deployment's: a page in
    /// a browser could otherwise send requests with the person's credentials. Requests without the header (every AI client) pass. Only
    /// while the server is enabled; otherwise the endpoint does not exist.
    /// </summary>
    public static IApplicationBuilder UseMcpOriginCheck(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(McpOptions.Path)
                && context.Request.Headers.Origin.ToString() is { Length: > 0 } origin
                && context.RequestServices.GetRequiredService<IOptions<McpOptions>>().Value.Enabled)
            {
                var tamiza = context.RequestServices.GetRequiredService<IOptions<TamizaOptions>>().Value;
                if (!string.Equals(origin.TrimEnd('/'), OriginOf(tamiza.PublicUrl), StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
            }

            await next();
        });
}
