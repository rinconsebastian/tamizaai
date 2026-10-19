namespace Tamiza.Api.Accounts.Keycloak;

/// <summary>Where Keycloak's token and admin endpoints are, derived from the OIDC settings (design D3).</summary>
public sealed record KeycloakAddress(string Realm, string TokenUrl, string AdminBase)
{
    private const string RealmsSegment = "/realms/";

    /// <summary>
    /// The base URL is the explicit one when set, else the discovery address's part before <c>/realms/</c>, else the authority's.
    /// Returns null when the authority has no <c>/realms/&lt;name&gt;</c> segment.
    /// </summary>
    public static KeycloakAddress? From(string authority, string? metadataAddress, string? explicitUrl)
    {
        if (!TryRealm(authority, out var realm))
        {
            return null;
        }

        string baseUrl;
        if (!string.IsNullOrWhiteSpace(explicitUrl))
        {
            if (!TryParseBaseUrl(explicitUrl, out var uri))
            {
                return null;
            }

            baseUrl = uri.AbsoluteUri.TrimEnd('/');
        }
        else
        {
            var source = !string.IsNullOrWhiteSpace(metadataAddress) && metadataAddress.Contains(RealmsSegment, StringComparison.Ordinal)
                ? metadataAddress
                : authority;
            baseUrl = source[..source.LastIndexOf(RealmsSegment, StringComparison.Ordinal)].TrimEnd('/');
        }

        return new KeycloakAddress(
            realm,
            $"{baseUrl}/realms/{realm}/protocol/openid-connect/token",
            $"{baseUrl}/admin/realms/{realm}");
    }

    /// <summary>The realm is the path segment after the last <c>/realms/</c>, which also covers a legacy <c>/auth/realms/x</c>.</summary>
    public static bool TryRealm(string authority, out string realm)
    {
        realm = "";
        var index = authority.LastIndexOf(RealmsSegment, StringComparison.Ordinal);
        if (index < 0)
        {
            return false;
        }

        var rest = authority[(index + RealmsSegment.Length)..].Trim('/');
        if (rest.Length == 0 || rest.Contains('/'))
        {
            return false;
        }

        realm = rest;
        return true;
    }

    public static bool TryParseBaseUrl(string value, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(parsed.Host)
            || parsed.Query.Length > 0
            || parsed.Fragment.Length > 0
            || parsed.UserInfo.Length > 0)
        {
            return false;
        }

        uri = parsed;
        return true;
    }
}
