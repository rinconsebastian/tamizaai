using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Tamiza.Api.IntegrationTests.Infrastructure;

/// <summary>
/// An API with one project created through the API against <see cref="KoboStub"/>, signed in as its admin.
/// Only admins of a project and superadmins can create projects, and there is no project yet, so "ada" is
/// the superadmin by default (<c>TAMIZA_SUPERADMIN_EMAILS</c>); <c>settings</c> can override it.
/// </summary>
public sealed class IngestionHarness : IAsyncDisposable
{
    private IngestionHarness(TamizaApiFactory factory, string db, HttpClient admin, Guid projectId, string secret)
    {
        Factory = factory;
        Db = db;
        Admin = admin;
        ProjectId = projectId;
        Secret = secret;
    }

    public TamizaApiFactory Factory { get; }

    public string Db { get; }

    public HttpClient Admin { get; }

    public Guid ProjectId { get; }

    public string Secret { get; }

    public string WebhookPath => $"/api/v1/webhooks/kobo/{ProjectId}";

    public string IngestionPath => $"/api/v1/projects/{ProjectId}/ingestion";

    public string SyncsPath => $"/api/v1/projects/{ProjectId}/syncs";

    /// <summary>
    /// Creates the project. With <paramref name="scheduler"/> the background scheduler runs with a short tick, so the
    /// project's initial synchronization starts right after creation.
    /// </summary>
    public static async Task<IngestionHarness> CreateAsync(
        PostgresFixture postgres, KoboStub kobo, bool scheduler = false, Dictionary<string, string?>? settings = null,
        Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? configureServices = null)
    {
        var db = await postgres.CreateDatabaseAsync();
        var all = new Dictionary<string, string?>
        {
            ["TAMIZA_KOBO_ALLOW_PRIVATE_NETWORKS"] = "true",
            ["Ingestion:SchedulerEnabled"] = scheduler ? "true" : "false",
            ["Ingestion:Tick"] = "00:00:00.200",
            ["TAMIZA_SUPERADMIN_EMAILS"] = "ada@example.org",
        };
        foreach (var (key, value) in settings ?? new())
        {
            all[key] = value;
        }

        var factory = new TamizaApiFactory(db, all, configureServices);
        var admin = TestData.ClientFor(factory, "ada", "ada@example.org");
        var response = await admin.PostAsJsonAsync("/api/v1/projects",
            new { name = "Household survey 2026", koboServerUrl = kobo.Url, assetUid = KoboStub.AssetUid, apiToken = KoboStub.Token });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new IngestionHarness(factory, db, admin,
            Guid.Parse(created.GetProperty("project").GetProperty("id").GetString()!),
            created.GetProperty("webhook").GetProperty("secret").GetString()!);
    }

    /// <summary>Posts a body to the webhook with the given credentials (defaults: <c>tamiza</c> and the project's secret).</summary>
    public async Task<HttpResponseMessage> DeliverAsync(
        string body, string? username = "tamiza", string? password = null, string contentType = "application/json", Guid? projectId = null)
    {
        using var client = Factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, projectId is null ? WebhookPath : $"/api/v1/webhooks/kobo/{projectId}")
        {
            Content = new StringContent(body, Encoding.UTF8, contentType),
        };
        if (username is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password ?? Secret}")));
        }

        return await client.SendAsync(request);
    }

    public Task<long> CountAsync(string where = "true") =>
        TestData.ScalarAsync<long>(Db, $"SELECT count(*) FROM tamiza.raw_submissions WHERE {where}");

    /// <summary>
    /// Waits until the project has at least <paramref name="runs"/> recorded runs and none is queued or running, and
    /// returns the latest one as the API lists it.
    /// </summary>
    public async Task<JsonElement> WaitForIdleAsync(int runs = 1, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (DateTime.UtcNow < deadline)
        {
            // One statement, so a run queued between two queries cannot make the project look idle.
            var idle = await TestData.ScalarAsync<bool>(Db,
                """
                SELECT count(*) FILTER (WHERE status IN ('queued', 'running')) = 0 AND count(*) >= @runs
                FROM tamiza.syncs WHERE project_id = @p
                """, ("p", ProjectId), ("runs", (long)runs));
            if (idle)
            {
                var page = await Admin.GetFromJsonAsync<JsonElement>(SyncsPath);
                return page.GetProperty("items")[0];
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("The project's synchronization did not finish in time.");
    }

    /// <summary>Starts a manual synchronization as the admin and returns the response.</summary>
    public Task<HttpResponseMessage> StartSyncAsync(string kind = "incremental") =>
        Admin.PostAsJsonAsync(SyncsPath, new { kind });

    /// <summary>Starts a manual synchronization and waits for it to end.</summary>
    public async Task<JsonElement> SyncAsync(string kind = "incremental")
    {
        var runs = await TestData.ScalarAsync<long>(Db, "SELECT count(*) FROM tamiza.syncs WHERE project_id = @p", ("p", ProjectId));
        var response = await StartSyncAsync(kind);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return await WaitForIdleAsync((int)runs + 1);
    }

    /// <summary>Kobo's text form of a UTC time: no offset.</summary>
    public static string KoboTime(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss");

    public async ValueTask DisposeAsync()
    {
        Admin.Dispose();
        await Factory.DisposeAsync();
    }
}
