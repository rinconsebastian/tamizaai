using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Indicators;

/// <summary>
/// <c>PUT /projects/{id}/dashboards/{dashboardId}/source</c> and the source a dashboard's GET returns (custom-dashboards: Dashboard
/// source). What a source save does to the dashboard's versions and to a duplicate is <c>DashboardVersionTests</c>' and
/// <c>DashboardTests</c>'.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DashboardSourceTests(PostgresFixture postgres)
{
    private const string Survey = "Encuesta de ejemplo, 120 respuestas, primera ronda";

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string? SourceOf(JsonElement dashboard) =>
        dashboard.TryGetProperty("source", out var source) ? source.GetString() : null;

    private Task<IndicatorsHarness> CreateAsync() => IndicatorsHarness.CreateAsync(postgres);

    private static Task<string?> StoredSourceAsync(IndicatorsHarness h, Guid? dashboardId = null) =>
        TestData.ScalarAsync<string?>(h.Db, "SELECT source FROM tamiza.dashboards WHERE id = @id", ("id", dashboardId ?? h.DashboardId));

    private static async Task<long> VersionCountAsync(IndicatorsHarness h, Guid? dashboardId = null) =>
        await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboard_versions WHERE dashboard_id = @id", ("id", dashboardId ?? h.DashboardId));

    [Fact]
    public async Task An_analyst_saves_a_source_that_every_member_reads_on_that_dashboard_only()
    {
        await using var h = await CreateAsync();
        var field = await h.AddDashboardAsync("Field");

        var saved = await h.SaveSourceAsync(Survey);

        Assert.Equal(Survey, SourceOf(saved));
        foreach (var client in new[] { h.Viewer, h.Analyst, h.Admin })
        {
            Assert.Equal(Survey, SourceOf(await h.GetDashboardAsync(client: client)));
        }

        Assert.Null(SourceOf(await h.GetDashboardAsync(field, h.Viewer)));
        Assert.Equal(Survey, await StoredSourceAsync(h));
        Assert.Null(await StoredSourceAsync(h, field));
    }

    [Fact]
    public async Task An_admin_saves_a_source_and_it_is_stored_trimmed()
    {
        await using var h = await CreateAsync();

        var saved = await h.SaveSourceAsync("  " + Survey + "  ", h.Admin);

        Assert.Equal(Survey, SourceOf(saved));
        Assert.Equal(Survey, await StoredSourceAsync(h));
    }

    [Fact]
    public async Task A_source_of_300_characters_is_saved_and_read_back_whole()
    {
        await using var h = await CreateAsync();
        var text = new string('s', 300);

        Assert.Equal(text, SourceOf(await h.SaveSourceAsync(text)));

        Assert.Equal(text, SourceOf(await h.GetDashboardAsync(client: h.Viewer)));
    }

    [Fact]
    public async Task A_source_is_read_with_its_accents_and_marks_as_it_was_written()
    {
        await using var h = await CreateAsync();
        const string text = "Encuesta del año <b>2026</b> & \"otra\" (100 %)";

        await h.SaveSourceAsync(text);

        Assert.Equal(text, SourceOf(await h.GetDashboardAsync(client: h.Viewer)));
        Assert.Equal(text, await StoredSourceAsync(h));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task A_blank_or_null_source_removes_it(string? blank)
    {
        await using var h = await CreateAsync();
        await h.SaveSourceAsync(Survey);

        var removed = await h.SaveSourceAsync(blank);

        Assert.Null(SourceOf(removed));
        Assert.Equal(JsonValueKind.Null, removed.GetProperty("source").ValueKind);
        var dashboard = await h.GetDashboardAsync(client: h.Viewer);
        Assert.False(dashboard.TryGetProperty("source", out _));
        Assert.Null(await StoredSourceAsync(h));
    }

    [Fact]
    public async Task A_dashboard_with_no_source_returns_json_with_no_source_key()
    {
        await using var h = await CreateAsync();
        var field = await h.AddDashboardAsync("Field");

        foreach (var id in new[] { h.DashboardId, field })
        {
            var dashboard = await h.GetDashboardAsync(id, h.Viewer);
            Assert.False(dashboard.TryGetProperty("source", out _));
            Assert.DoesNotContain("source", dashboard.GetRawText());
            Assert.Equal(["id", "name", "filters", "palette", "valueColors", "widgets", "version"], dashboard.EnumerateObject().Select(p => p.Name));
        }
    }

    [Fact]
    public async Task A_source_comes_last_in_the_dashboard_json()
    {
        await using var h = await CreateAsync();
        await h.SaveSourceAsync(Survey);

        var dashboard = await h.GetDashboardAsync(client: h.Viewer);

        Assert.Equal(["id", "name", "filters", "palette", "valueColors", "widgets", "version", "source"], dashboard.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task A_source_of_301_characters_is_refused_on_source_naming_300_and_the_source_is_unchanged()
    {
        await using var h = await CreateAsync();
        await h.SaveSourceAsync(Survey);
        var before = await h.StoredDashboardsAsync();
        var versions = await VersionCountAsync(h);

        var response = await h.PutSourceAsync(new string('s', 301));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["The source is longer than 300 characters."], IndicatorsHarness.Errors(await ReadAsync(response), "source"));
        Assert.Equal(before, await h.StoredDashboardsAsync());
        Assert.Equal(versions, await VersionCountAsync(h));
        Assert.Equal(Survey, SourceOf(await h.GetDashboardAsync(client: h.Viewer)));
    }

    [Theory]
    [InlineData("Encuesta\nde ejemplo")]
    [InlineData("Encuesta\tde ejemplo")]
    [InlineData("Encuesta\0de ejemplo")]
    [InlineData("Encuesta de ejemplo\r\n")]
    public async Task A_source_with_a_line_break_or_another_control_character_is_refused_and_nothing_is_saved(string text)
    {
        await using var h = await CreateAsync();
        var before = await h.StoredDashboardsAsync();

        var response = await h.PutSourceAsync(text);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["The source cannot contain line breaks or other control characters."], IndicatorsHarness.Errors(await ReadAsync(response), "source"));
        Assert.Equal(before, await h.StoredDashboardsAsync());
        Assert.Null(await StoredSourceAsync(h));
    }

    [Fact]
    public async Task A_request_with_no_body_is_refused_and_does_not_remove_the_source()
    {
        await using var h = await CreateAsync();
        await h.SaveSourceAsync(Survey);

        var response = await h.Analyst.PutAsync(h.SourceUrl(), null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(IndicatorsHarness.Errors(await ReadAsync(response), "source"));
        Assert.Equal(Survey, await StoredSourceAsync(h));
    }

    [Fact]
    public async Task A_viewer_cannot_change_the_source()
    {
        await using var h = await CreateAsync();
        var before = await h.StoredDashboardsAsync();

        var response = await h.PutSourceAsync(Survey, h.Viewer);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await h.StoredDashboardsAsync());
        Assert.Null(SourceOf(await h.GetDashboardAsync(client: h.Viewer)));
    }

    [Fact]
    public async Task A_user_who_is_not_a_member_gets_the_404_of_a_project_that_does_not_exist()
    {
        await using var h = await CreateAsync();
        var before = await h.StoredDashboardsAsync();

        var response = await h.PutSourceAsync(Survey, h.Outsider);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await h.StoredDashboardsAsync());
    }

    [Fact]
    public async Task A_source_saved_on_a_removed_dashboard_is_refused_as_not_found()
    {
        await using var h = await CreateAsync();
        var health = await h.AddDashboardAsync("Health");
        await h.RemoveDashboardAsync(health);

        var response = await h.PutSourceAsync(Survey, dashboardId: health);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("dashboards.notFound", (await ReadAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_dashboard_of_another_project_is_not_found()
    {
        await using var h = await CreateAsync();
        var (otherProject, otherDashboard) = await h.AddOtherProjectAsync();

        var response = await h.Analyst.PutAsJsonAsync(IndicatorsHarness.DashboardUrlOf(h.ProjectId, otherDashboard) + "/source", new { source = Survey });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(await TestData.ScalarAsync<string?>(h.Db, "SELECT source FROM tamiza.dashboards WHERE id = @id", ("id", otherDashboard)));
        Assert.NotEqual(otherProject, h.ProjectId);
    }

    [Fact]
    public async Task Saving_the_stored_source_writes_nothing_and_makes_no_version()
    {
        await using var h = await CreateAsync();
        await h.SaveSourceAsync(Survey);
        var dashboards = await h.StoredDashboardsAsync();
        var versions = await VersionCountAsync(h);

        var again = await h.SaveSourceAsync(Survey);
        var padded = await h.SaveSourceAsync("  " + Survey + " ");

        Assert.Equal(Survey, SourceOf(again));
        Assert.Equal(Survey, SourceOf(padded));
        Assert.Equal(dashboards, await h.StoredDashboardsAsync());
        Assert.Equal(versions, await VersionCountAsync(h));
    }

    [Fact]
    public async Task Removing_a_source_the_dashboard_does_not_have_writes_nothing_and_makes_no_version()
    {
        await using var h = await CreateAsync();
        var dashboards = await h.StoredDashboardsAsync();
        var versions = await VersionCountAsync(h);

        await h.SaveSourceAsync(null);
        await h.SaveSourceAsync("   ");

        Assert.Equal(dashboards, await h.StoredDashboardsAsync());
        Assert.Equal(versions, await VersionCountAsync(h));
    }

    [Fact]
    public async Task A_changed_source_moves_the_dashboards_last_change()
    {
        await using var h = await CreateAsync();
        var before = await TestData.ScalarAsync<DateTime>(h.Db, "SELECT updated_at FROM tamiza.dashboards WHERE id = @id", ("id", h.DashboardId));

        await h.SaveSourceAsync(Survey, h.Admin);

        var after = await TestData.ScalarAsync<DateTime>(h.Db, "SELECT updated_at FROM tamiza.dashboards WHERE id = @id", ("id", h.DashboardId));
        Assert.True(after > before);
    }

    [Fact]
    public async Task Of_two_saves_the_later_wins()
    {
        await using var h = await CreateAsync();

        await h.SaveSourceAsync("Primera fuente");
        await h.SaveSourceAsync("Segunda fuente", h.Admin);

        Assert.Equal("Segunda fuente", SourceOf(await h.GetDashboardAsync(client: h.Viewer)));
    }
}
