using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Tamiza.Api.IntegrationTests.Infrastructure;

/// <summary>Runs the API with the MCP server enabled, and talks to it with the SDK's own client or with plain HTTP.</summary>
public sealed class McpHarness : IAsyncDisposable
{
    public const string Address = "https://tamiza.test/mcp";

    private McpHarness(TamizaApiFactory factory, string connectionString)
    {
        Factory = factory;
        ConnectionString = connectionString;
    }

    public TamizaApiFactory Factory { get; }

    public string ConnectionString { get; }

    public static async Task<McpHarness> StartAsync(PostgresFixture postgres, IReadOnlyDictionary<string, string?>? overrides = null, Action<IServiceCollection>? configureServices = null)
    {
        var settings = new Dictionary<string, string?> { ["TAMIZA_MCP_ENABLED"] = "true" };
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            settings[key] = value;
        }

        var connectionString = await postgres.CreateDatabaseAsync();
        return new McpHarness(
            new TamizaApiFactory(connectionString, settings, services =>
            {
                McpProbeTools.Register(services);
                configureServices?.Invoke(services);
            }),
            connectionString);
    }

    public static string TokenFor(string subject, string? name = "Ada Lovelace", string? email = "ada@example.org") =>
        TestTokens.Create(subject, name, email, audience: TestTokens.McpAudience);

    /// <summary>A plain HTTP client for the MCP endpoint, with the bearer token when one is given.</summary>
    public HttpClient Http(string? token = null)
    {
        var client = Factory.CreateClient();
        if (token is not null)
        {
            client.WithToken(token);
        }

        return client;
    }

    /// <summary>An MCP client connected as the person the token names, the way Claude connects.</summary>
    public async Task<McpClient> ConnectAsync(string token)
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(Address), TransportMode = HttpTransportMode.StreamableHttp },
            Http(token), loggerFactory: null, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    /// <summary>Posts a JSON-RPC <c>initialize</c> request, which is what a client sends first.</summary>
    public static async Task<HttpResponseMessage> InitializeAsync(HttpClient client, string? origin = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Address)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "initialize",
                    @params = new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "test", version = "1" } },
                }),
                Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
        }

        return await client.SendAsync(request);
    }

    public async ValueTask DisposeAsync() => await Factory.DisposeAsync();
}
