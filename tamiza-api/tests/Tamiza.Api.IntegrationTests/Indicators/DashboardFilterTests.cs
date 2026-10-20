using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Indicators;

/// <summary><c>PUT /projects/{id}/dashboards/{dashboardId}/filters</c> and the filters a dashboard's GET returns (dashboard-filters).</summary>
[Collection(PostgresCollection.Name)]
public sealed class DashboardFilterTests(PostgresFixture postgres)
{
    private static object Filter(string label, string column, string kind = "text", object? @default = null) =>
        new { label, column, kind, @default };

    private static string[] Labels(JsonElement filters) =>
        filters.EnumerateArray().Select(f => f.GetProperty("label").GetString()!).ToArray();

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    // Dashboard filters

    [Fact]
    public async Task An_analyst_adds_a_filter_that_every_member_reads_on_that_dashboard_only()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        var field = await h.AddDashboardAsync("Field");

        var saved = await h.SaveFiltersAsync(new[] { Filter("Municipality", "municipality") });
        var health = (await h.GetDashboardAsync(client: h.Viewer)).GetProperty("filters");
        var other = (await h.GetDashboardAsync(field, h.Viewer)).GetProperty("filters");

        foreach (var filters in new[] { saved, health })
        {
            var only = filters.EnumerateArray().Single();
            Assert.Equal(("municipality", "Municipality", "text"), (only.GetProperty("column").GetString(), only.GetProperty("label").GetString(), only.GetProperty("kind").GetString()));
            Assert.Equal(JsonValueKind.Null, only.GetProperty("default").ValueKind);
        }

