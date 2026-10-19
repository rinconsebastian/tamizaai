using Microsoft.Extensions.Options;
using Tamiza.Api.Configuration;

namespace Tamiza.Api.Accounts.Keycloak;

/// <summary>
/// Settings of the Keycloak adapter, read from the flat <c>TAMIZA_KEYCLOAK_*</c> variables. The adapter exists only when the client
/// ID and secret are both set (<see cref="Enabled"/>); Compose always passes optional strings, often empty, so a blank value means
/// absent. Text values are trimmed, so a secret pasted with a trailing newline still works.
/// </summary>
public sealed class KeycloakAdminOptions
{
    public const string ClientIdVariable = "TAMIZA_KEYCLOAK_ADMIN_CLIENT_ID";
    public const string ClientSecretVariable = "TAMIZA_KEYCLOAK_ADMIN_CLIENT_SECRET";
    public const string UrlVariable = "TAMIZA_KEYCLOAK_ADMIN_URL";
    public const string LinkHoursVariable = "TAMIZA_KEYCLOAK_LINK_HOURS";

    private string _clientId = "";
    private string _clientSecret = "";
    private string _url = "";

    [ConfigurationKeyName(ClientIdVariable)]
    public string ClientId { get => _clientId; set => _clientId = value?.Trim() ?? ""; }

    /// <summary>Sent only to Keycloak's token endpoint. Never logged, never returned.</summary>
    [ConfigurationKeyName(ClientSecretVariable)]
    public string ClientSecret { get => _clientSecret; set => _clientSecret = value?.Trim() ?? ""; }

    /// <summary>Base URL of Keycloak for the token and admin calls, when it differs from the one derived from the OIDC settings.</summary>
    [ConfigurationKeyName(UrlVariable)]
    public string Url { get => _url; set => _url = value?.Trim() ?? ""; }

    /// <summary>How long the links in account emails last; null leaves the realm's own setting in force.</summary>
    [ConfigurationKeyName(LinkHoursVariable)]
    public int? LinkHours { get; set; }

    public bool Enabled => ClientId.Length > 0 && ClientSecret.Length > 0;

    /// <summary>The names of the two variables that turn the adapter on and are not set.</summary>
    public IReadOnlyList<string> MissingVariables()
    {
        var missing = new List<string>();
        if (ClientId.Length == 0)
        {
            missing.Add(ClientIdVariable);
        }

        if (ClientSecret.Length == 0)
        {
            missing.Add(ClientSecretVariable);
        }

        return missing;
    }
}

/// <summary>Stops the api at startup, naming the variable, for values that are typos the operator must see at deploy time.</summary>
public sealed class KeycloakAdminOptionsValidator(IOptions<OidcOptions> oidc) : IValidateOptions<KeycloakAdminOptions>
{
    public ValidateOptionsResult Validate(string? name, KeycloakAdminOptions options)
    {
        var failures = new List<string>();

        if (options.Url.Length > 0 && !KeycloakAddress.TryParseBaseUrl(options.Url, out _))
        {
            failures.Add($"{KeycloakAdminOptions.UrlVariable} must be an absolute http or https URL without credentials, query or fragment, such as http://keycloak:8080.");
        }

        if (options.LinkHours is < 1 or > 720)
        {
            failures.Add($"{KeycloakAdminOptions.LinkHoursVariable} must be between 1 and 720, not {options.LinkHours}.");
        }

        if (options.Enabled && !KeycloakAddress.TryRealm(oidc.Value.Authority, out _))
        {
            failures.Add($"OIDC_AUTHORITY must be a Keycloak realm URL ending in /realms/<name> when {KeycloakAdminOptions.ClientIdVariable} is set, not {oidc.Value.Authority}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
