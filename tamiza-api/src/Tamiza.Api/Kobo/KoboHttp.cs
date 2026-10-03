using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Tamiza.Api.Configuration;

namespace Tamiza.Api.Kobo;

/// <summary>Limits for calls to Kobo servers.</summary>
public sealed class KoboHttpOptions
{
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20);

    public long MaxResponseBytes { get; set; } = 10 * 1024 * 1024;
}

public static class KoboHttp
{
    public const string ClientName = "kobo";

    public static IServiceCollection AddKoboHttpClient(this IServiceCollection services)
    {
        services.AddOptions<KoboHttpOptions>();
        services.AddHttpClient(ClientName, (provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<KoboHttpOptions>>().Value;
                client.Timeout = options.Timeout;
                client.MaxResponseContentBufferSize = options.MaxResponseBytes;
            })
            .ConfigurePrimaryHttpMessageHandler(provider =>
            {
                var allowPrivate = provider.GetRequiredService<IOptions<TamizaOptions>>().Value.KoboAllowPrivateNetworks;
                return new SocketsHttpHandler
                {
                    // A redirect could lead anywhere; a proxy would dial on our behalf and skip the address check.
                    AllowAutoRedirect = false,
                    UseProxy = false,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    ConnectCallback = allowPrivate ? null : ConnectToPublicAddressAsync,
                };
            })
            .RedactLoggedHeaders(_ => true);
        return services;
    }

    /// <summary>
    /// Resolves the host and dials only addresses <see cref="IpAddressPolicy"/> allows. Checking the address that is
    /// actually dialed defeats DNS rebinding and late DNS changes.
    /// </summary>
    internal static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken);

        var allowed = addresses.Where(IpAddressPolicy.IsPublic).ToArray();
        if (allowed.Length == 0)
        {
            throw new KoboAddressNotAllowedException(host);
        }

        Exception? lastError = null;
        foreach (var address in allowed)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException error)
            {
                socket.Dispose();
                lastError = error;
            }
        }

        throw lastError!;
    }
}
