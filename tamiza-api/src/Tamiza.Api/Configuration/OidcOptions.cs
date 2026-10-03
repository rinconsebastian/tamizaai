using System.ComponentModel.DataAnnotations;

namespace Tamiza.Api.Configuration;

/// <summary>OpenID Connect settings for the external Keycloak realm, read from <c>OIDC_*</c> variables.</summary>
public sealed class OidcOptions
{
    /// <summary>Realm URL as the browser sees it; also the expected token issuer.</summary>
    [Required, Url]
    [ConfigurationKeyName("OIDC_AUTHORITY")]
    public string Authority { get; set; } = "";

    /// <summary>Public client the UI signs in with.</summary>
    [Required]
    [ConfigurationKeyName("OIDC_CLIENT_ID")]
    public string ClientId { get; set; } = "";

    /// <summary>Audience the API requires in access tokens.</summary>
    [Required]
    [ConfigurationKeyName("OIDC_AUDIENCE")]
    public string Audience { get; set; } = "";

    /// <summary>
    /// Optional discovery URL reachable from inside the stack when it differs from the public authority.
    /// Blank means "derive it from the authority"; Compose always passes the variable, often empty.
    /// </summary>
    [ConfigurationKeyName("OIDC_METADATA_ADDRESS")]
    public string? MetadataAddress { get; set; }

    [Required]
    [ConfigurationKeyName("OIDC_SCOPE")]
    public string Scope { get; set; } = "openid profile email";

    [ConfigurationKeyName("OIDC_REQUIRE_HTTPS_METADATA")]
    public bool RequireHttpsMetadata { get; set; } = true;
}
