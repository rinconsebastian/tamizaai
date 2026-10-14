using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tamiza.Api.IntegrationTests.Infrastructure;
using Tamiza.Api.Performance;
using static Tamiza.Api.IntegrationTests.Performance.PerformanceHarness;

namespace Tamiza.Api.IntegrationTests.Performance;

/// <summary>The dashboard's cache, shared computations and limits (tasks 3.3).</summary>
[Collection(PostgresCollection.Name)]
public sealed class PerformanceCacheTests(PostgresFixture postgres) : IDisposable
{
    private readonly AdjustableTimeProvider _clock = new();
    private readonly TestProbe _probe = new();
    private readonly KoboStub _kobo = new KoboStub().WithForm();

    private Task<PerformanceHarness> CreateAsync(Dictionary<string, string?>? settings = null) =>
        PerformanceHarness.CreateAsync(postgres, settings: settings, configureServices: services =>
        {
            services.AddSingleton<TimeProvider>(_clock);
            services.AddSingleton<IPerformanceProbe>(_probe);
        });

    private static long Total(JsonElement dashboard) => dashboard.GetProperty("submissions").GetProperty("total").GetInt64();

    private static DateTimeOffset ComputedAt(JsonElement dashboard) => dashboard.GetProperty("computedAt").GetDateTimeOffset();

    // Nothing new, Old figures

    [Fact]
    public async Task Two_requests_within_a_minute_with_nothing_new_show_the_same_computation()
    {
        await using var h = await CreateAsync();
        await h.AddManyAsync(2, Doc());

        var first = await h.GetAsync();
        await h.AddManyAsync(5, Doc()); // arrives without a webhook delivery or a synchronization: not seen yet
        var second = await h.GetAsync();

        Assert.Equal(2, Total(first));
        Assert.Equal(2, Total(second));
        Assert.Equal(ComputedAt(first), ComputedAt(second));
        Assert.Equal(1, _probe.Started);
    }

    [Fact]
    public async Task Figures_older_than_a_minute_are_computed_again()
    {
        await using var h = await CreateAsync();
        await h.AddManyAsync(2, Doc());
        var first = await h.GetAsync();
        await h.AddManyAsync(5, Doc());

        _clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(2, Total(await h.GetAsync()));
        _clock.Advance(TimeSpan.FromSeconds(2));
        var later = await h.GetAsync();

        Assert.Equal(7, Total(later));
        Assert.True(ComputedAt(later) > ComputedAt(first));
        Assert.Equal(2, _probe.Started);
    }

    [Fact]
    public async Task Another_range_is_another_computation()
    {
        await using var h = await CreateAsync();
        await h.AddManyAsync(2, Doc());

        await h.GetAsync();
        await h.GetAsync("?from=2026-09-01");
        await h.GetAsync("?from=2026-09-01");

        Assert.Equal(2, _probe.Started);
    }

    // Refresh after a webhook delivery

