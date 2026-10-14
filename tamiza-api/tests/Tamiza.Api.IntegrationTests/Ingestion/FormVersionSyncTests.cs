using System.Text.Json;
using System.Text.Json.Nodes;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Ingestion;

/// <summary>Form version definitions during synchronizations and in the background, end to end.</summary>
[Collection(PostgresCollection.Name)]
public sealed class FormVersionSyncTests(PostgresFixture postgres) : IDisposable
{
    private static readonly Dictionary<string, string?> Quick = new() { ["Ingestion:PageSize"] = "3" };

    private readonly KoboStub _kobo = new KoboStub().WithForm();

    private static JsonObject Submission(long id, string version)
    {
        var submission = KoboStub.Submission(id);
        submission["__version__"] = version;
        return submission;
    }

    private Task<IngestionHarness> HarnessAsync(bool scheduler = true, Dictionary<string, string?>? settings = null) =>
        IngestionHarness.CreateAsync(postgres, _kobo, scheduler, settings ?? Quick);

    private static Task<string?> StatesAsync(IngestionHarness harness) =>
        TestData.ScalarAsync<string>(harness.Db,
            "SELECT string_agg(format('%s %s %s', uid, status, coalesce(error_code, '-')), ' | ' ORDER BY uid) FROM tamiza.form_versions");

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

