using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Ingestion;

/// <summary><c>GET /projects/{id}/form-versions</c>: the versions in use and the state of each definition.</summary>
[Collection(PostgresCollection.Name)]
public sealed class FormVersionsApiTests(PostgresFixture postgres) : IDisposable
{
    private readonly KoboStub _kobo = new KoboStub().WithForm();

    private static async Task<HttpClient> MemberAsync(IngestionHarness harness, string subject, string role)
    {
        var userId = await TestData.SignInAsync(harness.Factory, subject, $"{subject}@example.org");
        await TestData.AddMemberAsync(harness.Db, harness.ProjectId, userId, role);
        return TestData.ClientFor(harness.Factory, subject, $"{subject}@example.org");
    }

    private static string Path(IngestionHarness harness) => $"/api/v1/projects/{harness.ProjectId}/form-versions";

    /// <summary>Stores <paramref name="count"/> submissions of the version (none of the version when null), optionally deleted.</summary>
    private static Task SubmissionsAsync(IngestionHarness harness, string? version, int count, bool deleted = false) =>
        TestData.ExecuteAsync(harness.Db,
            """
            INSERT INTO tamiza.raw_submissions (project_id, kobo_id, kobo_uuid, form_version, data, origin, received_at, updated_at, deleted)
            SELECT @p, (SELECT coalesce(max(kobo_id), 0) FROM tamiza.raw_submissions) + n, 'u-' || gen_random_uuid(), @version::text, '{}', 'sync', now(), now(), @deleted
            FROM generate_series(1, @count) n
            """,
            ("p", harness.ProjectId), ("version", (object?)version ?? DBNull.Value), ("count", count), ("deleted", deleted));

    private static Task RecordAsync(IngestionHarness harness, string uid, string status, string? deployedAt = null, string? errorCode = null, int attempts = 0) =>
        TestData.ExecuteAsync(harness.Db,
            """
            INSERT INTO tamiza.form_versions (project_id, uid, status, deployed_at, recorded_at, attempts, next_attempt_at, error_code, error)
            VALUES (@p, @uid, @status, @deployed::timestamptz, now(), @attempts, CASE WHEN @status IN ('pending', 'failed') THEN now() + interval '1 hour' END,
                    @code::text, CASE WHEN @code::text IS NULL THEN NULL ELSE 'The Kobo server could not be reached.' END)
            """,
            ("p", harness.ProjectId), ("uid", uid), ("status", status), ("deployed", (object?)deployedAt ?? DBNull.Value),
            ("code", (object?)errorCode ?? DBNull.Value), ("attempts", attempts));

    [Fact]
    public async Task An_analyst_reads_the_versions_with_their_counts_and_states_and_a_viewer_gets_403()
    {
        await using var harness = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var analyst = await MemberAsync(harness, "ana", "analyst");
        using var viewer = await MemberAsync(harness, "vera", "viewer");
        await SubmissionsAsync(harness, "vA", 120);
        await SubmissionsAsync(harness, "vB", 15);
        await RecordAsync(harness, "vA", "available", deployedAt: "2026-09-01T10:00:00Z");
        await RecordAsync(harness, "vB", "failed", errorCode: "kobo.unreachable", attempts: 1);

        var list = await analyst.GetFromJsonAsync<JsonElement>(Path(harness));
        var refused = await viewer.GetAsync(Path(harness));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        var versions = list.GetProperty("versions").EnumerateArray().ToList();
        Assert.Equal(["vA", "vB"], versions.Select(v => v.GetProperty("uid").GetString()));
        var a = versions[0];
        Assert.Equal(("available", 120L), (a.GetProperty("status").GetString(), a.GetProperty("submissions").GetInt64()));
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero), a.GetProperty("deployedAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, a.GetProperty("errorCode").ValueKind);
        Assert.Equal(JsonValueKind.Null, a.GetProperty("nextAttemptAt").ValueKind);
        var b = versions[1];
        Assert.Equal(("failed", 15L, "kobo.unreachable"), (b.GetProperty("status").GetString(), b.GetProperty("submissions").GetInt64(), b.GetProperty("errorCode").GetString()));
        Assert.Contains("could not be reached", b.GetProperty("error").GetString());
        Assert.Equal(JsonValueKind.String, b.GetProperty("nextAttemptAt").ValueKind);
        Assert.Equal(0, list.GetProperty("withoutVersion").GetInt64());
    }

