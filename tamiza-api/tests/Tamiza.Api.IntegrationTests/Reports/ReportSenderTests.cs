using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;
using Npgsql;
using Tamiza.Api.Email;
using Tamiza.Api.IntegrationTests.Infrastructure;
using Tamiza.Api.Reports;

namespace Tamiza.Api.IntegrationTests.Reports;

/// <summary>One dashboard's email, prepared and handed to the SMTP server, and how it went recorded with its settings (design D9, D10).</summary>
[Collection(PostgresCollection.Name)]
public sealed class ReportSenderTests(PostgresFixture postgres)
{
    private static async Task SendAsync(ReportsHarness h, Guid? dashboardId = null)
    {
        await using var scope = h.Indicators.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReportSender>().SendAsync(dashboardId ?? h.Indicators.DashboardId, CancellationToken.None);
    }

    private static Task<string?> FailureAsync(ReportsHarness h) =>
        h.ScalarAsync<string>("SELECT last_failure FROM tamiza.dashboard_reports WHERE dashboard_id = @dashboard");

    [Fact]
    public async Task Sends_the_body_the_text_and_the_workbook_and_records_the_send()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);
        await h.AddStatAsync();
        await h.SaveReportAsync();

        await SendAsync(h);

        var sent = Assert.Single(h.Email.Sent);
        Assert.Equal(["ana@partner.org", "luis@city.gov.co"], sent.Recipients);
        Assert.Contains("200 households", sent.Message.HtmlBody);
        Assert.Contains("- Total: 200 households", sent.Message.TextBody);
        Assert.Equal("grace@example.org", sent.Message.ReplyTo.Mailboxes.Single().Address);
        var attachment = Assert.IsType<MimePart>(Assert.Single(sent.Message.Attachments));
        Assert.EndsWith(".xlsx", attachment.FileName);
        Assert.True(await h.ScalarAsync<bool>("SELECT last_sent_at IS NOT NULL FROM tamiza.dashboard_reports WHERE dashboard_id = @dashboard"));
        Assert.Null(await FailureAsync(h));
        Assert.Empty(h.Indicators.ExportFiles);
    }

    [Fact]
    public async Task Sends_a_dashboard_with_a_widget_of_each_new_kind_and_layout_and_every_sheet_holds_its_whole_cross_table()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);
        await h.Indicators.PublishAsync("sources", "municipality text, water_source text, value numeric",
            string.Join(", ", Enumerable.Range(1, 12).SelectMany(m => Enumerable.Range(1, 9).Select(w => $"('M{m:00}', 'Source {w}', {m * w})"))),
            label: "Sources", format: "count");
        var y = 0;
        async Task AddAsync(string title, string chartType, object config) =>
            await h.Indicators.AddWidgetAsync(new { title, chartType, config, layout = new { x = 0, y = y += 4, w = 12, h = 4 } });
        await AddAsync("Total", "stat", new { indicator = "sources", measure = "value" });
        await AddAsync("Grouped", "column", new { indicator = "sources", measure = "value", groupBy = "municipality", seriesBy = "water_source", layout = "grouped" });
        await AddAsync("Stacked", "bar", new { indicator = "sources", measure = "value", groupBy = "municipality", seriesBy = "water_source", layout = "stacked" });
        await AddAsync("Percent", "column", new { indicator = "sources", measure = "value", groupBy = "municipality", seriesBy = "water_source", layout = "percent" });
        await AddAsync("Lines", "line", new { indicator = "sources", measure = "value", groupBy = "municipality", seriesBy = "water_source" });
        await AddAsync("Heat", "heatmap", new { indicator = "sources", measure = "value", groupBy = "municipality", seriesBy = "water_source" });
        await AddAsync("Table", "crosstab", new { indicator = "sources", measure = "value", groupBy = "municipality", seriesBy = "water_source" });
        await AddAsync("Pyramid", "pyramid", new { indicator = "sources", measure = "value", groupBy = "municipality", seriesBy = "water_source", pyramid = new { left = "Source 1", right = "Source 2" } });
        await AddAsync("Likert", "likert", new
        {
            indicator = "sources", measure = "value", groupBy = "municipality", seriesBy = "water_source",
            scale = new object[] { new { value = "Source 1", side = "negative" }, new { value = "Source 2", side = "neutral" }, new { value = "Source 3", side = "positive" } },
        });
        await h.SaveReportAsync();

        await SendAsync(h);

        var sent = Assert.Single(h.Email.Sent);
        Assert.Null(await FailureAsync(h));
        // The body shows six series columns and says how many more; the text part does too.
        Assert.Contains("3 more columns", sent.Message.HtmlBody);
        Assert.Contains("3 more columns", sent.Message.TextBody);
        Assert.DoesNotContain(">Source 9</th>", sent.Message.HtmlBody);
        var attachment = Assert.IsType<MimePart>(Assert.Single(sent.Message.Attachments));
        using var content = new MemoryStream();
        await attachment.Content.DecodeToAsync(content);
        content.Position = 0;
        var names = MiniExcelLibs.MiniExcel.GetSheetNames(content);
        Assert.Equal(["Summary", "2. Grouped", "3. Stacked", "4. Percent", "5. Lines", "6. Heat", "7. Table", "8. Pyramid", "9. Likert"], names);
        content.Position = 0;
        var heat = MiniExcelLibs.MiniExcel.Query(content, useHeaderRow: true, sheetName: "6. Heat").Cast<IDictionary<string, object?>>().ToList();
        // Twelve municipalities and the total row; the group column, nine sources and the total column.
        Assert.Equal(13, heat.Count);
        Assert.Equal(11, heat[0].Count);
    }

    [Fact]
    public async Task Empty_dashboard()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);
        await h.SaveReportAsync();

        await SendAsync(h);

        Assert.Empty(h.Email.Sent);
        Assert.Equal("reports.nothingToSend", await FailureAsync(h));
    }

    [Fact]
    public async Task Nothing_published_yet()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);
        await h.Indicators.AddIndicatorAsync("visits", label: "Visits");
        await h.Indicators.AddWidgetAsync(new { title = "Visits", chartType = "stat", config = new { indicator = "visits", measure = "value" } });
        await h.SaveReportAsync();

        await SendAsync(h);

        Assert.Empty(h.Email.Sent);
        Assert.Equal("reports.nothingToSend", await FailureAsync(h));
    }

    [Fact]
    public async Task Widget_added_later()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);
        await h.SaveReportAsync();
        await h.Indicators.AddWidgetAsync(new { title = "Added on Tuesday", chartType = "stat", config = new { indicator = "households", measure = "value" } });

        await SendAsync(h);

        Assert.Contains("Added on Tuesday", Assert.Single(h.Email.Sent).Message.HtmlBody);
    }

    [Fact]
    public async Task Read_timed_out_still_sends_the_email()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres, new() { ["DataDashboard:LockTimeout"] = "00:00:00.5" });
        await h.Indicators.PublishAsync("coverage", "municipality text, value numeric", "('Cali', 0.42)", format: "percent");
        await h.Indicators.AddWidgetAsync(new { title = "Locked", chartType = "stat", config = new { indicator = "households", measure = "value" } });
        await h.Indicators.AddWidgetAsync(new { title = "Coverage", chartType = "stat", config = new { indicator = "coverage", measure = "value" } });
        await h.SaveReportAsync();
        await using var holder = new NpgsqlConnection(h.Indicators.Db);
        await holder.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand($"LOCK TABLE \"{h.Indicators.Schema}\".households IN ACCESS EXCLUSIVE MODE", holder, transaction))
        {
            await command.ExecuteNonQueryAsync();
        }

        await SendAsync(h);

        var html = Assert.Single(h.Email.Sent).Message.HtmlBody;
        Assert.Contains("This widget could not be read in time.", html);
        Assert.Contains("42%", html);
        Assert.Null(await FailureAsync(h));
    }

    [Fact]
    public async Task Every_widget_timed_out_is_nothing_to_send()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres, new()
        {
            ["DataDashboard:TestStatementSleep"] = "00:00:02",
            ["DataDashboard:StatementTimeout"] = "00:00:00.500",
        });
        await h.Indicators.ExecuteAsync("""
            INSERT INTO tamiza.dashboard_widgets (project_id, dashboard_id, chart_type, title, config, layout, created_by, updated_by)
            SELECT @project, @dashboard, 'stat', 'Total', '{"indicator":"households","measure":"value"}', '{"x":0,"y":0,"w":3,"h":2}', id, id
            FROM tamiza.users WHERE keycloak_sub = 'grace'
            """, ("project", h.Indicators.ProjectId), ("dashboard", h.Indicators.DashboardId));
        await h.SaveReportAsync();

        await SendAsync(h);

        Assert.Empty(h.Email.Sent);
        Assert.Equal("reports.nothingToSend", await FailureAsync(h));
    }

    [Fact]
    public async Task Workbook_too_large()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres, new() { ["Reports:MaxAttachmentKb"] = "1" });
        await h.Indicators.AddWidgetAsync(new { title = "By municipality", chartType = "bar", config = new { indicator = "households", measure = "value", groupBy = "municipality" } });
        await h.SaveReportAsync();

        await SendAsync(h);

        var message = Assert.Single(h.Email.Sent).Message;
        Assert.Empty(message.Attachments);
        Assert.Contains("The workbook was too large to attach.", message.HtmlBody);
        Assert.DoesNotContain("attached workbook", message.HtmlBody);
        Assert.Empty(h.Indicators.ExportFiles);
    }

    [Fact]
    public async Task Failure_shown_then_cleared()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);
        await h.AddStatAsync();
        await h.SaveReportAsync();
        h.Email.Fail = new EmailResult(EmailOutcome.AuthFailed);

        await SendAsync(h);

        Assert.Equal("email.authFailed", await FailureAsync(h));
        Assert.True(await h.ScalarAsync<bool>("SELECT last_failed_at IS NOT NULL FROM tamiza.dashboard_reports WHERE dashboard_id = @dashboard"));

        h.Email.Fail = null;
        await SendAsync(h);

        Assert.Null(await FailureAsync(h));
        Assert.True(await h.ScalarAsync<bool>("SELECT last_sent_at IS NOT NULL FROM tamiza.dashboard_reports WHERE dashboard_id = @dashboard"));
        var view = await h.Indicators.Analyst.GetFromJsonAsync<JsonElement>(h.ReportUrl());
        Assert.Equal(JsonValueKind.Null, view.GetProperty("lastFailure").ValueKind);
    }

    [Theory]
    [InlineData("removed")]
    [InlineData("viewer")]
    public async Task An_author_who_no_longer_manages_the_project_sends_nothing(string change)
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);
        await h.AddStatAsync();
        await h.SaveReportAsync();
        await h.Indicators.ExecuteAsync(change == "removed"
                ? "DELETE FROM tamiza.project_members WHERE project_id = @project AND user_id = (SELECT id FROM tamiza.users WHERE keycloak_sub = 'grace')"
                : "UPDATE tamiza.project_members SET role = 'viewer' WHERE project_id = @project AND user_id = (SELECT id FROM tamiza.users WHERE keycloak_sub = 'grace')",
            ("project", h.Indicators.ProjectId));

        await SendAsync(h);

        Assert.Empty(h.Email.Sent);
        Assert.Equal("reports.authorNoAccess", await FailureAsync(h));
    }

    [Fact]
    public async Task Another_analyst_saving_it_again_takes_it_over()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);
        await h.AddStatAsync();
        await h.SaveReportAsync();
        await h.Indicators.ExecuteAsync(
            "DELETE FROM tamiza.project_members WHERE project_id = @project AND user_id = (SELECT id FROM tamiza.users WHERE keycloak_sub = 'grace')",
            ("project", h.Indicators.ProjectId));

        Assert.Equal(System.Net.HttpStatusCode.OK, (await h.PutReportAsync(client: h.Indicators.Admin)).StatusCode);
        await SendAsync(h);

        Assert.Equal("ada@example.org", Assert.Single(h.Email.Sent).Message.ReplyTo.Mailboxes.Single().Address);
        Assert.Null(await FailureAsync(h));
    }

    [Fact]
    public async Task A_removed_email_is_not_sent()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);
        await h.AddStatAsync();
        await h.SaveReportAsync();
        await h.Indicators.Analyst.DeleteAsync(h.ReportUrl());

        await SendAsync(h);

        Assert.Empty(h.Email.Sent);
    }

    [Fact]
    public async Task Recipient_refused_and_failure_shown_to_members()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);
        await h.AddStatAsync();
        await h.SaveReportAsync();
        h.Email.Fail = new EmailResult(EmailOutcome.Rejected, "nobody@example.org");

        await SendAsync(h);

        var view = await h.Indicators.Analyst.GetFromJsonAsync<JsonElement>(h.ReportUrl());
        var failure = view.GetProperty("lastFailure");
        Assert.Equal("email.rejected", failure.GetProperty("code").GetString());
        Assert.Equal("nobody@example.org", failure.GetProperty("address").GetString());
        Assert.Equal(["address", "at", "code"], failure.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Empty(h.Indicators.ExportFiles);
    }
}
