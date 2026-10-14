using System.Net;
using System.Net.Http.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Indicators;

/// <summary>Who may call what: reading and exporting is open to every member, changing widgets needs analyst or admin.</summary>
[Collection(PostgresCollection.Name)]
public sealed class IndicatorRoleMatrixTests(PostgresFixture postgres)
{
    public static TheoryData<string, string, HttpStatusCode> Matrix()
    {
        var data = new TheoryData<string, string, HttpStatusCode>();
        foreach (var (role, writes) in new[] { ("viewer", false), ("analyst", true), ("admin", true), ("outsider", false) })
        {
            var read = role == "outsider" ? HttpStatusCode.NotFound : HttpStatusCode.OK;
            var write = role == "outsider" ? HttpStatusCode.NotFound : writes ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
            data.Add(role, "overview", read);
            data.Add(role, "columns", read);
            data.Add(role, "query", read);
            data.Add(role, "export", read);
            data.Add(role, "create", role == "outsider" ? HttpStatusCode.NotFound : writes ? HttpStatusCode.Created : HttpStatusCode.Forbidden);
            data.Add(role, "update", write);
            data.Add(role, "delete", role == "outsider" ? HttpStatusCode.NotFound : writes ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden);
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
            "columns" => await client.GetAsync(h.Url + "/coverage/columns"),
            "query" => await client.PostAsJsonAsync(h.Url + "/query", new { config = new { indicator = "coverage", groupBy = "municipality" } }),
            "export" => await client.PostAsJsonAsync(h.Url + "/coverage/exports", new { format = "csv" }),
            "create" => await client.PostAsJsonAsync(h.Url + "/widgets", new { title = "N", chartType = "stat", config = new { indicator = "coverage" } }),
            "update" => await client.PatchJsonAsync($"{h.Url}/widgets/{widgetId}", new { title = "Renamed" }),
            "delete" => await client.DeleteAsync($"{h.Url}/widgets/{widgetId}"),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

        Assert.Equal(expected, response.StatusCode);
    }
}
