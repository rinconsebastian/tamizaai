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
}

public sealed record KoboError(string Code, string Message);

/// <summary>Raised by the connect callback when every address of a Kobo host is outside the allowed ranges.</summary>
public sealed class KoboAddressNotAllowedException(string host)
    : Exception($"The Kobo server '{host}' resolves only to addresses that are not allowed.")
{
    public string Host { get; } = host;
}
