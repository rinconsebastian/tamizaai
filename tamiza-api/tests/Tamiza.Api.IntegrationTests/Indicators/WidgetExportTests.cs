using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MiniExcelLibs;
using Tamiza.Api.IntegrationTests.Infrastructure;
using Tamiza.Api.Reports;

namespace Tamiza.Api.IntegrationTests.Indicators;

/// <summary>
/// <c>POST /projects/{id}/dashboards/{id}/widget-exports</c>: one widget's data as a workbook, read by the code that builds a scheduled
/// email's, with the filters in force on the member's screen, and downloaded through the shared single-use link (widget-export).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WidgetExportTests(PostgresFixture postgres)
{
    private const string SourceColumns = "municipality text, water_source text, value numeric, numerator numeric, denominator numeric, geometry text";

    private const string SourceRows = """
        ('Cali', 'Piped', 35, 7, 10, 'POINT(1 1)'), ('Cali', 'Well', 15, 3, 10, 'POINT(1 1)'),
        ('Palmira', 'Piped', 20, 2, 10, 'POINT(2 2)'), ('Buga', 'Well', 7, 1, 10, 'POINT(3 3)')
        """;

    private async Task<IndicatorsHarness> HarnessAsync(Dictionary<string, string?>? settings = null)
    {
        var h = await IndicatorsHarness.CreateAsync(postgres, settings);
        await h.PublishAsync("households_by_source", SourceColumns, SourceRows, label: "Households", format: "count", unit: "households");
        return h;
    }

    private static readonly Dictionary<string, object> Configs = new()
    {
        ["stat"] = new { indicator = "households_by_source", measure = "value" },
        ["bar"] = new { indicator = "households_by_source", measure = "value", groupBy = "municipality" },
        ["column"] = new { indicator = "households_by_source", measure = "value", groupBy = "municipality", seriesBy = "water_source" },
        ["line"] = new { indicator = "households_by_source", measure = "value", groupBy = "municipality", seriesBy = "water_source" },
        ["histogram"] = new { indicator = "households_by_source", measure = "value", buckets = 3 },
        ["pie"] = new { indicator = "households_by_source", measure = "value", groupBy = "municipality" },
        ["map"] = new
        {
            indicator = "households_by_source", measure = "value", groupBy = "municipality", geometryColumn = "geometry",
            tooltip = new object[] { new { column = "numerator", label = "Visited" } },
        },
        ["heatmap"] = new { indicator = "households_by_source", measure = "value", groupBy = "municipality", seriesBy = "water_source" },
        ["crosstab"] = new { indicator = "households_by_source", measure = "value", groupBy = "municipality", seriesBy = "water_source" },
        ["pyramid"] = new { indicator = "households_by_source", measure = "value", groupBy = "municipality", seriesBy = "water_source", pyramid = new { left = "Piped", right = "Well" } },
        ["boxplot"] = new { indicator = "households_by_source", measure = "value", groupBy = "municipality" },
        ["likert"] = new
        {
            indicator = "households_by_source", measure = "value", groupBy = "municipality", seriesBy = "water_source",
            scale = new object[] { new { value = "Piped", side = "negative" }, new { value = "Well", side = "positive" } },
        },
    };

    private static object Body(string title, string chartType, object config, object? filters = null, string locale = "en-US", int? version = null, string format = "xlsx") =>
        new { format, title, chartType, config, filters = filters ?? Array.Empty<object>(), locale, version };

    private static string UrlOf(IndicatorsHarness h, Guid? dashboardId = null) => h.DashboardUrl(dashboardId) + "/widget-exports";

    private static async Task<(HttpResponseMessage Response, JsonElement Json)> PostAsync(IndicatorsHarness h, object body, HttpClient? client = null)
    {
        var response = await (client ?? h.Viewer).PostAsJsonAsync(UrlOf(h), body);
        return (response, await response.Content.ReadFromJsonAsync<JsonElement>());
    }

    private static async Task<JsonElement> ExportAsync(IndicatorsHarness h, object body, HttpClient? client = null)
    {
        var (response, json) = await PostAsync(h, body, client);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return json;
    }

    /// <summary>A browser opening the link: no access token. Returns the workbook's path.</summary>
    private static async Task<string> DownloadAsync(IndicatorsHarness h, JsonElement export)
    {
        using var browser = h.Factory.CreateClient();
        var response = await browser.GetAsync(export.GetProperty("downloadUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var path = Path.Combine(Path.GetTempPath(), $"widget-export-{Guid.NewGuid():N}.xlsx");
        await File.WriteAllBytesAsync(path, await response.Content.ReadAsByteArrayAsync());
        return path;
    }

    private static List<IDictionary<string, object?>> Rows(string path, string sheet) =>
        MiniExcel.Query(path, useHeaderRow: true, sheetName: sheet).Cast<IDictionary<string, object?>>().ToList();

    private static string[] Values(IDictionary<string, object?> row) => [.. row.Values.Select(v => v?.ToString() ?? "")];

    // Roles (widget-export "Roles for widget exports")

    [Theory]
    [InlineData("viewer", HttpStatusCode.OK)]
    [InlineData("analyst", HttpStatusCode.OK)]
    [InlineData("admin", HttpStatusCode.OK)]
    [InlineData("outsider", HttpStatusCode.NotFound)]
    public async Task Every_member_gets_a_link_and_a_user_who_is_not_a_member_gets_none(string role, HttpStatusCode expected)
    {
        await using var h = await HarnessAsync();
        var client = role switch { "viewer" => h.Viewer, "analyst" => h.Analyst, "admin" => h.Admin, _ => h.Outsider };

        var (response, json) = await PostAsync(h, Body("Coverage", "bar", Configs["bar"]), client);

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.OK)
        {
            Assert.StartsWith("/api/v1/data-exports/", json.GetProperty("downloadUrl").GetString());
        }
        else
        {
            Assert.Empty(h.ExportFiles);
        }
    }

    // Rows of a widget workbook: the same table as the widget's table in a scheduled email, for every kind

    [Fact]
    public async Task The_data_sheet_is_the_widgets_table_in_a_scheduled_email_for_every_kind()
    {
        await using var h = await HarnessAsync();
        var y = 0;
        foreach (var (chartType, config) in Configs)
        {
            await h.AddWidgetAsync(new { title = chartType, chartType, config, layout = new { x = 0, y, w = 12, h = 4 } });
            y += 4;
        }

        ReportModel email;
        await using (var scope = h.Factory.Services.CreateAsyncScope())
        {
            email = (await scope.ServiceProvider.GetRequiredService<ReportDataBuilder>().BuildAsync(h.DashboardId, "en-US", CancellationToken.None))!;
        }

        Assert.Equal(Configs.Count, email.Widgets.Count);
        foreach (var widget in email.Widgets)
        {
            var path = await DownloadAsync(h, await ExportAsync(h, Body(widget.Title, widget.ChartType, Configs[widget.ChartType])));
            var sheets = MiniExcel.GetSheetNames(path);
            Assert.Equal([widget.Title, "Summary"], sheets);
            var actual = MiniExcel.Query(path, useHeaderRow: false, sheetName: sheets[0]).Cast<IDictionary<string, object?>>().ToList();

            var columns = widget.IsStat ? [widget.StatColumn!] : widget.Columns;
            var rows = widget.IsStat ? [[widget.StatCell!]] : widget.Rows;
            Assert.True(actual.Count == rows.Count + 1, $"{widget.ChartType}: {actual.Count - 1} rows, the email has {rows.Count}");
            Assert.Equal(columns.Count, actual[0].Count);
            for (var r = 0; r < rows.Count; r++)
            {
                var cells = actual[r + 1].Values.ToList();
                for (var c = 0; c < columns.Count; c++)
                {
                    var expected = rows[r][c].Value;
                    var message = $"{widget.ChartType}, row {r + 1}, column {c + 1}";
                    if (expected is double number)
                    {
                        Assert.True(Math.Abs(number - Convert.ToDouble(cells[c], System.Globalization.CultureInfo.InvariantCulture)) < 1e-9, message);
                    }
                    else
                    {
                        Assert.True((expected?.ToString() ?? "") == (cells[c]?.ToString() ?? ""), message);
                    }
                }
            }
        }
    }

    [Fact]
    public async Task A_bar_chart_lists_the_rows_and_the_order_the_query_answers()
    {
        await using var h = await HarnessAsync();
        var query = await h.QueryAsync(Configs["bar"], "bar");

        var path = await DownloadAsync(h, await ExportAsync(h, Body("Coverage", "bar", Configs["bar"])));

        Assert.Equal(IndicatorsHarness.Rows(query), Rows(path, "Coverage").Select(r => $"{Values(r)[0]}|{Convert.ToDouble(Values(r)[1], System.Globalization.CultureInfo.InvariantCulture):0.##}"));
    }

    // Rows and filters of a widget workbook

    private static object MunicipalityFilter(object? @default = null) =>
        new { label = "Municipality", column = "municipality", kind = "text", @default };

    [Fact]
    public async Task A_filter_in_force_narrows_the_rows_and_the_summary_names_it()
    {
        await using var h = await HarnessAsync();
        await h.SaveFiltersAsync(new[] { MunicipalityFilter(), new { label = "Round", column = "round", kind = "number", @default = (object?)null } });
        var filters = new[] { new { column = "municipality", kind = "text", values = new[] { "Cali" }, ignored = false } };

        var path = await DownloadAsync(h, await ExportAsync(h, Body("Coverage", "bar", Configs["bar"], filters)));

        Assert.Equal(["Cali"], Rows(path, "Coverage").Select(r => Values(r)[0]));
        var summary = Rows(path, "Summary").Select(Values).ToList();
        Assert.Contains(summary, r => r.SequenceEqual(["Filters: Municipality", "Cali"]));
        Assert.Contains(summary, r => r.SequenceEqual(["Filters: Round", "All"]));
        Assert.DoesNotContain(summary, r => r[0] == "Not filtered by");
    }

    [Fact]
    public async Task An_ignored_filter_does_not_narrow_the_rows_and_is_listed_as_not_filtering()
    {
        await using var h = await HarnessAsync();
        await h.SaveFiltersAsync(new[] { MunicipalityFilter() });
        var filters = new[] { new { column = "municipality", kind = "text", values = new[] { "Cali" }, ignored = true } };

        var path = await DownloadAsync(h, await ExportAsync(h, Body("Coverage", "bar", Configs["bar"], filters)));

        Assert.Equal(["Buga", "Cali", "Palmira"], Rows(path, "Coverage").Select(r => Values(r)[0]).Order());
        Assert.Contains(Rows(path, "Summary").Select(Values), r => r.SequenceEqual(["Not filtered by", "Municipality"]));
    }

    [Fact]
    public async Task A_default_the_member_cleared_is_not_applied()
    {
        await using var h = await HarnessAsync();
        await h.SaveFiltersAsync(new[] { MunicipalityFilter(new { values = new[] { "Cali" } }) });

        var path = await DownloadAsync(h, await ExportAsync(h, Body("Coverage", "bar", Configs["bar"])));

        Assert.Equal(3, Rows(path, "Coverage").Count);
        Assert.Contains(Rows(path, "Summary").Select(Values), r => r.SequenceEqual(["Filters: Municipality", "All"]));
    }

    [Fact]
    public async Task A_filter_the_dashboard_no_longer_has_is_409_and_leaves_no_file()
    {
        await using var h = await HarnessAsync();
        var filters = new[] { new { column = "round", kind = "number", values = (string[]?)null, min = "3", ignored = false } };

        var (response, json) = await PostAsync(h, Body("Coverage", "bar", Configs["bar"], new[] { new { column = "municipality", kind = "text", values = new[] { "Cali" }, ignored = false } }));
        var (changed, problem) = await PostAsync(h, Body("Coverage", "bar", Configs["bar"], filters));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("widgetExports.filtersChanged", json.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal("widgetExports.filtersChanged", problem.GetProperty("code").GetString());
        Assert.Empty(h.ExportFiles);
    }

    [Fact]
    public async Task A_filter_with_another_kind_than_the_dashboards_is_409()
    {
        await using var h = await HarnessAsync();
        await h.SaveFiltersAsync(new[] { MunicipalityFilter() });
        var filters = new[] { new { column = "municipality", kind = "number", values = (string[]?)null, min = "3", ignored = false } };

        var (response, problem) = await PostAsync(h, Body("Coverage", "bar", Configs["bar"], filters));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("widgetExports.filtersChanged", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_widget_that_lost_its_rows_still_gives_a_file_that_says_why()
    {
        await using var h = await HarnessAsync();
        await h.SaveFiltersAsync(new[] { MunicipalityFilter() });
        var filters = new[] { new { column = "municipality", kind = "text", values = new[] { "Rionegro" }, ignored = false } };

        var path = await DownloadAsync(h, await ExportAsync(h, Body("Coverage", "bar", Configs["bar"], filters)));

        var data = Rows(path, "Coverage");
        Assert.Equal(["Notes"], data[0].Keys);
        Assert.Contains("No rows match", data[0].Values.Single()!.ToString());
        Assert.Contains(Rows(path, "Summary").Select(Values), r => r.SequenceEqual(["Rows", "0"]));
    }

    [Fact]
    public async Task The_row_limit_is_noted_in_the_summary()
    {
        await using var h = await HarnessAsync(new() { ["IndicatorDashboard:MaxWidgetQueryRows"] = "2" });

        var path = await DownloadAsync(h, await ExportAsync(h, Body("Coverage", "bar", Configs["bar"])));

        Assert.Equal(2, Rows(path, "Coverage").Count);
        Assert.Contains(Rows(path, "Summary").Select(Values), r => r[0] == "Notes" && r[1].Contains("Only the first 2 rows"));
    }

    // Widget of a version (widget-export "Rows of a widget workbook", "Roles for widget exports")

    private static async Task<int> CurrentVersionAsync(IndicatorsHarness h) =>
        (await h.Analyst.GetFromJsonAsync<JsonElement>(h.DashboardUrl() + "/versions")).GetProperty("current").GetInt32();

    [Fact]
    public async Task An_analyst_exports_a_widget_of_a_version_that_the_dashboard_no_longer_has()
    {
        await using var h = await HarnessAsync();
        var widget = await h.AddWidgetAsync(new { title = "Gone", chartType = "bar", config = Configs["bar"] });
        var version = await CurrentVersionAsync(h);
        Assert.Equal(HttpStatusCode.NoContent, (await h.Analyst.DeleteAsync(h.WidgetUrl(widget.GetProperty("id").GetGuid()))).StatusCode);

        var export = await ExportAsync(h, Body("Gone", "bar", Configs["bar"], version: version), h.Analyst);

        Assert.Matches($@"^coverage-2026_indicators_gone_v{version}_\d{{4}}-\d{{2}}-\d{{2}}\.xlsx$", export.GetProperty("fileName").GetString());
        var path = await DownloadAsync(h, export);
        var summary = Rows(path, "Summary").Select(Values).ToList();
        Assert.Contains(summary, r => r[0] == "Version" && r[1].StartsWith($"Version {version}, saved by "));
        Assert.Equal(3, Rows(path, "Gone").Count);
    }

    [Fact]
    public async Task A_viewer_cannot_export_a_widget_of_a_version_and_an_unknown_version_is_404()
    {
        await using var h = await HarnessAsync();
        await h.AddWidgetAsync(new { title = "W", chartType = "bar", config = Configs["bar"] });
        var version = await CurrentVersionAsync(h);

        var (viewer, _) = await PostAsync(h, Body("W", "bar", Configs["bar"], version: version), h.Viewer);
        var (unknown, problem) = await PostAsync(h, Body("W", "bar", Configs["bar"], version: 999), h.Analyst);

        Assert.Equal(HttpStatusCode.Forbidden, viewer.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("dashboards.versionNotFound", problem.GetProperty("code").GetString());
        Assert.Empty(h.ExportFiles);
    }

    [Fact]
    public async Task Exporting_records_no_dashboard_version()
    {
        await using var h = await HarnessAsync();
        await h.AddWidgetAsync(new { title = "W", chartType = "bar", config = Configs["bar"] });
        var before = await CurrentVersionAsync(h);

        await ExportAsync(h, Body("W", "bar", Configs["bar"]));
        await ExportAsync(h, Body("W", "bar", Configs["bar"]), h.Analyst);

        Assert.Equal(before, await CurrentVersionAsync(h));
    }

    // Download (widget-export "Widget workbook download")

    [Fact]
    public async Task The_link_works_once_and_the_file_is_gone_after_it()
    {
        await using var h = await HarnessAsync();
        var export = await ExportAsync(h, Body("Coverage", "bar", Configs["bar"]));
        Assert.Single(h.ExportFiles);

        using var browser = h.Factory.CreateClient();
        var first = await browser.GetAsync(export.GetProperty("downloadUrl").GetString());
        var second = await browser.GetAsync(export.GetProperty("downloadUrl").GetString());

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", first.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    [Fact]
    public async Task The_answer_names_the_file_and_when_the_data_was_published()
    {
        await using var h = await HarnessAsync();

        var export = await ExportAsync(h, Body("Coverage by municipality", "bar", Configs["bar"]));
        var first = export.GetProperty("publishedAt").GetDateTimeOffset();
        await h.PublishAsync("households_by_source", SourceColumns, SourceRows, label: "Households", format: "count", unit: "households", script: false);
        var again = await ExportAsync(h, Body("Coverage by municipality", "bar", Configs["bar"]));

        Assert.Matches(@"^coverage-2026_indicators_coverage-by-municipality_\d{4}-\d{2}-\d{2}\.xlsx$", export.GetProperty("fileName").GetString());
        Assert.Equal(3, export.GetProperty("rowCount").GetInt64());
        Assert.True(again.GetProperty("publishedAt").GetDateTimeOffset() > first);
    }

    [Fact]
    public async Task A_file_past_the_size_limit_is_422_and_leaves_none_behind()
    {
        await using var h = await HarnessAsync(new() { ["DataDashboard:MaxExportFileSize"] = "2000" });

        var (response, problem) = await PostAsync(h, Body("Coverage", "bar", Configs["bar"]));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("data.exportTooLarge", problem.GetProperty("code").GetString());
        Assert.Contains("2,000", problem.GetProperty("detail").GetString());
        Assert.Empty(h.ExportFiles);
    }

    // Refused workbooks (widget-export "Refused widget workbooks")

    [Fact]
    public async Task An_indicator_that_has_not_published_is_404()
    {
        await using var h = await HarnessAsync();
        await h.AddIndicatorAsync("visits", label: "Visits");

        var (response, problem) = await PostAsync(h, Body("Visits", "stat", new { indicator = "visits", measure = "value" }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("indicators.notPublished", problem.GetProperty("code").GetString());
        Assert.Empty(h.ExportFiles);
    }

    [Fact]
    public async Task A_column_the_table_no_longer_has_is_422_naming_it()
    {
        await using var h = await HarnessAsync();

        var (response, problem) = await PostAsync(h, Body("Coverage", "bar", new { indicator = "households_by_source", measure = "value", groupBy = "neighborhood" }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("widgetExports.missingColumns", problem.GetProperty("code").GetString());
        Assert.Contains("neighborhood", problem.GetProperty("detail").GetString());
        Assert.Empty(h.ExportFiles);
    }

    [Fact]
    public async Task A_read_that_takes_too_long_is_503_and_leaves_no_file()
    {
        await using var h = await HarnessAsync(new()
        {
            ["DataDashboard:TestStatementSleep"] = "00:00:03",
            ["DataDashboard:StatementTimeout"] = "00:00:00.500",
        });

        var (response, problem) = await PostAsync(h, Body("Coverage", "bar", Configs["bar"]));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("data.timeout", problem.GetProperty("code").GetString());
        Assert.Empty(h.ExportFiles);
    }

    [Fact]
    public async Task A_dashboard_the_project_does_not_have_is_404()
    {
        await using var h = await HarnessAsync();

        var response = await h.Viewer.PostAsJsonAsync(UrlOf(h, Guid.NewGuid()), Body("Coverage", "bar", Configs["bar"]));
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("dashboards.notFound", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_malformed_request_is_400_with_the_fields_that_are_wrong()
    {
        await using var h = await HarnessAsync();

        var (response, problem) = await PostAsync(h, new
        {
            format = "csv",
            title = " ",
            chartType = "radar",
            config = new { indicator = "households_by_source", measure = "value" },
            filters = Enumerable.Range(0, 9).Select(i => new { column = $"c{i}", kind = "text", values = new[] { "x" }, ignored = false }).ToArray(),
            locale = "fr-FR",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var fields = problem.GetProperty("errors").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("format", fields);
        Assert.Contains("title", fields);
        Assert.Contains("chartType", fields);
        Assert.Contains("filters", fields);
        Assert.Contains("locale", fields);
        Assert.Empty(h.ExportFiles);
    }

    [Fact]
    public async Task A_widget_with_a_series_the_chart_type_does_not_take_is_refused_like_a_query()
    {
        await using var h = await HarnessAsync();

        var (response, problem) = await PostAsync(h, Body("Boxes", "boxplot", new { indicator = "households_by_source", measure = "value", groupBy = "municipality", seriesBy = "water_source" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(problem.GetProperty("errors").EnumerateObject(), p => p.Name.StartsWith("config", StringComparison.Ordinal));
    }

    // Column labels (widget-export "Column labels in widget files")

    private async Task<IndicatorsHarness> TeatroAsync(object? labels)
    {
        var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("teatro_hoy", "comuna text, value numeric, numerator numeric, denominator numeric",
            "('Zona 1', 0.5, 5, 10), ('Zona 2', 0.25, 5, 20)", label: "Teatro hoy", format: "decimal", labels: labels);
        return h;
    }

    private static readonly object TeatroIndicatorLabels = new
    {
        numerator = new { label = "Hogares que asistieron" },
        denominator = new { label = "Hogares" },
        comuna = new { label = "Comuna" },
    };

    private static object TeatroConfig(object? display = null) => new
    {
        indicator = "teatro_hoy", ratio = new { numerator = "numerator", denominator = "denominator" }, groupBy = "comuna", display,
    };

    [Fact]
    public async Task The_indicators_labels_name_the_columns_of_the_data_sheet_and_the_summary()
    {
        await using var h = await TeatroAsync(TeatroIndicatorLabels);

        var path = await DownloadAsync(h, await ExportAsync(h, Body("Asistencia", "column", TeatroConfig())));

        var sheet = Rows(path, "Asistencia");
        Assert.Equal(["Comuna", "Teatro hoy: Hogares que asistieron / Hogares"], sheet[0].Keys.Select(key => key.Split(" (")[0]));
        Assert.Contains(Rows(path, "Summary").Select(Values), r => r.SequenceEqual(["Plotted", "Teatro hoy: Hogares que asistieron / Hogares"]));
    }

    [Fact]
    public async Task A_widgets_own_label_in_the_request_is_written_in_the_headers_and_the_summary()
    {
        await using var h = await TeatroAsync(TeatroIndicatorLabels);

        var path = await DownloadAsync(h, await ExportAsync(h, Body("Asistencia", "column", TeatroConfig(new { labels = new { denominator = "Hogares de la base E1" } }))));

        Assert.Equal(["Comuna", "Teatro hoy: Hogares que asistieron / Hogares de la base E1"], Rows(path, "Asistencia")[0].Keys.Select(key => key.Split(" (")[0]));
        Assert.Contains(Rows(path, "Summary").Select(Values), r => r.SequenceEqual(["Plotted", "Teatro hoy: Hogares que asistieron / Hogares de la base E1"]));
    }

    [Fact]
    public async Task The_labels_of_a_widget_of_a_version_are_those_the_request_carries()
    {
        await using var h = await TeatroAsync(TeatroIndicatorLabels);
        var widget = await h.AddWidgetAsync(new { title = "Asistencia", chartType = "column", config = TeatroConfig(new { labels = new { denominator = "Hogares hoy" } }) });
        var version = await CurrentVersionAsync(h);
        Assert.Equal(HttpStatusCode.NoContent, (await h.Analyst.DeleteAsync(h.WidgetUrl(widget.GetProperty("id").GetGuid()))).StatusCode);

        var export = await ExportAsync(h, Body("Asistencia", "column", TeatroConfig(new { labels = new { denominator = "Hogares de la versión" } }), version: version), h.Analyst);

        var path = await DownloadAsync(h, export);
        Assert.Equal(["Comuna", "Teatro hoy: Hogares que asistieron / Hogares de la versión"], Rows(path, "Asistencia")[0].Keys.Select(key => key.Split(" (")[0]));
    }

    [Fact]
    public async Task A_request_without_a_display_and_an_indicator_without_labels_gives_the_workbook_it_gave_before()
    {
        await using var h = await TeatroAsync(null);

        var path = await DownloadAsync(h, await ExportAsync(h, Body("Asistencia", "column", TeatroConfig())));

        Assert.Equal(["comuna", "Teatro hoy: numerator / denominator"], Rows(path, "Asistencia")[0].Keys.Select(key => key.Split(" (")[0]));
        Assert.Contains(Rows(path, "Summary").Select(Values), r => r.SequenceEqual(["Plotted", "Teatro hoy: numerator / denominator"]));
    }

    [Fact]
    public async Task Labels_that_a_save_would_refuse_are_refused_and_leave_no_file()
    {
        await using var h = await TeatroAsync(null);

        var (response, problem) = await PostAsync(h, Body("Asistencia", "column", TeatroConfig(new { labels = new { denominator = new string('a', 81) } })));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(problem.GetProperty("errors").EnumerateObject(), p => p.Name.Contains("display.labels", StringComparison.Ordinal));
        Assert.Empty(h.ExportFiles);
    }

    // Widget context (widget-export "Widget context in a widget workbook", "Summary of a widget workbook")

    private static object Ratio(object display) => TeatroConfig(display);

    [Fact]
    public async Task Own_text_and_unit_in_the_header()
    {
        await using var h = await TeatroAsync(TeatroIndicatorLabels);
        var display = new { caption = new { text = "% de quienes asistieron" }, unit = "de quienes asistieron" };

        var path = await DownloadAsync(h, await ExportAsync(h, Body("2.2", "column", Ratio(display))));

        Assert.Equal(["Comuna", "% de quienes asistieron (de quienes asistieron)"], Rows(path, "2.2")[0].Keys);
        Assert.Contains(Rows(path, "Summary").Select(Values), r => r.SequenceEqual(["Plotted", "% de quienes asistieron"]));
    }

    [Fact]
    public async Task Hidden_caption_in_the_workbook()
    {
        await using var h = await TeatroAsync(TeatroIndicatorLabels);

        var path = await DownloadAsync(h, await ExportAsync(h, Body("2.2", "column", Ratio(new { caption = new { hidden = true } }))));

        Assert.Equal(["Comuna", "Teatro hoy: Hogares que asistieron / Hogares"], Rows(path, "2.2")[0].Keys);
        Assert.Contains(Rows(path, "Summary").Select(Values), r => r.SequenceEqual(["Plotted", "Teatro hoy: Hogares que asistieron / Hogares"]));
    }

    [Fact]
    public async Task Map_tooltip_labels()
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("campo", "comuna text, value numeric, geometry text, registros bigint, meta bigint",
            "('Zona 1', 0.5, 'POINT(1 1)', 12, 2000), ('Zona 2', 0.25, 'POINT(2 2)', 8, 3000)", label: "Campo", format: "decimal",
            labels: new { registros = new { label = "Registros recibidos" } });
        var config = new
        {
            indicator = "campo", measure = "value", groupBy = "comuna", geometryColumn = "geometry",
            tooltip = new object[] { new { column = "registros" }, new { column = "meta", label = "Meta muestral", format = "count" } },
        };

        var path = await DownloadAsync(h, await ExportAsync(h, Body("Mapa", "map", config)));

        var sheet = Rows(path, "Mapa");
        Assert.Equal(["comuna", "Campo: value", "Registros recibidos", "Meta muestral"], sheet[0].Keys);
        Assert.Equal(2000d, Convert.ToDouble(sheet[0]["Meta muestral"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(12d, Convert.ToDouble(sheet[0]["Registros recibidos"], System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Widget_texts_in_the_summary_with_the_dashboards_source_and_with_a_versions_source()
    {
        await using var h = await TeatroAsync(TeatroIndicatorLabels);
        await h.SaveSourceAsync("Encuesta de mercado, 478 encuestas");
        await h.AddWidgetAsync(new { title = "2.4", chartType = "column", config = TeatroConfig() });
        var version = await CurrentVersionAsync(h);
        await h.SaveSourceAsync("Otra fuente");
        var display = new { subtitle = "¿En qué lugares ha visto teatro?", footnote = "No suman 100 %: respuesta múltiple.", caption = new { text = "% de quienes asistieron" } };

        var current = await DownloadAsync(h, await ExportAsync(h, Body("2.4", "column", Ratio(display))));
        var past = await DownloadAsync(h, await ExportAsync(h, Body("2.4", "column", Ratio(display), version: version), h.Analyst));

        var summary = Rows(current, "Summary").Select(Values).ToList();
        Assert.Contains(summary, r => r.SequenceEqual(["Source", "Otra fuente"]));
        Assert.Contains(summary, r => r.SequenceEqual(["Subtitle", "¿En qué lugares ha visto teatro?"]));
        Assert.Contains(summary, r => r.SequenceEqual(["Plotted", "% de quienes asistieron"]));
        Assert.Contains(summary, r => r.SequenceEqual(["Footnote", "No suman 100 %: respuesta múltiple."]));
        Assert.Contains(Rows(past, "Summary").Select(Values), r => r.SequenceEqual(["Source", "Encuesta de mercado, 478 encuestas"]));
    }

    // Reference lines (widget-export "Reference lines in a widget workbook")

    private async Task<IndicatorsHarness> TeatroPercentAsync()
    {
        var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync("teatro_hoy", "comuna text, value numeric, numerator numeric, denominator numeric",
            "('Zona 1', 0.5, 5, 10), ('Zona 2', 0.25, 5, 20)", label: "Teatro hoy", format: "percent", unit: "de los hogares");
        return h;
    }

    [Fact]
    public async Task Reference_lines_are_one_item_of_the_summary_after_what_is_plotted_and_the_data_sheet_is_unchanged()
    {
        await using var h = await TeatroPercentAsync();
        var display = new
        {
            referenceLines = new object[] { new { kind = "mean" }, new { kind = "overall" }, new { kind = "value", value = 0.5, label = "Meta" } },
        };

        var path = await DownloadAsync(h, await ExportAsync(h, Body("2.1", "column", Ratio(display))));

        var summary = Rows(path, "Summary").Select(Values).ToList();
        var plotted = summary.FindIndex(r => r[0] == "Plotted");
        Assert.Equal(["Reference lines", "Mean: 37.5% de los hogares · Overall: 33.33% de los hogares · Meta: 50% de los hogares"], summary[plotted + 1]);
        Assert.Equal(["Zona 1", "Zona 2"], Rows(path, "2.1").Select(r => Values(r)[0]));
        Assert.Equal(2, Rows(path, "2.1")[0].Count);
    }

    [Fact]
    public async Task Reference_lines_of_a_spanish_member_use_the_spanish_labels_and_formats()
    {
        await using var h = await TeatroPercentAsync();
        var display = new { referenceLines = new object[] { new { kind = "mean" }, new { kind = "overall" } } };

        var path = await DownloadAsync(h, await ExportAsync(h, Body("2.1", "column", Ratio(display), locale: "es-CO")));

        Assert.Contains(
            Rows(path, "Resumen").Select(Values),
            r => r.SequenceEqual(["Líneas de referencia", "Promedio: 37,5% de los hogares · Valor general: 33,33% de los hogares"]));
    }

    [Fact]
    public async Task A_widget_without_reference_lines_has_no_such_item_in_its_summary()
    {
        await using var h = await TeatroPercentAsync();

        var path = await DownloadAsync(h, await ExportAsync(h, Body("2.1", "column", Ratio(new { subtitle = "¿Cuántos?" }))));

        Assert.DoesNotContain(Rows(path, "Summary").Select(Values), r => r[0] == "Reference lines");
    }

    [Fact]
    public async Task A_request_with_no_texts_on_a_dashboard_with_no_source_has_no_line_for_them()
    {
        await using var h = await TeatroAsync(TeatroIndicatorLabels);

        var path = await DownloadAsync(h, await ExportAsync(h, Body("2.2", "column", TeatroConfig())));

        Assert.DoesNotContain(Rows(path, "Summary").Select(Values), r => r[0] is "Source" or "Subtitle" or "Footnote");
        Assert.Equal(["Comuna", "Teatro hoy: Hogares que asistieron / Hogares"], Rows(path, "2.2")[0].Keys);
    }

    [Fact]
    public async Task A_widget_sent_with_its_ignored_filters_in_its_config_or_in_each_filter_gives_the_same_rows()
    {
        await using var h = await HarnessAsync();
        await h.SaveFiltersAsync(new[] { MunicipalityFilter() });
        var cali = new[] { new { column = "municipality", kind = "text", values = new[] { "Cali" }, ignored = false } };
        var caliIgnored = new[] { new { column = "municipality", kind = "text", values = new[] { "Cali" }, ignored = true } };
        var inConfig = new { indicator = "households_by_source", measure = "value", groupBy = "municipality", ignoredDashboardFilters = new[] { "municipality" } };

        var byFlag = await DownloadAsync(h, await ExportAsync(h, Body("W", "bar", Configs["bar"], caliIgnored)));
        var byConfig = await DownloadAsync(h, await ExportAsync(h, Body("W", "bar", inConfig, cali)));
        var byBoth = await DownloadAsync(h, await ExportAsync(h, Body("W", "bar", inConfig, caliIgnored)));

        foreach (var path in new[] { byFlag, byConfig, byBoth })
        {
            Assert.Equal(["Buga", "Cali", "Palmira"], Rows(path, "W").Select(r => Values(r)[0]).Order());
            Assert.Contains(Rows(path, "Summary").Select(Values), r => r.SequenceEqual(["Not filtered by", "Municipality"]));
        }
    }

    [Fact]
    public async Task A_subtitle_of_201_characters_is_refused_on_the_configs_display_and_leaves_no_file()
    {
        await using var h = await TeatroAsync(null);

        var (response, problem) = await PostAsync(h, Body("2.2", "column", Ratio(new { subtitle = new string('s', 201), unit = new string('u', 33) })));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var fields = problem.GetProperty("errors").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("config.display.subtitle", fields);
        Assert.Contains("config.display.unit", fields);
        Assert.Empty(h.ExportFiles);
    }

    [Fact]
    public async Task A_caption_with_a_text_and_hidden_is_refused_on_the_configs_caption()
    {
        await using var h = await TeatroAsync(null);

        var (response, problem) = await PostAsync(h, Body("2.2", "column", Ratio(new { caption = new { text = "x", hidden = true } })));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("config.display.caption", problem.GetProperty("errors").EnumerateObject().Select(p => p.Name));
    }

    // Language (widget-export "Language of widget files")

    [Fact]
    public async Task A_spanish_member_gets_the_spanish_words_and_the_widget_title_as_written()
    {
        await using var h = await HarnessAsync();
        await h.SaveFiltersAsync(new[] { MunicipalityFilter() });

        var path = await DownloadAsync(h, await ExportAsync(h, Body("Coverage by municipality", "column", Configs["column"], locale: "es-CO")));

        Assert.Equal(["Coverage by municipality", "Resumen"], MiniExcel.GetSheetNames(path));
        Assert.Contains(Rows(path, "Coverage by municipality")[0].Keys, key => key.StartsWith("Total", StringComparison.Ordinal));
        Assert.Equal(["Elemento", "Valor"], Rows(path, "Resumen")[0].Keys);
        Assert.Contains(Rows(path, "Resumen").Select(Values), r => r.SequenceEqual(["Filtros: Municipality", "Todos"]));
    }
}
