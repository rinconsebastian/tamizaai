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
    public async Task Sends_a_box_plot_and_its_sheet_holds_every_box_with_numbers_in_the_formats_of_its_figures()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);
        await h.Indicators.PublishAsync(
            "household_size", "municipality text, value numeric",
            string.Join(", ", Enumerable.Range(1, 12).SelectMany(m => Enumerable.Range(1, 5).Select(v => $"('M{m:00}', {m + v})"))),
            label: "Household size", format: "count", unit: "people");
        await h.Indicators.AddWidgetAsync(new
        {
            title = "Spread", chartType = "boxplot", config = new { indicator = "household_size", groupBy = "municipality" }, layout = new { x = 0, y = 0, w = 12, h = 4 },
        });
        await h.SaveReportAsync();

        await SendAsync(h);

        var sent = Assert.Single(h.Email.Sent);
        Assert.Null(await FailureAsync(h));
        // Ten rows in the body, as for any table, and all nine columns.
        Assert.Contains(">Median</th>", sent.Message.HtmlBody);
        Assert.Contains(">Outliers</th>", sent.Message.HtmlBody);
        Assert.Contains("2 more rows", sent.Message.HtmlBody);
        var attachment = Assert.IsType<MimePart>(Assert.Single(sent.Message.Attachments));
        using var content = new MemoryStream();
        await attachment.Content.DecodeToAsync(content);
        content.Position = 0;
        Assert.Equal(["Summary", "1. Spread"], MiniExcelLibs.MiniExcel.GetSheetNames(content));
        content.Position = 0;
        var sheet = MiniExcelLibs.MiniExcel.Query(content, useHeaderRow: true, sheetName: "1. Spread").Cast<IDictionary<string, object?>>().ToList();
        Assert.Equal(12, sheet.Count);
        Assert.Equal(9, sheet[0].Count);
        // M01 holds 2, 3, 4, 5, 6: the median is 4, written as a number.
        Assert.Equal(5, Convert.ToInt32(sheet[0]["Values"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(4d, Convert.ToDouble(sheet[0]["Median (people)"], System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Sends_the_column_labels_in_the_body_the_text_and_the_workbook()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);
        await h.Indicators.PublishAsync("teatro_hoy", "comuna text, value numeric, numerator numeric, denominator numeric",
            "('Zona 1', 0.5, 5, 10), ('Zona 2', 0.25, 5, 20)", label: "Teatro hoy",
            labels: new { numerator = new { label = "Hogares que asistieron" }, denominator = new { label = "Hogares" }, comuna = new { label = "Comuna" } });
        await h.Indicators.AddWidgetAsync(new
        {
            title = "Asistencia", chartType = "stat", config = new { indicator = "teatro_hoy", ratio = new { numerator = "numerator", denominator = "denominator" } },
            layout = new { x = 0, y = 0, w = 3, h = 2 },
        });
        await h.Indicators.AddWidgetAsync(new
        {
            title = "Por comuna", chartType = "column",
            config = new { indicator = "teatro_hoy", ratio = new { numerator = "numerator", denominator = "denominator" }, groupBy = "comuna" },
            layout = new { x = 0, y = 2, w = 12, h = 4 },
        });
        await h.SaveReportAsync();

        await SendAsync(h);

        var sent = Assert.Single(h.Email.Sent);
        Assert.Null(await FailureAsync(h));
        Assert.Contains("Teatro hoy: Hogares que asistieron / Hogares", sent.Message.HtmlBody);
        Assert.Contains(">Comuna</th>", sent.Message.HtmlBody);
        Assert.DoesNotContain("numerator", sent.Message.HtmlBody);
        Assert.Contains("Teatro hoy: Hogares que asistieron / Hogares", sent.Message.TextBody);
        Assert.DoesNotContain("numerator", sent.Message.TextBody);
        var attachment = Assert.IsType<MimePart>(Assert.Single(sent.Message.Attachments));
        using var content = new MemoryStream();
        await attachment.Content.DecodeToAsync(content);
        content.Position = 0;
        var sheet = MiniExcelLibs.MiniExcel.Query(content, useHeaderRow: true, sheetName: "2. Por comuna").Cast<IDictionary<string, object?>>().ToList();
        Assert.Equal(["Comuna", "Teatro hoy: Hogares que asistieron / Hogares"], sheet[0].Keys);
    }

    [Fact]
    public async Task A_label_that_looks_like_a_formula_is_written_to_the_workbook_header_as_text()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);
        await h.Indicators.PublishAsync("teatro_hoy", "comuna text, value numeric", "('Zona 1', 5), ('Zona 2', 7)", label: "Teatro hoy",
            labels: new { comuna = new { label = "=Hogares" } });
        await h.Indicators.AddWidgetAsync(new
        {
            title = "Por comuna", chartType = "column", config = new { indicator = "teatro_hoy", measure = "value", groupBy = "comuna" },
            layout = new { x = 0, y = 0, w = 12, h = 4 },
        });
        await h.SaveReportAsync();

        await SendAsync(h);

        var sent = Assert.Single(h.Email.Sent);
        var attachment = Assert.IsType<MimePart>(Assert.Single(sent.Message.Attachments));
        using var content = new MemoryStream();
        await attachment.Content.DecodeToAsync(content);
        content.Position = 0;
        var rows = MiniExcelLibs.MiniExcel.Query(content, useHeaderRow: false, sheetName: "1. Por comuna").Cast<IDictionary<string, object?>>().ToList();
        Assert.Equal("=Hogares", rows[0].Values.First());
        Assert.IsType<string>(rows[0].Values.First());
        content.Position = 0;
        using var package = new System.IO.Compression.ZipArchive(content);
        foreach (var entry in package.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal)))
        {
            using var reader = new StreamReader(entry.Open());
            Assert.DoesNotContain("<f>", await reader.ReadToEndAsync());
        }
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

    [Fact]
    public async Task Sends_a_widget_under_a_threshold_with_its_n_line_and_its_no_data_line_in_the_body_the_text_and_the_workbook()
    {
        await using var h = await ReportsHarness.CreateAsync(postgres);
        // A long table shaped like teatro_hoy with invented values: COMUNA 7 rests on 16 households, SIN NOMBRE on 20 and "Sin dato" on 4.
        (string Comuna, int Households)[] comunas = [("COMUNA 1", 120), ("COMUNA 7", 16), ("SIN NOMBRE", 20), ("Sin dato", 4)];
        var rows = string.Join(", ", comunas.SelectMany(c => new[]
        {
            $"('Asistio (E1)', '1', 'Si', '{c.Comuna}', 0, {c.Households / 2}, {c.Households})",
            $"('Asistio (E1)', '2', 'No', '{c.Comuna}', 0, {c.Households / 2}, {c.Households})",
        }));
        await h.Indicators.PublishAsync(
            "teatro_hoy", "variable text, orden text, categoria text, comuna text, value numeric, numerator numeric, denominator numeric", rows,
            label: "Teatro hoy", format: "percent",
            labels: new
            {
                variable = new { categories = "multiple" }, orden = new { categories = "multiple" }, categoria = new { categories = "multiple" },
                comuna = new { missing = new[] { "Sin dato" } }, denominator = new { label = "Hogares" },
            });
        var ratio = new { numerator = "numerator", denominator = "denominator" };
        var filters = new object[] { new { column = "variable", op = "equals", value = "Asistio (E1)" }, new { column = "categoria", op = "equals", value = "Si" } };
        await h.Indicators.AddWidgetAsync(new
        {
            title = "Asistieron", chartType = "stat", config = new { indicator = "teatro_hoy", ratio, filters }, layout = new { x = 0, y = 0, w = 3, h = 2 },
        });
        await h.Indicators.AddWidgetAsync(new
        {
            title = "2.1. Asistio, por comuna", chartType = "column",
            config = new { indicator = "teatro_hoy", ratio, groupBy = "comuna", filters, missingFooter = true, display = new { footnote = "Sin ponderar." } },
            layout = new { x = 0, y = 2, w = 12, h = 4 },
        });
        await h.Indicators.SaveBasesAsync(30, true);
        await h.SaveReportAsync();

        await SendAsync(h);

        var sent = Assert.Single(h.Email.Sent);
        Assert.Null(await FailureAsync(h));
        foreach (var body in new[] { sent.Message.HtmlBody, sent.Message.TextBody })
        {
            // The mark of a small base, the n column's values, and the three lines of the widget above its table, before its footnote.
            Assert.Contains("50% *", body);
            Assert.Contains("2 values have a base below 30, marked *.", body);
            Assert.Contains("n = 156 Hogares", body);
            Assert.Contains("Sin dato: 50% \u00b7 n = 4", body);
            Assert.DoesNotContain("COMUNA 1 *", body);
            Assert.True(body.IndexOf("n = 156 Hogares", StringComparison.Ordinal) < body.IndexOf("SIN NOMBRE", StringComparison.Ordinal));
            Assert.True(body.IndexOf("Sin ponderar.", StringComparison.Ordinal) > body.LastIndexOf("SIN NOMBRE", StringComparison.Ordinal));
        }

        // "Sin dato" is left out of the body's table, and the stat tile carries its n line too (it does not leave "Sin dato" out, so its n counts it; it is not a table, so it is not marked).
        Assert.DoesNotContain(">Sin dato</td>", sent.Message.HtmlBody);
        Assert.Contains("  n = 160 Hogares", sent.Message.TextBody);
        Assert.Contains(">n</th>", sent.Message.HtmlBody);

        // The workbook holds the numbers alone, and its Summary the notes.
        var attachment = Assert.IsType<MimePart>(Assert.Single(sent.Message.Attachments));
        using var content = new MemoryStream();
        await attachment.Content.DecodeToAsync(content);
        content.Position = 0;
        var sheet = MiniExcelLibs.MiniExcel.Query(content, useHeaderRow: true, sheetName: "2. 2.1. Asistio, por comuna").Cast<IDictionary<string, object?>>().ToList();
        Assert.Equal(["comuna", "Teatro hoy: numerator / Hogares", "n"], sheet[0].Keys.Select(key => key.Split(" (")[0]));
        var comuna7 = sheet.Single(r => r["comuna"]?.ToString() == "COMUNA 7");
        Assert.Equal(0.5, Convert.ToDouble(comuna7["Teatro hoy: numerator / Hogares"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(16d, Convert.ToDouble(comuna7["n"], System.Globalization.CultureInfo.InvariantCulture));
        content.Position = 0;
        var summary = MiniExcelLibs.MiniExcel.Query(content, useHeaderRow: true, sheetName: "Summary").Cast<IDictionary<string, object?>>()
            .Select(r => r.Values.Select(v => v?.ToString() ?? "").ToArray()).ToList();
        Assert.Contains(summary, r => r[0] == "Notes: 2.1. Asistio, por comuna" && r[1].Contains("n = 156 Hogares", StringComparison.Ordinal)
            && r[1].Contains("Sin dato: 50%", StringComparison.Ordinal));
        Assert.Contains(summary, r => r[0] == "Notes: Asistieron" && r[1] == "n = 160 Hogares");
    }
}
