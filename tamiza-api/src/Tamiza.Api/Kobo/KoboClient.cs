using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Tamiza.Api.Ingestion;

namespace Tamiza.Api.Kobo;

/// <summary>How to read submissions: since when (none reads all), whole records or only IDs, and per-page limits.</summary>
public sealed record SubmissionRead(DateTimeOffset? SubmittedSince, bool IdsOnly, int PageSize, long MaxPageBytes, TimeSpan PageTimeout);

/// <summary>One page of submissions in <c>_id</c> order. <see cref="Rows"/> is empty when only IDs were read.</summary>
public sealed record SubmissionPage(IReadOnlyList<SubmissionRow> Rows, IReadOnlyList<long> Ids);

/// <summary>Calls the KoboToolbox API v2. The token is sent in the Authorization header and never logged.</summary>
public sealed class KoboClient(IHttpClientFactory httpClientFactory, IOptions<KoboHttpOptions> httpOptions, ILogger<KoboClient> logger)
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

    public Task<(KoboAsset? Asset, KoboError? Error)> GetAssetAsync(string serverUrl, string assetUid, string token, CancellationToken cancellationToken) =>
        GetFormDocumentAsync($"{serverUrl}/api/v2/assets/{Uri.EscapeDataString(assetUid)}/?format=json", serverUrl, assetUid, token, null, KoboForm.Parse, cancellationToken);

    /// <summary>
    /// Reads one version of the form. A 404 is <see cref="KoboErrorCodes.VersionNotFound"/>: the caller decides whether the
    /// form itself is still readable before treating the version as unknown.
    /// </summary>
    public Task<(KoboFormVersion? Version, KoboError? Error)> GetVersionAsync(
        string serverUrl, string assetUid, string versionUid, string token, CancellationToken cancellationToken) =>
        GetFormDocumentAsync(
            $"{serverUrl}/api/v2/assets/{Uri.EscapeDataString(assetUid)}/versions/{Uri.EscapeDataString(versionUid)}/?format=json",
            serverUrl, assetUid, token,
            new KoboError(KoboErrorCodes.VersionNotFound, $"Kobo has no version '{versionUid}' of the form with asset UID '{assetUid}'."),
            KoboForm.ParseVersion, cancellationToken);

    private async Task<(T? Document, KoboError? Error)> GetFormDocumentAsync<T>(
        string url, string serverUrl, string assetUid, string token, KoboError? notFound, Func<JsonElement, T> parse, CancellationToken cancellationToken)
        where T : class
    {
        var (response, error) = await SendAsync(url, serverUrl, assetUid, token, cancellationToken, notFound: notFound);
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
                return (parse(document.RootElement), null);
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

    /// <summary>
    /// Streams the form's submissions page by page, in <c>_id</c> order. Throws <see cref="KoboReadException"/> when a
    /// page cannot be read; pages already returned stay valid.
    /// </summary>
    public async IAsyncEnumerable<SubmissionPage> ReadSubmissionsAsync(
        string serverUrl, string assetUid, string token, SubmissionRead read, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long? afterId = null;
        while (true)
        {
            var url = KoboDataQuery.PageUrl(serverUrl, assetUid, read.SubmittedSince, afterId, read.IdsOnly, read.PageSize);
            var page = await ReadPageAsync(url, serverUrl, assetUid, token, read, cancellationToken);
            yield return page;

            // A short page is the last one.
            if (page.Ids.Count < read.PageSize)
            {
                yield break;
            }

            afterId = page.Ids.Max();
        }
    }

    private async Task<SubmissionPage> ReadPageAsync(string url, string serverUrl, string assetUid, string token, SubmissionRead read, CancellationToken cancellationToken)
    {
        using var pageTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        pageTimeout.CancelAfter(read.PageTimeout);
        try
        {
            var (response, error) = await SendAsync(url, serverUrl, assetUid, token, pageTimeout.Token, KoboHttp.DataClientName, HttpCompletionOption.ResponseHeadersRead);
            if (error is not null)
            {
                throw new KoboReadException(error);
            }

            using (response)
            {
                await using var body = new LimitedReadStream(await response!.Content.ReadAsStreamAsync(pageTimeout.Token), read.MaxPageBytes);
                using var document = await JsonDocument.ParseAsync(body, cancellationToken: pageTimeout.Token);
                return ParsePage(document.RootElement, read.IdsOnly);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Kobo server {Server} did not deliver a page of submissions within {Timeout}.", serverUrl, read.PageTimeout);
            throw new KoboReadException(new KoboError(KoboErrorCodes.Unreachable, $"The Kobo server at {serverUrl} did not send a page of submissions in time."));
        }
        catch (LimitExceededException)
        {
            throw new KoboReadException(new KoboError(KoboErrorCodes.UnexpectedResponse,
                $"A page of submissions from Kobo was larger than {read.MaxPageBytes / (1024 * 1024)} MB. Lower the Ingestion page size."));
        }
        catch (JsonException)
        {
            logger.LogWarning("Kobo server {Server} returned an unreadable page of submissions.", serverUrl);
            throw new KoboReadException(new KoboError(KoboErrorCodes.UnexpectedResponse, "The Kobo server returned submissions Tamiza could not read."));
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            logger.LogWarning("Kobo server {Server} stopped sending submissions: {Reason}", serverUrl, exception.Message);
            throw new KoboReadException(new KoboError(KoboErrorCodes.Unreachable, $"The connection to the Kobo server at {serverUrl} failed while reading submissions."));
        }
    }

    private static SubmissionPage ParsePage(JsonElement page, bool idsOnly)
    {
        if (page.ValueKind != JsonValueKind.Object || !page.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            throw new KoboReadException(new KoboError(KoboErrorCodes.UnexpectedResponse, "The Kobo server returned a page of submissions without results."));
        }

        var rows = new List<SubmissionRow>();
        var ids = new List<long>();
        foreach (var result in results.EnumerateArray())
        {
            if (idsOnly)
            {
                if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("_id", out var id) || !id.TryGetInt64(out var koboId))
                {
                    throw new KoboReadException(new KoboError(KoboErrorCodes.UnexpectedResponse, "The Kobo server returned a submission without a numeric _id."));
                }

                ids.Add(koboId);
                continue;
            }

            var (row, error) = KoboSubmission.Parse(result);
            if (row is null)
            {
                throw new KoboReadException(new KoboError(KoboErrorCodes.UnexpectedResponse, $"The Kobo server returned a submission Tamiza could not read: {error}"));
            }

            rows.Add(row);
            ids.Add(row.KoboId);
        }

        return new SubmissionPage(rows, ids);
    }

    private static string Size(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / (1024 * 1024)} MB" : $"{bytes / 1024} KB";

    private async Task<(HttpResponseMessage? Response, KoboError? Error)> SendAsync(
        string url, string serverUrl, string assetUid, string token, CancellationToken cancellationToken,
        string clientName = KoboHttp.ClientName, HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead, KoboError? notFound = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory.CreateClient(clientName).SendAsync(request, completion, cancellationToken);
        }
        catch (HttpRequestException exception) when (exception.InnerException is KoboAddressNotAllowedException)
        {
            return (null, new KoboError(KoboErrorCodes.AddressNotAllowed, "The Kobo server's address is not allowed: it points to a private or local network."));
        }
        catch (HttpRequestException exception) when (exception.HttpRequestError == HttpRequestError.ConfigurationLimitExceeded)
        {
            // The body is buffered up to the client's limit; a larger one is a bad answer, not a connection problem.
            var limit = Size(httpOptions.Value.MaxResponseBytes);
            logger.LogWarning("Kobo server {Server} sent a response larger than {Limit}.", serverUrl, limit);
            return (null, new KoboError(KoboErrorCodes.UnexpectedResponse, $"The Kobo server's response was larger than {limit}, which Tamiza does not read."));
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
                notFound ?? new KoboError(KoboErrorCodes.FormNotFound, $"No form with asset UID '{assetUid}' was found on {serverUrl}."),
            HttpStatusCode.TooManyRequests =>
                new KoboError(KoboErrorCodes.RateLimited, "The Kobo server is limiting Tamiza's requests (HTTP 429). Tamiza will try again later."),
            _ => new KoboError(KoboErrorCodes.UnexpectedResponse, $"The Kobo server answered unexpectedly (HTTP {status})."),
        });
    }
}
