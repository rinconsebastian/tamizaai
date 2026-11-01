using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Indicators;

/// <summary>
/// <c>GET /projects/{id}/dashboards/{dashboardId}/values</c>: the values a text filter offers, across the indicator tables of the
/// dashboard's widgets (dashboard-filters: Values offered by a text filter).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DashboardValuesTests(PostgresFixture postgres)
{
    private static Task<HttpResponseMessage> GetValuesAsync(IndicatorsHarness h, string column, string? search = null, Guid? dashboardId = null, HttpClient? client = null) =>
        (client ?? h.Viewer).GetAsync(
            $"{h.DashboardUrl(dashboardId)}/values?column={Uri.EscapeDataString(column)}" + (search is null ? "" : $"&search={Uri.EscapeDataString(search)}"));

    private static async Task<string[]> ValuesAsync(IndicatorsHarness h, string column = "municipality", string? search = null, Guid? dashboardId = null)
    {
        var response = await GetValuesAsync(h, column, search, dashboardId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("values").EnumerateArray().Select(v => v.GetProperty("value").GetString()!).ToArray();
    }

    private static Task<JsonElement> AddStatAsync(IndicatorsHarness h, string indicator, Guid? dashboardId = null) =>
        h.AddWidgetAsync(new { title = indicator, chartType = "stat", config = new { indicator } }, dashboardId: dashboardId);

    [Fact]
    public async Task The_values_of_two_indicators_are_offered_once_each_most_frequent_first()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("coverage", "municipality text, value numeric", "('Cali', 1), ('Cali', 1), ('Buga', 1)");
        await h.PublishAsync("attendance", "municipality text, value numeric", "('Cali', 1), ('Palmira', 1)");
        await AddStatAsync(h, "coverage");
        await AddStatAsync(h, "attendance");
        await AddStatAsync(h, "attendance");

        Assert.Equal(["Cali", "Buga", "Palmira"], await ValuesAsync(h));
    }

    [Fact]
    public async Task A_search_lists_the_most_frequent_values_that_contain_it_ignoring_case()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        var many = string.Join(", ", Enumerable.Range(0, 60).Select(i => $"('Town {i:00}', 1)"));
        await h.PublishAsync("coverage", "municipality text, value numeric", many + ", ('Palmira', 1), ('Palmira', 1), ('El Palmar', 1)");
        await AddStatAsync(h, "coverage");

        Assert.Equal(50, (await ValuesAsync(h)).Length);
        Assert.Equal(["Palmira", "El Palmar"], await ValuesAsync(h, search: "pal"));
    }

    [Fact]
    public async Task A_value_longer_than_a_filter_accepts_is_not_offered()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("coverage", "municipality text, value numeric", $"('Cali', 1), ('{new string('x', 501)}', 1), ('{new string('y', 500)}', 1)");
        await AddStatAsync(h, "coverage");

        Assert.Equal(["Cali", new string('y', 500)], await ValuesAsync(h));
    }

    [Fact]
    public async Task A_table_with_the_column_as_another_type_an_unpublished_indicator_and_another_dashboards_widgets_are_skipped()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        var field = await h.AddDashboardAsync("Field");
        await h.PublishAsync("coverage", "municipality text, value numeric", "('Cali', 1)");
        await h.PublishAsync("codes", "municipality bigint, value numeric", "(76001, 1)");
        await h.PublishAsync("elsewhere", "municipality text, value numeric", "('Buga', 1)");
        await h.AddIndicatorAsync("pending");
        await AddStatAsync(h, "coverage");
        await AddStatAsync(h, "codes");
        await AddStatAsync(h, "pending");
        await AddStatAsync(h, "elsewhere", field);

        Assert.Equal(["Cali"], await ValuesAsync(h));
    }

    [Fact]
    public async Task A_dashboard_without_widgets_or_without_the_column_offers_no_values()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);

        Assert.Empty(await ValuesAsync(h));

        await h.PublishAsync("coverage", "municipality text, value numeric", "('Cali', 1)");
        await AddStatAsync(h, "coverage");
        Assert.Empty(await ValuesAsync(h, column: "zone"));
    }

    [Fact]
    public async Task A_column_name_a_script_cannot_publish_or_a_long_search_is_refused()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);

        var column = await GetValuesAsync(h, "Municipality");
        var search = await GetValuesAsync(h, "municipality", new string('s', 501));

        Assert.Equal(HttpStatusCode.BadRequest, column.StatusCode);
        Assert.Single(IndicatorsHarness.Errors(await column.Content.ReadFromJsonAsync<JsonElement>(), "column"));
        Assert.Equal(HttpStatusCode.BadRequest, search.StatusCode);
        Assert.Single(IndicatorsHarness.Errors(await search.Content.ReadFromJsonAsync<JsonElement>(), "search"));
    }

    // The indicators of a version's widgets (design D11 of dashboard versions)

    private static Task<HttpResponseMessage> GetValuesOfAsync(IndicatorsHarness h, Guid? dashboardId, params string[] indicators) =>
        h.Viewer.GetAsync($"{h.DashboardUrl(dashboardId)}/values?column=municipality"
            + string.Concat(indicators.Select(i => "&indicator=" + Uri.EscapeDataString(i))));

    private static async Task<string[]> ValuesOfAsync(IndicatorsHarness h, params string[] indicators)
    {
        var response = await GetValuesOfAsync(h, null, indicators);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("values").EnumerateArray().Select(v => v.GetProperty("value").GetString()!).ToArray();
    }

    [Fact]
    public async Task The_indicators_named_give_the_values_instead_of_the_dashboards_widgets()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("coverage", "municipality text, value numeric", "('Cali', 1), ('Cali', 1)");
        await h.PublishAsync("attendance", "municipality text, value numeric", "('Palmira', 1)");
        await h.PublishAsync("removed", "municipality text, value numeric", "('Buga', 1), ('Buga', 1), ('Buga', 1)");
        await AddStatAsync(h, "coverage");

        // A version held widgets of "removed" and "attendance", which the dashboard no longer has.
        Assert.Equal(["Buga", "Palmira"], await ValuesOfAsync(h, "removed", "attendance"));
        Assert.Equal(["Cali"], await ValuesOfAsync(h));
        Assert.Equal(["Buga", "Palmira"], await ValuesOfAsync(h, "removed", "attendance", "unpublished"));
    }

    [Fact]
    public async Task An_indicator_name_a_script_cannot_publish_one_named_twice_or_more_than_a_dashboard_holds_is_refused()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres, new() { ["IndicatorDashboard:MaxWidgetsPerDashboard"] = "2" });
        string[][] refused = [["Coverage"], ["coverage", "x;drop"], ["coverage", "coverage"], ["a", "b", "c"]];

        foreach (var indicators in refused)
        {
            var response = await GetValuesOfAsync(h, null, indicators);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Single(IndicatorsHarness.Errors(await response.Content.ReadFromJsonAsync<JsonElement>(), "indicator"));
        }

        Assert.Empty(await ValuesOfAsync(h, "a", "b"));
    }

    [Fact]
    public async Task An_indicator_of_another_project_is_not_read_and_a_removed_dashboard_is_not_found()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        var (other, otherDashboard) = await h.AddOtherProjectAsync();
        await h.PublishAsync("coverage", "municipality text, value numeric", "('Cali', 1)");
        await PublishedTables.PublishTableAsync(h.Db, other, "indicators", "coverage", "municipality text, value numeric", "('Hidden', 1)");
        await PublishedTables.PublishTableAsync(h.Db, other, "indicators", "secret", "municipality text, value numeric", "('Hidden', 1)");
        var health = await h.AddDashboardAsync("Health");
        await h.RemoveDashboardAsync(health);

        // Looked up in this project's tables only, whatever another project publishes under the name.
        Assert.Equal(["Cali"], await ValuesOfAsync(h, "coverage"));
        Assert.Empty(await ValuesOfAsync(h, "secret"));
        Assert.Equal(HttpStatusCode.NotFound, (await GetValuesOfAsync(h, health, "secret")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetValuesOfAsync(h, otherDashboard, "secret")).StatusCode);
    }

    [Fact]
    public async Task A_removed_dashboard_is_not_found()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        var health = await h.AddDashboardAsync("Health");
        await h.RemoveDashboardAsync(health);

        var response = await GetValuesAsync(h, "municipality", dashboardId: health);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("dashboards.notFound", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
}
