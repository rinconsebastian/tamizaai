using System.Text.Json.Nodes;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using WireMock.Types;
using WireMock.Util;

namespace Tamiza.Api.IntegrationTests.Infrastructure;

/// <summary>
/// An in-process stand-in for the parts of Keycloak's admin API that account emails use: the client-credentials token endpoint and
/// the realm's users (search, get, create) and <c>execute-actions-email</c>. It keeps the users in memory and records every call.
/// </summary>
public sealed class KeycloakStub : IDisposable
{
    public const string Realm = "tamiza";
    public const string ClientId = "tamiza-admin";
    public const string ClientSecret = "kc-secret-4e7b91";

    private readonly object _gate = new();
    private readonly List<StubUser> _users = [];
    private readonly List<Call> _calls = [];
    private readonly HashSet<string> _validTokens = [];
    private int _tokensIssued;

    public WireMockServer Server { get; } = WireMockServer.Start();

    public string Url => Server.Url!;

    /// <summary>Seconds a token is valid, as <c>expires_in</c>.</summary>
    public int TokenLifetimeSeconds { get; set; } = 300;

    /// <summary>When set, <c>execute-actions-email</c> answers this status with this Keycloak error message (SMTP not configured is 500).</summary>
    public (int Status, string Message)? ActionsFailure { get; set; }

    /// <summary>When set, the user create call answers this status.</summary>
    public int? CreateFailure { get; set; }

    /// <summary>When set, the token endpoint answers this status (400 invalid_client when the credentials are wrong anyway).</summary>
    public int? TokenFailure { get; set; }

    /// <summary>When set, the token endpoint answers a redirect to this address.</summary>
    public string? TokenRedirect { get; set; }

    /// <summary>When set, every admin call waits this long before answering.</summary>
    public TimeSpan AdminDelay { get; set; }

    public KeycloakStub()
    {
        Server.Given(Request.Create().WithPath("/*").UsingAnyMethod()).RespondWith(Response.Create().WithCallback(Handle));
    }

    /// <summary>The deployment variables that point the api at this stub.</summary>
    public Dictionary<string, string?> Settings(string? secret = null, int? linkHours = null) => new()
    {
        ["TAMIZA_KEYCLOAK_ADMIN_CLIENT_ID"] = ClientId,
        ["TAMIZA_KEYCLOAK_ADMIN_CLIENT_SECRET"] = secret ?? ClientSecret,
        ["TAMIZA_KEYCLOAK_ADMIN_URL"] = Url,
        ["TAMIZA_KEYCLOAK_LINK_HOURS"] = linkHours?.ToString(),
    };

    public string TokenPath => $"/realms/{Realm}/protocol/openid-connect/token";

    public string UsersPath => $"/admin/realms/{Realm}/users";

    /// <summary>Adds an account to the realm, as the Keycloak console would.</summary>
    public StubUser AddUser(string email, bool enabled = true, bool emailVerified = true, bool hasEmail = true)
    {
        var user = new StubUser(Guid.NewGuid().ToString(), hasEmail ? email : null) { Enabled = enabled, EmailVerified = emailVerified };
        lock (_gate)
        {
            _users.Add(user);
        }

        return user;
    }

    public void RemoveUser(string id)
    {
        lock (_gate)
        {
            _users.RemoveAll(u => u.Id == id);
        }
    }

    public StubUser? FindUser(string email)
    {
        lock (_gate)
        {
            return _users.FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
        }
    }

    public IReadOnlyList<StubUser> Users
    {
        get
        {
            lock (_gate)
            {
                return _users.ToList();
            }
        }
    }

    /// <summary>Makes every token issued so far stop working, so the next admin call gets 401.</summary>
    public void ExpireTokens()
    {
        lock (_gate)
        {
            _validTokens.Clear();
        }
    }

