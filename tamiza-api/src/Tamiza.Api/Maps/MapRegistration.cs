using System.Net;
using Microsoft.Extensions.Options;

namespace Tamiza.Api.Maps;

public static class MapRegistration
{
    public static IServiceCollection AddTamizaMaps(this IServiceCollection services)
    {
        // Flat TAMIZA_MAP_* variables bind from the configuration root, like the other deployment settings.
        services.AddOptions<MapOptions>()
            .Configure<IConfiguration>((options, configuration) => configuration.Bind(options))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MapOptions>, MapOptionsValidator>();

        services.AddGoogleTilesHttpClient();
        services.AddSingleton<GoogleFailureLog>();
        services.AddSingleton<GoogleTileSessions>();
        services.AddSingleton<GoogleTiles>();
        services.AddSingleton<TileLinks>();
        services.AddSingleton<TileLimits>();
        services.AddHostedService<MapStartupCheck>();
        return services;
    }
}

/// <summary>Limits of the Google client that tests may lower.</summary>
public sealed class GoogleHttpOptions
{
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    public long MaxResponseBytes { get; set; } = 2 * 1024 * 1024;
}

public static class GoogleHttp
{
    public const string ClientName = "maps-google";

    /// <summary>
    /// Registers the named client the api talks to Google's Map Tiles API with. It never follows a redirect and never uses a proxy,
    /// so the key cannot be sent to another address. Google wants the key in the query string, so the default logging of the client
    /// factory, which writes the request address, is removed (<c>RemoveAllLoggers</c>), and the headers are redacted anyway.
    /// </summary>
    public static IServiceCollection AddGoogleTilesHttpClient(this IServiceCollection services)
    {
        services.AddOptions<GoogleHttpOptions>();
        services.AddHttpClient(ClientName, (provider, client) =>
            {
                var http = provider.GetRequiredService<IOptions<GoogleHttpOptions>>().Value;
                client.Timeout = http.Timeout;
                client.MaxResponseContentBufferSize = http.MaxResponseBytes;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                AutomaticDecompression = DecompressionMethods.All,
            })
            .RedactLoggedHeaders(_ => true)
            .RemoveAllLoggers();
        return services;
    }
}

/// <summary>
/// Says once at startup what the provider setting means for the network: which hosts will be contacted and by whom, and which
/// settings have no effect. It never writes the key.
/// </summary>
public sealed class MapStartupCheck(IOptions<MapOptions> options, ILogger<MapStartupCheck> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        switch (settings.EffectiveProvider)
        {
            case MapOptions.ProviderOsm:
                logger.LogInformation(
                    "Map basemaps: provider osm. Viewers' browsers will contact tile.openstreetmap.org; the api contacts no tile provider.");
                break;
            case MapOptions.ProviderGoogle:
                logger.LogInformation(
                    "Map basemaps: provider google. The api will contact tile.googleapis.com; browsers contact no tile provider.");
                break;
            default:
                logger.LogInformation("Map basemaps are off ({Variable} is not set): no request is made to any tile provider.", MapOptions.ProviderVariable);
                break;
        }

        var ignored = settings.IsGoogle ? [] : settings.GoogleVariablesSet();
        if (ignored.Count > 0)
        {
            logger.LogWarning(
                "{Variables} {Verb} set but ignored: {Provider} is not {Google}.",
                string.Join(", ", ignored), ignored.Count == 1 ? "is" : "are", MapOptions.ProviderVariable, MapOptions.ProviderGoogle);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
