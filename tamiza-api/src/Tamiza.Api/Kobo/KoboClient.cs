using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Tamiza.Api.Kobo;

/// <summary>Calls the KoboToolbox API v2. The token is sent in the Authorization header and never logged.</summary>
public sealed class KoboClient(IHttpClientFactory httpClientFactory, ILogger<KoboClient> logger)
{
    /// <summary>Checks that the token can read the form definition and its submissions; returns the form.</summary>
    public async Task<(KoboAsset? Asset, KoboError? Error)> VerifyAccessAsync(string serverUrl, string assetUid, string token, CancellationToken cancellationToken)
    {
        var (asset, error) = await GetAssetAsync(serverUrl, assetUid, token, cancellationToken);
        if (error is not null)
        {
            return (null, error);
        }

        var dataError = await CheckSubmissionsAccessAsync(serverUrl, assetUid, token, cancellationToken);
        return dataError is null ? (asset, null) : (null, dataError);
    }

    public async Task<(KoboAsset? Asset, KoboError? Error)> GetAssetAsync(string serverUrl, string assetUid, string token, CancellationToken cancellationToken)
    {
        var (response, error) = await SendAsync($"{serverUrl}/api/v2/assets/{Uri.EscapeDataString(assetUid)}/?format=json", serverUrl, assetUid, token, cancellationToken);
        if (error is not null)
        {
            return (null, error);
        }

        using (response)
        {
            try
            {
                await using var body = await response!.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
                return (KoboForm.Parse(document.RootElement), null);
            }
            catch (Exception exception) when (exception is JsonException or HttpRequestException)
            {
                logger.LogWarning("Kobo server {Server} returned an unreadable form document.", serverUrl);
                return (null, new KoboError(KoboErrorCodes.UnexpectedResponse, "The Kobo server returned a form document Tamiza could not read."));
            }
        }
    }

    private async Task<KoboError?> CheckSubmissionsAccessAsync(string serverUrl, string assetUid, string token, CancellationToken cancellationToken)
    {
        var (response, error) = await SendAsync($"{serverUrl}/api/v2/assets/{Uri.EscapeDataString(assetUid)}/data/?format=json&limit=1", serverUrl, assetUid, token, cancellationToken);
        if (error is not null)
        {
            return error.Code == KoboErrorCodes.Unauthorized
                ? new KoboError(KoboErrorCodes.Unauthorized, "The API token can read the form but not its submissions.")
                : error;
        }

        response!.Dispose();
        return null;
    }

    private async Task<(HttpResponseMessage? Response, KoboError? Error)> SendAsync(string url, string serverUrl, string assetUid, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory.CreateClient(KoboHttp.ClientName).SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception) when (exception.InnerException is KoboAddressNotAllowedException)
        {
            return (null, new KoboError(KoboErrorCodes.AddressNotAllowed, "The Kobo server's address is not allowed: it points to a private or local network."));
        }
        catch (Exception exception) when (exception is HttpRequestException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning("Kobo server {Server} could not be reached: {Reason}", serverUrl, exception.Message);
            return (null, new KoboError(KoboErrorCodes.Unreachable, $"The Kobo server at {serverUrl} could not be reached."));
        }

        if (response.IsSuccessStatusCode)
        {
            return (response, null);
        }

        var status = (int)response.StatusCode;
        response.Dispose();
        logger.LogInformation("Kobo server {Server} answered {Status} for asset {AssetUid}.", serverUrl, status, assetUid);
        return (null, response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new KoboError(KoboErrorCodes.Unauthorized, "Kobo rejected the API token, or the token has no access to this form."),
            HttpStatusCode.NotFound =>
                new KoboError(KoboErrorCodes.FormNotFound, $"No form with asset UID '{assetUid}' was found on {serverUrl}."),
            _ => new KoboError(KoboErrorCodes.UnexpectedResponse, $"The Kobo server answered unexpectedly (HTTP {status})."),
        });
    }
}
