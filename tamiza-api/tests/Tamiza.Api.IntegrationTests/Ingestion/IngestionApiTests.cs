using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Ingestion;

/// <summary>The ingestion endpoints with the scheduler off, so queued runs stay queued.</summary>
[Collection(PostgresCollection.Name)]
public sealed class IngestionApiTests(PostgresFixture postgres) : IDisposable
{
    private readonly KoboStub _kobo = new KoboStub().WithForm();

    private static async Task<HttpClient> MemberAsync(IngestionHarness harness, string subject, string role)
    {
        var userId = await TestData.SignInAsync(harness.Factory, subject, $"{subject}@example.org");
        await TestData.AddMemberAsync(harness.Db, harness.ProjectId, userId, role);
        return TestData.ClientFor(harness.Factory, subject, $"{subject}@example.org");
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    // 6.1 — status and settings

    [Fact]
    public async Task Status_counts_submissions_and_shows_the_last_webhook_and_the_active_run()
    {
        await using var harness = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var analyst = await MemberAsync(harness, "ana", "analyst");

        var empty = await analyst.GetFromJsonAsync<JsonElement>(harness.IngestionPath);
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("lastWebhookAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("lastSync").ValueKind);
        Assert.Equal(12, empty.GetProperty("syncIntervalHours").GetInt32());
        Assert.Equal(24, empty.GetProperty("reconcileIntervalHours").GetInt32());

        await harness.DeliverAsync("""{"_id": 1, "_uuid": "a"}""");
        await harness.DeliverAsync("""{"_id": 2, "_uuid": "b"}""");
        await TestData.ExecuteAsync(harness.Db, "UPDATE tamiza.raw_submissions SET deleted = true WHERE kobo_id = 2");
        await harness.StartSyncAsync();

        var status = await analyst.GetFromJsonAsync<JsonElement>(harness.IngestionPath);
        Assert.Equal(1, status.GetProperty("submissionCount").GetInt64());
        Assert.Equal(1, status.GetProperty("deletedCount").GetInt64());
        Assert.Equal(JsonValueKind.String, status.GetProperty("lastWebhookAt").ValueKind);
        Assert.Equal("queued", status.GetProperty("activeSync").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Only_admins_change_the_schedule()
    {
        await using var harness = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var analyst = await MemberAsync(harness, "ana", "analyst");

        var response = await analyst.PatchAsJsonAsync(harness.IngestionPath, new { syncIntervalHours = 6 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(12, await TestData.ScalarAsync<int>(harness.Db, "SELECT sync_interval_hours FROM tamiza.projects"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(200)]
    public async Task Intervals_outside_one_to_168_hours_are_rejected(int hours)
    {
        await using var harness = await IngestionHarness.CreateAsync(postgres, _kobo);

        var response = await harness.Admin.PatchAsJsonAsync(harness.IngestionPath, new { syncIntervalHours = hours, reconcileIntervalHours = hours });
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("1 to 168", errors.GetProperty("syncIntervalHours")[0].GetString());
        Assert.True(errors.TryGetProperty("reconcileIntervalHours", out _));
        Assert.Equal(12, await TestData.ScalarAsync<int>(harness.Db, "SELECT sync_interval_hours FROM tamiza.projects"));
    }

    [Fact]
    public async Task A_new_interval_counts_from_the_last_run_of_its_kind()
    {
        await using var harness = await IngestionHarness.CreateAsync(postgres, _kobo);
        await TestData.ExecuteAsync(harness.Db, """
            INSERT INTO tamiza.syncs (id, project_id, kind, trigger, status, queued_at, started_at, finished_at)
            VALUES (gen_random_uuid(), @p, 'incremental', 'schedule', 'succeeded', '2026-10-01T08:00:00Z', '2026-10-01T08:00:00Z', '2026-10-01T08:01:00Z')
            """, ("p", harness.ProjectId));

        var response = await harness.Admin.PatchAsJsonAsync(harness.IngestionPath, new { syncIntervalHours = 6 });
        var status = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(6, status.GetProperty("syncIntervalHours").GetInt32());
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 14, 0, 0, TimeSpan.Zero), status.GetProperty("nextSyncAt").GetDateTimeOffset());
        // Unchanged: no reconciliation interval was sent.
        Assert.Equal(24, status.GetProperty("reconcileIntervalHours").GetInt32());
    }

    // 6.2 — starting runs and the log

    [Fact]
    public async Task Viewers_cannot_start_runs_or_read_the_status_and_the_log_and_non_members_get_404()
    {
        await using var harness = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var viewer = await MemberAsync(harness, "vera", "viewer");
        using var stranger = TestData.ClientFor(harness.Factory, "sam", "sam@example.org");

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync(harness.SyncsPath, new { kind = "incremental" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(harness.SyncsPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(harness.IngestionPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(harness.SyncsPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(harness.IngestionPath)).StatusCode);
        Assert.Equal(0L, await TestData.ScalarAsync<long>(harness.Db, "SELECT count(*) FROM tamiza.syncs"));
    }

    [Fact]
    public async Task An_analyst_queues_a_run_and_a_second_request_while_it_is_active_gets_409()
    {
        await using var harness = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var analyst = await MemberAsync(harness, "ana", "analyst");

        var first = await analyst.PostAsJsonAsync(harness.SyncsPath, new { kind = "full" });
        var second = await harness.Admin.PostAsJsonAsync(harness.SyncsPath, new { kind = "incremental" });

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var queued = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("full", queued.GetProperty("kind").GetString());
        Assert.Equal("manual", queued.GetProperty("trigger").GetString());
        Assert.Equal("queued", queued.GetProperty("status").GetString());
        Assert.Equal("ana", queued.GetProperty("requestedBy").GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("sync.in_progress", await CodeAsync(second));
        Assert.Equal(1L, await TestData.ScalarAsync<long>(harness.Db, "SELECT count(*) FROM tamiza.syncs"));
    }

    [Fact]
    public async Task Reconciliations_cannot_be_started_by_hand()
    {
        await using var harness = await IngestionHarness.CreateAsync(postgres, _kobo);

        var response = await harness.Admin.PostAsJsonAsync(harness.SyncsPath, new { kind = "reconciliation" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_log_is_paged_newest_first()
    {
        await using var harness = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var analyst = await MemberAsync(harness, "ana", "analyst");
        await TestData.ExecuteAsync(harness.Db, """
            INSERT INTO tamiza.syncs (id, project_id, kind, trigger, status, queued_at, started_at, finished_at, created_count)
            SELECT gen_random_uuid(), @p, 'incremental', 'schedule', 'succeeded', t, t, t + interval '1 minute', n
            FROM generate_series(1, 25) AS n, LATERAL (SELECT timestamptz '2026-10-01T00:00:00Z' + n * interval '1 hour' AS t) AS times
            """, ("p", harness.ProjectId));

        var page2 = await analyst.GetFromJsonAsync<JsonElement>($"{harness.SyncsPath}?page=2&pageSize=10");
        var page3 = await analyst.GetFromJsonAsync<JsonElement>($"{harness.SyncsPath}?page=3&pageSize=10");
        var defaults = await analyst.GetFromJsonAsync<JsonElement>(harness.SyncsPath);

        Assert.Equal(25, page2.GetProperty("total").GetInt32());
        Assert.Equal(Enumerable.Range(6, 10).Reverse(), page2.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("createdCount").GetInt32()));
        Assert.Equal(5, page3.GetProperty("items").GetArrayLength());
        Assert.Equal(20, defaults.GetProperty("items").GetArrayLength());
        Assert.Equal(25, defaults.GetProperty("items")[0].GetProperty("createdCount").GetInt32());
    }

    // 6.3 — OpenAPI

    [Fact]
    public async Task The_openapi_document_lists_the_ingestion_operations()
    {
        await using var harness = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var client = harness.Factory.CreateClient();

        var document = await client.GetFromJsonAsync<JsonElement>("/api/v1/openapi.json");
        var operations = document.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Where(op => op.Value.ValueKind == JsonValueKind.Object && op.Value.TryGetProperty("operationId", out _))
            .Select(op => op.Value.GetProperty("operationId").GetString())
            .ToHashSet();

        Assert.Superset(new HashSet<string?> { "ReceiveKoboSubmission", "GetIngestionStatus", "UpdateIngestionSettings", "StartSync", "ListSyncs" }, operations);
        Assert.True(document.GetProperty("components").GetProperty("schemas").TryGetProperty("IngestionStatus", out _));
        Assert.True(document.GetProperty("components").GetProperty("schemas").TryGetProperty("SyncPage", out _));
    }

    public void Dispose() => _kobo.Dispose();
}
