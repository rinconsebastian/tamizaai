using System.Text.Json;
using System.Text.Json.Nodes;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Tamiza.Api.IntegrationTests.Infrastructure;

/// <summary>An in-process OpenAI-compatible chat-completions server on localhost, like <see cref="KoboStub"/> for Kobo.</summary>
public sealed class AssistantStub : IDisposable
{
    public const string Key = "sk-assistant-stub-5d1e8a";
    public const string Model = "stub-model-1";
    public const string Path = "/v1/chat/completions";

    public WireMockServer Server { get; } = WireMockServer.Start();

    public string Url => Server.Url!;

    /// <summary>The base URL an operator would configure.</summary>
    public string BaseUrl => Url + "/v1";

    /// <summary>The deployment variables that point the api at this stub.</summary>
    public Dictionary<string, string?> Settings(string? key = null, string? model = null) => new()
    {
        ["TAMIZA_ASSISTANT_BASE_URL"] = BaseUrl,
        ["TAMIZA_ASSISTANT_MODEL"] = model ?? Model,
        ["TAMIZA_ASSISTANT_API_KEY"] = key ?? Key,
    };

    public AssistantStub()
    {
        Answer("Done.");
    }

    /// <summary>The body of a chat-completions answer. <paramref name="usage"/> is the raw JSON of the <c>usage</c> object, or null for none.</summary>
    public static string ChatBody(string? text, string? finishReason = "stop", string? usage = """{"prompt_tokens":100,"completion_tokens":20,"total_tokens":120}""")
    {
        var choice = new JsonObject
        {
            ["index"] = 0,
            ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = text },
            ["finish_reason"] = finishReason,
        };
        var body = new JsonObject { ["id"] = "chatcmpl-stub", ["choices"] = new JsonArray(choice) };
        if (usage is not null)
        {
            body["usage"] = JsonNode.Parse(usage);
        }

        return body.ToJsonString();
    }

    /// <summary>Answers every request carrying the key with <paramref name="text"/>; any other key gets 401.</summary>
    public AssistantStub Answer(string text, string? finishReason = "stop", string? usage = """{"prompt_tokens":100,"completion_tokens":20,"total_tokens":120}""") =>
        RespondRaw(200, ChatBody(text, finishReason, usage));

    /// <summary>Answers every request carrying the key with the given status and body.</summary>
    public AssistantStub RespondRaw(int status, string body, IDictionary<string, string>? headers = null, TimeSpan? delay = null, string key = Key)
    {
        Server.Reset();
        var response = Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body);
        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            response = response.WithHeader(name, value);
        }

        if (delay is not null)
        {
            response = response.WithDelay(delay.Value);
        }

        Server.Given(Request.Create().WithPath(Path).UsingPost().WithHeader("Authorization", $"Bearer {key}"))
            .RespondWith(response);
        Server.Given(Request.Create().WithPath(Path).UsingPost()).AtPriority(100)
            .RespondWith(Response.Create().WithStatusCode(401).WithBody("""{"error":{"message":"Incorrect API key provided."}}"""));
        return this;
    }

    /// <summary>Answers with a redirect to <paramref name="location"/> on this server.</summary>
    public AssistantStub Redirect(string location) =>
        RespondRaw(302, "", new Dictionary<string, string> { ["Location"] = Url + location });

    /// <summary>The requests the completions endpoint received, in order.</summary>
    public IReadOnlyList<Received> Requests() =>
        Server.LogEntries
            .Where(e => e.RequestMessage!.Path == Path)
            .Select(e => new Received(
                e.RequestMessage!.Headers!.TryGetValue("Authorization", out var authorization) ? authorization.Single() : null,
                JsonNode.Parse(e.RequestMessage.Body ?? "{}")!.AsObject()))
            .ToList();

    /// <summary>Number of requests, at any path, that reached the server.</summary>
    public int RequestsAt(string path) => Server.LogEntries.Count(e => e.RequestMessage!.Path == path);

    public sealed record Received(string? Authorization, JsonObject Body)
    {
        /// <summary>The property names of the request body, in alphabetical order.</summary>
        public string[] Fields => Body.Select(p => p.Key).Order(StringComparer.Ordinal).ToArray();

        public JsonArray Messages => Body["messages"]!.AsArray();
    }

    public void Dispose() => Server.Stop();
}