    [Fact]
    public async Task A_webhook_delivery_makes_the_next_request_compute_again()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo, configureServices: services =>
        {
            services.AddSingleton<TimeProvider>(_clock);
            services.AddSingleton<IPerformanceProbe>(_probe);
        });
        var url = $"/api/v1/projects/{h.ProjectId}/performance";

        var before = await h.Admin.GetFromJsonAsync<JsonElement>(url);
        var delivery = await h.DeliverAsync("""{"_id": 1, "_uuid": "u-1", "__version__": "v1", "_submission_time": "2026-10-01T10:00:00"}""");
        var after = await h.Admin.GetFromJsonAsync<JsonElement>(url);
        var again = await h.Admin.GetFromJsonAsync<JsonElement>(url);

        Assert.Equal(HttpStatusCode.OK, delivery.StatusCode);
        Assert.Equal(0, Total(before));
        Assert.Equal(1, Total(after));
        Assert.True(ComputedAt(after) > ComputedAt(before));
        Assert.Equal(ComputedAt(after), ComputedAt(again));
        Assert.Equal(2, _probe.Started);
        PerformanceHarness.ReleasePool(h.Db);
    }

    [Fact]
    public async Task A_new_webhook_mark_on_the_project_makes_the_next_request_compute_again()
    {
        await using var h = await CreateAsync();
        await h.AddManyAsync(1, Doc());
        var before = await h.GetAsync();
        await h.AddManyAsync(1, Doc());

        await h.ExecuteAsync("UPDATE tamiza.projects SET last_webhook_at = now()");
        var after = await h.GetAsync();

        Assert.Equal(1, Total(before));
        Assert.Equal(2, Total(after));
        Assert.Equal(2, _probe.Started);
    }

    [Fact]
    public async Task A_finished_synchronization_makes_the_next_request_compute_again()
    {
        await using var h = await CreateAsync();
        await h.AddManyAsync(1, Doc());
        var before = await h.GetAsync();
        await h.AddManyAsync(1, Doc());

        // A synchronization that is still running changes nothing; one that finishes does.
        await h.ExecuteAsync(
            "INSERT INTO tamiza.syncs (id, project_id, kind, trigger, status, queued_at, started_at) VALUES (@id, @p, 'incremental', 'schedule', 'running', now(), now())",
            ("id", Guid.NewGuid()), ("p", h.ProjectId));
        var whileRunning = await h.GetAsync();
        await h.ExecuteAsync("UPDATE tamiza.syncs SET status = 'succeeded', finished_at = now()");
        var after = await h.GetAsync();

        Assert.Equal(1, Total(before));
        Assert.Equal(1, Total(whileRunning));
        Assert.Equal(2, Total(after));
        Assert.Equal(2, _probe.Started);
    }

    [Fact]
    public async Task Saving_the_frame_makes_the_next_request_compute_again()
    {
        await using var h = await CreateAsync();
        await h.AddManyAsync(1, Doc(("municipality", "cali")));
        var before = await h.GetAsync();

        Assert.Equal(HttpStatusCode.OK, (await h.SaveFrameAsync(Frame(["municipality"], (["cali"], 10)))).StatusCode);
        var after = await h.GetAsync();

        Assert.Equal("notConfigured", before.GetProperty("coverage").GetProperty("state").GetString());
        Assert.Equal("configured", after.GetProperty("coverage").GetProperty("state").GetString());
        Assert.Equal(2, _probe.Started);
    }

    [Fact]
    public async Task Changing_the_time_zone_makes_the_next_request_compute_again()
    {
        await using var h = await CreateAsync();
        await h.AddAsync(Doc(), "2026-09-02T02:30:00Z");
        var before = await h.GetAsync();

        var patch = await h.Admin.PatchAsJsonAsync($"/api/v1/projects/{h.ProjectId}", new { timeZone = "America/Bogota" });
        var after = await h.GetAsync();

        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.Equal("2026-09-02", before.GetProperty("submissions").GetProperty("days")[0].GetProperty("day").GetString());
        Assert.Equal("America/Bogota", after.GetProperty("timeZone").GetString());
        Assert.Equal("2026-09-01", after.GetProperty("submissions").GetProperty("days")[0].GetProperty("day").GetString());
        Assert.Equal(2, _probe.Started);
    }

    // Shared computations

    [Fact]
    public async Task Two_identical_requests_at_the_same_time_run_one_computation()
    {
        await using var h = await CreateAsync();
        await h.AddManyAsync(3, Doc());
        _probe.Hold();

        var first = h.RawGetAsync();
        await _probe.WaitForStartedAsync(1);
        var second = h.RawGetAsync(client: h.Analyst);
        await Task.Delay(500); // let the second reach the computation in flight
        _probe.Release();

        var dashboards = await Task.WhenAll(Read(await first), Read(await second));

        Assert.Equal(3, Total(dashboards[0]));
        Assert.Equal(ComputedAt(dashboards[0]), ComputedAt(dashboards[1]));
        Assert.Equal(1, _probe.Started);
    }

    [Fact]
    public async Task A_member_who_leaves_while_computing_does_not_stop_it_for_another()
    {
        await using var h = await CreateAsync();
        await h.AddManyAsync(3, Doc());
        _probe.Hold();
        using var leaves = new CancellationTokenSource();

        var first = h.RawGetAsync(ct: leaves.Token);
        await _probe.WaitForStartedAsync(1);
        var second = h.RawGetAsync(client: h.Analyst);
        await Task.Delay(500);
        await leaves.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        _probe.Release();

        var response = await second;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, Total(await Read(response)));
        Assert.Equal(1, _probe.Started);
    }

    [Fact]
    public async Task A_computation_whose_only_caller_left_still_fills_the_cache()
    {
        await using var h = await CreateAsync();
        await h.AddManyAsync(3, Doc());
        _probe.Hold();
        using var leaves = new CancellationTokenSource();

        var first = h.RawGetAsync(ct: leaves.Token);
        await _probe.WaitForStartedAsync(1);
        await leaves.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        _probe.Release();
        await Task.Delay(1000); // the computation ends on its own
        var dashboard = await h.GetAsync();

        Assert.Equal(3, Total(dashboard));
        Assert.Equal(1, _probe.Started);
    }

    // Slots

    [Fact]
    public async Task At_most_two_computations_run_at_a_time_and_a_third_waits_for_a_slot()
    {
        await using var h = await CreateAsync();
        await h.AddManyAsync(3, Doc());
        _probe.Hold();

        var requests = new[] { "?from=2026-09-01", "?from=2026-09-02", "?from=2026-08-31" }.Select(q => h.RawGetAsync(q)).ToArray();
        await _probe.WaitForStartedAsync(2);
        await Task.Delay(700);
        var whileHeld = _probe.Started;
        _probe.Release();
        var responses = await Task.WhenAll(requests);

        Assert.Equal(2, whileHeld);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(3, _probe.Started);
    }

    [Fact]
    public async Task A_request_that_waits_too_long_for_a_slot_fails_with_the_timeout_code()
    {
        await using var h = await CreateAsync(new()
        {
            ["Performance:MaxConcurrentComputations"] = "1",
            ["Performance:QueueTimeout"] = "00:00:01",
        });
        await h.AddManyAsync(3, Doc());
        _probe.Hold();

        var holder = h.RawGetAsync("?from=2026-09-01");
        await _probe.WaitForStartedAsync(1);
        var waiting = await h.RawGetAsync("?from=2026-09-02");
        _probe.Release();
        await holder;

        Assert.Equal(HttpStatusCode.ServiceUnavailable, waiting.StatusCode);
        Assert.Equal("dashboard.timeout", (await waiting.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(1, _probe.Started);
    }

    // Computation too long

    [Fact]
    public async Task A_computation_over_the_limit_is_stopped_with_503_and_the_next_request_to_the_project_is_normal()
    {
        await using var h = await CreateAsync(new()
        {
            ["Performance:StatementTimeout"] = "00:00:01",
            ["Performance:TestStatementSleep"] = "00:00:30",
        });
        await h.AddManyAsync(3, Doc());

        var clock = Stopwatch.StartNew();
        var response = await h.RawGetAsync();
        clock.Stop();
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var project = await h.Viewer.GetAsync($"/api/v1/projects/{h.ProjectId}");
        var again = await h.RawGetAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("dashboard.timeout", problem.GetProperty("code").GetString());
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), $"took {clock.Elapsed}");
        Assert.Equal(HttpStatusCode.OK, project.StatusCode);
        // A failure is not cached.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, again.StatusCode);
        Assert.Equal(2, _probe.Started);
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM pg_stat_activity WHERE query LIKE '%pg_sleep%' AND pid <> pg_backend_pid() AND state = 'active'"));
    }

    private static Task<JsonElement> Read(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public void Dispose() => _kobo.Dispose();
}
