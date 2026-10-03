using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Tamiza.Api.IntegrationTests.Infrastructure;
using Tamiza.DbUp;

namespace Tamiza.Api.IntegrationTests.Ingestion;

/// <summary>Synchronization end to end: the scheduler, the runner, KoboStub as the Kobo server and the database.</summary>
[Collection(PostgresCollection.Name)]
public sealed class SyncTests(PostgresFixture postgres) : IDisposable
{
    private static readonly Dictionary<string, string?> SmallPages = new() { ["Ingestion:PageSize"] = "3", ["Ingestion:IdPageSize"] = "2" };

    private readonly KoboStub _kobo = new KoboStub().WithForm();

    private static string Recent(int minutesAgo = 0) => IngestionHarness.KoboTime(DateTimeOffset.UtcNow.AddMinutes(-minutesAgo));

    private Task<IngestionHarness> HarnessAsync(Dictionary<string, string?>? settings = null) =>
        IngestionHarness.CreateAsync(postgres, _kobo, scheduler: true, settings ?? SmallPages);

    private static string Text(JsonElement element, string property) => element.GetProperty(property).GetString()!;

    private static int Int(JsonElement element, string property) => element.GetProperty(property).GetInt32();

    private int PagesRead(int fromRequest = 0) => _kobo.DataRequests.Skip(fromRequest).Count(r => r.Limit != 1);

    // 5.1 / 5.4 — initial backfill

    [Fact]
    public async Task Creating_a_project_back_fills_every_existing_submission_with_a_full_initial_run()
    {
        _kobo.WithSubmissions(KoboStub.MakeSubmissions(7, firstId: 100));
        await using var harness = await HarnessAsync();

        var run = await harness.WaitForIdleAsync();

        Assert.Equal("succeeded", Text(run, "status"));
        Assert.Equal("full", Text(run, "kind"));
        Assert.Equal("initial", Text(run, "trigger"));
        Assert.Equal(7, Int(run, "createdCount"));
        Assert.Equal(3, PagesRead());
        Assert.Equal(7, await harness.CountAsync("origin = 'sync' AND NOT deleted"));
    }

    [Fact]
    public async Task An_incremental_run_reads_from_the_last_successful_start_minus_the_margin_and_stores_missed_submissions()
    {
        _kobo.WithSubmissions([KoboStub.Submission(1, Recent(10))]);
        await using var harness = await HarnessAsync();
        await harness.WaitForIdleAsync();
        var initialStart = await TestData.ScalarAsync<DateTime>(harness.Db, "SELECT started_at FROM tamiza.syncs");
        // Submitted in Kobo, but its webhook never arrived.
        _kobo.Submissions.Add(KoboStub.Submission(2, Recent()));
        var before = _kobo.DataRequests.Count;

        var run = await harness.SyncAsync();

        Assert.Equal("succeeded", Text(run, "status"));
        Assert.Equal("incremental", Text(run, "kind"));
        Assert.Equal("manual", Text(run, "trigger"));
        Assert.Equal("ada", run.GetProperty("requestedBy").GetProperty("name").GetString());
        Assert.Equal(IngestionHarness.KoboTime(new DateTimeOffset(initialStart, TimeSpan.Zero).AddHours(-1)), _kobo.DataRequests[before].SubmittedSince);
        Assert.Equal(1, await harness.CountAsync("kobo_id = 2 AND origin = 'sync'"));
    }

    [Fact]
    public async Task Counts_separate_new_changed_and_identical_submissions()
    {
        _kobo.WithSubmissions(KoboStub.MakeSubmissions(6, time: Recent(5)));
        await using var harness = await HarnessAsync();
        await harness.WaitForIdleAsync();
        _kobo.Submissions[0]["municipality"] = "palmira";
        _kobo.Submissions[1]["municipality"] = "buga";
        _kobo.Submissions.AddRange(KoboStub.MakeSubmissions(4, firstId: 50, time: Recent()));

        var run = await harness.SyncAsync();

        Assert.Equal(4, Int(run, "createdCount"));
        Assert.Equal(2, Int(run, "updatedCount"));
        Assert.Equal(0, Int(run, "deletedCount"));
        Assert.True(run.TryGetProperty("finishedAt", out var finished) && finished.ValueKind == JsonValueKind.String);
    }

