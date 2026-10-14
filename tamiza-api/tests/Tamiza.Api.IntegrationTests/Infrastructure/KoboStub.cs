using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using WireMock;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using WireMock.Types;
using WireMock.Util;

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
            ],
            "choices": [
              { "list_name": "municipalities", "name": "cali", "label": ["Cali"] },
              { "list_name": "municipalities", "name": "palmira", "label": ["Palmira"] },
              { "list_name": "sex", "name": "f", "label": ["Female"] },
              { "list_name": "sex", "name": "m", "label": ["Male"] }
            ]
          }
        }
        """;

    public WireMockServer Server { get; } = WireMockServer.Start();

    public string Url => Server.Url!;

    public string AssetPath => $"/api/v2/assets/{AssetUid}/";

    public string DataPath => $"/api/v2/assets/{AssetUid}/data/";

    public string VersionsPath => $"{AssetPath}versions/";

    /// <summary>Serves the form and its submissions to requests carrying <paramref name="token"/>; anything else gets 401.</summary>
    public KoboStub WithForm(string formJson = FormJson, string token = Token, int submissionsStatus = 200)
    {
        Server.Reset();
        Server.Given(Request.Create().WithPath(AssetPath).UsingGet().WithHeader("Authorization", $"Token {token}"))
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(formJson));
        Server.Given(Request.Create().WithPath(DataPath).UsingGet().WithHeader("Authorization", $"Token {token}"))
            .RespondWith(Response.Create().WithCallback(request => DataResponse(request, submissionsStatus)));
        Server.Given(Request.Create().WithPath(VersionsPath + "*").UsingGet().WithHeader("Authorization", $"Token {token}"))
            .RespondWith(Response.Create().WithCallback(VersionResponse));
        Server.Given(Request.Create().WithPath(AssetPath + "*").UsingGet()).AtPriority(100)
            .RespondWith(Response.Create().WithStatusCode(401).WithBody("""{"detail":"Invalid token."}"""));
        return this;
    }

    /// <summary>
    /// The asset document of a form deployed as <paramref name="deployedVersionId"/>: <paramref name="formJson"/> with
    /// <c>version_id</c> (the latest version, a draft when it differs) and <c>deployed_version_id</c>. Without a deployed
    /// version the form is not deployed.
    /// </summary>
    public static string Deployed(string formJson, string? deployedVersionId, string? versionId = null)
    {
        var form = JsonNode.Parse(formJson)!.AsObject();
        form["version_id"] = versionId ?? deployedVersionId;
        form["deployed_version_id"] = deployedVersionId;
        return form.ToJsonString();
    }

    // Version definitions served at /versions/{uid}/; they survive WithForm, which only resets the mappings.
    private readonly Dictionary<string, (string FormJson, string? DeployedAt)> _versions = [];

    /// <summary>
    /// Serves the definition of a version: the <c>content</c> of <paramref name="formJson"/> and its deployment date
    /// (null for a draft). Versions not defined answer 404.
    /// </summary>
    public KoboStub WithVersion(string uid, string formJson = FormJson, string? deployedAt = "2026-09-01T10:00:00Z")
    {
        lock (_versions)
        {
            _versions[uid] = (formJson, deployedAt);
        }

        return this;
    }

    /// <summary>Returns a status code to fail a version request with, or null to answer it normally. Receives the version uid.</summary>
    public Func<string, int?>? VersionFailure { get; set; }

    /// <summary>Delay before each version response, to hold a fetch in flight.</summary>
    public TimeSpan VersionDelay { get; set; }

    private int _versionRequestsStarted;

    /// <summary>Version requests the stub has begun to answer, counted before any delay (the log only lists answered ones).</summary>
    public int VersionRequestsStarted => Volatile.Read(ref _versionRequestsStarted);

    /// <summary>The uids requested at <c>/versions/{uid}/</c>, in order, including requests that failed.</summary>
    public IReadOnlyList<string> VersionRequests() =>
        Server.LogEntries
            .Select(e => e.RequestMessage!.Path)
            .Where(path => path.StartsWith(VersionsPath, StringComparison.Ordinal))
            .Select(path => path[VersionsPath.Length..].TrimEnd('/'))
            .ToList();

    private ResponseMessage VersionResponse(IRequestMessage request)
    {
        var uid = request.Path[VersionsPath.Length..].TrimEnd('/');
        Interlocked.Increment(ref _versionRequestsStarted);
        if (VersionDelay > TimeSpan.Zero)
        {
            Thread.Sleep(VersionDelay);
        }

        if (VersionFailure?.Invoke(uid) is { } failure)
        {
            return Json(failure, """{"detail":"Failure injected by the test."}""");
        }

        (string FormJson, string? DeployedAt) version;
        lock (_versions)
        {
            if (!_versions.TryGetValue(uid, out version))
            {
                return Json(404, """{"detail":"Not found."}""");
            }
        }

        var document = new JsonObject
        {
            ["uid"] = uid,
            ["content_hash"] = $"hash-{uid}",
            ["date_modified"] = "2026-09-01T09:00:00Z",
            ["date_deployed"] = version.DeployedAt is null ? JsonValue.Create(false) : JsonValue.Create(version.DeployedAt),
            ["content"] = JsonNode.Parse(version.FormJson)!["content"]!.DeepClone(),
        };
        return Json(200, document.ToJsonString());
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

    /// <summary>The form's submissions, served by the data endpoint like Kobo's API v2. Tests may change it between runs.</summary>
    public List<JsonObject> Submissions { get; } = [];

    /// <summary>Every data request received, in order, with its parsed paging parameters.</summary>
    public List<DataRequest> DataRequests { get; } = [];

    /// <summary>Returns a status code to fail a data request with, or null to answer it normally.</summary>
    public Func<DataRequest, int?>? DataFailure { get; set; }

    /// <summary>Delay before each data response, to exercise page timeouts.</summary>
    public TimeSpan DataDelay { get; set; }

    public sealed record DataRequest(int Index, int Limit, string? SubmittedSince, long? AfterId, bool IdsOnly);

    public static JsonObject Submission(long id, string time = "2026-09-01T10:00:00", string? uuid = null, string municipality = "cali") => new()
    {
        ["_id"] = id,
        ["_uuid"] = uuid ?? $"uuid-{id}",
        ["__version__"] = "v1",
        ["_submission_time"] = time,
        ["municipality"] = municipality,
        ["sex"] = id % 2 == 0 ? "f" : "m",
    };

    public static IEnumerable<JsonObject> MakeSubmissions(int count, long firstId = 1, string time = "2026-09-01T10:00:00") =>
        Enumerable.Range(0, count).Select(i => Submission(firstId + i, time));

    public KoboStub WithSubmissions(IEnumerable<JsonObject> submissions)
    {
        lock (Submissions)
        {
            Submissions.Clear();
            Submissions.AddRange(submissions);
        }

        return this;
    }

    // Supports what KoboClient sends: limit, query with _submission_time $gte and _id $gt, fields=["_id"]; sorted by _id.
    private ResponseMessage DataResponse(IRequestMessage request, int status)
    {
        string? Param(string name) =>
            request.Query is not null && request.Query.TryGetValue(name, out var values) ? values.FirstOrDefault() : null;

        var query = JsonNode.Parse(Param("query") ?? "{}")!.AsObject();
        DataRequest data;
        lock (DataRequests)
        {
            data = new DataRequest(
                DataRequests.Count,
                int.Parse(Param("limit") ?? "100", CultureInfo.InvariantCulture),
                query["_submission_time"]?["$gte"]?.GetValue<string>(),
                query["_id"]?["$gt"]?.GetValue<long>(),
                Param("fields") is not null);
            DataRequests.Add(data);
        }

        if (DataDelay > TimeSpan.Zero)
        {
            Thread.Sleep(DataDelay);
        }

        if ((DataFailure?.Invoke(data) ?? (status == 200 ? null : status)) is { } failure)
        {
            return Json(failure, """{"detail":"Failure injected by the test."}""");
        }

        List<JsonObject> matching;
        lock (Submissions)
        {
            matching = Submissions
                .Where(s => data.SubmittedSince is null || string.CompareOrdinal(s["_submission_time"]!.GetValue<string>(), data.SubmittedSince) >= 0)
                .OrderBy(s => s["_id"]!.GetValue<long>())
                .ToList();
        }

        var results = new JsonArray(matching
            .Where(s => data.AfterId is null || s["_id"]!.GetValue<long>() > data.AfterId)
            .Take(data.Limit)
            .Select(s => (JsonNode)(data.IdsOnly ? new JsonObject { ["_id"] = s["_id"]!.GetValue<long>() } : s.DeepClone()))
            .ToArray());
        var page = new JsonObject { ["count"] = matching.Count, ["next"] = null, ["previous"] = null, ["results"] = results };
        return Json(200, page.ToJsonString());
    }

    private static ResponseMessage Json(int status, string body) => new()
    {
        StatusCode = status,
        Headers = new Dictionary<string, WireMockList<string>> { ["Content-Type"] = new("application/json") },
        BodyData = new BodyData { BodyAsString = body, DetectedBodyType = BodyType.String },
    };

    public void Dispose() => Server.Stop();
}