    [Fact]
    public async Task Submissions_without_a_version_are_counted_apart()
    {
        await using var harness = await IngestionHarness.CreateAsync(postgres, _kobo);
        await SubmissionsAsync(harness, null, 3);
        await SubmissionsAsync(harness, null, 2, deleted: true);
        await SubmissionsAsync(harness, "vA", 1);
        await RecordAsync(harness, "vA", "available");

        var list = await harness.Admin.GetFromJsonAsync<JsonElement>(Path(harness));

        Assert.Equal(3, list.GetProperty("withoutVersion").GetInt64());
        Assert.Single(list.GetProperty("versions").EnumerateArray());
    }

    [Fact]
    public async Task A_version_in_use_without_a_record_reads_as_pending()
    {
        await using var harness = await IngestionHarness.CreateAsync(postgres, _kobo);
        await SubmissionsAsync(harness, "vNew", 4);

        var list = await harness.Admin.GetFromJsonAsync<JsonElement>(Path(harness));

        var version = Assert.Single(list.GetProperty("versions").EnumerateArray());
        Assert.Equal(("vNew", "pending", 4L), (version.GetProperty("uid").GetString(), version.GetProperty("status").GetString(), version.GetProperty("submissions").GetInt64()));
        Assert.Equal(JsonValueKind.Null, version.GetProperty("deployedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, version.GetProperty("nextAttemptAt").ValueKind);
    }

    [Fact]
    public async Task The_current_version_comes_first_then_the_newest_deployment_then_versions_without_a_date()
    {
        await using var harness = await IngestionHarness.CreateAsync(postgres, _kobo);
        foreach (var (uid, deployed) in new[] { ("vOld", "2026-01-01T00:00:00Z"), ("vMid", "2026-05-01T00:00:00Z"), ("vNew", "2026-08-01T00:00:00Z"), ("vB", null), ("vA", null) })
        {
            await SubmissionsAsync(harness, uid, 1);
            await RecordAsync(harness, uid, "available", deployedAt: deployed);
        }

        // The current version is the oldest one, and another is current but used by no submission yet.
        await TestData.ExecuteAsync(harness.Db, "UPDATE tamiza.projects SET form_version = 'vOld'");
        var oldCurrent = await harness.Admin.GetFromJsonAsync<JsonElement>(Path(harness));
        await RecordAsync(harness, "vUnused", "available");
        await TestData.ExecuteAsync(harness.Db, "UPDATE tamiza.projects SET form_version = 'vUnused'");
        var unusedCurrent = await harness.Admin.GetFromJsonAsync<JsonElement>(Path(harness));

        Assert.Equal(["vOld", "vNew", "vMid", "vA", "vB"], oldCurrent.GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("uid").GetString()));
        Assert.Equal([true, false, false, false, false], oldCurrent.GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("current").GetBoolean()));
        Assert.Equal(["vUnused", "vNew", "vMid", "vOld", "vA", "vB"], unusedCurrent.GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("uid").GetString()));
        Assert.Equal(0, unusedCurrent.GetProperty("versions")[0].GetProperty("submissions").GetInt64());
        Assert.True(unusedCurrent.GetProperty("versions")[0].GetProperty("current").GetBoolean());
    }

    [Fact]
    public async Task Deleted_submissions_are_left_out_of_the_counts_and_of_the_list()
    {
        await using var harness = await IngestionHarness.CreateAsync(postgres, _kobo);
        await SubmissionsAsync(harness, "vA", 5);
        await SubmissionsAsync(harness, "vA", 2, deleted: true);
        await SubmissionsAsync(harness, "vGone", 3, deleted: true);
        await RecordAsync(harness, "vA", "available");
        await RecordAsync(harness, "vGone", "failed", errorCode: "kobo.unreachable");

        var list = await harness.Admin.GetFromJsonAsync<JsonElement>(Path(harness));

        var version = Assert.Single(list.GetProperty("versions").EnumerateArray());
        Assert.Equal(("vA", 5L), (version.GetProperty("uid").GetString(), version.GetProperty("submissions").GetInt64()));
    }

    [Fact]
    public async Task Only_project_members_can_read_the_list()
    {
        await using var harness = await IngestionHarness.CreateAsync(postgres, _kobo);
        var outsiderId = await TestData.SignInAsync(harness.Factory, "otto", "otto@example.org");
        Assert.NotEqual(Guid.Empty, outsiderId);
        using var outsider = TestData.ClientFor(harness.Factory, "otto", "otto@example.org");
        using var anonymous = harness.Factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(Path(harness))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Path(harness))).StatusCode);
    }

    [Fact]
    public async Task The_endpoint_is_in_the_openapi_document()
    {
        await using var harness = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var anonymous = harness.Factory.CreateClient();

        var document = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/openapi.json");

        Assert.True(document.GetProperty("paths").TryGetProperty("/api/v1/projects/{projectId}/form-versions", out var path));
        Assert.True(path.TryGetProperty("get", out _));
    }

    public void Dispose() => _kobo.Dispose();
}