    [Fact]
    public async Task A_failure_halfway_keeps_the_stored_pages_and_the_next_run_reads_everything_again()
    {
        _kobo.WithSubmissions(KoboStub.MakeSubmissions(12, firstId: 1));
        _kobo.DataFailure = request => request.AfterId >= 9 ? 503 : null;
        await using var harness = await HarnessAsync();

        var failed = await harness.WaitForIdleAsync();

        Assert.Equal("failed", Text(failed, "status"));
        Assert.Equal("kobo.unexpected_response", Text(failed, "errorCode"));
        Assert.Equal(9, Int(failed, "createdCount"));
        Assert.Equal(9, await harness.CountAsync());

        _kobo.DataFailure = null;
        var retried = await harness.SyncAsync();

        // No successful run yet, so there is no cutoff: the manual run reads everything.
        Assert.Equal("full", Text(retried, "kind"));
        Assert.Equal("succeeded", Text(retried, "status"));
        Assert.Equal(3, Int(retried, "createdCount"));
        Assert.Equal(12, await harness.CountAsync());
    }

    [Fact]
    public async Task A_rejected_token_fails_the_run_with_the_reason()
    {
        await using var harness = await HarnessAsync();
        await harness.WaitForIdleAsync();
        _kobo.WithForm(token: "the-token-was-revoked");

        var run = await harness.SyncAsync();

        Assert.Equal("failed", Text(run, "status"));
        Assert.Equal("kobo.unauthorized", Text(run, "errorCode"));
        Assert.Contains("token", Text(run, "error"));
    }

    [Fact]
    public async Task An_unreadable_stored_token_fails_the_run()
    {
        await using var harness = await HarnessAsync();
        await harness.WaitForIdleAsync();
        await TestData.ExecuteAsync(harness.Db, "UPDATE tamiza.projects SET encrypted_api_token = 'not-a-protected-value'");

        var run = await harness.SyncAsync();

        Assert.Equal("kobo.token_unreadable", Text(run, "errorCode"));
    }

    [Fact]
    public async Task A_run_refreshes_the_form_snapshot_and_field_check()
    {
        var withoutTimes = KoboStub.FormJson
            .Replace("""{ "type": "start", "name": "start", "$xpath": "start" },""", "")
            .Replace("""{ "type": "end", "name": "end", "$xpath": "end" },""", "");
        _kobo.WithForm(withoutTimes);
        await using var harness = await HarnessAsync();
        await harness.WaitForIdleAsync();
        Assert.False(await CompletionTimeAvailableAsync(harness));

        _kobo.WithForm(); // redeployed with start and end
        await harness.SyncAsync();

        Assert.True(await CompletionTimeAvailableAsync(harness));
    }

    private static async Task<bool> CompletionTimeAvailableAsync(IngestionHarness harness)
    {
        var project = await harness.Admin.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{harness.ProjectId}");
        return project.GetProperty("fieldCheck").EnumerateArray()
            .Single(m => m.GetProperty("metric").GetString() == "completionTime").GetProperty("available").GetBoolean();
    }

    // 5.2 — deletions

    [Fact]
    public async Task A_full_run_marks_submissions_deleted_in_kobo_and_updates_edited_ones()
    {
        _kobo.WithSubmissions(KoboStub.MakeSubmissions(4));
        await using var harness = await HarnessAsync();
        await harness.WaitForIdleAsync();
        _kobo.Submissions.RemoveAll(s => s["_id"]!.GetValue<long>() == 2);
        // Edited in Kobo: same _id, new _uuid, and an old submission time an incremental run would not read.
        _kobo.Submissions[0]["_uuid"] = "edited";
        _kobo.Submissions[0]["municipality"] = "palmira";

        var run = await harness.SyncAsync("full");

        Assert.Equal("succeeded", Text(run, "status"));
        Assert.Equal(1, Int(run, "deletedCount"));
        Assert.Equal(1, Int(run, "updatedCount"));
        Assert.Equal(1, await harness.CountAsync("kobo_id = 2 AND deleted AND deleted_at IS NOT NULL"));
        Assert.Equal(1, await harness.CountAsync("kobo_id = 1 AND kobo_uuid = 'edited' AND data->>'municipality' = 'palmira'"));
    }

    [Fact]
    public async Task A_scheduled_reconciliation_marks_deletions_from_ids_only()
    {
        _kobo.WithSubmissions(KoboStub.MakeSubmissions(5));
        await using var harness = await HarnessAsync();
        await harness.WaitForIdleAsync();
        _kobo.Submissions.RemoveAll(s => s["_id"]!.GetValue<long>() is 1 or 4);
        var before = _kobo.DataRequests.Count;

        await TestData.ExecuteAsync(harness.Db, "UPDATE tamiza.projects SET next_reconcile_at = now() - interval '1 minute'");
        var run = await harness.WaitForIdleAsync(runs: 2);

        Assert.Equal("reconciliation", Text(run, "kind"));
        Assert.Equal("schedule", Text(run, "trigger"));
        Assert.Equal(2, Int(run, "deletedCount"));
        Assert.All(_kobo.DataRequests.Skip(before), r => Assert.True(r.IdsOnly));
        Assert.Equal(2, await harness.CountAsync("deleted"));
        var status = await harness.Admin.GetFromJsonAsync<JsonElement>(harness.IngestionPath);
        Assert.Equal(3, status.GetProperty("submissionCount").GetInt64());
        Assert.Equal(2, status.GetProperty("deletedCount").GetInt64());
    }

