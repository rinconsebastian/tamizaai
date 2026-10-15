using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tamiza.Api.IntegrationTests.CleanData;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Pipeline;

/// <summary>
/// Sample downloads: queued by an analyst, prepared by the analytics worker (here written with SQL, as it would), read by their
/// requester only, and handed over through single-use links.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed partial class SampleDownloadsApiTests(PostgresFixture postgres)
{
    private const string Contents = """
        {"sample":{"requested":500,"submissions":3},"raw":{"tables":[{"name":"main","rows":3},{"name":"members","rows":6}]},
         "clean":{"status":"succeeded","version":2,"error":null,"tables":[{"name":"main","rows":2}]},
         "reference":[{"name":"population","rows":2,"updatedAt":"2026-10-19T10:00:00+00:00"}]}
        """;

    private static string Base(CleanDataHarness h) => $"/api/v1/projects/{h.ProjectId}/sample-downloads";

    private static string Id(JsonElement download) => download.GetProperty("id").GetString()!;

    private static async Task<JsonElement> StartAsync(HttpClient client, CleanDataHarness h, object? body = null)
    {
        var response = body is null ? await client.PostAsync(Base(h), null) : await client.PostAsJsonAsync(Base(h), body);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<HttpClient> OutsiderAsync(CleanDataHarness h)
    {
        await TestData.SignInAsync(h.Factory, "mallory", "mallory@example.org");
        return TestData.ClientFor(h.Factory, "mallory", "mallory@example.org");
    }

    /// <summary>A zip of a few kilobytes that cannot be shrunk, so it spans several parts.</summary>
    private static byte[] MakeZip()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("tamiza-sample-20261020-140311/raw/main.parquet", CompressionLevel.NoCompression);
            using var writer = entry.Open();
            writer.Write(System.Security.Cryptography.RandomNumberGenerator.GetBytes(3_000));
        }

        return stream.ToArray();
    }

    /// <summary>
    /// A download of <paramref name="email"/>'s as the worker leaves it: <paramref name="status"/> and, when it is ready, the zip in
    /// parts of 1,000 bytes. <paramref name="endedMinutesAgo"/> is when a finished download ended.
    /// </summary>
    private static async Task<Guid> AddAsync(
        CleanDataHarness h, string email, string status = "succeeded", byte[]? zip = null, int endedMinutesAgo = 1, long? declaredSize = null)
    {
        var id = Guid.NewGuid();
        var finished = status is "succeeded" or "failed";
        zip ??= MakeZip();
        await TestData.ExecuteAsync(h.Db, """
            INSERT INTO tamiza.sample_downloads
                (id, project_id, requested_by, sample_size, status, queued_at, started_at, finished_at, size_bytes, file_sha256, contents, error_code, error)
            VALUES (@id, @p, (SELECT id FROM tamiza.users WHERE email = @email), 500, @status, now() - interval '70 minutes',
                    CASE WHEN @status <> 'queued' THEN now() - interval '69 minutes' END,
                    CASE WHEN @finished THEN now() - make_interval(mins => @ago) END,
                    CASE WHEN @status = 'succeeded' THEN @size END, CASE WHEN @status = 'succeeded' THEN repeat('a', 64) END,
                    CASE WHEN @status = 'succeeded' THEN @contents::jsonb END,
                    CASE WHEN @status = 'failed' THEN 'step.flatten' END, CASE WHEN @status = 'failed' THEN 'Flattening failed.' END)
            """,
            ("id", id), ("p", h.ProjectId), ("email", email), ("status", status), ("finished", finished), ("ago", endedMinutesAgo),
            ("size", declaredSize ?? zip.Length), ("contents", Contents));
        if (status == "succeeded")
        {
            for (var part = 0; part * 1_000 < zip.Length; part++)
            {
                await TestData.ExecuteAsync(h.Db,
                    "INSERT INTO tamiza.sample_download_parts (download_id, part, data) VALUES (@id, @part, @data)",
                    ("id", id), ("part", part), ("data", zip[(part * 1_000)..Math.Min(zip.Length, (part + 1) * 1_000)]));
            }
        }

        return id;
    }

    // Requesting a download

    [Fact]
    public async Task An_analyst_queues_a_download_without_creating_a_version_a_run_or_a_test()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);

        var download = await StartAsync(h.Analyst, h, new { sampleSize = 300 });

        Assert.Equal("queued", download.GetProperty("status").GetString());
        Assert.Equal(300, download.GetProperty("sampleSize").GetInt32());
        Assert.Equal(JsonValueKind.Null, download.GetProperty("contents").ValueKind);
        Assert.Equal(JsonValueKind.Null, download.GetProperty("expiresAt").ValueKind);
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_versions"));
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.runs"));
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.script_tests"));
        var read = await h.Analyst.GetFromJsonAsync<JsonElement>($"{Base(h)}/{Id(download)}");
        Assert.Equal(Id(download), Id(read));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("empty")]
    public async Task The_sample_defaults_to_500_submissions(string? body)
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);

        var download = await StartAsync(h.Admin, h, body is null ? null : new { });

        Assert.Equal(500, download.GetProperty("sampleSize").GetInt32());
    }

    [Fact]
    public async Task A_viewer_cannot_request_a_download()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);

        var response = await h.Viewer.PostAsJsonAsync(Base(h), new { sampleSize = 100 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.sample_downloads"));
    }

    [Fact]
    public async Task A_user_who_is_not_a_member_gets_not_found_and_nothing_is_queued()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        using var outsider = await OutsiderAsync(h);

        var response = await outsider.PostAsJsonAsync(Base(h), new { sampleSize = 100 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.sample_downloads"));
    }

    [Theory]
    [InlineData(10000)]
    [InlineData(5001)]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task A_sample_size_out_of_range_is_rejected_and_nothing_is_queued(int sampleSize)
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);

        var response = await h.Analyst.PostAsJsonAsync(Base(h), new { sampleSize });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(problem.GetProperty("errors").TryGetProperty("sampleSize", out _));
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.sample_downloads"));
    }

    [Fact]
    public async Task A_new_request_replaces_the_users_ready_download_and_its_parts()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        var ready = await AddAsync(h, "grace@example.org");
        var others = await AddAsync(h, "ada@example.org");
        Assert.True(await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.sample_download_parts WHERE download_id = @id", ("id", ready)) > 1);

        var replacement = await StartAsync(h.Analyst, h);

        Assert.Equal(HttpStatusCode.NotFound, (await h.Analyst.GetAsync($"{Base(h)}/{ready}")).StatusCode);
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.sample_download_parts WHERE download_id = @id", ("id", ready)));
        Assert.Equal("queued", (await h.Analyst.GetFromJsonAsync<JsonElement>($"{Base(h)}/{Id(replacement)}")).GetProperty("status").GetString());
        // Another user's download is kept.
        Assert.Equal(HttpStatusCode.OK, (await h.Admin.GetAsync($"{Base(h)}/{others}")).StatusCode);
    }

    [Fact]
    public async Task A_new_request_replaces_the_users_queued_download_and_a_failed_one()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        var first = await StartAsync(h.Analyst, h);
        var second = await StartAsync(h.Analyst, h, new { sampleSize = 50 });
        await TestData.ExecuteAsync(h.Db, "UPDATE tamiza.sample_downloads SET status = 'failed', finished_at = now()");

        var third = await StartAsync(h.Analyst, h);

        Assert.Equal(HttpStatusCode.NotFound, (await h.Analyst.GetAsync($"{Base(h)}/{Id(first)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.Analyst.GetAsync($"{Base(h)}/{Id(second)}")).StatusCode);
        Assert.Equal(1, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.sample_downloads"));
        Assert.Equal(Id(third), (await h.Analyst.GetFromJsonAsync<JsonElement>($"{Base(h)}/{Id(third)}")).GetProperty("id").GetString());
    }

    [Fact]
    public async Task A_download_being_prepared_is_not_replaced()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        var running = await AddAsync(h, "grace@example.org", status: "running");

        await StartAsync(h.Analyst, h);

        var read = await h.Analyst.GetFromJsonAsync<JsonElement>($"{Base(h)}/{running}");
        Assert.Equal("running", read.GetProperty("status").GetString());
        Assert.Equal(2, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.sample_downloads"));
    }

    // Reading a download

    [Fact]
    public async Task A_ready_download_says_what_it_holds_and_until_when()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        var id = await AddAsync(h, "grace@example.org", endedMinutesAgo: 10);

        var read = await h.Analyst.GetFromJsonAsync<JsonElement>($"{Base(h)}/{id}");

        Assert.Equal("succeeded", read.GetProperty("status").GetString());
        Assert.True(read.GetProperty("sizeBytes").GetInt64() > 3_000);
        var contents = read.GetProperty("contents");
        Assert.Equal(3, contents.GetProperty("sample").GetProperty("submissions").GetInt32());
        Assert.Equal(["main", "members"], contents.GetProperty("raw").GetProperty("tables").EnumerateArray().Select(t => t.GetProperty("name").GetString()));
        Assert.Equal("succeeded", contents.GetProperty("clean").GetProperty("status").GetString());
        Assert.Equal(2, contents.GetProperty("clean").GetProperty("version").GetInt32());
        Assert.Equal("population", contents.GetProperty("reference")[0].GetProperty("name").GetString());
        var finished = read.GetProperty("finishedAt").GetDateTimeOffset();
        Assert.Equal(finished.AddHours(1), read.GetProperty("expiresAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task A_failed_download_gives_the_reason()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        var id = await AddAsync(h, "grace@example.org", status: "failed");

        var read = await h.Analyst.GetFromJsonAsync<JsonElement>($"{Base(h)}/{id}");

        Assert.Equal("failed", read.GetProperty("status").GetString());
        Assert.Equal("step.flatten", read.GetProperty("errorCode").GetString());
        Assert.Equal("Flattening failed.", read.GetProperty("error").GetString());
        Assert.Equal(JsonValueKind.Null, read.GetProperty("expiresAt").ValueKind);
    }

    [Fact]
    public async Task Another_members_download_is_not_found()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        var id = await AddAsync(h, "grace@example.org");

        Assert.Equal(HttpStatusCode.NotFound, (await h.Admin.GetAsync($"{Base(h)}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.Admin.PostAsync($"{Base(h)}/{id}/link", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Viewer.GetAsync($"{Base(h)}/{id}")).StatusCode);
    }

    [Fact]
    public async Task A_download_is_not_available_an_hour_after_it_became_ready()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        var fresh = await AddAsync(h, "grace@example.org", endedMinutesAgo: 59);
        var old = await AddAsync(h, "grace@example.org", endedMinutesAgo: 61);

        Assert.Equal(HttpStatusCode.OK, (await h.Analyst.GetAsync($"{Base(h)}/{fresh}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.Analyst.GetAsync($"{Base(h)}/{old}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.Analyst.PostAsync($"{Base(h)}/{old}/link", null)).StatusCode);
    }

    // What a queued download waits for

    [Fact]
    public async Task A_queued_download_says_what_it_waits_for()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        var earlier = await StartAsync(h.Admin, h);
        var download = await StartAsync(h.Analyst, h);
        var url = $"{Base(h)}/{Id(download)}";
        async Task<string?> WaitingFor() => (await h.Analyst.GetFromJsonAsync<JsonElement>(url)).GetProperty("waitingFor").GetString();

        Assert.Equal("download", await WaitingFor());

        await TestData.ExecuteAsync(h.Db, """
            INSERT INTO tamiza.script_tests (id, project_id, requested_by, kind, code, sample_size, status, queued_at)
            VALUES (gen_random_uuid(), @p, (SELECT id FROM tamiza.users WHERE email = 'ada@example.org'), 'clean', 'x', 500, 'queued', now())
            """, ("p", h.ProjectId));
        Assert.Equal("test", await WaitingFor());

        await TestData.ExecuteAsync(h.Db,
            "INSERT INTO tamiza.runs (id, project_id, trigger, status, queued_at, started_at) VALUES (gen_random_uuid(), @p, 'manual', 'running', now(), now())",
            ("p", h.ProjectId));
        Assert.Equal("run", await WaitingFor());

        await TestData.ExecuteAsync(h.Db, "UPDATE tamiza.runs SET status = 'succeeded', finished_at = now()");
        await TestData.ExecuteAsync(h.Db, "UPDATE tamiza.script_tests SET status = 'succeeded', finished_at = now()");
        await TestData.ExecuteAsync(h.Db, "UPDATE tamiza.sample_downloads SET status = 'succeeded', finished_at = now() WHERE id = @id", ("id", Guid.Parse(Id(earlier))));
        Assert.Null(await WaitingFor());
    }

    [Fact]
    public async Task A_test_waits_for_a_download_being_prepared_but_not_for_a_queued_one()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        var queued = await AddAsync(h, "ada@example.org", status: "queued");
        var response = await h.Analyst.PostAsJsonAsync($"/api/v1/projects/{h.ProjectId}/script-tests", new { kind = "clean", code = "def clean(raw, ctx):\n    return raw\n" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var test = await response.Content.ReadFromJsonAsync<JsonElement>();
        var url = $"/api/v1/projects/{h.ProjectId}/script-tests/{Id(test)}";
        async Task<string?> WaitingFor() => (await h.Analyst.GetFromJsonAsync<JsonElement>(url)).GetProperty("waitingFor").GetString();

        Assert.Null(await WaitingFor()); // the worker takes tests before queued downloads

        await TestData.ExecuteAsync(h.Db, "UPDATE tamiza.sample_downloads SET status = 'running', started_at = now() WHERE id = @id", ("id", queued));
        Assert.Equal("download", await WaitingFor());

        await TestData.ExecuteAsync(h.Db, "UPDATE tamiza.sample_downloads SET status = 'succeeded', finished_at = now() WHERE id = @id", ("id", queued));
        Assert.Null(await WaitingFor());
    }

    // The single-use link

    private static async Task<JsonElement> LinkAsync(HttpClient client, CleanDataHarness h, Guid id)
    {
        var response = await client.PostAsync($"{Base(h)}/{id}/link", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task The_link_downloads_the_zip_that_was_stored_once()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        var zip = MakeZip();
        var id = await AddAsync(h, "grace@example.org", zip: zip);

        var link = await LinkAsync(h.Analyst, h, id);
        using var browser = h.Factory.CreateClient();
        var response = await browser.GetAsync(link.GetProperty("downloadUrl").GetString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.Matches(FileNamePattern(), response.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        Assert.Equal(response.Content.Headers.ContentDisposition!.FileName!.Trim('"'), link.GetProperty("fileName").GetString());
        Assert.Equal(zip, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(zip.Length, link.GetProperty("sizeBytes").GetInt64());
        Assert.StartsWith("/api/v1/data-exports/", link.GetProperty("downloadUrl").GetString());
        using var archive = new ZipArchive(new MemoryStream(zip));
        Assert.Single(archive.Entries);
    }

    [GeneratedRegex(@"^[a-z0-9-]+_sample_\d{4}-\d{2}-\d{2}\.zip$")]
    private static partial Regex FileNamePattern();

    [Fact]
    public async Task A_link_works_once_and_a_new_one_can_be_asked_for()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        var id = await AddAsync(h, "grace@example.org");
        using var browser = h.Factory.CreateClient();
        var first = (await LinkAsync(h.Analyst, h, id)).GetProperty("downloadUrl").GetString();

        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(first)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync(first)).StatusCode);

        var second = (await LinkAsync(h.Analyst, h, id)).GetProperty("downloadUrl").GetString();
        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(second)).StatusCode);
    }

    [Theory]
    [InlineData("queued", "sampleDownload.notReady")]
    [InlineData("running", "sampleDownload.notReady")]
    [InlineData("failed", "sampleDownload.failed")]
    public async Task No_link_is_issued_for_a_download_that_is_not_ready(string status, string code)
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        var id = await AddAsync(h, "grace@example.org", status: status);

        var response = await h.Analyst.PostAsync($"{Base(h)}/{id}/link", null);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Empty(h.ExportFiles);
    }

    [Fact]
    public async Task Parts_shorter_than_the_recorded_size_issue_no_link_and_leave_no_file()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        var zip = MakeZip();
        var id = await AddAsync(h, "grace@example.org", zip: zip, declaredSize: zip.Length + 500);

        var response = await h.Analyst.PostAsync($"{Base(h)}/{id}/link", null);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("sampleDownload.incomplete", problem.GetProperty("code").GetString());
        Assert.Empty(h.ExportFiles);
    }

    [Fact]
    public async Task A_viewer_cannot_ask_for_a_link()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        var id = await AddAsync(h, "grace@example.org");

        Assert.Equal(HttpStatusCode.Forbidden, (await h.Viewer.PostAsync($"{Base(h)}/{id}/link", null)).StatusCode);
        Assert.Empty(h.ExportFiles);
    }

    [Fact]
    public async Task The_openapi_document_describes_the_routes_and_their_models()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        using var client = h.Factory.CreateClient();

        var document = await client.GetFromJsonAsync<JsonElement>("/api/v1/openapi.json");

        var paths = document.GetProperty("paths");
        Assert.Equal("StartSampleDownload", paths.GetProperty("/api/v1/projects/{projectId}/sample-downloads").GetProperty("post").GetProperty("operationId").GetString());
        Assert.Equal("GetSampleDownload", paths.GetProperty("/api/v1/projects/{projectId}/sample-downloads/{downloadId}").GetProperty("get").GetProperty("operationId").GetString());
        Assert.Equal("LinkSampleDownload", paths.GetProperty("/api/v1/projects/{projectId}/sample-downloads/{downloadId}/link").GetProperty("post").GetProperty("operationId").GetString());
        var schemas = document.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.GetProperty("SampleDownloadDetails").GetProperty("properties").TryGetProperty("waitingFor", out _));
        Assert.True(schemas.GetProperty("SampleDownloadLink").GetProperty("properties").TryGetProperty("downloadUrl", out _));
    }
}
