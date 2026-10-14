using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;
using Tamiza.Api.IntegrationTests.Ingestion;

namespace Tamiza.Api.IntegrationTests.Projects;

/// <summary>Re-checking the form and replacing the token give failed and not found form versions another chance.</summary>
[Collection(PostgresCollection.Name)]
public sealed class FormVersionRecheckTests(PostgresFixture postgres) : IDisposable
{
    private readonly KoboStub _kobo = new KoboStub().WithForm();

    private Task<IngestionHarness> HarnessAsync(bool scheduler = false, Dictionary<string, string?>? settings = null) =>
        IngestionHarness.CreateAsync(postgres, _kobo, scheduler, settings);

    private string RecheckPath(IngestionHarness harness) => $"/api/v1/projects/{harness.ProjectId}/kobo-check";

    private static Task SeedAsync(IngestionHarness harness, string uid, string status, int submissions = 1, string? errorCode = null, bool deleted = false) =>
        TestData.ExecuteAsync(harness.Db,
            """
            INSERT INTO tamiza.form_versions (project_id, uid, status, recorded_at, attempts, next_attempt_at, error_code, error)
            SELECT @p, @uid::text, @status::text, now(), CASE WHEN @status::text = 'failed' THEN 2 ELSE 0 END,
                   CASE WHEN @status = 'failed' THEN now() + interval '4 hours' END, @code::text, CASE WHEN @code::text IS NOT NULL THEN 'An error.' END
            WHERE @status::text <> 'none';
            INSERT INTO tamiza.raw_submissions (project_id, kobo_id, kobo_uuid, form_version, data, origin, received_at, updated_at, deleted)
            SELECT @p, abs(hashtext(@uid::text)) + n, @uid::text || '-' || n, @uid::text, '{}', 'sync', now(), now(), @deleted FROM generate_series(1, @count) n;
            """,
            ("p", harness.ProjectId), ("uid", uid), ("status", status), ("code", (object?)errorCode ?? DBNull.Value), ("count", submissions), ("deleted", deleted));

    private static Task<string?> StatesAsync(IngestionHarness harness) =>
        TestData.ScalarAsync<string>(harness.Db, "SELECT string_agg(format('%s %s', uid, status), ' | ' ORDER BY uid) FROM tamiza.form_versions");