    public IReadOnlyList<Call> Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls.ToList();
            }
        }
    }

    public IReadOnlyList<Call> TokenCalls => Calls.Where(c => c.Path == TokenPath).ToList();

    public IReadOnlyList<Call> CallsTo(string method, string pathStartingWith) =>
        Calls.Where(c => c.Method == method && c.Path.StartsWith(pathStartingWith, StringComparison.Ordinal)).ToList();

    public IReadOnlyList<Call> CreateCalls => CallsTo("POST", UsersPath).Where(c => c.Path == UsersPath).ToList();

    public IReadOnlyList<Call> ActionCalls => Calls.Where(c => c.Path.EndsWith("/execute-actions-email", StringComparison.Ordinal)).ToList();

    private ResponseMessage Handle(IRequestMessage request)
    {
        var query = request.Query is null
            ? new Dictionary<string, string>()
            : request.Query.ToDictionary(p => p.Key, p => p.Value.FirstOrDefault() ?? "");
        var body = request.Body ?? "";
        var authorization = request.Headers is not null && request.Headers.TryGetValue("Authorization", out var values) ? values.FirstOrDefault() : null;
        var call = new Call(request.Method.ToUpperInvariant(), request.Path, query, authorization, body, DateTimeOffset.UtcNow);
        lock (_gate)
        {
            _calls.Add(call);
        }

        if (call.Path == TokenPath && call.Method == "POST")
        {
            return Token(body);
        }

        if (!call.Path.StartsWith(UsersPath, StringComparison.Ordinal))
        {
            return Json(404, """{"error":"Not found"}""");
        }

        if (AdminDelay > TimeSpan.Zero)
        {
            Thread.Sleep(AdminDelay);
        }

        bool valid;
        lock (_gate)
        {
            valid = authorization is not null && authorization.StartsWith("Bearer ", StringComparison.Ordinal) && _validTokens.Contains(authorization[7..]);
        }

        if (!valid)
        {
            return Json(401, """{"error":"HTTP 401 Unauthorized"}""");
        }

        var rest = call.Path[UsersPath.Length..].Trim('/');
        return rest switch
        {
            "" when call.Method == "GET" => Search(query),
            "" when call.Method == "POST" => Create(body),
            _ when call.Method == "GET" && !rest.Contains('/') => Get(Uri.UnescapeDataString(rest)),
            _ when call.Method == "PUT" && rest.EndsWith("/execute-actions-email", StringComparison.Ordinal) =>
                Actions(Uri.UnescapeDataString(rest[..rest.IndexOf('/')])),
            _ => Json(404, """{"error":"Not found"}"""),
        };
    }

    private ResponseMessage Token(string form)
    {
        if (TokenRedirect is { } redirect)
        {
            var moved = Json(302, "");
            moved.AddHeader("Location", redirect);
            return moved;
        }

        var fields = System.Web.HttpUtility.ParseQueryString(form);
        if (TokenFailure is { } status)
        {
            return Json(status, """{"error":"server_error"}""");
        }

        if (fields["grant_type"] != "client_credentials" || fields["client_id"] != ClientId || fields["client_secret"] != ClientSecret)
        {
            return Json(401, """{"error":"unauthorized_client","error_description":"Invalid client or Invalid client credentials"}""");
        }

        string token;
        lock (_gate)
        {
            token = $"kc-token-{++_tokensIssued}";
            _validTokens.Add(token);
        }

        return Json(200, new JsonObject { ["access_token"] = token, ["expires_in"] = TokenLifetimeSeconds, ["token_type"] = "Bearer" }.ToJsonString());
    }

    private ResponseMessage Search(Dictionary<string, string> query)
    {
        var result = new JsonArray();
        lock (_gate)
        {
            foreach (var user in _users.Where(u => query.TryGetValue("email", out var email) && string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(user.ToJson());
            }
        }

        return Json(200, result.ToJsonString());
    }

    private ResponseMessage Get(string id)
    {
        lock (_gate)
        {
            return _users.FirstOrDefault(u => u.Id == id) is { } user
                ? Json(200, user.ToJson().ToJsonString())
                : Json(404, """{"error":"User not found"}""");
        }
    }

    private ResponseMessage Create(string body)
    {
        if (CreateFailure is { } status)
        {
            return Json(status, """{"errorMessage":"Failure injected by the test."}""");
        }

        var json = JsonNode.Parse(body)!.AsObject();
        var email = json["email"]?.GetValue<string>() ?? "";
        StubUser user;
        lock (_gate)
        {
            if (_users.Any(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)))
            {
                return Json(409, """{"errorMessage":"User exists with same username or email"}""");
            }

            user = new StubUser(Guid.NewGuid().ToString(), email.ToLowerInvariant())
            {
                Enabled = json["enabled"]?.GetValue<bool>() ?? true,
                EmailVerified = json["emailVerified"]?.GetValue<bool>() ?? false,
                Attributes = json["attributes"]?.AsObject(),
                CreatedByApi = true,
            };
            _users.Add(user);
        }

        var response = Json(201, "");
        response.AddHeader("Location", $"{Url}{UsersPath}/{user.Id}");
        return response;
    }

    private ResponseMessage Actions(string id)
    {
        if (ActionsFailure is { } failure)
        {
            return Json(failure.Status, new JsonObject { ["errorMessage"] = failure.Message }.ToJsonString());
        }

        lock (_gate)
        {
            if (_users.FirstOrDefault(u => u.Id == id) is not { } user)
            {
                return Json(404, """{"error":"User not found"}""");
            }

            if (!user.Enabled)
            {
                return Json(400, """{"errorMessage":"User is disabled"}""");
            }

            if (string.IsNullOrEmpty(user.Email))
            {
                return Json(400, """{"errorMessage":"User email missing"}""");
            }
        }

        return Json(204, "");
    }

    private static ResponseMessage Json(int status, string body) => new()
    {
        StatusCode = status,
        Headers = new Dictionary<string, WireMockList<string>> { ["Content-Type"] = new("application/json") },
        BodyData = new BodyData { BodyAsString = body, DetectedBodyType = BodyType.String },
    };

    public void Dispose() => Server.Stop();

    public sealed class StubUser(string id, string? email)
    {
        public string Id { get; } = id;

        public string? Email { get; set; } = email;

        public bool Enabled { get; set; } = true;

        public bool EmailVerified { get; set; }

        public JsonObject? Attributes { get; set; }

        public bool CreatedByApi { get; set; }

        public JsonObject ToJson()
        {
            var json = new JsonObject { ["id"] = Id, ["username"] = Email ?? Id, ["enabled"] = Enabled, ["emailVerified"] = EmailVerified };
            if (Email is not null)
            {
                json["email"] = Email;
            }

            return json;
        }
    }

    public sealed record Call(string Method, string Path, IReadOnlyDictionary<string, string> Query, string? Authorization, string Body, DateTimeOffset At)
    {
        public JsonNode? Json => string.IsNullOrWhiteSpace(Body) ? null : JsonNode.Parse(Body);
    }
}
