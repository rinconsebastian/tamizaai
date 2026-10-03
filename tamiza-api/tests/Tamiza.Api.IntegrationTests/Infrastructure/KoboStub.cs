using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Tamiza.Api.IntegrationTests.Infrastructure;

/// <summary>An in-process Kobo API v2 stand-in on localhost.</summary>
public sealed class KoboStub : IDisposable
{
    public const string AssetUid = "aTestAsset123";
    public const string Token = "kobo-token-7f3a9c";

    public const string FormJson = """
        {
          "name": "Household survey",
          "content": {
            "survey": [
              { "type": "start", "name": "start", "$xpath": "start" },
              { "type": "end", "name": "end", "$xpath": "end" },
              { "type": "select_one municipalities", "name": "municipality", "$xpath": "municipality", "label": ["Municipality"] },
              { "type": "select_one sex", "name": "sex", "$xpath": "sex", "label": ["Sex"] },
              { "type": "text", "name": "enumerator", "$xpath": "enumerator", "label": ["Enumerator"] }
            ]
          }
        }
        """;

    public WireMockServer Server { get; } = WireMockServer.Start();

    public string Url => Server.Url!;

    public string AssetPath => $"/api/v2/assets/{AssetUid}/";

    public string DataPath => $"/api/v2/assets/{AssetUid}/data/";

    /// <summary>Serves the form and its submissions to requests carrying <paramref name="token"/>; anything else gets 401.</summary>
    public KoboStub WithForm(string formJson = FormJson, string token = Token, int submissionsStatus = 200)
    {
        Server.Reset();
        Server.Given(Request.Create().WithPath(AssetPath).UsingGet().WithHeader("Authorization", $"Token {token}"))
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(formJson));
        Server.Given(Request.Create().WithPath(DataPath).UsingGet().WithHeader("Authorization", $"Token {token}"))
            .RespondWith(Response.Create().WithStatusCode(submissionsStatus).WithHeader("Content-Type", "application/json").WithBody("""{"count":0,"results":[]}"""));
        Server.Given(Request.Create().WithPath(AssetPath + "*").UsingGet()).AtPriority(100)
            .RespondWith(Response.Create().WithStatusCode(401).WithBody("""{"detail":"Invalid token."}"""));
        return this;
    }

    public KoboStub Respond(string path, int status, TimeSpan? delay = null)
    {
        Server.Reset();
        var response = Response.Create().WithStatusCode(status).WithHeader("Location", "https://elsewhere.example.org/");
        if (delay is not null)
        {
            response = response.WithDelay(delay.Value);
        }

        Server.Given(Request.Create().WithPath(path).UsingGet()).RespondWith(response);
        return this;
    }

    public void Dispose() => Server.Stop();
}
