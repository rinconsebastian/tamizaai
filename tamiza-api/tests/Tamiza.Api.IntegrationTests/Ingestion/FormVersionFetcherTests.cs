using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tamiza.Api.Data;
using Tamiza.Api.Ingestion;
using Tamiza.Api.IntegrationTests.Infrastructure;
using Tamiza.Api.Kobo;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Tamiza.Api.IntegrationTests.Ingestion;

/// <summary>Fetching the definitions of recorded form versions: statuses, retry delays and what a pass does when Kobo fails.</summary>
[Collection(PostgresCollection.Name)]
public sealed class FormVersionFetcherTests(PostgresFixture postgres) : IDisposable
{
    private readonly KoboStub _kobo = new KoboStub().WithForm();
    private readonly AdjustableTimeProvider _clock = new();

    /// <summary>A form whose choice <c>1</c> of the list <c>yes_no</c> has the given label.</summary>
    internal static string FormWithYes(string label) => $$"""
        {
          "name": "Household survey",
          "content": {
            "survey": [{ "type": "select_one yes_no", "name": "has_water", "$xpath": "has_water", "label": ["Has water"] }],
            "choices": [{ "list_name": "yes_no", "name": "1", "label": ["{{label}}"] }, { "list_name": "yes_no", "name": "2", "label": ["No"] }]
          }
        }
        """;

    private Task<IngestionHarness> HarnessAsync() =>
        IngestionHarness.CreateAsync(postgres, _kobo, configureServices: services => services.AddSingleton<TimeProvider>(_clock));

    private static Task SeedPendingAsync(IngestionHarness harness, params string[] uids) =>
        TestData.ExecuteAsync(harness.Db,
            """
            INSERT INTO tamiza.form_versions (project_id, uid, status, recorded_at, next_attempt_at)
            SELECT @project_id, uid, 'pending', now(), now() FROM unnest(@uids) AS uid
            """,
            ("project_id", harness.ProjectId), ("uids", uids));

    private async Task<FetchResult> FetchAsync(
        IngestionHarness harness, FetchMode mode = FetchMode.Due, TimeSpan? budget = null, string? token = KoboStub.Token, CancellationToken ct = default)
    {
        using var scope = harness.Factory.Services.CreateScope();
        var project = await scope.ServiceProvider.GetRequiredService<TamizaDbContext>().Projects.AsNoTracking().SingleAsync(p => p.Id == harness.ProjectId, ct);
        return await scope.ServiceProvider.GetRequiredService<FormVersionFetcher>().FetchAsync(project, token, mode, budget, ct);
    }

