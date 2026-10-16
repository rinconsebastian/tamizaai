using Microsoft.Extensions.Options;

namespace Tamiza.Api.Assistant;

/// <summary>
/// Limits of the provider client that tests may lower. <see cref="TimeoutOverride"/> replaces the configured
/// <c>TAMIZA_ASSISTANT_TIMEOUT_SECONDS</c> (whose smallest valid value is ten seconds) when it is set.
/// </summary>
public sealed class AssistantHttpOptions
{
    public TimeSpan? TimeoutOverride { get; set; }

    public long MaxResponseBytes { get; set; } = 4 * 1024 * 1024;
}

public static class AssistantHttp
{
    public const string ClientName = "assistant";

    /// <summary>
    /// Registers the named client the assistant talks to the provider with. Unlike Kobo's it has no connect callback: the
    /// URL is operator configuration, and a model server on the local network or the Docker host must work. It still never
    /// follows a redirect, so the key cannot be sent to another address, and never uses a proxy.
    /// </summary>
    public static IServiceCollection AddAssistantHttpClient(this IServiceCollection services)
    {
        services.AddOptions<AssistantHttpOptions>();
        services.AddHttpClient(ClientName, (provider, client) =>
            {
                var http = provider.GetRequiredService<IOptions<AssistantHttpOptions>>().Value;
                var assistant = provider.GetRequiredService<IOptions<AssistantOptions>>().Value;
                client.Timeout = http.TimeoutOverride ?? TimeSpan.FromSeconds(assistant.TimeoutSeconds);
                client.MaxResponseContentBufferSize = http.MaxResponseBytes;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            })
            .RedactLoggedHeaders(_ => true);
        return services;
    }
}