        Assert.Equal(0, other.GetArrayLength());
    }

    [Fact]
    public async Task A_new_dashboard_has_no_filters()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        var added = await h.AddDashboardAsync("Field");

        Assert.Equal(0, (await h.GetDashboardAsync()).GetProperty("filters").GetArrayLength());
        Assert.Equal(0, (await h.GetDashboardAsync(added)).GetProperty("filters").GetArrayLength());
    }

    [Fact]
    public async Task A_ninth_filter_is_refused_and_the_dashboard_keeps_its_eight()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.SaveFiltersAsync(Enumerable.Range(0, 8).Select(i => Filter($"F{i}", $"c{i}")).ToArray());
        var before = await h.StoredDashboardsAsync();

        var response = await h.PutFiltersAsync(Enumerable.Range(0, 9).Select(i => Filter($"F{i}", $"c{i}")).ToArray());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["A dashboard can have at most 8 filters."], IndicatorsHarness.Errors(await ReadAsync(response), "filters"));
        Assert.Equal(before, await h.StoredDashboardsAsync());
    }

    [Fact]
    public async Task Two_filters_on_one_column_are_refused_on_the_second_and_nothing_is_saved()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        var before = await h.StoredDashboardsAsync();

        var response = await h.PutFiltersAsync(new[] { Filter("Municipality", "municipality"), Filter("Town", "municipality") });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ["Column 'municipality' already has a filter on this dashboard."],
            IndicatorsHarness.Errors(await ReadAsync(response), "filters[1].column"));
        Assert.Equal(before, await h.StoredDashboardsAsync());
    }

    [Fact]
    public async Task Filters_are_kept_in_the_order_they_are_saved_in()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.SaveFiltersAsync(new[] { Filter("Municipality", "municipality"), Filter("Round", "round", "number") });

        await h.SaveFiltersAsync(new[] { Filter("Round", "round", "number"), Filter("Municipality", "municipality") });

        Assert.Equal(["Round", "Municipality"], Labels((await h.GetDashboardAsync(client: h.Viewer)).GetProperty("filters")));
    }

    [Fact]
    public async Task A_label_is_saved_trimmed_and_shown_as_typed()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);

        var saved = await h.SaveFiltersAsync(new[] { Filter("  Municipio  ", "municipality"), Filter("<b>Zona</b>", "zone") });

        Assert.Equal(["Municipio", "<b>Zona</b>"], Labels(saved));
        Assert.Equal(["Municipio", "<b>Zona</b>"], Labels((await h.GetDashboardAsync()).GetProperty("filters")));
    }

    [Fact]
    public async Task A_label_taken_by_another_filter_in_any_case_is_refused()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);

        var response = await h.PutFiltersAsync(new[] { Filter("Round", "round", "number"), Filter("ROUND", "visit_round", "number") });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["Another filter of this dashboard is labelled \"ROUND\"."], IndicatorsHarness.Errors(await ReadAsync(response), "filters[1].label"));
    }

    [Fact]
    public async Task A_column_name_a_script_cannot_publish_is_refused()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);

        var response = await h.PutFiltersAsync(new[] { Filter("Municipality", "Municipality") });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(IndicatorsHarness.Errors(await ReadAsync(response), "filters[0].column"));
    }

    [Fact]
    public async Task An_invalid_default_is_refused_and_nothing_is_saved()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        var before = await h.StoredDashboardsAsync();

        var response = await h.PutFiltersAsync(new[] { Filter("Round", "round", "number", new { min = "three" }) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("'three'", IndicatorsHarness.Errors(await ReadAsync(response), "filters[0].default").Single());
        Assert.Equal(before, await h.StoredDashboardsAsync());
    }

    [Fact]
    public async Task A_default_of_each_kind_is_saved_and_read_back()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);

        await h.SaveFiltersAsync(new[]
        {
            Filter("Municipality", "municipality", "text", new { values = new[] { "Cali", "Palmira" } }),
            Filter("Consent", "consent", "boolean", new { values = new[] { "true" } }),
            Filter("Round", "round", "number", new { min = "3", max = "3" }),
            Filter("Visit", "visited_at", "date", new { max = "2026-09-30" }),
        });

        var defaults = (await h.GetDashboardAsync(client: h.Viewer)).GetProperty("filters").EnumerateArray().Select(f => f.GetProperty("default")).ToArray();
        Assert.Equal(["Cali", "Palmira"], IndicatorsHarness.Strings(defaults[0].GetProperty("values")));
        Assert.Equal(["true"], IndicatorsHarness.Strings(defaults[1].GetProperty("values")));
        Assert.Equal(("3", "3"), (defaults[2].GetProperty("min").GetString(), defaults[2].GetProperty("max").GetString()));
        Assert.Equal(JsonValueKind.Null, defaults[3].GetProperty("min").ValueKind);
        Assert.Equal("2026-09-30", defaults[3].GetProperty("max").GetString());
    }

    [Fact]
    public async Task A_filter_on_a_column_no_table_has_is_accepted()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("coverage", IndicatorsHarness.StandardColumns, "('Cali', 1, 1, 1)");

        var saved = await h.SaveFiltersAsync(new[] { Filter("Zone", "zone") });

        Assert.Equal(["Zone"], Labels(saved));
    }

    [Fact]
    public async Task Of_two_saves_the_later_wins()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);

        await h.SaveFiltersAsync(new[] { Filter("Municipality", "municipality") });
        await h.SaveFiltersAsync(new[] { Filter("Round", "round", "number") }, h.Admin);

        Assert.Equal(["Round"], Labels((await h.GetDashboardAsync()).GetProperty("filters")));
    }

    [Fact]
    public async Task Saving_no_filters_removes_them_all_and_a_body_without_a_list_is_refused()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.SaveFiltersAsync(new[] { Filter("Municipality", "municipality") });

        var missing = await h.PutFiltersAsync(null);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Single(IndicatorsHarness.Errors(await ReadAsync(missing), "filters"));

        Assert.Equal(0, (await h.SaveFiltersAsync(Array.Empty<object>())).GetArrayLength());
        Assert.Equal(0, (await h.GetDashboardAsync()).GetProperty("filters").GetArrayLength());
    }

    [Fact]
    public async Task Filters_saved_on_a_removed_dashboard_are_refused_as_not_found()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        var health = await h.AddDashboardAsync("Health");
        await h.RemoveDashboardAsync(health);

        var response = await h.PutFiltersAsync(new[] { Filter("Municipality", "municipality") }, dashboardId: health);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("dashboards.notFound", (await ReadAsync(response)).GetProperty("code").GetString());
    }

    // Ignoring dashboard filters (dashboard-indicators)

    private static object Stat(string title = "Households, all municipalities", string[]? ignored = null) =>
        new { title, chartType = "stat", config = new { indicator = "coverage", ignoredDashboardFilters = ignored } };

    private static string[] Ignored(JsonElement widget) =>
        IndicatorsHarness.Strings(widget.GetProperty("config").GetProperty("ignoredDashboardFilters"));

    private async Task<IndicatorsHarness> WithCoverageAsync()
    {
        var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("coverage", IndicatorsHarness.StandardColumns, "('Cali', 1, 1, 1)");
        return h;
    }

    [Fact]
    public async Task A_widget_saved_with_ignored_filters_reads_them_back()
    {
        await using var h = await WithCoverageAsync();

        var created = await h.AddWidgetAsync(Stat(ignored: ["municipality"]));

        Assert.Equal(["municipality"], Ignored(created));
        Assert.Equal(["municipality"], Ignored((await h.GetWidgetsAsync(client: h.Viewer))[0]));
    }

    [Fact]
    public async Task A_stored_config_without_ignored_filters_ignores_none()
    {
        await using var h = await WithCoverageAsync();
        var widget = await h.AddWidgetAsync(Stat(ignored: ["municipality"]));
        await h.ExecuteAsync(
            "UPDATE tamiza.dashboard_widgets SET config = config - 'ignoredDashboardFilters' WHERE id = @id", ("id", widget.GetProperty("id").GetGuid()));

        Assert.Empty(Ignored((await h.GetWidgetsAsync())[0]));
    }

    [Fact]
    public async Task A_widget_keeps_its_ignored_filters_when_it_is_moved()
    {
        await using var h = await WithCoverageAsync();
        var field = await h.AddDashboardAsync("Field");
        await h.SaveFiltersAsync(new[] { Filter("Municipality", "municipality") }, dashboardId: field);
        var widget = await h.AddWidgetAsync(Stat(ignored: ["municipality"]));

        var moved = await h.Analyst.PatchJsonAsync(h.WidgetUrl(widget.GetProperty("id").GetGuid()), new { dashboardId = field });

        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(["municipality"], Ignored((await h.GetWidgetsAsync(field))[0]));
    }

    [Fact]
    public async Task An_ignored_filter_still_holds_when_the_filter_is_removed_and_defined_again()
    {
        await using var h = await WithCoverageAsync();
        await h.SaveFiltersAsync(new[] { Filter("Round", "round", "number") });
        await h.AddWidgetAsync(Stat(ignored: ["round"]));

        await h.SaveFiltersAsync(Array.Empty<object>());
        await h.SaveFiltersAsync(new[] { Filter("Visit round", "round", "number") });

        Assert.Equal(["round"], Ignored((await h.GetWidgetsAsync())[0]));
    }

    [Fact]
    public async Task A_viewer_cannot_change_what_a_widget_ignores()
    {
        await using var h = await WithCoverageAsync();
        var widget = await h.AddWidgetAsync(Stat());
        var before = await h.StoredWidgetsAsync();

        var response = await h.Viewer.PatchJsonAsync(
            h.WidgetUrl(widget.GetProperty("id").GetGuid()), new { config = new { indicator = "coverage", ignoredDashboardFilters = new[] { "municipality" } } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await h.StoredWidgetsAsync());
    }

    [Fact]
    public async Task A_query_answers_the_same_with_or_without_ignored_filters_in_its_config()
    {
        await using var h = await WithCoverageAsync();

        var plain = await h.QueryAsync(new { indicator = "coverage", groupBy = "municipality" });
        var ignoring = await h.QueryAsync(new { indicator = "coverage", groupBy = "municipality", ignoredDashboardFilters = new[] { "municipality" } });

        Assert.Equal(plain.GetRawText(), ignoring.GetRawText());
    }

    [Fact]
    public async Task An_invalid_ignored_filter_is_refused_on_its_index()
    {
        await using var h = await WithCoverageAsync();

        var response = await h.PostWidgetAsync(Stat(ignored: ["round", "Round"]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(IndicatorsHarness.Errors(await ReadAsync(response), "config.ignoredDashboardFilters[1]"));
    }

    [Fact]
    public async Task Saving_filters_records_who_and_when()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        var before = await h.StoredDashboardsAsync();

        await h.SaveFiltersAsync(new[] { Filter("Municipality", "municipality") });

        Assert.NotEqual(before, await h.StoredDashboardsAsync());
        Assert.Equal(1L, await TestData.ScalarAsync<long>(h.Db,
            "SELECT count(*) FROM tamiza.dashboards d JOIN tamiza.users u ON u.id = d.updated_by WHERE d.id = @id AND u.name = 'grace'",
            ("id", h.DashboardId)));
    }
}
