using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Tamiza.Api.IntegrationTests.Infrastructure;

/// <summary>
/// A fake of Google's Map Tiles API, plugged in as the primary handler of the <c>maps-google</c> client, so no test reaches the
/// network. It answers as the documentation describes: <c>createSession</c> with a session and its <c>expiry</c> in seconds,
/// <c>2dtiles</c> with an image, <c>viewport</c> with <c>copyright</c> and <c>maxZoomRects</c>, and errors as
/// <c>{"error": {"code", "message", "errors": [{"reason"}], "status"}}</c> (reasons <c>expired</c>, <c>notFound</c>, <c>quotaExceeded</c>).
/// The key is a fake that only the tests know.
/// </summary>
public sealed class GoogleStub : HttpMessageHandler
{
    public const string Key = "AIzaStubKey_not-a-real-key_0123456789";

    /// <summary>A few bytes that start like a PNG: the relay passes bytes on without reading them.</summary>
    public static readonly byte[] TileBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    public const string TileETag = "\"tile-etag-1\"";
    public const string TileCacheControl = "private, max-age=3600";

    /// <summary>One request the stub received.</summary>
    public sealed record Call(HttpMethod Method, Uri Uri, string? Body, IReadOnlyDictionary<string, string> Headers)
    {
        public string Path => Uri.AbsolutePath;

        public IReadOnlyDictionary<string, string> Query =>
            Uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => pair.Split('=', 2))
                .ToDictionary(p => Uri_Decode(p[0]), p => p.Length > 1 ? Uri_Decode(p[1]) : "");

        private static string Uri_Decode(string text) => Uri.UnescapeDataString(text);
    }

    private readonly object _gate = new();
    private readonly List<Call> _calls = [];
    private readonly HashSet<string> _rejectedSessions = [];
    private int _sessions;

    /// <summary>The seconds since the epoch the next session expires at; two weeks from now by default.</summary>
    public Func<long> Expiry { get; set; } = () => DateTimeOffset.UtcNow.AddDays(14).ToUnixTimeSeconds();

    /// <summary>Answers a request before the defaults do; null lets the default answer.</summary>
    public Func<Call, HttpResponseMessage?>? Intercept { get; set; }

    /// <summary>The JSON array of <c>maxZoomRects</c> the viewport answers with.</summary>
    public string MaxZoomRects { get; set; } = """[{"maxZoom":19,"north":90,"south":-90,"east":180,"west":-180}]""";

    public string Copyright { get; set; } = "Map data ©2026 Google";

    public IReadOnlyList<Call> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    public IReadOnlyList<Call> CallsAt(string pathPrefix) => Calls.Where(c => c.Path.StartsWith(pathPrefix, StringComparison.Ordinal)).ToList();

    public IReadOnlyList<Call> SessionCalls => CallsAt("/v1/createSession");

    public IReadOnlyList<Call> TileCalls => CallsAt("/v1/2dtiles/");

    public IReadOnlyList<Call> ViewportCalls => CallsAt("/tile/v1/viewport");

    public int SessionsCreated => Volatile.Read(ref _sessions);

    /// <summary>From now on the session is refused as expired.</summary>
    public void Reject(string session)
    {
        lock (_gate)
        {
            _rejectedSessions.Add(session);
        }
    }

    public static HttpResponseMessage Error(HttpStatusCode status, string reason, string message, string googleStatus) =>
        new(status)
        {
            Content = new StringContent(
                new JsonObject
                {
                    ["error"] = new JsonObject
                    {
                        ["code"] = (int)status,
                        ["message"] = message,
                        ["errors"] = new JsonArray(new JsonObject { ["message"] = message, ["domain"] = "global", ["reason"] = reason }),
                        ["status"] = googleStatus,
                    },
                }.ToJsonString(),
                Encoding.UTF8, "application/json"),
        };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var call = new Call(request.Method, request.RequestUri!, body, headers);
        lock (_gate)
        {
            _calls.Add(call);
        }

        if (Intercept?.Invoke(call) is { } answer)
        {
            return answer;
        }

        return call.Path switch
        {
            "/v1/createSession" => CreateSession(),
            "/tile/v1/viewport" => Viewport(call),
            _ when call.Path.StartsWith("/v1/2dtiles/", StringComparison.Ordinal) => Tile(call),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private HttpResponseMessage CreateSession()
    {
        var number = Interlocked.Increment(ref _sessions);
        var json = new JsonObject
        {
            ["session"] = $"session-{number}",
            ["expiry"] = Expiry().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["tileWidth"] = 256,
            ["tileHeight"] = 256,
            ["imageFormat"] = "png",
        };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
    }

    private bool IsRejected(Call call)
    {
        lock (_gate)
        {
            return call.Query.TryGetValue("session", out var session) && _rejectedSessions.Contains(session);
        }
    }

    private HttpResponseMessage Tile(Call call)
    {
        if (IsRejected(call))
        {
            return Error(HttpStatusCode.BadRequest, "expired", "The session token has expired.", "INVALID_ARGUMENT");
        }

        if (call.Headers.TryGetValue("If-None-Match", out var etag) && etag == TileETag)
        {
            var notModified = new HttpResponseMessage(HttpStatusCode.NotModified);
            notModified.Headers.TryAddWithoutValidation("ETag", TileETag);
            notModified.Headers.TryAddWithoutValidation("Cache-Control", TileCacheControl);
            return notModified;
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(TileBytes) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        response.Headers.TryAddWithoutValidation("ETag", TileETag);
        response.Headers.TryAddWithoutValidation("Cache-Control", TileCacheControl);
        return response;
    }

    private HttpResponseMessage Viewport(Call call)
    {
        if (IsRejected(call))
        {
            return Error(HttpStatusCode.BadRequest, "expired", "The session token has expired.", "INVALID_ARGUMENT");
        }

        var json = new JsonObject { ["copyright"] = Copyright, ["maxZoomRects"] = JsonNode.Parse(MaxZoomRects) };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
    }
}
