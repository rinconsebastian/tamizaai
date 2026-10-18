using System.Net;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tamiza.Api.Maps;

namespace Tamiza.Api.UnitTests.Maps;

/// <summary>A clock a test sets and moves.</summary>
public sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    private long _ticks = start.UtcTicks;

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
}

/// <summary>Answers each request with what a test script says; records the requests. No test reaches the network.</summary>
public sealed class ScriptedHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> answer) : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly List<(HttpMethod Method, Uri Uri, string? Body)> _requests = [];

    public IReadOnlyList<(HttpMethod Method, Uri Uri, string? Body)> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (_gate)
        {
            _requests.Add((request.Method, request.RequestUri!, body));
        }

        return answer(request, body);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Session(string token, long expiry) => Json($$"""{"session":"{{token}}","expiry":"{{expiry}}","tileWidth":256,"tileHeight":256,"imageFormat":"png"}""");

    public static HttpResponseMessage Png(byte[]? bytes = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes ?? [0x89, 0x50, 0x4E, 0x47]) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        return response;
    }

    public static HttpResponseMessage Error(HttpStatusCode status, string reason, string message) =>
        Json(new System.Text.Json.Nodes.JsonObject
        {
            ["error"] = new System.Text.Json.Nodes.JsonObject
            {
                ["code"] = (int)status,
                ["message"] = message,
                ["errors"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["message"] = message, ["domain"] = "global", ["reason"] = reason }),
                ["status"] = "X",
            },
        }.ToJsonString(), status);
}

/// <summary>Keeps every log line (message and exception), so a test can assert that a secret reaches none.</summary>
public sealed class LogCapture : ILoggerProvider
{
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_lines)
            {
                return [.. _lines];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _lines);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, List<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (lines)
            {
                lines.Add($"{logLevel} {category}: {formatter(state, exception)} {exception}");
            }
        }
    }
}

/// <summary>The Maps services on a plain service collection, with Google replaced by a handler and a manual clock.</summary>
public sealed class MapsServices : IDisposable
{
    public const string Key = "AIzaUnitTestKey_not-real_0123456789";

    /// <summary>Five minutes into a 45-minute tile-link window (windows are aligned to the epoch).</summary>
    public static readonly DateTimeOffset DefaultStart =
        DateTimeOffset.FromUnixTimeSeconds(new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds() / 2700 * 2700 + 300);

    private readonly ServiceProvider _provider;

    public MapsServices(
        ScriptedHandler google, ManualTime? time = null, Action<MapOptions>? configure = null, LogCapture? logs = null)
    {
        Time = time ?? new ManualTime(DefaultStart);
        Logs = logs ?? new LogCapture();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Trace));
        services.AddSingleton<TimeProvider>(Time);
        services.AddMemoryCache();
        services.AddDataProtection();
        services.Configure<MapOptions>(options =>
        {
            options.Provider = "google";
            options.GoogleApiKey = Key;
            configure?.Invoke(options);
        });
        services.AddGoogleTilesHttpClient();
        services.AddSingleton<GoogleFailureLog>();
        services.AddSingleton<GoogleTileSessions>();
        services.AddSingleton<GoogleTiles>();
        services.AddSingleton<TileLinks>();
        services.AddSingleton<TileLimits>();
        services.AddHttpClient(GoogleHttp.ClientName).ConfigurePrimaryHttpMessageHandler(() => google);
        // The negative control of the logging test: a client without RemoveAllLoggers, which logs the request address.
        services.AddHttpClient("control").ConfigurePrimaryHttpMessageHandler(() => google);

        _provider = services.BuildServiceProvider();
    }

    public ManualTime Time { get; }

    public LogCapture Logs { get; }

    public GoogleTileSessions Sessions => _provider.GetRequiredService<GoogleTileSessions>();

    public GoogleTiles Tiles => _provider.GetRequiredService<GoogleTiles>();

    public TileLinks Links => _provider.GetRequiredService<TileLinks>();

    public TileLimits Limits => _provider.GetRequiredService<TileLimits>();

    public IDataProtectionProvider Protection => _provider.GetRequiredService<IDataProtectionProvider>();

    public IMemoryCache Cache => _provider.GetRequiredService<IMemoryCache>();

    public HttpClient Control => _provider.GetRequiredService<IHttpClientFactory>().CreateClient("control");

    public void Dispose() => _provider.Dispose();
}
