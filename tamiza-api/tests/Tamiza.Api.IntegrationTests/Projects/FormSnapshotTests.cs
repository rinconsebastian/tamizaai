using System.Net.Http.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Projects;

/// <summary>
/// The project's form snapshot follows the form's deployed version, not its latest draft, and every way of taking it
/// records which version it describes.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FormSnapshotTests(PostgresFixture postgres) : IDisposable
{
    private const string EnumeratorRow = """{ "type": "text", "name": "enumerator", "$xpath": "enumerator", "label": ["Enumerator"] }""";

    /// <summary>The deployed form plus a question someone added in the form builder and did not deploy.</summary>
    private static readonly string DraftJson = KoboStub.FormJson.Replace(
        EnumeratorRow, EnumeratorRow + """, { "type": "integer", "name": "income", "$xpath": "income", "label": ["Income"] }""");

    private readonly KoboStub _kobo = new();

    private Task<IngestionHarness> HarnessAsync(bool scheduler = false) => IngestionHarness.CreateAsync(postgres, _kobo, scheduler);

    private static Task<string?> FieldNamesAsync(string db) =>
        TestData.ScalarAsync<string>(db, "SELECT string_agg(f.field->>'name', ',' ORDER BY f.position) FROM tamiza.projects, jsonb_array_elements(form_fields) WITH ORDINALITY AS f(field, position)");

    private static Task<string?> SnapshotVersionAsync(string db) => TestData.ScalarAsync<string>(db, "SELECT form_version FROM tamiza.projects");

    private static Task<string?> RowAsync(string db, string uid) =>
        TestData.ScalarAsync<string>(db,
            "SELECT format('%s %s %s %s', status, jsonb_array_length(fields), attempts, coalesce(error_code, '-')) FROM tamiza.form_versions WHERE uid = @uid", ("uid", uid));

    [Fact]
    public async Task Undeployed_edits_are_not_in_the_snapshot()
    {
        _kobo.WithForm(KoboStub.Deployed(DraftJson, deployedVersionId: "vLive", versionId: "vDraft")).WithVersion("vLive", KoboStub.FormJson);
        await using var harness = await HarnessAsync();

        Assert.Equal("start,end,municipality,sex,enumerator", await FieldNamesAsync(harness.Db));
        Assert.Equal("vLive", await SnapshotVersionAsync(harness.Db));
        Assert.Equal("available 5 0 -", await RowAsync(harness.Db, "vLive"));
        Assert.Equal(["vLive"], _kobo.VersionRequests());
    }

    [Fact]
    public async Task A_form_without_a_deployment_keeps_its_content_and_no_version()
    {
        _kobo.WithForm(KoboStub.Deployed(DraftJson, deployedVersionId: null, versionId: "vDraft"));
        await using var harness = await HarnessAsync();

        Assert.Equal("start,end,municipality,sex,enumerator,income", await FieldNamesAsync(harness.Db));
        Assert.Null(await SnapshotVersionAsync(harness.Db));
        Assert.Equal(0, await TestData.ScalarAsync<long>(harness.Db, "SELECT count(*) FROM tamiza.form_versions"));
        Assert.Empty(_kobo.VersionRequests());
    }

    [Fact]
    public async Task A_deployed_version_that_is_the_latest_content_is_kept_without_a_request()
    {
        _kobo.WithForm(KoboStub.Deployed(KoboStub.FormJson, deployedVersionId: "vLive"));
        await using var harness = await HarnessAsync();

        Assert.Equal("vLive", await SnapshotVersionAsync(harness.Db));
        Assert.Equal("available 5 0 -", await RowAsync(harness.Db, "vLive"));
        Assert.Null(await TestData.ScalarAsync<DateTime?>(harness.Db, "SELECT deployed_at FROM tamiza.form_versions"));
        Assert.Empty(_kobo.VersionRequests());
    }

    [Fact]
    public async Task A_deployed_version_Kobo_does_not_know_falls_back_to_the_latest_content_and_is_not_found()
    {
        _kobo.WithForm(KoboStub.Deployed(DraftJson, deployedVersionId: "vLive", versionId: "vDraft"));
        await using var harness = await HarnessAsync();

        Assert.Equal("start,end,municipality,sex,enumerator,income", await FieldNamesAsync(harness.Db));
        Assert.Equal("vLive", await SnapshotVersionAsync(harness.Db));
        Assert.Equal("not_found 0 0 kobo.version_not_found", await RowAsync(harness.Db, "vLive"));
    }

    [Fact]
    public async Task A_deployed_version_that_cannot_be_read_falls_back_to_the_latest_content_and_is_retried_in_an_hour()
    {
        _kobo.WithForm(KoboStub.Deployed(DraftJson, deployedVersionId: "vLive", versionId: "vDraft")).WithVersion("vLive", KoboStub.FormJson);
        _kobo.VersionFailure = _ => 500;
        await using var harness = await HarnessAsync();

        Assert.Equal("start,end,municipality,sex,enumerator,income", await FieldNamesAsync(harness.Db));
        Assert.Equal("failed 0 1 kobo.unexpected_response", await RowAsync(harness.Db, "vLive"));
        var untilRetry = await TestData.ScalarAsync<double>(harness.Db,
            "SELECT extract(epoch FROM next_attempt_at - now())::float8 FROM tamiza.form_versions WHERE uid = 'vLive'");
        Assert.InRange(untilRetry, 3500, 3600);
    }

    [Fact]
    public async Task A_label_missing_in_the_default_language_is_recorded_without_a_label()
    {
        const string form = """
            {
              "name": "Languages",
              "content": {
                "translations": ["Spanish (es)", "English (en)"],
                "survey": [{ "type": "select_one wells", "name": "well", "label": ["Pozo", "Well"] }],
                "choices": [
                  { "list_name": "wells", "name": "1", "label": ["Pozo", "Well"] },
                  { "list_name": "wells", "name": "3", "label": [null, "Tank"] }
                ]
              }
            }
            """;
        _kobo.WithForm(form);
        await using var harness = await HarnessAsync();

        var labels = await TestData.ScalarAsync<string>(harness.Db,
            "SELECT string_agg(format('%s=%s', c->>'name', coalesce(c->>'label', '<none>')), ',' ORDER BY c->>'name') FROM tamiza.projects, jsonb_array_elements(form_choices) c");

        Assert.Equal("1=Pozo,3=<none>", labels);
    }

    [Fact]
    public async Task A_question_label_missing_in_the_default_language_is_recorded_without_a_label()
    {
        const string form = """
            {
              "name": "Languages",
              "content": {
                "translations": ["Spanish (es)", "English (en)"],
                "survey": [
                  { "type": "text", "name": "name", "$xpath": "name", "label": ["Nombre", "Name"] },
                  { "type": "text", "name": "notes", "$xpath": "notes", "label": [null, "Notes"] }
                ]
              }
            }
            """;
        _kobo.WithForm(form);
        await using var harness = await HarnessAsync();

        var labels = await TestData.ScalarAsync<string>(harness.Db,
            "SELECT string_agg(format('%s=%s', f->>'name', coalesce(f->>'label', '<none>')), ',' ORDER BY f->>'name') FROM tamiza.projects, jsonb_array_elements(form_fields) f");

        Assert.Equal("name=Nombre,notes=<none>", labels);
    }

    [Fact]
    public async Task Questions_in_repeat_groups_are_marked_in_the_snapshot()
    {
        const string form = """
            {
              "name": "Repeats",
              "content": {
                "survey": [
                  { "type": "begin_group", "name": "respondent", "label": ["Respondent"] },
                  { "type": "select_one municipalities", "name": "municipality", "label": ["Municipality"] },
                  { "type": "end_group" },
                  { "type": "begin_repeat", "name": "members", "label": ["Members"] },
                  { "type": "integer", "name": "member_age", "label": ["Age"] },
                  { "type": "end_repeat" }
                ],
                "choices": [ { "list_name": "municipalities", "name": "cali", "label": ["Cali"] } ]
              }
            }
            """;
        _kobo.WithForm(form);
        await using var harness = await HarnessAsync();

        var marks = await TestData.ScalarAsync<string>(harness.Db,
            "SELECT string_agg(format('%s=%s', f->>'name', f->>'inRepeat'), ',' ORDER BY f->>'name') FROM tamiza.projects, jsonb_array_elements(form_fields) f");

        Assert.Equal("member_age=true,municipality=false", marks);
    }

    [Fact]
    public async Task A_second_synchronization_reads_the_stored_deployed_version_without_a_request()
    {
        _kobo.WithForm(KoboStub.Deployed(DraftJson, deployedVersionId: "vLive", versionId: "vDraft")).WithVersion("vLive", KoboStub.FormJson);
        await using var harness = await HarnessAsync(scheduler: true);
        await harness.WaitForIdleAsync();
        Assert.Equal(["vLive"], _kobo.VersionRequests());

        await harness.SyncAsync();
        await harness.SyncAsync();

        // Creation fetched the version once; both synchronizations used the stored definition.
        Assert.Equal(["vLive"], _kobo.VersionRequests());
        Assert.Equal("start,end,municipality,sex,enumerator", await FieldNamesAsync(harness.Db));
        Assert.Equal("vLive", await SnapshotVersionAsync(harness.Db));
    }

    [Fact]
    public async Task Creation_recheck_token_replacement_and_synchronization_record_the_version_they_describe()
    {
        _kobo.WithForm();
        await using var harness = await HarnessAsync(scheduler: true);
        await harness.WaitForIdleAsync();
        Assert.Null(await SnapshotVersionAsync(harness.Db));

        // Re-check after a deployment (the latest content is the deployed version).
        _kobo.WithForm(KoboStub.Deployed(KoboStub.FormJson, deployedVersionId: "vA"));
        (await harness.Admin.PostAsync($"/api/v1/projects/{harness.ProjectId}/kobo-check", null)).EnsureSuccessStatusCode();
        Assert.Equal("vA", await SnapshotVersionAsync(harness.Db));

        // Token replacement after a redeployment with a draft on top.
        _kobo.WithForm(KoboStub.Deployed(DraftJson, deployedVersionId: "vB", versionId: "vC"), token: "rotated-token").WithVersion("vB", KoboStub.FormJson);
        (await harness.Admin.PutAsJsonAsync($"/api/v1/projects/{harness.ProjectId}/kobo-token", new { apiToken = "rotated-token" })).EnsureSuccessStatusCode();
        Assert.Equal("vB", await SnapshotVersionAsync(harness.Db));
        Assert.Equal("start,end,municipality,sex,enumerator", await FieldNamesAsync(harness.Db));

        // A synchronization after another redeployment.
        _kobo.WithForm(KoboStub.Deployed(KoboStub.FormJson, deployedVersionId: "vD"), token: "rotated-token");
        await harness.SyncAsync();
        Assert.Equal("vD", await SnapshotVersionAsync(harness.Db));
        Assert.Equal(
            "vA available,vB available,vD available",
            await TestData.ScalarAsync<string>(harness.Db, "SELECT string_agg(format('%s %s', uid, status), ',' ORDER BY uid) FROM tamiza.form_versions"));
    }

    public void Dispose() => _kobo.Dispose();
}