    private static string WebhookBody(long id, string version) =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["_id"] = id,
            ["_uuid"] = $"u-{id}",
            ["__version__"] = version,
            ["_submission_time"] = "2026-10-01T10:00:00",
        });

    [Fact]
    public async Task A_synchronization_fetches_the_versions_it_stored_before_it_queues_the_run()
    {
        _kobo.WithSubmissions([Submission(1, "vA"), Submission(2, "vA"), Submission(3, "vB"), Submission(4, "vB")]);
        _kobo.WithVersion("vA", FormVersionFetcherTests.FormWithYes("Si")).WithVersion("vB", FormVersionFetcherTests.FormWithYes("Sí"));
        await using var harness = await HarnessAsync();

        var run = await harness.WaitForIdleAsync();

        Assert.Equal("succeeded", run.GetProperty("status").GetString());
        Assert.Equal("vA available - | vB available -", await StatesAsync(harness));
        // The run queued after the synchronization sees both definitions.
        Assert.True(await TestData.ScalarAsync<bool>(harness.Db,
            "SELECT count(*) = 1 AND bool_and((SELECT max(fetched_at) FROM tamiza.form_versions) <= queued_at) FROM tamiza.runs"));
        Assert.Equal(["vA", "vB"], _kobo.VersionRequests().Order());
    }

    [Fact]
    public async Task A_synchronization_succeeds_when_every_fetch_fails()
    {
        _kobo.WithSubmissions([Submission(1, "vA"), Submission(2, "vB")]);
        _kobo.VersionFailure = _ => 500;
        await using var harness = await HarnessAsync();

        var run = await harness.WaitForIdleAsync();

        Assert.Equal("succeeded", run.GetProperty("status").GetString());
        Assert.Equal(2, run.GetProperty("createdCount").GetInt32());
        Assert.Equal(2, await harness.CountAsync("NOT deleted"));
        Assert.Equal("vA failed kobo.unexpected_response | vB failed kobo.unexpected_response", await StatesAsync(harness));
        // The pipeline still runs, on the current form.
        Assert.Equal(1, await TestData.ScalarAsync<long>(harness.Db, "SELECT count(*) FROM tamiza.runs"));
    }

    [Fact]
    public async Task A_rate_limited_fetch_does_not_fail_the_synchronization()
    {
        _kobo.WithSubmissions([Submission(1, "vA")]);
        _kobo.WithVersion("vA", FormVersionFetcherTests.FormWithYes("Si"));
        await using var harness = await HarnessAsync();
        await harness.WaitForIdleAsync();
        _kobo.VersionFailure = _ => 429;
        var recent = Submission(2, "vC");
        recent["_submission_time"] = IngestionHarness.KoboTime(DateTimeOffset.UtcNow);
        _kobo.Submissions.Add(recent);

        var run = await harness.SyncAsync();

        Assert.Equal("succeeded", run.GetProperty("status").GetString());
        Assert.Equal("vA available - | vC failed kobo.rate_limited", await StatesAsync(harness));
    }

    [Fact]
    public async Task A_version_stored_without_a_row_is_recorded_and_fetched_by_the_next_synchronization()
    {
        _kobo.WithVersion("vOld", FormVersionFetcherTests.FormWithYes("Si"));
        await using var harness = await HarnessAsync();
        await harness.WaitForIdleAsync();
        // What an older image leaves after a rollback: a submission of a version nobody recorded.
        await TestData.ExecuteAsync(harness.Db,
            """
            INSERT INTO tamiza.raw_submissions (project_id, kobo_id, kobo_uuid, form_version, data, origin, received_at, updated_at)
            VALUES (@p, 900, 'old-900', 'vOld', '{}', 'webhook', now(), now()), (@p, 901, 'gone-901', 'vGone', '{}', 'webhook', now(), now());
            UPDATE tamiza.raw_submissions SET deleted = true, deleted_at = now() WHERE kobo_id = 901;
            """, ("p", harness.ProjectId));
        Assert.Null(await StatesAsync(harness));

        await harness.SyncAsync();

        // Deleted submissions record nothing.
        Assert.Equal("vOld available -", await StatesAsync(harness));
    }

    [Fact]
    public async Task An_api_shutdown_during_the_fetch_is_recorded_as_an_interruption()
    {
        _kobo.WithSubmissions([Submission(1, "vA")]);
        _kobo.WithVersion("vA", FormVersionFetcherTests.FormWithYes("Si"));
        _kobo.VersionDelay = TimeSpan.FromSeconds(5);
        await using var harness = await HarnessAsync();
        await WaitUntilAsync(harness.Db, "SELECT count(*) = 1 FROM tamiza.syncs WHERE status = 'running'");
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (_kobo.VersionRequestsStarted == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.True(_kobo.VersionRequestsStarted > 0);
        await harness.Factory.DisposeAsync();

        Assert.Equal("failed sync.interrupted", await TestData.ScalarAsync<string>(harness.Db, "SELECT format('%s %s', status, error_code) FROM tamiza.syncs"));
        // The submissions the synchronization stored stay stored, and the version is still waiting.
        Assert.Equal(1, await harness.CountAsync("NOT deleted"));
        Assert.Equal("vA pending -", await StatesAsync(harness));
    }

    [Fact]
    public async Task A_version_that_arrived_by_webhook_is_fetched_in_the_background_without_a_synchronization()
    {
        _kobo.WithVersion("vC", FormVersionFetcherTests.FormWithYes("Sí"));
        await using var harness = await HarnessAsync();
        await harness.WaitForIdleAsync();
        // The next scheduled synchronization is 12 hours away.
        Assert.True(await TestData.ScalarAsync<bool>(harness.Db, "SELECT next_sync_at > now() + interval '11 hours' FROM tamiza.projects"));

        var response = await harness.DeliverAsync(WebhookBody(77, "vC"));
        Assert.True(response.IsSuccessStatusCode);

        await WaitUntilAsync(harness.Db, "SELECT count(*) = 1 FROM tamiza.form_versions WHERE uid = 'vC' AND status = 'available'");
        Assert.Equal(1, await TestData.ScalarAsync<long>(harness.Db, "SELECT count(*) FROM tamiza.syncs"));
    }

    [Fact]
    public async Task The_background_worker_leaves_a_project_to_its_running_synchronization()
    {
        _kobo.WithSubmissions(KoboStub.MakeSubmissions(3));
        _kobo.WithVersion("vC", FormVersionFetcherTests.FormWithYes("Sí"));
        await using var harness = await HarnessAsync();
        await harness.WaitForIdleAsync();
        var baseline = _kobo.VersionRequests().Count; // the stub's submissions are of version v1, requested by the first synchronization
        _kobo.DataDelay = TimeSpan.FromMilliseconds(1500);

        Assert.Equal(System.Net.HttpStatusCode.Accepted, (await harness.StartSyncAsync("full")).StatusCode);
        await WaitUntilAsync(harness.Db, "SELECT count(*) = 1 FROM tamiza.syncs WHERE status = 'running'");
        await harness.DeliverAsync(WebhookBody(77, "vC"));
        // The worker ticks every 200 ms; it must not take the version while the synchronization runs.
        await Task.Delay(1000);
        var whileRunning = _kobo.VersionRequests().Count - baseline;
        await harness.WaitForIdleAsync(runs: 2);

        Assert.Equal(0, whileRunning);
        Assert.Contains("vC available -", await StatesAsync(harness));
        Assert.Equal(["vC"], _kobo.VersionRequests().Skip(baseline));
    }

    [Fact]
    public async Task With_the_scheduler_off_no_version_is_requested()
    {
        _kobo.WithVersion("vC", FormVersionFetcherTests.FormWithYes("Sí"));
        await using var harness = await HarnessAsync(scheduler: false);

        await harness.DeliverAsync(WebhookBody(1, "vC"));
        await Task.Delay(800);

        Assert.Equal("vC pending -", await StatesAsync(harness));
        Assert.Empty(_kobo.VersionRequests());
    }

    public void Dispose() => _kobo.Dispose();
}
