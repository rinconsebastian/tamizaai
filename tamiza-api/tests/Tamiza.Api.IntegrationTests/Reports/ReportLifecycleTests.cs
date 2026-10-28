using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Indicators;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Reports;

/// <summary>A scheduled email lives and dies with its dashboard, and a copy of the dashboard has none (design D3).</summary>
[Collection(PostgresCollection.Name)]
public sealed class ReportLifecycleTests(PostgresFixture postgres)
{
    private static Task InsertReportAsync(IndicatorsHarness h, Guid dashboardId) =>
        h.ExecuteAsync("""
            INSERT INTO tamiza.dashboard_reports (dashboard_id, recipients, days, send_time, active, locale, updated_by)
            SELECT @dashboard, ARRAY['ana@partner.org'], ARRAY[1,2,3,4,5,6,7]::smallint[], '07:00', true, 'en-US', id
            FROM tamiza.users WHERE keycloak_sub = 'grace'
            """, ("dashboard", dashboardId));

    private static Task<long> ReportsAsync(IndicatorsHarness h) =>
        TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboard_reports");

    private static async Task<Guid> AddDashboardAsync(IndicatorsHarness h, string name)
    {
        var response = await h.Analyst.PostAsJsonAsync(h.DashboardsUrl, new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Dashboard_removed()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        var housing = await AddDashboardAsync(h, "Housing");
        await InsertReportAsync(h, housing);

        Assert.Equal(HttpStatusCode.NoContent, (await h.Analyst.DeleteAsync(h.DashboardUrl(housing))).StatusCode);

        Assert.Equal(0L, await ReportsAsync(h));
    }

    [Fact]
    public async Task Removing_the_project_removes_its_reports()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await InsertReportAsync(h, h.DashboardId);

        await h.ExecuteAsync("DELETE FROM tamiza.projects WHERE id = @id", ("id", h.ProjectId));

        Assert.Equal(0L, await ReportsAsync(h));
    }

    [Fact]
    public async Task Scheduled_email_not_copied()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await InsertReportAsync(h, h.DashboardId);

        var response = await h.Analyst.PostAsJsonAsync(h.DashboardUrl() + "/copies", new { name = "Indicators (copy)" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var copy = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(1L, await ReportsAsync(h));
        Assert.Equal(0L, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.dashboard_reports WHERE dashboard_id = @id", ("id", copy)));
    }
}
