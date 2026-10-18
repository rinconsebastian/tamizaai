using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Tamiza.Api.Indicators;

namespace Tamiza.Api.Maps;

/// <summary>What a call to Google came to; <see cref="Ok"/> and <see cref="NotModified"/> carry a result.</summary>
public enum GoogleOutcome
{
    Ok,
    NotModified,

    /// <summary>Google has no such tile (answered 404 to the browser).</summary>
    Missing,

    /// <summary>Google refused for quota, billing or permission (<c>basemaps.unavailable</c>, 503).</summary>
    Unavailable,

    /// <summary>Anything else: unreachable, timed out, an unusable answer, or a session Google rejected twice (<c>basemaps.upstream</c>, 502).</summary>
    Upstream,
}

/// <summary>How Google's error answers read. The documentation lists reasons but no status for a tile that does not exist, so the reason decides first.</summary>
internal enum GoogleErrorKind
{
    /// <summary>The session lapsed or was refused.</summary>
    SessionRejected,

    Missing,

    /// <summary>Quota, billing, key or permission: the operator must act.</summary>
    Refused,

    Other,
}

internal static class GoogleErrors
{
    public static GoogleErrorKind Classify(HttpStatusCode status, string body)
    {
        string? reason = null;
        string? message = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                message = error.TryGetProperty("message", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;
                if (error.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0
                    && errors[0].ValueKind == JsonValueKind.Object && errors[0].TryGetProperty("reason", out var why) && why.ValueKind == JsonValueKind.String)
                {
                    reason = why.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON: only the status says anything.
        }

        var namesSession = message?.Contains("session", StringComparison.OrdinalIgnoreCase) == true;
        var namesKey = message?.Contains("key", StringComparison.OrdinalIgnoreCase) == true;
        if (string.Equals(reason, "expired", StringComparison.OrdinalIgnoreCase)
            || (namesSession && status is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden))
        {
            return GoogleErrorKind.SessionRejected;
        }

        if (status == HttpStatusCode.NotFound || string.Equals(reason, "notFound", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(reason, "invalid", StringComparison.OrdinalIgnoreCase) && !namesSession && !namesKey))
        {
            return GoogleErrorKind.Missing;
        }

        if (status is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden
            || string.Equals(reason, "quotaExceeded", StringComparison.OrdinalIgnoreCase)
            || string.Equals(reason, "rateLimitExceeded", StringComparison.OrdinalIgnoreCase))
        {
            return GoogleErrorKind.Refused;
        }

        return GoogleErrorKind.Other;
    }
}

/// <summary>
/// Failures are logged at most once a minute per kind and category, without the request address (which holds the key) and without
/// anything Google said.
/// </summary>
public sealed class GoogleFailureLog(ILogger<GoogleFailureLog> logger, TimeProvider time)
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _last = new(StringComparer.Ordinal);

    public void Log(string kind, string what, string reason)
    {
        var key = $"{kind}:{what}";
        var now = time.GetUtcNow();
        if (_last.TryGetValue(key, out var last) && now - last < Interval)
        {
            return;
        }

        _last[key] = now;
        logger.LogWarning("Google Map Tiles: the {What} for {Kind} failed: {Reason}.", what, kind, reason);
    }
}

/// <summary>A session token, or why there is none.</summary>
public sealed record SessionOutcome(string? Token, GoogleOutcome Failure)
{
    public bool Succeeded => Token is not null;
}

/// <summary>
/// One Map Tiles API session per basemap kind (streets is Google's <c>roadmap</c>, satellite its <c>satellite</c>), kept in memory and
/// created on the first request (design D15). A session is renewed under an hour before its <c>expiry</c>, or once after Google rejects
/// it; a semaphore per kind makes concurrent callers share one creation.
/// </summary>
public sealed class GoogleTileSessions(IHttpClientFactory httpClients, IOptions<MapOptions> options, TimeProvider time, GoogleFailureLog failures)
{
    public const string CreateSessionUrl = "https://tile.googleapis.com/v1/createSession";

    /// <summary>A session closer than this to its expiry is replaced.</summary>
    public static readonly TimeSpan RenewBefore = TimeSpan.FromHours(1);

    private sealed record Session(string Token, DateTimeOffset Expiry);

    private sealed class Slot
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public Session? Current { get; set; }
    }

    private readonly ConcurrentDictionary<string, Slot> _slots = new(StringComparer.Ordinal);

    /// <summary>Google's name for a basemap kind.</summary>
    public static string MapType(string kind) => kind == BasemapKinds.Satellite ? "satellite" : "roadmap";

    /// <summary>The kind's session, created when there is none or it is about to expire.</summary>
    public Task<SessionOutcome> GetAsync(string kind, CancellationToken ct) => EnsureAsync(kind, rejected: null, ct);

    /// <summary>
    /// A session to replace the one Google <paramref name="rejected"/>. If another caller already replaced it, that one is returned,
    /// so a burst of rejections creates one session.
    /// </summary>
    public Task<SessionOutcome> RenewAsync(string kind, string rejected, CancellationToken ct) => EnsureAsync(kind, rejected, ct);

    private async Task<SessionOutcome> EnsureAsync(string kind, string? rejected, CancellationToken ct)
    {
        var slot = _slots.GetOrAdd(kind, _ => new Slot());
        await slot.Gate.WaitAsync(ct);
        try
        {
            var current = slot.Current;
            var usable = current is not null && current.Token != rejected && current.Expiry - time.GetUtcNow() > RenewBefore;
            if (usable)
            {
                return new SessionOutcome(current!.Token, GoogleOutcome.Ok);
            }

            var created = await CreateAsync(kind, ct);
            if (created.Session is { } session)
            {
                slot.Current = session;
                return new SessionOutcome(session.Token, GoogleOutcome.Ok);
            }

            return new SessionOutcome(null, created.Failure);
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    private async Task<(Session? Session, GoogleOutcome Failure)> CreateAsync(string kind, CancellationToken ct)
    {
        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{CreateSessionUrl}?key={Uri.EscapeDataString(settings.GoogleApiKey)}")
        {
            Content = JsonContent.Create(new
            {
                mapType = MapType(kind),
                language = settings.EffectiveGoogleLanguage,
                region = settings.EffectiveGoogleRegion,
            }),
        };

        try
        {
            using var response = await httpClients.CreateClient(GoogleHttp.ClientName).SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var refused = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden;
                failures.Log(kind, "session", $"Google answered {(int)response.StatusCode}");
                return (null, refused ? GoogleOutcome.Unavailable : GoogleOutcome.Upstream);
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("session", out var token) || token.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(token.GetString()))
            {
                failures.Log(kind, "session", "the answer had no session");
                return (null, GoogleOutcome.Upstream);
            }

            // "expiry" is a string of seconds since the epoch. Without a usable one the session is kept for a short while only.
            var expiry = root.TryGetProperty("expiry", out var expires) && long.TryParse(expires.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(seconds, 0, 253_402_300_799))
                : time.GetUtcNow() + RenewBefore + TimeSpan.FromMinutes(30);
            return (new Session(token.GetString()!, expiry), GoogleOutcome.Ok);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException)
        {
            // The exception text could quote the request address, so only its kind is kept.
            failures.Log(kind, "session", exception is HttpRequestException http ? $"{http.HttpRequestError}" : exception.GetType().Name);
            return (null, GoogleOutcome.Upstream);
        }
    }
}