    [Fact]
    public async Task An_incomplete_listing_marks_nothing_and_fails_with_the_reason()
    {
        _kobo.WithSubmissions(KoboStub.MakeSubmissions(5));
        await using var harness = await HarnessAsync();
        await harness.WaitForIdleAsync();
        _kobo.Submissions.RemoveAt(0);
        // The first page of IDs arrives; the second fails.
        _kobo.DataFailure = request => request.IdsOnly && request.AfterId is not null ? 502 : null;

        await TestData.ExecuteAsync(harness.Db, "UPDATE tamiza.projects SET next_reconcile_at = now() - interval '1 minute'");
        var run = await harness.WaitForIdleAsync(runs: 2);

        Assert.Equal("failed", Text(run, "status"));
        Assert.Equal("kobo.unexpected_response", Text(run, "errorCode"));
        Assert.Equal(0, await harness.CountAsync("deleted"));
    }

    [Fact]
    public async Task A_form_emptied_in_kobo_marks_every_submission_deleted()
    {
        _kobo.WithSubmissions(KoboStub.MakeSubmissions(3));
        await using var harness = await HarnessAsync();
        await harness.WaitForIdleAsync();
        _kobo.Submissions.Clear();

        var run = await harness.SyncAsync("full");

        Assert.Equal(3, Int(run, "deletedCount"));
        Assert.Equal(3, await harness.CountAsync("deleted"));
    }

    [Fact]
    public async Task A_submission_received_by_webhook_during_a_full_run_is_not_marked_deleted()
    {
        _kobo.WithSubmissions(KoboStub.MakeSubmissions(2));
        await using var harness = await HarnessAsync();
        await harness.WaitForIdleAsync();
        _kobo.DataDelay = TimeSpan.FromMilliseconds(700);

        Assert.Equal(System.Net.HttpStatusCode.Accepted, (await harness.StartSyncAsync("full")).StatusCode);
        await WaitUntilAsync(harness.Db, "SELECT count(*) = 1 FROM tamiza.syncs WHERE status = 'running'");
        // Kobo's listing already passed this _id's position (or never had it yet).
        await harness.DeliverAsync("""{"_id": 999, "_uuid": "late", "_submission_time": "2026-10-01T00:00:00"}""");
        var run = await harness.WaitForIdleAsync(runs: 2);

        Assert.Equal("succeeded", Text(run, "status"));
        Assert.Equal(0, Int(run, "deletedCount"));
        Assert.Equal(1, await harness.CountAsync("kobo_id = 999 AND NOT deleted"));
    }

    // 5.3 — scheduling

    [Fact]
    public async Task A_run_sets_the_next_due_time_and_a_due_project_gets_a_scheduled_run()
    {
        await using var harness = await HarnessAsync();
        await harness.WaitForIdleAsync();

        Assert.True(await TestData.ScalarAsync<bool>(harness.Db, """
            SELECT p.next_sync_at = s.started_at + interval '12 hours' AND p.next_reconcile_at = s.started_at + interval '24 hours'
            FROM tamiza.projects p JOIN tamiza.syncs s ON s.project_id = p.id
            """));

        await TestData.ExecuteAsync(harness.Db, "UPDATE tamiza.projects SET next_sync_at = now() - interval '1 minute'");
        var run = await harness.WaitForIdleAsync(runs: 2);

        Assert.Equal("schedule", Text(run, "trigger"));
        Assert.Equal("incremental", Text(run, "kind"));
    }

    [Fact]
    public async Task A_failed_scheduled_run_is_retried_an_hour_later()
    {
        await using var harness = await HarnessAsync();
        await harness.WaitForIdleAsync();
        _kobo.DataFailure = _ => 503;

        await TestData.ExecuteAsync(harness.Db, "UPDATE tamiza.projects SET next_sync_at = now() - interval '1 minute'");
        var run = await harness.WaitForIdleAsync(runs: 2);

        Assert.Equal("failed", Text(run, "status"));
        Assert.True(await TestData.ScalarAsync<bool>(harness.Db, """
            SELECT p.next_sync_at = s.finished_at + interval '1 hour'
            FROM tamiza.projects p JOIN tamiza.syncs s ON s.project_id = p.id
            WHERE s.trigger = 'schedule'
            """));
    }

