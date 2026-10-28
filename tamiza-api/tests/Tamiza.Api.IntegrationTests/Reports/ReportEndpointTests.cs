using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tamiza.Api.IntegrationTests.Indicators;
using Tamiza.Api.IntegrationTests.Infrastructure;
using Tamiza.Api.Reports;

namespace Tamiza.Api.IntegrationTests.Reports;

/// <summary>Reading, saving, removing and sending now a dashboard's scheduled email (design D13).</summary>
[Collection(PostgresCollection.Name)]
public sealed class ReportEndpointTests(PostgresFixture postgres)
{
    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string?> CodeAsync(HttpResponseMessage response) => (await ReadAsync(response)).GetProperty("code").GetString();

    private static async Task<string[]> ErrorsAsync(HttpResponseMessage response, string field) =>
        IndicatorsHarness.Errors(await ReadAsync(response), field);

    private static Task<HttpResponseMessage> SendNowAsync(ReportsHarness h, HttpClient? client = null, Guid? dashboardId = null) =>
        (client ?? h.Indicators.Analyst).PostAsync(h.ReportUrl(dashboardId) + "/send", null);

    [Fact]
    public async Task Email_settings_saved()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);

        var response = await h.PutReportAsync(["ana@partner.org", "luis@city.gov.co"], [4, 1], "07:00", active: true, locale: "es-CO");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var view = await h.Indicators.Analyst.GetFromJsonAsync<JsonElement>(h.ReportUrl());
        var settings = view.GetProperty("settings");
        Assert.Equal(["ana@partner.org", "luis@city.gov.co"], IndicatorsHarness.Strings(settings.GetProperty("recipients")));
        Assert.Equal([1, 4], settings.GetProperty("days").EnumerateArray().Select(d => d.GetInt32()));
        Assert.Equal("07:00", settings.GetProperty("time").GetString());
        Assert.True(settings.GetProperty("active").GetBoolean());
        Assert.Equal("es-CO", settings.GetProperty("locale").GetString());
        Assert.Equal("America/Bogota", view.GetProperty("timeZone").GetString());
        Assert.Equal("grace", view.GetProperty("updatedBy").GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, view.GetProperty("lastSentAt").ValueKind);
    }

    [Fact]
    public async Task A_dashboard_without_an_email_reads_as_none()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);

        var view = await h.Indicators.Analyst.GetFromJsonAsync<JsonElement>(h.ReportUrl());

        Assert.Equal(JsonValueKind.Null, view.GetProperty("settings").ValueKind);
        Assert.Equal("America/Bogota", view.GetProperty("timeZone").GetString());
    }

    [Fact]
    public async Task One_email_per_dashboard()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);
        await h.SaveReportAsync(["ana@partner.org"]);

        await h.SaveReportAsync(["luis@city.gov.co"], time: "09:15");

        Assert.Equal(1L, await h.ScalarAsync<long>("SELECT count(*) FROM tamiza.dashboard_reports"));
        var settings = (await h.Indicators.Analyst.GetFromJsonAsync<JsonElement>(h.ReportUrl())).GetProperty("settings");
        Assert.Equal(["luis@city.gov.co"], IndicatorsHarness.Strings(settings.GetProperty("recipients")));
        Assert.Equal("09:15", settings.GetProperty("time").GetString());
    }

    [Fact]
    public async Task Email_removed()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);
        await h.AddStatAsync();
        await h.SaveReportAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await h.Indicators.Analyst.DeleteAsync(h.ReportUrl())).StatusCode);

        var view = await h.Indicators.Analyst.GetFromJsonAsync<JsonElement>(h.ReportUrl());
        Assert.Equal(JsonValueKind.Null, view.GetProperty("settings").ValueKind);
        h.At(2026, 10, 27, 7);
        await h.PassAsync();
        Assert.Empty(h.Email.Sent);
    }

    [Fact]
    public async Task Viewer_refused()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);
        await h.SaveReportAsync();
        var viewer = h.Indicators.Viewer;

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(h.ReportUrl())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.PutReportAsync(client: viewer)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync(h.ReportUrl())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendNowAsync(h, viewer)).StatusCode);
        Assert.Equal(1L, await h.ScalarAsync<long>("SELECT count(*) FROM tamiza.dashboard_reports"));
    }

    [Fact]
    public async Task Not_a_member()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);

        Assert.Equal(HttpStatusCode.NotFound, (await h.Indicators.Outsider.GetAsync(h.ReportUrl())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.PutReportAsync(client: h.Indicators.Outsider)).StatusCode);
    }

    [Fact]
    public async Task An_admin_manages_it_too()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);

        Assert.Equal(HttpStatusCode.OK, (await h.PutReportAsync(client: h.Indicators.Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await SendNowAsync(h, h.Indicators.Admin)).StatusCode);
    }

    [Fact]
    public async Task Turned_off()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres, email: false);

        var save = await h.PutReportAsync();
        var send = await SendNowAsync(h);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, save.StatusCode);
        Assert.Equal("reports.unavailable", await CodeAsync(save));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, send.StatusCode);
        Assert.Equal("reports.unavailable", await CodeAsync(send));
        Assert.Equal(HttpStatusCode.OK, (await h.Indicators.Analyst.GetAsync(h.ReportUrl())).StatusCode);
        Assert.Equal(0L, await h.ScalarAsync<long>("SELECT count(*) FROM tamiza.dashboard_reports"));
    }

    [Fact]
    public async Task A_change_to_a_removed_dashboard_is_not_found()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);
        var gone = await h.Indicators.AddDashboardAsync("Gone");
        await h.Indicators.RemoveDashboardAsync(gone);

        foreach (var response in new[]
                 {
                     await h.Indicators.Analyst.GetAsync(h.ReportUrl(gone)), await h.PutReportAsync(dashboardId: gone),
                     await h.Indicators.Analyst.DeleteAsync(h.ReportUrl(gone)), await SendNowAsync(h, dashboardId: gone),
                 })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("dashboards.notFound", await CodeAsync(response));
        }
    }

    [Fact]
    public async Task Address_of_any_domain()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);

        Assert.Equal(HttpStatusCode.OK, (await h.PutReportAsync(["ana@partner.org", "luis@city.gov.co"])).StatusCode);
    }

    [Fact]
    public async Task Invalid_address()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);

        var response = await h.PutReportAsync(["ana@partner.org", "luis at city"]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["\"luis at city\" is not a valid email address."], await ErrorsAsync(response, "recipients"));
        Assert.Equal(0L, await h.ScalarAsync<long>("SELECT count(*) FROM tamiza.dashboard_reports"));
    }

    [Fact]
    public async Task No_domain_or_display_name()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);

        var response = await h.PutReportAsync(["luis", "Ana <ana@partner.org>"]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        // One message names every refused address, since a page shows a field's first message only.
        var error = Assert.Single(await ErrorsAsync(response, "recipients"));
        Assert.Contains("\"luis\"", error);
        Assert.Contains("\"Ana <ana@partner.org>\"", error);
    }

    [Fact]
    public async Task A_local_part_ending_in_a_dot_is_refused()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);

        var response = await h.PutReportAsync(["ana@partner.org", "maria.@ngo.org"]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["\"maria.@ngo.org\" is not a valid email address."], await ErrorsAsync(response, "recipients"));
    }

    [Fact]
    public async Task A_huge_list_is_refused_before_any_address_is_checked()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);

        var response = await h.PutReportAsync(Enumerable.Range(1, 2000).Select(i => $"person{i}@partner.org").ToArray());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["A scheduled email can have at most 20 recipients."], await ErrorsAsync(response, "recipients"));
    }

    [Theory]
    [InlineData("en-CO", "en")]
    [InlineData("es-DE", "es")]
    [InlineData("es-CO", "es-CO")]
    public async Task A_browser_region_without_data_is_saved_as_its_language(string locale, string stored)
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);

        await h.SaveReportAsync(locale: locale);

        var settings = (await h.Indicators.Analyst.GetFromJsonAsync<JsonElement>(h.ReportUrl())).GetProperty("settings");
        Assert.Equal(stored, settings.GetProperty("locale").GetString());
    }

    [Fact]
    public async Task Removing_and_saving_again_keeps_the_send_now_rule()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);
        await h.AddStatAsync();
        await h.SaveReportAsync();
        Assert.Equal(HttpStatusCode.Accepted, (await SendNowAsync(h)).StatusCode);
        await h.PassAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await h.Indicators.Analyst.DeleteAsync(h.ReportUrl())).StatusCode);
        Assert.Equal("{}", await h.ScalarAsync<string>("SELECT recipients::text FROM tamiza.dashboard_reports WHERE dashboard_id = @dashboard"));
        Assert.Equal("reports.none", await CodeAsync(await SendNowAsync(h)));
        await h.SaveReportAsync(["new@partner.org"]);
        var again = await SendNowAsync(h);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("reports.tooSoon", await CodeAsync(again));
        await h.PassAsync();
        Assert.Single(h.Email.Sent);
    }

    [Fact]
    public async Task Removing_and_saving_again_never_sends_a_day_twice()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);
        await h.AddStatAsync();
        h.At(2026, 10, 25, 20);
        await h.SaveReportAsync(time: "07:00");
        h.At(2026, 10, 26, 7);
        await h.PassAsync();
        Assert.Single(h.Email.Sent);

        h.At(2026, 10, 26, 7, 30);
        await h.Indicators.Analyst.DeleteAsync(h.ReportUrl());
        await h.SaveReportAsync(time: "08:00");
        h.At(2026, 10, 26, 8);
        await h.PassAsync();

        Assert.Single(h.Email.Sent);
    }

    [Fact]
    public async Task Too_many_recipients()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);

        var response = await h.PutReportAsync(Enumerable.Range(1, 21).Select(i => $"person{i}@partner.org").ToArray());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["A scheduled email can have at most 20 recipients."], await ErrorsAsync(response, "recipients"));
    }

    [Fact]
    public async Task Same_address_twice()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);

        await h.SaveReportAsync(["Ana@Partner.org", "ana@partner.org"]);

        var settings = (await h.Indicators.Analyst.GetFromJsonAsync<JsonElement>(h.ReportUrl())).GetProperty("settings");
        Assert.Equal(["Ana@Partner.org"], IndicatorsHarness.Strings(settings.GetProperty("recipients")));
    }

    [Theory]
    [InlineData("days")]
    [InlineData("time")]
    [InlineData("locale")]
    public async Task Weekdays_time_and_locale_are_checked(string field)
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);

        var response = field switch
        {
            "days" => await h.PutReportAsync(days: []),
            "time" => await h.PutReportAsync(time: "25:00"),
            _ => await h.PutReportAsync(locale: "fr-FR"),
        };

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(await ErrorsAsync(response, field));
        Assert.Equal(0L, await h.ScalarAsync<long>("SELECT count(*) FROM tamiza.dashboard_reports"));
    }

    [Fact]
    public async Task Send_now()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);
        await h.AddStatAsync();
        await h.SaveReportAsync(active: false);

        Assert.Equal(HttpStatusCode.Accepted, (await SendNowAsync(h)).StatusCode);
        await h.PassAsync();

        Assert.Single(h.Email.Sent);
        var settings = (await h.Indicators.Analyst.GetFromJsonAsync<JsonElement>(h.ReportUrl())).GetProperty("settings");
        Assert.False(settings.GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task Send_now_during_a_busy_hour()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);
        h.At(2026, 10, 25, 20);
        var due = new List<Guid>();
        for (var i = 1; i <= 5; i++)
        {
            var dashboard = await h.Indicators.AddDashboardAsync($"Due {i}");
            await h.AddStatAsync(dashboard);
            await h.SaveReportAsync(time: "07:00", dashboardId: dashboard);
            due.Add(dashboard);
        }

        await h.Indicators.ExecuteAsync("UPDATE tamiza.dashboards SET name = 'Housing', normalized_name = 'housing' WHERE id = @id", ("id", h.Indicators.DashboardId));
        await h.AddStatAsync();
        await h.SaveReportAsync(active: false);
        h.At(2026, 10, 26, 7);
        // The scheduler is preparing the first due email when the analyst sends "Housing" now.
        await using (var scope = h.Indicators.Factory.Services.CreateAsyncScope())
        {
            var first = await scope.ServiceProvider.GetRequiredService<ReportQueue>().ClaimDueAsync(h.Clock.GetUtcNow(), CancellationToken.None);
            Assert.Equal(HttpStatusCode.Accepted, (await SendNowAsync(h)).StatusCode);
            await scope.ServiceProvider.GetRequiredService<ReportSender>().SendAsync(first!.Value, CancellationToken.None);
        }

        await h.PassAsync();

        var subjects = h.Email.Sent.Select(s => s.Message.Subject).ToList();
        Assert.Equal(6, subjects.Count);
        Assert.Contains("Housing", subjects[1]);
    }

    [Fact]
    public async Task No_settings()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);

        var response = await SendNowAsync(h);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("reports.none", await CodeAsync(response));
    }

    [Fact]
    public async Task Twice_in_a_row()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);
        await h.SaveReportAsync();
        Assert.Equal(HttpStatusCode.Accepted, (await SendNowAsync(h)).StatusCode);

        h.Clock.Advance(TimeSpan.FromMinutes(3));
        var again = await SendNowAsync(h);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("reports.tooSoon", await CodeAsync(again));
        h.Clock.Advance(TimeSpan.FromMinutes(8));
        Assert.Equal(HttpStatusCode.Accepted, (await SendNowAsync(h)).StatusCode);
    }

    [Fact]
    public async Task Schedule_unchanged()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);
        await h.AddStatAsync();
        h.At(2026, 10, 25, 20);
        await h.SaveReportAsync(time: "07:00");

        h.At(2026, 10, 26, 6);
        Assert.Equal(HttpStatusCode.Accepted, (await SendNowAsync(h)).StatusCode);
        await h.PassAsync();
        h.At(2026, 10, 26, 7);
        await h.PassAsync();

        Assert.Equal(2, h.Email.Sent.Count);
    }

    [Fact]
    public async Task The_openapi_document_lists_the_four_operations()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);

        var document = await h.Indicators.Analyst.GetFromJsonAsync<JsonElement>("/api/v1/openapi.json");

        var paths = document.GetProperty("paths");
        var report = paths.GetProperty("/api/v1/projects/{projectId}/dashboards/{dashboardId}/report");
        Assert.True(report.TryGetProperty("get", out var get));
        Assert.True(report.TryGetProperty("put", out var put));
        Assert.True(report.TryGetProperty("delete", out _));
        Assert.True(paths.GetProperty("/api/v1/projects/{projectId}/dashboards/{dashboardId}/report/send").TryGetProperty("post", out _));
        Assert.True(put.TryGetProperty("requestBody", out _));
        Assert.True(get.GetProperty("responses").GetProperty("200").TryGetProperty("content", out _));
    }
}
