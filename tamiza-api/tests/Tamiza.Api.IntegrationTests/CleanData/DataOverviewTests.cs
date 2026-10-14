using System.Net;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.CleanData;

/// <summary><c>GET /projects/{id}/data</c>: the published sources, their tables, and when and by which run they were published.</summary>
[Collection(PostgresCollection.Name)]
public sealed class DataOverviewTests(PostgresFixture postgres)
{
    private static string[] SourceNames(JsonElement overview) =>
        overview.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("source").GetString()!).ToArray();

    private static JsonElement Source(JsonElement overview, string name) =>
        overview.GetProperty("sources").EnumerateArray().Single(s => s.GetProperty("source").GetString() == name);

    private static (string Name, long? Rows)[] Tables(JsonElement source) =>
        source.GetProperty("tables").EnumerateArray()
            .Select(t => (t.GetProperty("name").GetString()!, t.GetProperty("rowCount").ValueKind == JsonValueKind.Null ? (long?)null : t.GetProperty("rowCount").GetInt64()))
            .ToArray();

    [Fact]
    public async Task A_viewer_sees_the_output_tables_first_when_script_2_published_them()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("clean", ("main", "name text", "('a'), ('b')"));
        await h.PublishAsync("output", ("households", "size bigint", "(1), (2), (3)"), ("persons", "name text", "('x')"));

        var overview = await h.GetOverviewAsync();

        Assert.Equal(["output", "clean"], SourceNames(overview));
        Assert.Equal("output", overview.GetProperty("defaultSource").GetString());
        Assert.Equal([("households", 3L), ("persons", 1L)], Tables(Source(overview, "output")));
        Assert.Equal([("main", 2L)], Tables(Source(overview, "clean")));
    }

    [Fact]
    public async Task Without_script_2_only_the_clean_tables_are_offered_and_they_are_the_default()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("clean", ("main", "name text", "('a')"), ("members", "name text", "('b'), ('c')"));

        var overview = await h.GetOverviewAsync();

        Assert.Equal(["clean"], SourceNames(overview));
        Assert.Equal("clean", overview.GetProperty("defaultSource").GetString());
        Assert.Equal([("main", 1L), ("members", 2L)], Tables(Source(overview, "clean")));
    }

    [Fact]
    public async Task With_nothing_published_the_list_is_empty_and_there_is_no_default()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);

        var overview = await h.GetOverviewAsync();

        Assert.Empty(overview.GetProperty("sources").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, overview.GetProperty("defaultSource").ValueKind);
    }

    [Fact]
    public async Task The_raw_and_indicator_schemas_are_never_offered()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.ExecuteAsync($"CREATE SCHEMA \"{h.Schema("raw")}\"; CREATE TABLE \"{h.Schema("raw")}\".main (name text);");
        await h.PublishAsync("indicators", ("indicators", "value double precision", "(1.0)"));

        var overview = await h.GetOverviewAsync();

        Assert.Empty(overview.GetProperty("sources").EnumerateArray());
    }

    [Fact]
    public async Task The_source_names_the_run_and_time_that_published_it()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        var run = await h.PublishAsync("output", ("households", "size bigint", "(1)"));

        var output = Source(await h.GetOverviewAsync(), "output");

        Assert.Equal(run, output.GetProperty("runId").GetGuid());
        Assert.True(DateTimeOffset.UtcNow - output.GetProperty("publishedAt").GetDateTimeOffset() < TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task The_overview_carries_the_projects_time_zone()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres, timeZone: "America/Bogota");

        var overview = await h.GetOverviewAsync();

        Assert.Equal("America/Bogota", overview.GetProperty("timeZone").GetString());
    }

    [Fact]
    public async Task A_failed_run_leaves_the_earlier_tables_and_their_run_in_the_list()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        var published = await h.PublishAsync("output", ("households", "size bigint", "(1), (2)"));
        // The next run's script 2 failed: it published nothing and recorded a failed step.
        await PublishedTables.RecordStepAsync(h.Db, h.ProjectId, "output", [], status: "failed", finishedAt: DateTimeOffset.UtcNow.AddMinutes(1));

        var output = Source(await h.GetOverviewAsync(), "output");

        Assert.Equal(published, output.GetProperty("runId").GetGuid());
        Assert.Equal([("households", 2L)], Tables(output));
    }

    [Fact]
    public async Task A_table_no_run_recorded_is_listed_without_a_row_count()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.ExecuteAsync($"CREATE SCHEMA \"{h.Schema("output")}\"; CREATE TABLE \"{h.Schema("output")}\".loose (a bigint);");

        var output = Source(await h.GetOverviewAsync(), "output");

        Assert.Equal([("loose", (long?)null)], Tables(output));
        Assert.Equal(JsonValueKind.Null, output.GetProperty("runId").ValueKind);
        Assert.Equal(JsonValueKind.Null, output.GetProperty("publishedAt").ValueKind);
    }

    [Fact]
    public async Task Another_projects_tables_are_never_listed()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        var other = Guid.NewGuid();
        await h.ExecuteAsync($"CREATE SCHEMA \"p_{other:N}_output\"; CREATE TABLE \"p_{other:N}_output\".secret (a bigint);");

        var overview = await h.GetOverviewAsync();

        Assert.Empty(overview.GetProperty("sources").EnumerateArray());
    }

    [Fact]
    public async Task Every_role_may_read_it_and_a_non_member_gets_404()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("clean", ("main", "name text", "('a')"));
        await TestData.SignInAsync(h.Factory, "mallory", "mallory@example.org");
        using var stranger = TestData.ClientFor(h.Factory, "mallory", "mallory@example.org");

        Assert.Equal(HttpStatusCode.OK, (await h.Viewer.GetAsync(h.Url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Analyst.GetAsync(h.Url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Admin.GetAsync(h.Url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(h.Url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.Viewer.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/data")).StatusCode);
    }

    [Fact]
    public async Task A_request_without_a_token_is_refused()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        using var anonymous = h.Factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(h.Url)).StatusCode);
    }
}