    [Fact]
    public async Task A_due_run_waits_for_the_active_one()
    {
        _kobo.WithSubmissions(KoboStub.MakeSubmissions(4));
        await using var harness = await HarnessAsync();
        await harness.WaitForIdleAsync();
        _kobo.DataDelay = TimeSpan.FromMilliseconds(400);
        await harness.StartSyncAsync("full");
        await WaitUntilAsync(harness.Db, "SELECT count(*) = 1 FROM tamiza.syncs WHERE status = 'running'");

        await TestData.ExecuteAsync(harness.Db, "UPDATE tamiza.projects SET next_sync_at = now() - interval '1 minute'");
        await Task.Delay(600);

        Assert.Equal(1L, await TestData.ScalarAsync<long>(harness.Db, "SELECT count(*) FROM tamiza.syncs WHERE status IN ('queued', 'running')"));
        var run = await harness.WaitForIdleAsync(runs: 3);
        Assert.Equal("schedule", Text(run, "trigger"));
    }

    [Fact]
    public async Task Projects_synchronize_concurrently()
    {
        _kobo.WithSubmissions(KoboStub.MakeSubmissions(4));
        _kobo.DataDelay = TimeSpan.FromMilliseconds(500);
        await using var harness = await HarnessAsync();
        var second = await harness.Admin.PostAsJsonAsync("/api/v1/projects",
            new { name = "Second", koboServerUrl = _kobo.Url, assetUid = KoboStub.AssetUid, apiToken = KoboStub.Token });
        second.EnsureSuccessStatusCode();

        await WaitUntilAsync(harness.Db, "SELECT count(*) = 2 FROM tamiza.syncs WHERE status = 'running'");

        await WaitUntilAsync(harness.Db, "SELECT count(*) = 2 FROM tamiza.syncs WHERE status = 'succeeded'", TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task A_run_left_running_by_a_previous_process_is_marked_interrupted_and_does_not_block()
    {
        var db = await postgres.CreateDatabaseAsync();
        await SchemaMigrator.MigrateAsync(db, NullLogger.Instance);
        var userId = Guid.CreateVersion7();
        await TestData.ExecuteAsync(db, "INSERT INTO tamiza.users (id, keycloak_sub, name, created_at, updated_at) VALUES (@id, 'other', 'Grace', now(), now())", ("id", userId));
        var projectId = await TestData.InsertProjectAsync(db, userId);
        await TestData.ExecuteAsync(db, """
            UPDATE tamiza.projects SET kobo_server_url = @url, next_sync_at = now() + interval '1 day', next_reconcile_at = now() + interval '1 day';
            INSERT INTO tamiza.syncs (id, project_id, kind, trigger, status, queued_at, started_at)
            VALUES (gen_random_uuid(), @project, 'incremental', 'schedule', 'running', now() - interval '5 minutes', now() - interval '5 minutes');
            """, ("url", _kobo.Url), ("project", projectId));

        await using var factory = new TamizaApiFactory(db, new Dictionary<string, string?>
        {
            ["TAMIZA_KOBO_ALLOW_PRIVATE_NETWORKS"] = "true",
            ["Ingestion:SchedulerEnabled"] = "true",
            ["Ingestion:Tick"] = "00:00:00.200",
        });
        var adminId = await TestData.SignInAsync(factory, "ada", "ada@example.org");
        await TestData.AddMemberAsync(db, projectId, adminId, "admin");

        await WaitUntilAsync(db, "SELECT count(*) = 1 FROM tamiza.syncs WHERE status = 'failed' AND error_code = 'sync.interrupted'");
        using var admin = TestData.ClientFor(factory, "ada", "ada@example.org");
        var response = await admin.PostAsJsonAsync($"/api/v1/projects/{projectId}/syncs", new { kind = "incremental" });

        Assert.Equal(System.Net.HttpStatusCode.Accepted, response.StatusCode);
        // The seeded token cannot be decrypted, so the run fails, but it runs: nothing blocks it.
        await WaitUntilAsync(db, "SELECT count(*) = 1 FROM tamiza.syncs WHERE status = 'failed' AND error_code = 'kobo.token_unreadable'");
    }

    private static async Task WaitUntilAsync(string db, string condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (!await TestData.ScalarAsync<bool>(db, condition))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for: {condition}");
            }

            await Task.Delay(50);
        }
    }

    public void Dispose() => _kobo.Dispose();
}
