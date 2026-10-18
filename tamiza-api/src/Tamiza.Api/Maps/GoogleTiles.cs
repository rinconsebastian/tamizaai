using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Tamiza.Api.Maps;

/// <summary>A tile as Google gave it: only what the browser may keep, never Google's message.</summary>
public sealed record GoogleTile(GoogleOutcome Outcome, byte[]? Body = null, string? ContentType = null, string? CacheControl = null, string? ETag = null);

/// <summary>The data credits Google gives for the area on screen and the deepest zoom it has imagery for there.</summary>
public sealed record GoogleCredits(GoogleOutcome Outcome, string? Text = null, int MaxZoom = GoogleTiles.MaxZoom);

/// <summary>
/// Fetches tiles and viewport information from Google's Map Tiles API with the operator's key (design D15). Each call uses the
/// kind's session; when Google rejects the session, one new session is created and the call is made once more. Google's body is
/// read to tell the cases apart and is never passed on. Nothing is stored.
/// </summary>
public sealed class GoogleTiles(IHttpClientFactory httpClients, GoogleTileSessions sessions, IOptions<MapOptions> options, GoogleFailureLog failures)
{
    public const int MaxZoom = 22;
    public const string TileUrl = "https://tile.googleapis.com/v1/2dtiles";
    public const string ViewportUrl = "https://tile.googleapis.com/tile/v1/viewport";
    private const int MaxCreditsLength = 500;

