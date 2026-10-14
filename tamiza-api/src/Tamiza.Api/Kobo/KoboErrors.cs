namespace Tamiza.Api.Kobo;

/// <summary>Machine-readable codes returned in the <c>code</c> extension of Kobo-related Problem Details.</summary>
public static class KoboErrorCodes
{
    public const string InvalidUrl = "kobo.invalid_url";
    public const string HttpsRequired = "kobo.https_required";
    public const string AddressNotAllowed = "kobo.address_not_allowed";
    public const string Unauthorized = "kobo.unauthorized";
    public const string FormNotFound = "kobo.form_not_found";
    public const string Unreachable = "kobo.unreachable";
    public const string UnexpectedResponse = "kobo.unexpected_response";
    public const string TokenUnreadable = "kobo.token_unreadable";

    /// <summary>The form is readable, but Kobo has no such version of it.</summary>
    public const string VersionNotFound = "kobo.version_not_found";

    /// <summary>Kobo answered 429: the account makes too many automated requests.</summary>
    public const string RateLimited = "kobo.rate_limited";

    /// <summary>
    /// Errors that affect every request to the server or form, not one version: the rest of a fetch pass would fail
    /// the same way.
    /// </summary>
    public static bool IsServerWide(string code) =>
        code is Unreachable or Unauthorized or TokenUnreadable or AddressNotAllowed or RateLimited or FormNotFound;
}

public sealed record KoboError(string Code, string Message);

/// <summary>Raised while streaming submissions from Kobo, where an error cannot be returned alongside each page.</summary>
public sealed class KoboReadException(KoboError error) : Exception(error.Message)
{
    public KoboError Error { get; } = error;
}

/// <summary>Raised by the connect callback when every address of a Kobo host is outside the allowed ranges.</summary>
public sealed class KoboAddressNotAllowedException(string host)
    : Exception($"The Kobo server '{host}' resolves only to addresses that are not allowed.")
{
    public string Host { get; } = host;
}
