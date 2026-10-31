using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Indicators;

/// <summary>
/// Who may call what: reading and exporting is open to every member, changing widgets and dashboards needs analyst or admin, and a
/// user who is not a member gets the 404 of a project that does not exist.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class IndicatorRoleMatrixTests(PostgresFixture postgres)
{
    public static TheoryData<string, string, HttpStatusCode> Matrix()
    {
        var data = new TheoryData<string, string, HttpStatusCode>();
        foreach (var (role, writes) in new[] { ("viewer", false), ("analyst", true), ("admin", true), ("outsider", false) })
        {
            var read = role == "outsider" ? HttpStatusCode.NotFound : HttpStatusCode.OK;
            HttpStatusCode Write(HttpStatusCode allowed) =>
                role == "outsider" ? HttpStatusCode.NotFound : writes ? allowed : HttpStatusCode.Forbidden;
            data.Add(role, "overview", read);
            data.Add(role, "dashboard", read);
            data.Add(role, "columns", read);
            data.Add(role, "query", read);
            data.Add(role, "export", read);
            data.Add(role, "create", Write(HttpStatusCode.Created));
            data.Add(role, "update", Write(HttpStatusCode.OK));
            data.Add(role, "move", Write(HttpStatusCode.OK));
            data.Add(role, "delete", Write(HttpStatusCode.NoContent));
            data.Add(role, "add dashboard", Write(HttpStatusCode.Created));
            data.Add(role, "rename dashboard", Write(HttpStatusCode.OK));
            data.Add(role, "remove dashboard", Write(HttpStatusCode.NoContent));
            data.Add(role, "reorder dashboards", Write(HttpStatusCode.OK));
            data.Add(role, "duplicate dashboard", Write(HttpStatusCode.Created));
            data.Add(role, "save filters", Write(HttpStatusCode.OK));
            data.Add(role, "values", read);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Each_role_gets_the_status_its_rights_allow(string role, string action, HttpStatusCode expected)
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("coverage", IndicatorsHarness.StandardColumns, "('Cali', 1, 1, 1)");
        var widgetId = (await h.AddWidgetAsync(new { title = "W", chartType = "stat", config = new { indicator = "coverage" } })).GetProperty("id").GetGuid();
        var client = role switch { "viewer" => h.Viewer, "analyst" => h.Analyst, "admin" => h.Admin, _ => h.Outsider };

        var response = action switch
        {
            "overview" => await client.GetAsync(h.Url),
            "dashboard" => await client.GetAsync(h.DashboardUrl()),
            "columns" => await client.GetAsync(h.Url + "/coverage/columns"),
            "query" => await client.PostAsJsonAsync(h.Url + "/query", new { config = new { indicator = "coverage", groupBy = "municipality" } }),
            "export" => await client.PostAsJsonAsync(h.Url + "/coverage/exports", new { format = "csv" }),
            "create" => await client.PostAsJsonAsync(h.WidgetsUrl(), new { title = "N", chartType = "stat", config = new { indicator = "coverage" } }),
            "update" => await client.PatchJsonAsync(h.WidgetUrl(widgetId), new { title = "Renamed" }),
            "move" => await client.PatchJsonAsync(h.WidgetUrl(widgetId), new { dashboardId = await h.InsertDashboardAsync("Second") }),
            "delete" => await client.DeleteAsync(h.WidgetUrl(widgetId)),
            "add dashboard" => await client.PostAsJsonAsync(h.DashboardsUrl, new { name = "Added" }),
            "rename dashboard" => await client.PatchJsonAsync(h.DashboardUrl(), new { name = "Renamed" }),
            // A second dashboard, so the project's only one is not what the removal meets.
            "remove dashboard" => await client.DeleteAsync(h.DashboardUrl(await h.InsertDashboardAsync("Second"))),
            "reorder dashboards" => await ReorderAsync(h, client),
            "duplicate dashboard" => await client.PostAsJsonAsync(h.DashboardUrl() + "/copies", new { name = "Copy" }),
            "save filters" => await client.PutAsJsonAsync(h.FiltersUrl(), new { filters = new[] { new { label = "Municipality", column = "municipality", kind = "text" } } }),
            "values" => await client.GetAsync(h.DashboardUrl() + "/values?column=municipality"),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

        Assert.Equal(expected, response.StatusCode);
    }

    // Viewer refused, Not a member (custom-dashboards)

    [Theory]
    [InlineData("viewer", HttpStatusCode.Forbidden)]
    [InlineData("outsider", HttpStatusCode.NotFound)]
    public async Task A_refused_change_to_the_dashboards_changes_nothing(string role, HttpStatusCode expected)
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("coverage", IndicatorsHarness.StandardColumns, "('Cali', 1, 1, 1)");
        await h.AddWidgetAsync(new { title = "W", chartType = "stat", config = new { indicator = "coverage" } });
        var second = await h.InsertDashboardAsync("Second");
        var dashboards = await h.StoredDashboardsAsync();
        var widgets = await h.StoredWidgetsAsync();
        var client = role == "viewer" ? h.Viewer : h.Outsider;

        HttpResponseMessage[] responses =
        [
            await client.PostAsJsonAsync(h.DashboardsUrl, new { name = "Added" }),
            await client.PatchJsonAsync(h.DashboardUrl(), new { name = "Renamed" }),
            await client.DeleteAsync(h.DashboardUrl(second)),
            await client.PutAsJsonAsync(h.DashboardsUrl + "/order", new { expectedIds = new[] { h.DashboardId, second }, dashboardIds = new[] { second, h.DashboardId } }),
            await client.PostAsJsonAsync(h.DashboardUrl() + "/copies", new { name = "Copy" }),
            await client.PutAsJsonAsync(h.FiltersUrl(), new { filters = new[] { new { label = "Municipality", column = "municipality", kind = "text" } } }),
        ];

        Assert.All(responses, response => Assert.Equal(expected, response.StatusCode));
        Assert.Equal(dashboards, await h.StoredDashboardsAsync());
        Assert.Equal(widgets, await h.StoredWidgetsAsync());
    }

    // A widget of each kind with a series

    private static readonly (string ChartType, object Config)[] SeriesKinds =
    [
        ("column", new { indicator = "sources", measure = "value", groupBy = "municipality", seriesBy = "water_source", layout = "stacked" }),
        ("heatmap", new { indicator = "sources", measure = "value", groupBy = "municipality", seriesBy = "water_source" }),
        ("crosstab", new { indicator = "sources", measure = "value", groupBy = "municipality", seriesBy = "water_source" }),
    ];

    private static object Pyramid() => new { indicator = "sources", measure = "value", groupBy = "municipality", seriesBy = "water_source", pyramid = new { left = "Piped", right = "Well" } };

    private static object Likert() => new
    {
        indicator = "sources", measure = "value", groupBy = "municipality", seriesBy = "water_source",
        scale = new object[] { new { value = "Piped", side = "negative" }, new { value = "Well", side = "positive" } },
    };

    private static IEnumerable<(string ChartType, object Config)> AllNewKinds() => [.. SeriesKinds, ("pyramid", Pyramid()), ("likert", Likert())];

    [Fact]
    public async Task A_viewer_reads_a_widget_of_each_new_kind_and_cannot_create_change_move_or_remove_one()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("sources", "municipality text, water_source text, value numeric", "('Cali', 'Piped', 5), ('Cali', 'Well', 3)");
        var ids = new List<Guid>();
        foreach (var (chartType, config) in AllNewKinds())
        {
            ids.Add((await h.AddWidgetAsync(new { title = chartType, chartType, config })).GetProperty("id").GetGuid());
        }

        var widgets = await h.StoredWidgetsAsync();
        var view = await h.Viewer.GetFromJsonAsync<System.Text.Json.JsonElement>(h.DashboardUrl());
        var read = await h.Viewer.PostAsJsonAsync(h.Url + "/query", new { config = SeriesKinds[0].Config, chartType = "column" });

        Assert.Equal(5, view.GetProperty("widgets").GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        foreach (var (chartType, config) in AllNewKinds())
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await h.Viewer.PostAsJsonAsync(h.WidgetsUrl(), new { title = "N", chartType, config })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await h.Viewer.PatchJsonAsync(h.WidgetUrl(ids[0]), new { title = "Renamed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Viewer.PatchJsonAsync(h.WidgetUrl(ids[1]), new { config = SeriesKinds[1].Config })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Viewer.DeleteAsync(h.WidgetUrl(ids[2]))).StatusCode);
        Assert.Equal(widgets, await h.StoredWidgetsAsync());
    }

    [Fact]
    public async Task An_outsider_gets_the_404_of_a_project_that_does_not_exist_for_a_widget_with_a_series()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("sources", "municipality text, water_source text, value numeric", "('Cali', 'Piped', 5)");

        var query = await h.Outsider.PostAsJsonAsync(h.Url + "/query", new { config = SeriesKinds[0].Config, chartType = "column" });
        var create = await h.Outsider.PostAsJsonAsync(h.WidgetsUrl(), new { title = "N", chartType = "heatmap", config = SeriesKinds[1].Config });

        Assert.Equal(HttpStatusCode.NotFound, query.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, create.StatusCode);
    }

    [Fact]
    public async Task An_analyst_saves_and_changes_each_new_kind_and_the_widget_reads_back_with_its_settings()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("sources", "municipality text, water_source text, value numeric", "('Cali', 'Piped', 5), ('Cali', 'Well', 3)");

        foreach (var (chartType, config) in AllNewKinds())
        {
            var response = await h.Analyst.PostAsJsonAsync(h.WidgetsUrl(), new { title = chartType, chartType, config });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var saved = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.Equal(chartType, saved.GetProperty("chartType").GetString());
            Assert.Equal("water_source", saved.GetProperty("config").GetProperty("seriesBy").GetString());
        }

        var all = await h.Analyst.GetFromJsonAsync<System.Text.Json.JsonElement>(h.DashboardUrl());
        var pyramid = all.GetProperty("widgets").EnumerateArray().Single(w => w.GetProperty("chartType").GetString() == "pyramid");
        var likert = all.GetProperty("widgets").EnumerateArray().Single(w => w.GetProperty("chartType").GetString() == "likert");
        var stacked = all.GetProperty("widgets").EnumerateArray().Single(w => w.GetProperty("chartType").GetString() == "column");
        Assert.Equal("Piped", pyramid.GetProperty("config").GetProperty("pyramid").GetProperty("left").GetString());
        Assert.Equal(["negative", "positive"], likert.GetProperty("config").GetProperty("scale").EnumerateArray().Select(step => step.GetProperty("side").GetString()));
        Assert.Equal("stacked", stacked.GetProperty("config").GetProperty("layout").GetString());
        Assert.Equal(JsonValueKind.Null, stacked.GetProperty("config").GetProperty("pyramid").ValueKind);
        // The default size of each kind.
        Assert.Equal((6, 5), (pyramid.GetProperty("layout").GetProperty("w").GetInt32(), pyramid.GetProperty("layout").GetProperty("h").GetInt32()));
        Assert.Equal((6, 4), (likert.GetProperty("layout").GetProperty("w").GetInt32(), likert.GetProperty("layout").GetProperty("h").GetInt32()));
    }

    [Fact]
    public async Task A_widget_that_breaks_a_rule_of_series_is_refused_with_the_key_and_saves_nothing()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("sources", "municipality text, water_source text, value numeric, numerator numeric, denominator numeric", "('Cali', 'Piped', 5, 1, 2)");
        var before = await h.StoredWidgetsAsync();

        var heatmapWithoutSeries = await h.Analyst.PostAsJsonAsync(h.WidgetsUrl(), new { title = "N", chartType = "heatmap", config = new { indicator = "sources", measure = "value", groupBy = "municipality" } });
        var pieWithSeries = await h.Analyst.PostAsJsonAsync(h.WidgetsUrl(), new { title = "N", chartType = "pie", config = new { indicator = "sources", measure = "value", groupBy = "municipality", seriesBy = "water_source" } });
        var stackedRatio = await h.Analyst.PostAsJsonAsync(h.WidgetsUrl(), new
        {
            title = "N", chartType = "column",
            config = new { indicator = "sources", ratio = new { numerator = "numerator", denominator = "denominator" }, groupBy = "municipality", seriesBy = "water_source", layout = "stacked" },
        });
        var missingColumn = await h.Analyst.PostAsJsonAsync(h.WidgetsUrl(), new { title = "N", chartType = "crosstab", config = new { indicator = "sources", measure = "value", groupBy = "municipality", seriesBy = "gone" } });

        foreach (var (response, key) in new[] { (heatmapWithoutSeries, "config.seriesBy"), (pieWithSeries, "config.seriesBy"), (stackedRatio, "config.layout"), (missingColumn, "config.seriesBy") })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(key, (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("errors").EnumerateObject().Select(p => p.Name));
        }

        Assert.Equal(before, await h.StoredWidgetsAsync());
    }

    /// <summary>Swaps the project's dashboards, after adding a second one, from the order they are in.</summary>
    private static async Task<HttpResponseMessage> ReorderAsync(IndicatorsHarness h, HttpClient client)
    {
        var second = await h.InsertDashboardAsync("Second");
        return await client.PutAsJsonAsync(
            h.DashboardsUrl + "/order", new { expectedIds = new[] { h.DashboardId, second }, dashboardIds = new[] { second, h.DashboardId } });
    }
}