    private static async Task<int> UnavailableAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("unavailableFormVersions").GetInt32();
    }

    private static async Task<HttpClient> AnalystAsync(IngestionHarness harness)
    {
        var analystId = await TestData.SignInAsync(harness.Factory, "grace", "grace@example.org");
        await TestData.AddMemberAsync(harness.Db, harness.ProjectId, analystId, "analyst");
        return TestData.ClientFor(harness.Factory, "grace", "grace@example.org");
    }

    [Fact]
    public async Task A_recheck_tries_failed_and_not_found_versions_again_and_reports_what_is_still_unavailable()
    {
        _kobo.WithVersion("vA", FormVersionFetcherTests.FormWithYes("Si")).WithVersion("vB", FormVersionFetcherTests.FormWithYes("Sí"));
        await using var harness = await HarnessAsync();
        await SeedAsync(harness, "vA", "available", submissions: 3);
        await SeedAsync(harness, "vB", "failed", submissions: 2, errorCode: "kobo.unreachable");
        await SeedAsync(harness, "vD", "not_found", submissions: 1, errorCode: "kobo.version_not_found");
        using var analyst = await AnalystAsync(harness);

        var recheck = await analyst.PostAsync(RecheckPath(harness), null);

        // vB is back; vD is still unknown to Kobo. vA, already available, is not requested.
        Assert.Equal(1, await UnavailableAsync(recheck));
        Assert.Equal("vA available | vB available | vD not_found", await StatesAsync(harness));
        Assert.Equal(["vB", "vD"], _kobo.VersionRequests().Order());
    }

    [Fact]
    public async Task A_version_failed_by_a_rejected_token_is_restored_after_replacing_the_token_and_rechecking()
    {
        _kobo.WithVersion("vB", FormVersionFetcherTests.FormWithYes("Sí"));
        await using var harness = await HarnessAsync();
        await SeedAsync(harness, "vB", "failed", errorCode: "kobo.unauthorized");
        using var analyst = await AnalystAsync(harness);
        Assert.Equal(1, await UnavailableAsync(await harness.Admin.GetAsync($"/api/v1/projects/{harness.ProjectId}")));

        _kobo.WithForm(token: "rotated-token").WithVersion("vB", FormVersionFetcherTests.FormWithYes("Sí"));
        var replaced = await harness.Admin.PutAsJsonAsync($"/api/v1/projects/{harness.ProjectId}/kobo-token", new { apiToken = "rotated-token" });
        // Replacing the token makes the version pending and due; the worker is off, so nothing fetched it yet.
        Assert.Equal(1, await UnavailableAsync(replaced));
        Assert.Equal("vB pending", await StatesAsync(harness));

        var recheck = await analyst.PostAsync(RecheckPath(harness), null);

        Assert.Equal(0, await UnavailableAsync(recheck));
        Assert.Equal("vB available", await StatesAsync(harness));
    }

    [Fact]
    public async Task Replacing_the_token_makes_failed_and_not_found_versions_due_within_a_tick()
    {
        _kobo.WithVersion("vB", FormVersionFetcherTests.FormWithYes("Sí")).WithVersion("vD", FormVersionFetcherTests.FormWithYes("Si"));
        await using var harness = await HarnessAsync(scheduler: true);
        await harness.WaitForIdleAsync();
        await SeedAsync(harness, "vB", "failed", errorCode: "kobo.unauthorized");
        await SeedAsync(harness, "vD", "not_found", errorCode: "kobo.version_not_found");
        // The worker does not touch them on its own: the first attempt of vB is four hours away and vD is not retried.
        await Task.Delay(700);
        Assert.Equal("vB failed | vD not_found", await StatesAsync(harness));

        _kobo.WithForm(token: "rotated-token").WithVersion("vB", FormVersionFetcherTests.FormWithYes("Sí")).WithVersion("vD", FormVersionFetcherTests.FormWithYes("Si"));
        (await harness.Admin.PutAsJsonAsync($"/api/v1/projects/{harness.ProjectId}/kobo-token", new { apiToken = "rotated-token" })).EnsureSuccessStatusCode();

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (await StatesAsync(harness) != "vB available | vD available" && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        Assert.Equal("vB available | vD available", await StatesAsync(harness));
    }

    [Fact]
    public async Task A_recheck_whose_budget_runs_out_leaves_the_rest_due_for_the_worker()
    {
        _kobo.WithVersion("vA", FormVersionFetcherTests.FormWithYes("Si")).WithVersion("vB", FormVersionFetcherTests.FormWithYes("Sí")).WithVersion("vC", FormVersionFetcherTests.FormWithYes("Sí"));
        await using var harness = await HarnessAsync(settings: new() { ["Ingestion:RecheckVersionBudget"] = "00:00:01" });
        foreach (var uid in new[] { "vA", "vB", "vC" })
        {
            await SeedAsync(harness, uid, "failed", errorCode: "kobo.unreachable");
        }

        _kobo.VersionDelay = TimeSpan.FromMilliseconds(1300);
        var recheck = await harness.Admin.PostAsync(RecheckPath(harness), null);

        // The first request used up the budget, so the other two were not requested and are pending and due at once.
        Assert.Equal(2, await UnavailableAsync(recheck));
        Assert.Equal("vA available | vB pending | vC pending", await StatesAsync(harness));
        Assert.Equal(2, await TestData.ScalarAsync<long>(harness.Db,
            "SELECT count(*) FROM tamiza.form_versions WHERE status = 'pending' AND next_attempt_at <= now()"));
        Assert.Single(_kobo.VersionRequests());
    }

    [Fact]
    public async Task The_project_details_count_the_versions_in_use_without_a_definition()
    {
        await using var harness = await HarnessAsync();
        var details = $"/api/v1/projects/{harness.ProjectId}";
        Assert.Equal(0, await UnavailableAsync(await harness.Admin.GetAsync(details)));

        await SeedAsync(harness, "vA", "available", submissions: 4);
        await SeedAsync(harness, "vB", "pending", submissions: 2);
        await SeedAsync(harness, "vC", "failed", submissions: 1, errorCode: "kobo.unreachable");
        await SeedAsync(harness, "vD", "not_found", submissions: 1, errorCode: "kobo.version_not_found");
        await SeedAsync(harness, "vE", "none", submissions: 2); // used by submissions but never recorded
        await SeedAsync(harness, "vF", "failed", submissions: 2, errorCode: "kobo.unreachable", deleted: true); // nobody uses it any more
        await SeedAsync(harness, "vG", "pending", submissions: 0); // recorded, no submissions
        await TestData.ExecuteAsync(harness.Db,
            "INSERT INTO tamiza.raw_submissions (project_id, kobo_id, kobo_uuid, form_version, data, origin, received_at, updated_at) VALUES (@p, 1, 'no-version', NULL, '{}', 'sync', now(), now())",
            ("p", harness.ProjectId));

        // vB, vC, vD and vE.
        Assert.Equal(4, await UnavailableAsync(await harness.Admin.GetAsync(details)));
    }

    public void Dispose() => _kobo.Dispose();
}
