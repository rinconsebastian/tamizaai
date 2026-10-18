using System.Net;
using System.Net.Http.Json;
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
        ];

        Assert.All(responses, response => Assert.Equal(expected, response.StatusCode));
        Assert.Equal(dashboards, await h.StoredDashboardsAsync());
        Assert.Equal(widgets, await h.StoredWidgetsAsync());
    }

    /// <summary>Swaps the project's dashboards, after adding a second one, from the order they are in.</summary>
    private static async Task<HttpResponseMessage> ReorderAsync(IndicatorsHarness h, HttpClient client)
    {
        var second = await h.InsertDashboardAsync("Second");
        return await client.PutAsJsonAsync(
            h.DashboardsUrl + "/order", new { expectedIds = new[] { h.DashboardId, second }, dashboardIds = new[] { second, h.DashboardId } });
    }
}
