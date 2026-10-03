using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Pipeline;

[Collection(PostgresCollection.Name)]
public sealed class RunsApiTests(PostgresFixture postgres) : IDisposable
{
    private readonly KoboStub _kobo = new KoboStub().WithForm();

    private static string Runs(IngestionHarness h) => $"/api/v1/projects/{h.ProjectId}/runs";

    private static string Settings(IngestionHarness h) => $"/api/v1/projects/{h.ProjectId}/pipeline";

    private static async Task<HttpClient> MemberAsync(IngestionHarness harness, string subject, string role)
    {
        var userId = await TestData.SignInAsync(harness.Factory, subject, $"{subject}@example.org");
        await TestData.AddMemberAsync(harness.Db, harness.ProjectId, userId, role);
        return TestData.ClientFor(harness.Factory, subject, $"{subject}@example.org");
    }

    private static Task<long> RunCountAsync(IngestionHarness h, string where = "true") =>
        TestData.ScalarAsync<long>(h.Db, $"SELECT count(*) FROM tamiza.runs WHERE {where}");

    private static async Task WaitUntilAsync(IngestionHarness h, string condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!await TestData.ScalarAsync<bool>(h.Db, condition))
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for: {condition}");
            await Task.Delay(50);
        }
    }

    // 6.2 — starting runs, the list and details

    [Fact]
    public async Task An_analyst_queues_a_run_and_a_second_waits_only_behind_a_running_one()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var analyst = await MemberAsync(h, "ana", "analyst");
        using var viewer = await MemberAsync(h, "vera", "viewer");

        var first = await analyst.PostAsync(Runs(h), null);
        var duplicate = await h.Admin.PostAsync(Runs(h), null);
        var forbidden = await viewer.PostAsync(Runs(h), null);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var run = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("manual", run.GetProperty("trigger").GetString());
        Assert.Equal("queued", run.GetProperty("status").GetString());
        Assert.Equal("ana", run.GetProperty("requestedBy").GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("run.already_queued", (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // Once the worker starts it, one more may wait behind it.
        await TestData.ExecuteAsync(h.Db, "UPDATE tamiza.runs SET status = 'running', started_at = now()");
        Assert.Equal(HttpStatusCode.Accepted, (await analyst.PostAsync(Runs(h), null)).StatusCode);
        Assert.Equal(2L, await RunCountAsync(h));
    }

    [Fact]
    public async Task The_list_pages_newest_first_and_details_show_each_step()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var viewer = await MemberAsync(h, "vera", "viewer");
        await TestData.ExecuteAsync(h.Db, """
            INSERT INTO tamiza.runs (id, project_id, trigger, status, queued_at, started_at, finished_at)
            SELECT gen_random_uuid(), @p, 'schedule', 'succeeded', t, t, t + interval '1 minute'
            FROM generate_series(1, 25) AS n, LATERAL (SELECT timestamptz '2026-10-01T00:00:00Z' + n * interval '1 hour' AS t) AS x
            """, ("p", h.ProjectId));
        var runId = Guid.CreateVersion7();
        await TestData.ExecuteAsync(h.Db, """
            INSERT INTO tamiza.runs (id, project_id, trigger, status, queued_at, started_at, finished_at, error_code, error)
            VALUES (@id, @p, 'manual', 'failed', '2026-10-05T00:00:00Z', '2026-10-05T00:00:00Z', '2026-10-05T00:01:00Z', 'step.clean', 'KeyError: ''age''');
            INSERT INTO tamiza.run_steps (run_id, step, status, input_rows, output_rows, tables, log, error) VALUES
                (@id, 'flatten', 'succeeded', 3, 9, '[{"name":"main","rows":3},{"name":"members","rows":6}]', 'Flattened 3 submissions', NULL),
                (@id, 'clean', 'failed', NULL, NULL, '[]', 'INFO starting', 'KeyError: ''age'''),
                (@id, 'output', 'skipped', NULL, NULL, '[]', 'Not run: the clean step failed.', NULL),
                (@id, 'indicators', 'skipped', NULL, NULL, '[]', 'Not run: the clean step failed.', NULL);
            """, ("id", runId), ("p", h.ProjectId));

        var page = await viewer.GetFromJsonAsync<JsonElement>($"{Runs(h)}?page=1&pageSize=10");
        var details = await viewer.GetFromJsonAsync<JsonElement>($"{Runs(h)}/{runId}");
        var missing = await viewer.GetAsync($"{Runs(h)}/{Guid.CreateVersion7()}");

        Assert.Equal(26, page.GetProperty("total").GetInt32());
        Assert.Equal(10, page.GetProperty("items").GetArrayLength());
        var newest = page.GetProperty("items")[0];
        Assert.Equal(runId.ToString(), newest.GetProperty("id").GetString());
        Assert.Equal("step.clean", newest.GetProperty("errorCode").GetString());
        Assert.Equal(["succeeded", "failed", "skipped", "skipped"], newest.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("status").GetString()));
        var steps = details.GetProperty("steps");
        Assert.Equal(["flatten", "clean", "output", "indicators"], steps.EnumerateArray().Select(s => s.GetProperty("step").GetString()));
        Assert.Equal(6, steps[0].GetProperty("tables")[1].GetProperty("rows").GetInt64());
        Assert.Equal("KeyError: 'age'", steps[1].GetProperty("error").GetString());
        Assert.Equal("INFO starting", steps[1].GetProperty("log").GetString());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    // 6.3 — settings and triggers

    [Fact]
    public async Task Settings_default_to_running_after_sync_without_a_schedule_and_only_admins_change_them()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var analyst = await MemberAsync(h, "ana", "analyst");
        await TestData.ExecuteAsync(h.Db, """
            INSERT INTO tamiza.runs (id, project_id, trigger, status, queued_at, started_at)
            VALUES (gen_random_uuid(), @p, 'manual', 'succeeded', '2026-10-01T08:00:00Z', '2026-10-01T08:00:00Z')
            """, ("p", h.ProjectId));

        var defaults = await analyst.GetFromJsonAsync<JsonElement>(Settings(h));
        var forbidden = await analyst.PutAsJsonAsync(Settings(h), new { runAfterSync = false, runIntervalHours = 24 });
        var invalid = await h.Admin.PutAsJsonAsync(Settings(h), new { runAfterSync = true, runIntervalHours = 200 });
        var incomplete = await h.Admin.PutAsJsonAsync(Settings(h), new { runIntervalHours = 24 });
        var saved = await h.Admin.PutAsJsonAsync(Settings(h), new { runAfterSync = false, runIntervalHours = 24 });
        var cleared = await h.Admin.PutAsJsonAsync(Settings(h), new { runAfterSync = true, runIntervalHours = (int?)null });

        Assert.True(defaults.GetProperty("runAfterSync").GetBoolean());
        Assert.Equal(JsonValueKind.Null, defaults.GetProperty("runIntervalHours").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("1 to 168", (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetProperty("runIntervalHours")[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, incomplete.StatusCode);
        var body = await saved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("runAfterSync").GetBoolean());
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.Zero), body.GetProperty("nextRunAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, (await cleared.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("nextRunAt").ValueKind);
    }

    [Fact]
    public async Task A_synchronization_with_changes_queues_a_run_and_one_without_changes_does_not()
    {
        _kobo.WithSubmissions(KoboStub.MakeSubmissions(3));
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo, scheduler: true);
        await h.WaitForIdleAsync();
        await WaitUntilAsync(h, "SELECT count(*) = 1 FROM tamiza.runs WHERE trigger = 'sync'");

        // The worker would have run it; nothing changed since.
        await TestData.ExecuteAsync(h.Db, "UPDATE tamiza.runs SET status = 'succeeded', started_at = now(), finished_at = now()");
        await h.SyncAsync("full");
        await Task.Delay(300);

        Assert.Equal(1L, await RunCountAsync(h));
    }

    [Fact]
    public async Task No_run_is_queued_when_the_setting_is_off_or_for_a_webhook()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo, scheduler: true);
        await h.WaitForIdleAsync();
        (await h.Admin.PutAsJsonAsync(Settings(h), new { runAfterSync = false, runIntervalHours = (int?)null })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await h.DeliverAsync("""{"_id": 77, "_uuid": "w-77"}""")).StatusCode);
        _kobo.WithSubmissions(KoboStub.MakeSubmissions(2));
        var sync = await h.SyncAsync("full");
        await Task.Delay(300);

        Assert.Equal(2, sync.GetProperty("createdCount").GetInt32());
        Assert.Equal(0L, await RunCountAsync(h));
    }

    [Fact]
    public async Task A_due_schedule_queues_a_scheduled_run()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo, scheduler: true);
        await h.WaitForIdleAsync();
        await TestData.ExecuteAsync(h.Db, "UPDATE tamiza.projects SET run_interval_hours = 24, next_run_at = now() - interval '1 minute'");

        await WaitUntilAsync(h, "SELECT count(*) = 1 FROM tamiza.runs WHERE trigger = 'schedule' AND status = 'queued'");
        await Task.Delay(500);

        Assert.Equal(1L, await RunCountAsync(h, "trigger = 'schedule'"));
    }

    // 6.4 — OpenAPI

    [Fact]
    public async Task The_openapi_document_lists_the_pipeline_operations()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var client = h.Factory.CreateClient();

        var document = await client.GetFromJsonAsync<JsonElement>("/api/v1/openapi.json");
        var operations = document.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Where(op => op.Value.ValueKind == JsonValueKind.Object && op.Value.TryGetProperty("operationId", out _))
            .Select(op => op.Value.GetProperty("operationId").GetString())
            .ToHashSet();

        Assert.Superset(new HashSet<string?>
        {
            "ListScripts", "SaveScript", "RemoveScript", "ListScriptVersions", "GetScriptVersion", "RestoreScriptVersion",
            "StartRun", "ListRuns", "GetRun", "GetPipelineSettings", "UpdatePipelineSettings",
        }, operations);
    }

    public void Dispose() => _kobo.Dispose();
}
