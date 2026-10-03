namespace Tamiza.Api.Kobo;

/// <summary>Validates and normalizes a Kobo server URL entered by a user.</summary>
public static class KoboServerUrl
{
    public const string KoboToolbox = "https://kf.kobotoolbox.org";
    public const string KoboToolboxEu = "https://eu.kobotoolbox.org";

    /// <summary>
    /// Returns the normalized base URL (no trailing slash), or an error: <see cref="KoboErrorCodes.InvalidUrl"/> for a
    /// malformed URL, <see cref="KoboErrorCodes.HttpsRequired"/> for plain HTTP when private networks are not allowed.
    /// </summary>
    public static (string? Url, KoboError? Error) Parse(string? input, bool allowInsecure)
    {
        if (string.IsNullOrWhiteSpace(input)
            || !Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || string.IsNullOrEmpty(uri.Host))
        {
            return (null, new KoboError(KoboErrorCodes.InvalidUrl, "Enter the server's full address, for example https://kf.kobotoolbox.org."));
        }

        if (uri.Scheme == Uri.UriSchemeHttp && !allowInsecure)
        {
            return (null, new KoboError(KoboErrorCodes.HttpsRequired, "The Kobo server must use HTTPS."));
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        return ($"{uri.Scheme}://{uri.Authority}{path}", null);
    }
}