    /// <summary>Repoints the project to a port nobody listens on, and returns the live address to restore it.</summary>
    private async Task<string> PointAtDeadServerAsync(IngestionHarness harness)
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        await TestData.ExecuteAsync(harness.Db, "UPDATE tamiza.projects SET kobo_server_url = @url", ("url", $"http://127.0.0.1:{port}"));
        return _kobo.Url.TrimEnd('/');
    }

    private static Task<string?> StatesAsync(IngestionHarness harness) =>
        TestData.ScalarAsync<string>(harness.Db,
            "SELECT string_agg(format('%s %s %s %s', uid, status, attempts, coalesce(error_code, '-')), ' | ' ORDER BY uid) FROM tamiza.form_versions");

    private static Task<string?> LabelAsync(IngestionHarness harness, string uid) =>
        TestData.ScalarAsync<string>(harness.Db,
            "SELECT c->>'label' FROM tamiza.form_versions, jsonb_array_elements(choices) c WHERE uid = @uid AND c->>'name' = '1'", ("uid", uid));

    [Fact]
    public async Task Fetched_versions_keep_their_own_labels_for_a_relabeled_choice()
    {
        _kobo.WithVersion("vA", FormWithYes("Si"), "2026-09-01T10:00:00Z").WithVersion("vB", FormWithYes("Sí"), "2026-09-05T10:00:00Z");
        await using var harness = await HarnessAsync();
        await SeedPendingAsync(harness, "vA", "vB");

        var result = await FetchAsync(harness);

        Assert.Equal(new FetchResult(2, 2), result);
        Assert.Equal("vA available 0 - | vB available 0 -", await StatesAsync(harness));
        Assert.Equal("Si", await LabelAsync(harness, "vA"));
        Assert.Equal("Sí", await LabelAsync(harness, "vB"));
        Assert.Equal(new DateTime(2026, 9, 5, 10, 0, 0, DateTimeKind.Utc),
            await TestData.ScalarAsync<DateTime>(harness.Db, "SELECT deployed_at FROM tamiza.form_versions WHERE uid = 'vB'"));
        Assert.Null(await TestData.ScalarAsync<DateTime?>(harness.Db, "SELECT next_attempt_at FROM tamiza.form_versions WHERE uid = 'vA'"));
    }

    [Fact]
    public async Task A_definition_is_fetched_once()
    {
        _kobo.WithVersion("vA", FormWithYes("Si"));
        await using var harness = await HarnessAsync();
        await SeedPendingAsync(harness, "vA");
        await FetchAsync(harness);

        var due = await FetchAsync(harness);
        var all = await FetchAsync(harness, FetchMode.All);

        Assert.Equal(default, due);
        Assert.Equal(default, all);
        Assert.Equal(["vA"], _kobo.VersionRequests());
    }

    [Fact]
    public async Task When_kobo_is_unreachable_one_request_is_made_and_every_selected_version_fails()
    {
        await using var harness = await HarnessAsync();
        await SeedPendingAsync(harness, "vA", "vB", "vC");
        var live = await PointAtDeadServerAsync(harness);

        var result = await FetchAsync(harness);

        Assert.Equal(new FetchResult(3, 0), result);
        // One timeout, not one per version: the pass stops at the first server-wide error.
        Assert.Equal(1, harness.Factory.Logs.Lines.Count(line => line.Contains("could not be reached")));
        Assert.Equal("vA failed 1 kobo.unreachable | vB failed 1 kobo.unreachable | vC failed 1 kobo.unreachable", await StatesAsync(harness));
        var untilRetry = await TestData.ScalarAsync<double>(harness.Db, "SELECT min(extract(epoch FROM next_attempt_at - now()))::float8 FROM tamiza.form_versions");
        Assert.InRange(untilRetry, 3500, 3700);
        Assert.Contains("could not be reached", await TestData.ScalarAsync<string>(harness.Db, "SELECT error FROM tamiza.form_versions WHERE uid = 'vA'"));
        Assert.NotEmpty(live);
    }

    [Fact]
    public async Task A_failed_version_is_due_again_after_an_hour_without_waiting_for_a_synchronization()
    {
        _kobo.WithVersion("vB", FormWithYes("Sí"));
        await using var harness = await HarnessAsync();
        await SeedPendingAsync(harness, "vB");
        var live = await PointAtDeadServerAsync(harness);
        await FetchAsync(harness);
        await TestData.ExecuteAsync(harness.Db, "UPDATE tamiza.projects SET kobo_server_url = @url", ("url", live));
        var requests = _kobo.VersionRequests().Count;

        _clock.Advance(TimeSpan.FromMinutes(59));
        var early = await FetchAsync(harness);
        _clock.Advance(TimeSpan.FromMinutes(2));
        var due = await FetchAsync(harness);

        Assert.Equal(default, early);
        Assert.Equal(requests, _kobo.VersionRequests().Count - 1); // only the pass after the hour asked Kobo
        Assert.Equal(new FetchResult(1, 1), due);
        Assert.Equal("vB available 0 -", await StatesAsync(harness));
    }

    [Fact]
    public async Task Repeated_failures_double_the_delay_up_to_a_day()
    {
        _kobo.WithVersion("vB", FormWithYes("Sí"));
        _kobo.VersionFailure = _ => 500;
        await using var harness = await HarnessAsync();
        await SeedPendingAsync(harness, "vB");
        var delays = new List<double>();

        foreach (var _ in Enumerable.Range(0, 7))
        {
            await FetchAsync(harness);
            var next = await TestData.ScalarAsync<DateTime>(harness.Db, "SELECT next_attempt_at FROM tamiza.form_versions");
            delays.Add(Math.Round((new DateTimeOffset(next, TimeSpan.Zero) - _clock.GetUtcNow()).TotalHours, 1));
            _clock.Advance(TimeSpan.FromHours(25));
        }

        // 1 h, 2 h, then four hours after the third failure, doubling up to one day.
        Assert.Equal([1d, 2d, 4d, 8d, 16d, 24d, 24d], delays);
        Assert.Equal("vB failed 7 kobo.unexpected_response", await StatesAsync(harness));
    }

    [Fact]
    public async Task A_version_unknown_to_kobo_is_not_found_and_not_requested_again_until_asked()
    {
        await using var harness = await HarnessAsync();
        await SeedPendingAsync(harness, "vX");

        var first = await FetchAsync(harness);
        var later = await FetchAsync(harness);
        var requestsAfterDue = _kobo.VersionRequests().Count;
        var retried = await FetchAsync(harness, FetchMode.All);

        Assert.Equal(new FetchResult(1, 0), first);
        Assert.Equal(default, later);
        Assert.Equal("vX not_found 0 kobo.version_not_found", await StatesAsync(harness));
        Assert.Null(await TestData.ScalarAsync<DateTime?>(harness.Db, "SELECT next_attempt_at FROM tamiza.form_versions"));
        Assert.Equal(1, requestsAfterDue);
        Assert.Equal(new FetchResult(1, 0), retried);
        Assert.Equal(2, _kobo.VersionRequests().Count);
    }

    [Fact]
    public async Task A_version_restored_in_kobo_is_fetched_by_a_pass_over_everything()
    {
        await using var harness = await HarnessAsync();
        await SeedPendingAsync(harness, "vX");
        await FetchAsync(harness);
        _kobo.WithVersion("vX", FormWithYes("Si"));

        var result = await FetchAsync(harness, FetchMode.All);

        Assert.Equal(new FetchResult(1, 1), result);
        Assert.Equal("vX available 0 -", await StatesAsync(harness));
    }

    [Fact]
    public async Task When_the_form_itself_cannot_be_read_a_missing_version_fails_with_the_forms_error_not_as_not_found()
    {
        await using var harness = await HarnessAsync();
        await SeedPendingAsync(harness, "vA", "vB");
        _kobo.Respond(_kobo.AssetPath, 404); // the form was deleted, or is no longer shared with the token

        var result = await FetchAsync(harness);

        Assert.Equal(new FetchResult(2, 0), result);
        Assert.Equal("vA failed 1 kobo.form_not_found | vB failed 1 kobo.form_not_found", await StatesAsync(harness));
        Assert.Single(_kobo.VersionRequests());
    }

    [Fact]
    public async Task A_rejected_token_stops_the_pass_after_one_request()
    {
        await using var harness = await HarnessAsync();
        await SeedPendingAsync(harness, "vA", "vB");

        var result = await FetchAsync(harness, token: "revoked-token");

        Assert.Equal(new FetchResult(2, 0), result);
        Assert.Equal("vA failed 1 kobo.unauthorized | vB failed 1 kobo.unauthorized", await StatesAsync(harness));
        Assert.Single(_kobo.VersionRequests());
    }

    [Fact]
    public async Task An_unreadable_stored_token_fails_the_versions_without_a_request()
    {
        await using var harness = await HarnessAsync();
        await SeedPendingAsync(harness, "vA");

        await FetchAsync(harness, token: null);

        Assert.Equal("vA failed 1 kobo.token_unreadable", await StatesAsync(harness));
        Assert.Empty(_kobo.VersionRequests());
    }

    [Fact]
    public async Task A_version_Kobo_fails_alone_does_not_stop_the_others()
    {
        _kobo.WithVersion("vA", FormWithYes("Si")).WithVersion("vB", FormWithYes("Sí"));
        _kobo.VersionFailure = uid => uid == "vA" ? 500 : null;
        await using var harness = await HarnessAsync();
        await SeedPendingAsync(harness, "vA", "vB");

        var result = await FetchAsync(harness);

        Assert.Equal(new FetchResult(2, 1), result);
        Assert.Equal("vA failed 1 kobo.unexpected_response | vB available 0 -", await StatesAsync(harness));
    }

    [Fact]
    public async Task When_the_budget_runs_out_the_versions_not_requested_stay_pending_and_due()
    {
        _kobo.WithVersion("vA", FormWithYes("Si")).WithVersion("vB", FormWithYes("Sí"));
        await using var harness = await HarnessAsync();
        await SeedPendingAsync(harness, "vA", "vB");
        await TestData.ExecuteAsync(harness.Db, "UPDATE tamiza.form_versions SET status = 'failed', attempts = 2, next_attempt_at = now() + interval '3 hours' WHERE uid = 'vB'");

        var result = await FetchAsync(harness, FetchMode.All, budget: TimeSpan.Zero);

        Assert.Equal(new FetchResult(2, 0), result);
        Assert.Empty(_kobo.VersionRequests());
        Assert.Equal("vA pending 0 - | vB pending 2 -", await StatesAsync(harness));
        Assert.Equal(2, await TestData.ScalarAsync<long>(harness.Db, "SELECT count(*) FROM tamiza.form_versions WHERE next_attempt_at <= now() + interval '1 second'"));
    }

    [Fact]
    public async Task An_available_definition_is_never_overwritten_by_a_late_write()
    {
        _kobo.WithVersion("vA", FormWithYes("Si"));
        await using var harness = await HarnessAsync();
        await SeedPendingAsync(harness, "vA");
        await FetchAsync(harness);

        using var scope = harness.Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<FormVersionStore>();
        var now = DateTimeOffset.UtcNow;
        await store.SaveFailedAsync(harness.ProjectId, "vA", new KoboError("kobo.unreachable", "late"), now, default);
        await store.SaveNotFoundAsync(harness.ProjectId, "vA", new KoboError("kobo.version_not_found", "late"), now, default);
        await store.SaveAvailableAsync(harness.ProjectId, "vA", [], [], null, now, default);
        await store.MakeDueAsync(harness.ProjectId, ["vA"], now, default);

        Assert.Equal("vA available 0 -", await StatesAsync(harness));
        Assert.Equal("Si", await LabelAsync(harness, "vA"));
    }

    [Fact]
    public async Task Cancellation_is_rethrown_and_does_not_mark_the_version_failed()
    {
        _kobo.Server.Given(Request.Create().WithPath(_kobo.VersionsPath + "*").UsingGet()).AtPriority(-5)
            .RespondWith(Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromSeconds(10)).WithBody("{}"));
        await using var harness = await HarnessAsync();
        await SeedPendingAsync(harness, "vA");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FetchAsync(harness, ct: cancellation.Token));

        Assert.Equal("vA pending 0 -", await StatesAsync(harness));
    }

    [Fact]
    public async Task Waiting_versions_of_other_projects_are_left_alone()
    {
        _kobo.WithVersion("vA", FormWithYes("Si"));
        await using var harness = await HarnessAsync();
        var other = await TestData.InsertProjectAsync(harness.Db, await TestData.ScalarAsync<Guid>(harness.Db, "SELECT created_by FROM tamiza.projects"), "Other");
        await SeedPendingAsync(harness, "vA");
        await TestData.ExecuteAsync(harness.Db,
            "INSERT INTO tamiza.form_versions (project_id, uid, status, recorded_at, next_attempt_at) VALUES (@p, 'vA', 'pending', now(), now())", ("p", other));

        await FetchAsync(harness);

        Assert.Equal("available", await TestData.ScalarAsync<string>(harness.Db, "SELECT status FROM tamiza.form_versions WHERE project_id = @p", ("p", harness.ProjectId)));
        Assert.Equal("pending", await TestData.ScalarAsync<string>(harness.Db, "SELECT status FROM tamiza.form_versions WHERE project_id = @p", ("p", other)));
    }

    public void Dispose() => _kobo.Dispose();
}