    /// <summary>One tile. <paramref name="ifNoneMatch"/> is the browser's <c>ETag</c>, forwarded so Google can answer 304.</summary>
    public async Task<GoogleTile> GetTileAsync(string kind, int z, int x, int y, string? ifNoneMatch, CancellationToken ct)
    {
        var result = await SendAsync(kind, "tile", session =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{TileUrl}/{z}/{x}/{y}?{Query(session)}");
            if (!string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch.Length <= 512)
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
            }

            return request;
        }, ct);

        if (result.Response is not { } response)
        {
            return new GoogleTile(result.Failure);
        }

        using (response)
        {
            // As Google wrote them: the parsed header would reorder the directives.
            var etag = Raw(response, "ETag");
            var cacheControl = Raw(response, "Cache-Control");
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new GoogleTile(GoogleOutcome.NotModified, ETag: etag, CacheControl: cacheControl);
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType is null || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                failures.Log(kind, "tile", "the answer was not an image");
                return new GoogleTile(GoogleOutcome.Upstream);
            }

            var body = await ReadBodyAsync(response, kind, "tile", ct);
            return body is null
                ? new GoogleTile(GoogleOutcome.Upstream)
                : new GoogleTile(GoogleOutcome.Ok, body, response.Content.Headers.ContentType!.ToString(), cacheControl, etag);
        }
    }

    /// <summary>
    /// The data credits and the depth of imagery for a view: the <c>copyright</c> text, and the largest <c>maxZoom</c> of the
    /// <c>maxZoomRects</c> that contain the centre of the box (22 when none does). Google's rectangles add up: it always lists
    /// world-wide ones at 9 and 19 first, and each deeper one covers a smaller area, so the deepest rectangle over a place is the
    /// depth of its imagery. A box that crosses the antimeridian has <c>west &gt; east</c>.
    /// </summary>
    public async Task<GoogleCredits> GetCreditsAsync(string kind, int zoom, double north, double south, double east, double west, CancellationToken ct)
    {
        var result = await SendAsync(kind, "credits", session => new HttpRequestMessage(HttpMethod.Get, string.Create(
            CultureInfo.InvariantCulture, $"{ViewportUrl}?{Query(session)}&zoom={zoom}&north={north}&south={south}&east={east}&west={west}")), ct);
        if (result.Response is not { } response)
        {
            return new GoogleCredits(result.Failure);
        }

        using (response)
        {
            try
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var root = document.RootElement;
                var text = root.TryGetProperty("copyright", out var copyright) && copyright.ValueKind == JsonValueKind.String ? copyright.GetString() ?? "" : "";
                var maxZoom = MaxZoom;
                if (root.TryGetProperty("maxZoomRects", out var rects) && rects.ValueKind == JsonValueKind.Array)
                {
                    var centreLatitude = (north + south) / 2;
                    var centreLongitude = west <= east ? (west + east) / 2 : (((west + east + 360) / 2) + 540) % 360 - 180;
                    var deepest = -1;
                    foreach (var rect in rects.EnumerateArray())
                    {
                        if (rect.ValueKind == JsonValueKind.Object && rect.TryGetProperty("maxZoom", out var depth) && depth.TryGetInt32(out var rectZoom)
                            && Contains(rect, centreLatitude, centreLongitude))
                        {
                            deepest = Math.Max(deepest, rectZoom);
                        }
                    }

                    if (deepest >= 0)
                    {
                        maxZoom = deepest;
                    }
                }

                return new GoogleCredits(
                    GoogleOutcome.Ok, text.Length > MaxCreditsLength ? text[..MaxCreditsLength] : text, Math.Clamp(maxZoom, 0, MaxZoom));
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or HttpRequestException)
            {
                failures.Log(kind, "credits", "the answer was not usable");
                return new GoogleCredits(GoogleOutcome.Upstream);
            }
        }
    }

    private static string? Raw(HttpResponseMessage response, string name) =>
        response.Headers.NonValidated.TryGetValues(name, out var values) ? values.ToString() : null;

    private string Query(string session) =>
        $"session={Uri.EscapeDataString(session)}&key={Uri.EscapeDataString(options.Value.GoogleApiKey)}";

    /// <summary>Whether a <c>maxZoomRects</c> entry contains a point; a rectangle may cross the antimeridian (<c>west &gt; east</c>).</summary>
    private static bool Contains(JsonElement rect, double latitude, double longitude)
    {
        if (!Number(rect, "north", out var rectNorth) || !Number(rect, "south", out var rectSouth)
            || !Number(rect, "east", out var rectEast) || !Number(rect, "west", out var rectWest))
        {
            return false;
        }

        var inLongitude = rectWest <= rectEast
            ? rectWest <= longitude && longitude <= rectEast
            : longitude >= rectWest || longitude <= rectEast;
        return rectSouth <= latitude && latitude <= rectNorth && inLongitude;
    }

    private static bool Number(JsonElement element, string name, out double value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property) && property.TryGetDouble(out value);
    }

    /// <summary>The response when Google answered 2xx or 304; otherwise the outcome. The caller disposes the response.</summary>
    private async Task<(HttpResponseMessage? Response, GoogleOutcome Failure)> SendAsync(
        string kind, string what, Func<string, HttpRequestMessage> build, CancellationToken ct)
    {
        var session = await sessions.GetAsync(kind, ct);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (session.Token is not { } token)
            {
                return (null, session.Failure);
            }

            HttpResponseMessage response;
            try
            {
                using var request = build(token);
                response = await httpClients.CreateClient(GoogleHttp.ClientName).SendAsync(request, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
            {
                // The exception text could quote the request address, so only its kind is kept.
                failures.Log(kind, what, exception is HttpRequestException http ? $"{http.HttpRequestError}" : "timeout");
                return (null, GoogleOutcome.Upstream);
            }

            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotModified)
            {
                return (response, GoogleOutcome.Ok);
            }

            string body;
            using (response)
            {
                try
                {
                    body = await response.Content.ReadAsStringAsync(ct);
                }
                catch (HttpRequestException)
                {
                    body = "";
                }

                var kindOfError = GoogleErrors.Classify(response.StatusCode, body);
                switch (kindOfError)
                {
                    case GoogleErrorKind.SessionRejected when attempt == 0:
                        session = await sessions.RenewAsync(kind, token, ct);
                        continue;
                    case GoogleErrorKind.Missing:
                        return (null, GoogleOutcome.Missing);
                    case GoogleErrorKind.Refused:
                        failures.Log(kind, what, $"Google refused it ({(int)response.StatusCode}): check the key, the billing and the quota");
                        return (null, GoogleOutcome.Unavailable);
                    default:
                        failures.Log(kind, what, $"Google answered {(int)response.StatusCode}");
                        return (null, GoogleOutcome.Upstream);
                }
            }
        }

        return (null, GoogleOutcome.Upstream);
    }

    private async Task<byte[]?> ReadBodyAsync(HttpResponseMessage response, string kind, string what, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadAsByteArrayAsync(ct);
        }
        catch (HttpRequestException exception)
        {
            failures.Log(kind, what, $"{exception.HttpRequestError}");
            return null;
        }
    }
}
