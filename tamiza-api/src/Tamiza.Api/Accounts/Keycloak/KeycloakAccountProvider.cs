using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Tamiza.Api.Configuration;

namespace Tamiza.Api.Accounts.Keycloak;

/// <summary>
/// The only code that knows Keycloak's admin REST API. It signs in with the confidential client's service account
/// (client credentials) and maps <see cref="IAccountProvider"/> to the realm's users endpoints. Policy lives in
/// <see cref="AccountEmails"/>. Failures are returned, never thrown, and logged without the email, the secret or the token.
/// </summary>
public sealed class KeycloakAccountProvider(
    IHttpClientFactory httpClients,
    IOptions<KeycloakAdminOptions> admin,
    IOptions<OidcOptions> oidc,
    IOptions<TamizaOptions> tamiza,
    TimeProvider time,
    ILogger<KeycloakAccountProvider> logger) : IAccountProvider
{
    private static readonly TimeSpan TokenMargin = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpires;

    private KeycloakAddress? Address => KeycloakAddress.From(oidc.Value.Authority, oidc.Value.MetadataAddress, admin.Value.Url);

    public async Task<ProviderResult<ProviderAccount?>> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        var reply = await SendAdminAsync("find-by-email", HttpMethod.Get,
            $"/users?email={Uri.EscapeDataString(normalizedEmail)}&exact=true&max=2", null, cancellationToken);
        if (reply.Failure != ProviderFailure.None)
        {
            return new ProviderResult<ProviderAccount?>(null, reply.Failure, reply.Status);
        }

        if (reply.Status != 200)
        {
            return Failed<ProviderAccount?>("find-by-email", reply.Status, null);
        }

        var users = Parse(reply.Body) as JsonArray;
        return new ProviderResult<ProviderAccount?>(users?.FirstOrDefault() is JsonObject first ? ToAccount(first) : null);
    }

    public async Task<ProviderResult<ProviderAccount?>> FindBySubjectAsync(string subject, CancellationToken cancellationToken)
    {
        var reply = await SendAdminAsync("find-by-subject", HttpMethod.Get, $"/users/{Uri.EscapeDataString(subject)}", null, cancellationToken);
        if (reply.Failure != ProviderFailure.None)
        {
            return new ProviderResult<ProviderAccount?>(null, reply.Failure, reply.Status);
        }

        return reply.Status switch
        {
            200 when Parse(reply.Body) is JsonObject user => new ProviderResult<ProviderAccount?>(ToAccount(user)),
            404 => new ProviderResult<ProviderAccount?>(null),
            _ => Failed<ProviderAccount?>("find-by-subject", reply.Status, null),
        };
    }

    public async Task<ProviderResult<string>> CreateAsync(string email, string? language, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["username"] = email,
            ["email"] = email,
            ["enabled"] = true,
            ["emailVerified"] = false,
        };
        if (language is "en" or "es")
        {
            body["attributes"] = new JsonObject { ["locale"] = new JsonArray(language) };
        }

        var reply = await SendAdminAsync("create", HttpMethod.Post, "/users", body.ToJsonString(), cancellationToken);
        if (reply.Failure != ProviderFailure.None)
        {
            return new ProviderResult<string>(null, reply.Failure, reply.Status);
        }

        if (reply.Status == 201 && reply.Location is { Length: > 0 } location)
        {
            var id = location.TrimEnd('/')[(location.TrimEnd('/').LastIndexOf('/') + 1)..];
            if (id.Length > 0)
            {
                return new ProviderResult<string>(Uri.UnescapeDataString(id));
            }
        }

        if (reply.Status == 409)
        {
            return new ProviderResult<string>(null, ProviderFailure.Conflict, 409);
        }

        return Failed<string>("create", reply.Status, null);
    }

    public Task<ProviderResult> SendSetupEmailAsync(string accountId, CancellationToken cancellationToken) =>
        SendActionsAsync("send-setup", accountId, ["UPDATE_PASSWORD", "VERIFY_EMAIL"], cancellationToken);

    public Task<ProviderResult> SendPasswordResetEmailAsync(string accountId, CancellationToken cancellationToken) =>
        SendActionsAsync("send-reset", accountId, ["UPDATE_PASSWORD"], cancellationToken);

    private async Task<ProviderResult> SendActionsAsync(string operation, string accountId, string[] actions, CancellationToken cancellationToken)
    {
        var query = $"?client_id={Uri.EscapeDataString(oidc.Value.ClientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(tamiza.Value.PublicUrl.TrimEnd('/') + "/")}";
        if (admin.Value.LinkHours is { } hours)
        {
            query += $"&lifespan={hours * 3600}";
        }

        var body = new JsonArray(actions.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray()).ToJsonString();
        var reply = await SendAdminAsync(operation, HttpMethod.Put, $"/users/{Uri.EscapeDataString(accountId)}/execute-actions-email{query}", body, cancellationToken);
        if (reply.Failure != ProviderFailure.None)
        {
            return new ProviderResult(reply.Failure, reply.Status);
        }

        if (reply.Status is 200 or 204)
        {
            return ProviderResult.Ok;
        }

        // Keycloak answers 400 for a disabled user or one without an email, and for a rejected redirect URI; only the first two
        // mean "this account cannot be emailed".
        var message = ErrorMessage(reply.Body);
        var unavailable = reply.Status == 404
            || (reply.Status == 400 && (message.Contains("disabled", StringComparison.OrdinalIgnoreCase)
                || message.Contains("email missing", StringComparison.OrdinalIgnoreCase)));
        if (unavailable)
        {
            return new ProviderResult(ProviderFailure.AccountUnavailable, reply.Status);
        }

        logger.LogWarning("Keycloak {Operation} failed with HTTP {Status} for account {AccountId}.", operation, reply.Status, accountId);
        return new ProviderResult(ProviderFailure.Other, reply.Status);
    }

    private ProviderResult<T> Failed<T>(string operation, int status, string? accountId)
    {
        logger.LogWarning("Keycloak {Operation} failed with HTTP {Status}{Account}.", operation, status, accountId is null ? "" : $" for account {accountId}");
        return new ProviderResult<T>(default, ProviderFailure.Other, status);
    }

    private async Task<Reply> SendAdminAsync(string operation, HttpMethod method, string path, string? json, CancellationToken cancellationToken)
    {
        var address = Address;
        if (address is null)
        {
            logger.LogWarning("Keycloak {Operation} skipped: OIDC_AUTHORITY is not a realm URL.", operation);
            return new Reply(ProviderFailure.Other, 0, "", null);
        }

        for (var attempt = 0; ; attempt++)
        {
            var token = await GetTokenAsync(address, operation, cancellationToken);
            if (token.Failure != ProviderFailure.None)
            {
                return new Reply(token.Failure, token.Status ?? 0, "", null);
            }

            using var request = new HttpRequestMessage(method, address.AdminBase + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
            if (json is not null)
            {
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            var reply = await ExchangeAsync(operation, request, cancellationToken);
            if (reply.Failure == ProviderFailure.None && reply.Status == 401 && attempt == 0)
            {
                Invalidate(token.Value!);
                continue;
            }

            if (reply.Failure == ProviderFailure.None && reply.Status is 401 or 403)
            {
                logger.LogWarning(
                    "Keycloak {Operation} was refused with HTTP {Status}: check that the service account of {ClientId} has the realm-management roles manage-users and view-users.",
                    operation, reply.Status, admin.Value.ClientId);
                return reply with { Failure = ProviderFailure.Unauthorized };
            }

            return reply;
        }
    }

    private async Task<ProviderResult<string>> GetTokenAsync(KeycloakAddress address, string operation, CancellationToken cancellationToken)
    {
        await _tokenGate.WaitAsync(cancellationToken);
        try
        {
            if (_token is not null && time.GetUtcNow() < _tokenExpires - TokenMargin)
            {
                return new ProviderResult<string>(_token);
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, address.TokenUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = admin.Value.ClientId,
                    ["client_secret"] = admin.Value.ClientSecret,
                }),
            };
            var reply = await ExchangeAsync($"{operation}-token", request, cancellationToken);
            if (reply.Failure != ProviderFailure.None)
            {
                return new ProviderResult<string>(null, reply.Failure, reply.Status);
            }

            if (reply.Status != 200 || Parse(reply.Body) is not JsonObject json || json["access_token"]?.GetValue<string>() is not { Length: > 0 } token)
            {
                var failure = reply.Status is 400 or 401 or 403 ? ProviderFailure.Unauthorized : ProviderFailure.Other;
                logger.LogWarning("Keycloak token request for {Operation} failed with HTTP {Status}: check the client ID and secret of {ClientId}.",
                    operation, reply.Status, admin.Value.ClientId);
                return new ProviderResult<string>(null, failure, reply.Status);
            }

            var seconds = json["expires_in"] is JsonValue value && value.TryGetValue<int>(out var expires) ? expires : 60;
            _token = token;
            _tokenExpires = time.GetUtcNow().AddSeconds(seconds);
            return new ProviderResult<string>(token);
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    private void Invalidate(string token)
    {
        _tokenGate.Wait();
        try
        {
            if (_token == token)
            {
                _token = null;
            }
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    private async Task<Reply> ExchangeAsync(string operation, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClients.CreateClient(KeycloakHttp.ClientName).SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new Reply(ProviderFailure.None, (int)response.StatusCode, body, response.Headers.Location?.OriginalString);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException
            && !cancellationToken.IsCancellationRequested)
        {
            // The exception text could quote the address (the email); only its type is logged.
            logger.LogWarning("Keycloak {Operation} could not be completed ({Reason}).", operation, exception.GetType().Name);
            return new Reply(ProviderFailure.Unreachable, 0, "", null);
        }
    }

    private static ProviderAccount ToAccount(JsonObject user) => new(
        user["id"]?.GetValue<string>() ?? "",
        user["emailVerified"] is JsonValue verified && verified.TryGetValue<bool>(out var v) && v,
        user["enabled"] is not JsonValue enabled || !enabled.TryGetValue<bool>(out var e) || e,
        !string.IsNullOrWhiteSpace(user["email"]?.GetValue<string>()));

    private static JsonNode? Parse(string body)
    {
        try
        {
            return JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ErrorMessage(string body) =>
        Parse(body) is JsonObject json ? json["errorMessage"]?.ToString() ?? json["error"]?.ToString() ?? "" : "";

    private readonly record struct Reply(ProviderFailure Failure, int Status, string Body, string? Location);
}

/// <summary>Limits of the Keycloak client that tests may lower.</summary>
public sealed class KeycloakHttpOptions
{
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);
}

/// <summary>Registers the named client the Keycloak adapter talks to Keycloak with.</summary>
public static class KeycloakHttp
{
    public const string ClientName = "keycloak-admin";

    /// <summary>
    /// A redirect from Keycloak is never followed, so the client secret and the tokens cannot be sent to another address, and no proxy
    /// is used. Request logging is off: the admin URLs carry email addresses and the headers carry tokens.
    /// </summary>
    public static IServiceCollection AddKeycloakHttpClient(this IServiceCollection services)
    {
        services.AddOptions<KeycloakHttpOptions>();
        services.AddHttpClient(ClientName, (provider, client) =>
            {
                client.Timeout = provider.GetRequiredService<IOptions<KeycloakHttpOptions>>().Value.Timeout;
                client.MaxResponseContentBufferSize = 1024 * 1024;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            })
            .RemoveAllLoggers();
        return services;
    }
}
