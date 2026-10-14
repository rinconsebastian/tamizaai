using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MiniExcelLibs;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Indicators;

/// <summary><c>POST /projects/{id}/indicators/exports</c>: the whole published table, downloaded through the shared single-use link.</summary>
[Collection(PostgresCollection.Name)]
public sealed class IndicatorExportTests(PostgresFixture postgres)
{
    private const string Columns = IndicatorsHarness.RequiredColumns + ", year bigint, geometry text";

    private const string Rows = """
        ('coverage', 'Cali',    40.5, 4, 10, 2025, 'POINT(1 1)'),
        ('coverage', 'Palmira', 60,   6, 10, 2025, NULL),
        ('target',   'Cali, "centre"', 90, 9, 10, 2026, NULL)
        """;

    private static async Task<IndicatorsHarness> PublishedAsync(PostgresFixture postgres)
    {
        var h = await IndicatorsHarness.CreateAsync(postgres);
        await h.PublishAsync(Columns, Rows);
        return h;
    }

    private static async Task<JsonElement> PrepareAsync(IndicatorsHarness h, string format, HttpClient? client = null)
    {
        var response = await (client ?? h.Viewer).PostAsJsonAsync(h.Url + "/exports", new { format });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>A browser opening the link: no access token.</summary>
    private static async Task<HttpResponseMessage> DownloadAsync(IndicatorsHarness h, JsonElement export)
    {
        using var browser = h.Factory.CreateClient();
        return await browser.GetAsync(export.GetProperty("downloadUrl").GetString());
    }

    // Viewer exports

    [Fact]
    public async Task A_viewer_exports_every_row_and_column_to_csv_exactly_as_published()
    {
        await using var h = await PublishedAsync(postgres);

        var export = await PrepareAsync(h, "csv");
        var response = await DownloadAsync(h, export);
        var lines = new UTF8Encoding(false).GetString(await response.Content.ReadAsByteArrayAsync()).TrimEnd('\r', '\n').Split("\r\n");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, export.GetProperty("rowCount").GetInt64());
        Assert.Equal(0, export.GetProperty("cutCells").GetInt64());
        Assert.StartsWith("/api/v1/data-exports/", export.GetProperty("downloadUrl").GetString());
        Assert.Matches(@"^coverage-2026_indicators_\d{4}-\d{2}-\d{2}\.csv$", export.GetProperty("fileName").GetString());
        Assert.Equal(
            [
                "﻿indicator,disaggregation,value,numerator,denominator,year,geometry",
                "coverage,Cali,40.5,4,10,2025,POINT(1 1)",
                "coverage,Palmira,60,6,10,2025,",
                "target,\"Cali, \"\"centre\"\"\",90,9,10,2026,",
            ],
            lines);
    }

    [Fact]
    public async Task A_viewer_exports_the_table_to_xlsx_with_typed_cells()
    {
        await using var h = await PublishedAsync(postgres);

        var export = await PrepareAsync(h, "xlsx");
        var response = await DownloadAsync(h, export);
        var rows = new MemoryStream(await response.Content.ReadAsByteArrayAsync()).Query(useHeaderRow: true).Cast<IDictionary<string, object?>>().ToList();

        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType!.MediaType);
        Assert.EndsWith("_indicators_" + DateTime.UtcNow.ToString("yyyy-MM-dd") + ".xlsx", export.GetProperty("fileName").GetString());
        Assert.Equal(3, rows.Count);
        Assert.Equal(["indicator", "disaggregation", "value", "numerator", "denominator", "year", "geometry"], rows[0].Keys);
        Assert.Equal("coverage", rows[0]["indicator"]);
        Assert.Equal(40.5, Convert.ToDouble(rows[0]["value"]));
        Assert.Equal(2025, Convert.ToInt64(rows[0]["year"]));
        Assert.Null(rows[1]["geometry"]);
        Assert.Equal("Cali, \"centre\"", rows[2]["disaggregation"]);
    }

    [Fact]
    public async Task Every_role_can_export()
    {
        await using var h = await PublishedAsync(postgres);

        foreach (var client in new[] { h.Viewer, h.Analyst, h.Admin })
        {
            var export = await PrepareAsync(h, "csv", client);
            Assert.Equal(HttpStatusCode.OK, (await DownloadAsync(h, export)).StatusCode);
        }
    }

    [Fact]
    public async Task The_link_works_once_and_needs_no_token_and_the_file_is_deleted_afterwards()
    {
        await using var h = await PublishedAsync(postgres);
        var export = await PrepareAsync(h, "csv");
        Assert.Single(h.ExportFiles);

        var first = await DownloadAsync(h, export);
        await first.Content.ReadAsByteArrayAsync();
        var second = await DownloadAsync(h, export);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (h.ExportFiles.Length > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        Assert.Empty(h.ExportFiles);
    }

    [Fact]
    public async Task The_export_has_the_extra_columns_a_script_added_and_none_of_the_clean_data_dashboards_tables()
    {
        await using var h = await PublishedAsync(postgres);
        await PublishedTables.PublishAsync(h.Db, h.ProjectId, "output", ("persons", "name text", "('Ana')"));

        var export = await PrepareAsync(h, "csv");
        var text = new UTF8Encoding(false).GetString(await (await DownloadAsync(h, export)).Content.ReadAsByteArrayAsync());

        Assert.Contains("year,geometry", text);
        Assert.DoesNotContain("Ana", text);
    }

    // Not published / validation / access

    [Theory]
    [InlineData("csv")]
    [InlineData("xlsx")]
    public async Task While_script_3_has_not_published_there_is_nothing_to_export(string format)
    {
        await using var h = await IndicatorsHarness.CreateAsync(postgres);

        var response = await h.Viewer.PostAsJsonAsync(h.Url + "/exports", new { format });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("indicators.notPublished", problem.GetProperty("code").GetString());
        Assert.Empty(h.ExportFiles);
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("")]
    [InlineData(null)]
    public async Task A_format_other_than_csv_or_xlsx_is_a_validation_error(string? format)
    {
        await using var h = await PublishedAsync(postgres);

        var response = await h.Viewer.PostAsJsonAsync(h.Url + "/exports", new { format });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(IndicatorsHarness.Errors(problem, "format"));
    }

    [Fact]
    public async Task A_user_who_is_not_a_member_gets_404()
    {
        await using var h = await PublishedAsync(postgres);

        var response = await h.Outsider.PostAsJsonAsync(h.Url + "/exports", new { format = "csv" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(h.ExportFiles);
    }

    [Fact]
    public async Task The_clean_data_exports_keep_working_next_to_it()
    {
        await using var h = await PublishedAsync(postgres);
        await PublishedTables.PublishAsync(h.Db, h.ProjectId, "output", ("persons", "name text", "('Ana')"));

        var response = await h.Viewer.PostAsJsonAsync($"/api/v1/projects/{h.ProjectId}/data/output/persons/exports", new { format = "csv" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
