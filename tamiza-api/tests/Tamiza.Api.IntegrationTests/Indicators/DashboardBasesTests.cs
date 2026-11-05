using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Indicators;

/// <summary>
/// <c>PUT /projects/{id}/dashboards/{dashboardId}/bases</c> and the base settings a dashboard's GET returns (design D8 of
/// add-small-sample-warnings; dashboard-indicators: Base settings of a dashboard). What a save does to the dashboard's versions and to a
/// duplicate is <c>DashboardVersionTests</c>' and <c>DashboardTests</c>'.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DashboardBasesTests(PostgresFixture postgres)
{
    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private Task<IndicatorsHarness> CreateAsync() => IndicatorsHarness.CreateAsync(postgres);

    private static async Task<(int? MinN, bool ShowN)> StoredAsync(IndicatorsHarness h, Guid? dashboardId = null)
    {
        var minN = await TestData.ScalarAsync<int?>(h.Db, "SELECT min_n FROM tamiza.dashboards WHERE id = @id", ("id", dashboardId ?? h.DashboardId));
        var showN = await TestData.ScalarAsync<bool>(h.Db, "SELECT show_n FROM tamiza.dashboards WHERE id = @id", ("id", dashboardId ?? h.DashboardId));
        return (minN, showN);
    }

    private static async Task<long> VersionCountAsync(IndicatorsHarness h, Guid? dashboardId = null) =>
        await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboard_versions WHERE dashboard_id = @id", ("id", dashboardId ?? h.DashboardId));

    private static Task<DateTime> UpdatedAtAsync(IndicatorsHarness h) =>
        TestData.ScalarAsync<DateTime>(h.Db, "SELECT updated_at FROM tamiza.dashboards WHERE id = @id", ("id", h.DashboardId));

    private static readonly string[] WithoutBases = ["id", "name", "filters", "palette", "valueColors", "widgets", "version"];

    // Analyst sets the threshold

    [Fact]
    public async Task Analyst_sets_the_threshold()
    {
        await using var h = await CreateAsync();
        var field = await h.AddDashboardAsync("Field");

        var saved = await h.SaveBasesAsync(30, true);

        Assert.Equal(30, saved.GetProperty("minN").GetInt32());
        Assert.True(saved.GetProperty("showN").GetBoolean());
        // Every member who opens the dashboard reads them, and no other dashboard does.
        foreach (var client in new[] { h.Viewer, h.Analyst, h.Admin })
        {
            var dashboard = await h.GetDashboardAsync(client: client);
            Assert.Equal(30, dashboard.GetProperty("minN").GetInt32());
            Assert.True(dashboard.GetProperty("showN").GetBoolean());
        }

        Assert.Equal(WithoutBases, (await h.GetDashboardAsync(field, h.Viewer)).EnumerateObject().Select(p => p.Name));
        Assert.Equal((30, true), await StoredAsync(h));
        Assert.Equal((null, false), await StoredAsync(h, field));
    }

    [Fact]
    public async Task An_admin_saves_them_too_and_the_threshold_may_be_set_without_the_line_and_the_line_without_the_threshold()
    {
        await using var h = await CreateAsync();

        await h.SaveBasesAsync(50, null, h.Admin);
        Assert.Equal((50, false), await StoredAsync(h));

        await h.SaveBasesAsync(null, true, h.Admin);
        Assert.Equal((null, true), await StoredAsync(h));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10000)]
    public async Task The_ends_of_the_range_are_saved(int minN)
    {
        await using var h = await CreateAsync();

        var saved = await h.SaveBasesAsync(minN, false);

        Assert.Equal(minN, saved.GetProperty("minN").GetInt32());
        Assert.Equal((minN, false), await StoredAsync(h));
    }

    [Fact]
    public async Task A_null_threshold_turns_it_off_and_a_missing_line_is_off()
    {
        await using var h = await CreateAsync();
        await h.SaveBasesAsync(30, true);

        var off = await h.SaveBasesAsync(null, null);

        Assert.Equal(JsonValueKind.Null, off.GetProperty("minN").ValueKind);
        Assert.False(off.GetProperty("showN").GetBoolean());
        Assert.Equal((null, false), await StoredAsync(h));
    }

    // Viewer cannot change base settings

    [Fact]
    public async Task Viewer_cannot_change_base_settings()
    {
        await using var h = await CreateAsync();
        var before = await h.StoredDashboardsAsync();
        var versions = await VersionCountAsync(h);

        var response = await h.PutBasesAsync(30, true, h.Viewer);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await h.StoredDashboardsAsync());
        Assert.Equal(versions, await VersionCountAsync(h));
    }

    [Fact]
    public async Task A_user_who_is_not_a_member_gets_the_404_of_a_project_that_does_not_exist()
    {
        await using var h = await CreateAsync();
        var before = await h.StoredDashboardsAsync();

        var response = await h.PutBasesAsync(30, true, h.Outsider);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await h.StoredDashboardsAsync());
    }

    // Base settings saved unchanged

    [Fact]
    public async Task Base_settings_saved_unchanged()
    {
        await using var h = await CreateAsync();
        await h.SaveBasesAsync(30, true);
        var dashboards = await h.StoredDashboardsAsync();
        var updatedAt = await UpdatedAtAsync(h);
        var versions = await VersionCountAsync(h);

        var again = await h.SaveBasesAsync(30, true);

        Assert.Equal(30, again.GetProperty("minN").GetInt32());
        Assert.Equal(dashboards, await h.StoredDashboardsAsync());
        Assert.Equal(updatedAt, await UpdatedAtAsync(h));
        Assert.Equal(versions, await VersionCountAsync(h));
    }

    [Fact]
    public async Task Turning_off_what_is_off_writes_nothing_and_makes_no_version()
    {
        await using var h = await CreateAsync();
        var dashboards = await h.StoredDashboardsAsync();
        var versions = await VersionCountAsync(h);

        await h.SaveBasesAsync(null, false);
        await h.SaveBasesAsync(null, null);

        Assert.Equal(dashboards, await h.StoredDashboardsAsync());
        Assert.Equal(versions, await VersionCountAsync(h));
    }

    [Fact]
    public async Task A_changed_setting_moves_the_dashboards_last_change()
    {
        await using var h = await CreateAsync();
        var before = await UpdatedAtAsync(h);

        await h.SaveBasesAsync(30, false, h.Admin);

        Assert.True(await UpdatedAtAsync(h) > before);
    }

    // Threshold out of range

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10001)]
    public async Task Threshold_out_of_range(int minN)
    {
        await using var h = await CreateAsync();
        await h.SaveBasesAsync(30, true);
        var before = await h.StoredDashboardsAsync();
        var versions = await VersionCountAsync(h);

        var response = await h.PutBasesAsync(minN, false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ["The small-base threshold must be a whole number from 1 to 10,000; leave it empty to turn it off."],
            IndicatorsHarness.Errors(await ReadAsync(response), "minN"));
        Assert.Equal(before, await h.StoredDashboardsAsync());
        Assert.Equal(versions, await VersionCountAsync(h));
    }

    [Fact]
    public async Task A_request_with_no_body_is_refused_and_does_not_turn_the_settings_off()
    {
        await using var h = await CreateAsync();
        await h.SaveBasesAsync(30, true);

        var response = await h.Analyst.PutAsync(h.BasesUrl(), null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(IndicatorsHarness.Errors(await ReadAsync(response), "minN"));
        Assert.Equal((30, true), await StoredAsync(h));
    }

    [Fact]
    public async Task The_check_of_the_table_refuses_a_threshold_out_of_range_too()
    {
        await using var h = await CreateAsync();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => h.ExecuteAsync("UPDATE tamiza.dashboards SET min_n = 0 WHERE id = @id", ("id", h.DashboardId)));

        Assert.Contains("ck_dashboards_min_n", ex.ToString());
    }

    // Removed and foreign dashboards

    [Fact]
    public async Task Settings_saved_on_a_removed_dashboard_are_refused_as_not_found()
    {
        await using var h = await CreateAsync();
        var health = await h.AddDashboardAsync("Health");
        await h.RemoveDashboardAsync(health);

        var response = await h.PutBasesAsync(30, true, dashboardId: health);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("dashboards.notFound", (await ReadAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_dashboard_of_another_project_is_not_found()
    {
        await using var h = await CreateAsync();
        var (_, otherDashboard) = await h.AddOtherProjectAsync();

        var response = await h.Analyst.PutAsJsonAsync(IndicatorsHarness.DashboardUrlOf(h.ProjectId, otherDashboard) + "/bases", new { minN = 30, showN = true });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal((null, false), await StoredAsync(h, otherDashboard));
    }

    // The view

    [Fact]
    public async Task The_view_of_a_dashboard_without_base_settings_is_what_it_was()
    {
        await using var h = await CreateAsync();

        var dashboard = await h.GetDashboardAsync(client: h.Viewer);

        Assert.Equal(WithoutBases, dashboard.EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain("minN", dashboard.GetRawText());
        Assert.DoesNotContain("showN", dashboard.GetRawText());
    }

    [Fact]
    public async Task The_settings_come_after_the_source_and_each_is_written_only_when_it_is_set()
    {
        await using var h = await CreateAsync();
        await h.SaveSourceAsync("Encuesta de ejemplo");

        await h.SaveBasesAsync(30, true);
        Assert.Equal([.. WithoutBases, "source", "minN", "showN"], (await h.GetDashboardAsync(client: h.Viewer)).EnumerateObject().Select(p => p.Name));

        await h.SaveBasesAsync(30, false);
        Assert.Equal([.. WithoutBases, "source", "minN"], (await h.GetDashboardAsync(client: h.Viewer)).EnumerateObject().Select(p => p.Name));

        await h.SaveBasesAsync(null, true);
        Assert.Equal([.. WithoutBases, "source", "showN"], (await h.GetDashboardAsync(client: h.Viewer)).EnumerateObject().Select(p => p.Name));

        await h.SaveBasesAsync(null, false);
        Assert.Equal([.. WithoutBases, "source"], (await h.GetDashboardAsync(client: h.Viewer)).EnumerateObject().Select(p => p.Name));
    }

    // OpenAPI

    [Fact]
    public async Task The_openapi_document_describes_the_request_the_response_and_the_views_fields()
    {
        await using var h = await CreateAsync();
        using var anonymous = h.Factory.CreateClient();

        var document = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/openapi.json");

        var put = document.GetProperty("paths").GetProperty("/api/v1/projects/{projectId}/dashboards/{dashboardId}/bases").GetProperty("put");
        Assert.Equal("SaveDashboardBases", put.GetProperty("operationId").GetString());
        var schemas = document.GetProperty("components").GetProperty("schemas");
        var request = schemas.GetProperty("DashboardBasesRequest").GetProperty("properties");
        Assert.Contains("1 to 10,000", request.GetProperty("minN").GetProperty("description").GetString());
        Assert.True(request.TryGetProperty("showN", out _));
        Assert.Equal(["minN", "showN"], schemas.GetProperty("DashboardBases").GetProperty("properties").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        var view = schemas.GetProperty("DashboardView").GetProperty("properties");
        Assert.True(view.TryGetProperty("minN", out _), "DashboardView.minN");
        Assert.True(view.TryGetProperty("showN", out _), "DashboardView.showN");
        Assert.DoesNotContain(
            schemas.GetProperty("DashboardView").TryGetProperty("required", out var required) ? required.EnumerateArray().Select(r => r.GetString()).ToArray() : [],
            name => name is "minN" or "showN");
    }
}
