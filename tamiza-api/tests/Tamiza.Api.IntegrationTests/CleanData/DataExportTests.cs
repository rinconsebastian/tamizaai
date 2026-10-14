using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MiniExcelLibs;
using Npgsql;
using Tamiza.Api.CleanData;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.CleanData;

/// <summary>Exports: <c>POST .../exports</c> prepares a file, and the public single-use link downloads it.</summary>
[Collection(PostgresCollection.Name)]
public sealed class DataExportTests(PostgresFixture postgres)
{
    private const string PersonColumns = "name text, municipality text, age bigint, income numeric, consent boolean, born date, at timestamptz, note text";

    private const string PersonRows = """
        ('Ana',      'Cali',    17, 10.5, true,  '2026-08-31', '2026-09-01 04:59:59+00', NULL),
        ('Beto',     'Cali',    18, 20,   false, '2026-09-01', '2026-09-01 05:00:00+00', ''),
        ('Carla',    'Palmira', 29, NULL, NULL,  '2026-09-02', '2026-09-02 02:30:00+00', 'x'),
        ('Dani',     'Cali',    NULL, 7.25, false, NULL,       NULL,                     NULL),
        ('Eve, "E"', 'Palmira', 40, 1,    true,  '2026-09-04', '2026-09-03 23:30:00-05', 'y')
        """;

    private async Task<CleanDataHarness> WithPersonsAsync(string? timeZone = null, Dictionary<string, string?>? settings = null, Action<IServiceCollection>? configure = null)
    {
        var h = await CleanDataHarness.CreateAsync(postgres, timeZone, settings, configure);
        await h.PublishAsync("output", ("persons", PersonColumns, PersonRows));
        return h;
    }

    private static string ExportUrl(CleanDataHarness h, string table = "persons", string source = "output") =>
        $"{h.Url}/{source}/{Uri.EscapeDataString(table)}/exports";

    private static async Task<HttpResponseMessage> PostAsync(CleanDataHarness h, object body, HttpClient? client = null, string table = "persons") =>
        await (client ?? h.Viewer).PostAsJsonAsync(ExportUrl(h, table), body);

    private static async Task<JsonElement> PrepareAsync(CleanDataHarness h, object body, HttpClient? client = null, string table = "persons")
    {
        var response = await PostAsync(h, body, client, table);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>A browser opening the link: no access token.</summary>
    private static async Task<HttpResponseMessage> DownloadAsync(CleanDataHarness h, JsonElement export)
    {
        using var browser = h.Factory.CreateClient();
        return await browser.GetAsync(export.GetProperty("downloadUrl").GetString());
    }

    private static string[] CsvLines(byte[] bytes) =>
        new UTF8Encoding(false).GetString(bytes).TrimEnd('\r', '\n').Split("\r\n");

    private static async Task<string> WaitUntilEmptyAsync(CleanDataHarness h)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (h.ExportFiles.Length > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        return string.Join(", ", h.ExportFiles);
    }

    // Filtered export

    [Fact]
    public async Task A_viewer_exports_the_filtered_rows_to_csv_in_the_chosen_order()
    {
        await using var h = await WithPersonsAsync();

        var export = await PrepareAsync(h, new
        {
            format = "csv",
            filters = new[] { new { column = "municipality", op = "equals", value = "Cali" } },
            sort = new { column = "name", direction = "desc" },
        });
        var response = await DownloadAsync(h, export);
        var bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, export.GetProperty("rowCount").GetInt64());
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Equal(
            [
                "﻿name,municipality,age,income,consent,born,at,note",
                "Dani,Cali,,7.25,false,,,",
                "Beto,Cali,18,20,false,2026-09-01,2026-09-01T05:00:00+00:00,",
                "Ana,Cali,17,10.5,true,2026-08-31,2026-09-01T04:59:59+00:00,",
            ],
            CsvLines(bytes));
    }

    [Fact]
    public async Task The_export_has_every_matching_row_not_only_a_page()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("output", ("numbers", "n bigint", ""));
        await h.ExecuteAsync($"INSERT INTO \"{h.Schema("output")}\".numbers SELECT g FROM generate_series(1, 4830) g");

        var export = await PrepareAsync(h, new { format = "csv", filters = new[] { new { column = "n", op = "range", min = "1", max = "830" } } }, table: "numbers");
        var lines = CsvLines(await (await DownloadAsync(h, export)).Content.ReadAsByteArrayAsync());

        Assert.Equal(830, export.GetProperty("rowCount").GetInt64());
        Assert.Equal(831, lines.Length); // the header and every match
    }

    [Fact]
    public async Task Csv_quotes_commas_and_quotes_and_keeps_accents()
    {
        await using var h = await WithPersonsAsync();
        await h.ExecuteAsync($"INSERT INTO \"{h.Schema("output")}\".persons (name, municipality) VALUES ('Bogotá, D.C.', 'Bogotá')");

        var export = await PrepareAsync(h, new { format = "csv", filters = new[] { new { column = "name", op = "contains", value = "," } } });
        var lines = CsvLines(await (await DownloadAsync(h, export)).Content.ReadAsByteArrayAsync());

        Assert.Contains("\"Eve, \"\"E\"\"\",Palmira,40,1,true,2026-09-04,2026-09-04T04:30:00+00:00,y", lines);
        Assert.Contains("\"Bogotá, D.C.\",Bogotá,,,,,,", lines);
    }

    [Fact]
    public async Task Timestamps_are_written_in_the_projects_zone_with_their_offset()
    {
        await using var h = await WithPersonsAsync(timeZone: "America/Bogota");

        var export = await PrepareAsync(h, new { format = "csv", filters = new[] { new { column = "name", op = "equals", value = "Carla" } } });
        var lines = CsvLines(await (await DownloadAsync(h, export)).Content.ReadAsByteArrayAsync());

        Assert.Equal("Carla,Palmira,29,,,2026-09-02,2026-09-01T21:30:00-05:00,x", lines[1]); // 02:30 UTC
    }

    [Fact]
    public async Task A_long_text_is_whole_in_the_export()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("output", ("notes", "id bigint, comment text", ""));
        await h.ExecuteAsync($"INSERT INTO \"{h.Schema("output")}\".notes VALUES (1, repeat('é', 2000))");

        var export = await PrepareAsync(h, new { format = "csv" }, table: "notes");
        var lines = CsvLines(await (await DownloadAsync(h, export)).Content.ReadAsByteArrayAsync());

        Assert.Equal("1," + new string('é', 2000), lines[1]);
    }

    [Fact]
    public async Task An_xlsx_export_has_typed_cells_and_the_filtered_rows()
    {
        await using var h = await WithPersonsAsync(timeZone: "America/Bogota");

        var export = await PrepareAsync(h, new
        {
            format = "xlsx",
            filters = new[] { new { column = "municipality", op = "equals", value = "Palmira" } },
            sort = new { column = "age", direction = "asc" },
        });
        var response = await DownloadAsync(h, export);
        var rows = new MemoryStream(await response.Content.ReadAsByteArrayAsync()).Query(useHeaderRow: true).Cast<IDictionary<string, object?>>().ToList();

        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(2, rows.Count);
        Assert.Equal("Carla", rows[0]["name"]);
        Assert.Equal(29, Convert.ToInt64(rows[0]["age"]));
        Assert.Null(rows[0]["income"]);
        Assert.Equal(new DateTime(2026, 9, 1, 21, 30, 0), Assert.IsType<DateTime>(rows[0]["at"]));
        Assert.Equal(true, rows[1]["consent"]);
        Assert.Equal(0, export.GetProperty("cutCells").GetInt64());
    }

    [Fact]
    public async Task A_cell_over_32767_characters_is_cut_in_xlsx_and_reported()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("output", ("notes", "id bigint, comment text", ""));
        await h.ExecuteAsync($"INSERT INTO \"{h.Schema("output")}\".notes VALUES (1, repeat('x', 40000)), (2, 'short')");

        var export = await PrepareAsync(h, new { format = "xlsx" }, table: "notes");
        var rows = new MemoryStream(await (await DownloadAsync(h, export)).Content.ReadAsByteArrayAsync()).Query(useHeaderRow: true).Cast<IDictionary<string, object?>>().ToList();

        Assert.Equal(1, export.GetProperty("cutCells").GetInt64());
        Assert.Equal(32_767, ((string)rows[0]["comment"]!).Length);
        Assert.Equal("short", rows[1]["comment"]);
    }

    [Fact]
    public async Task A_cell_that_looks_like_a_formula_is_text_in_the_xlsx()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("output", ("notes", "comment text", "('=1+1')"));

        var export = await PrepareAsync(h, new { format = "xlsx" }, table: "notes");
        var bytes = await (await DownloadAsync(h, export)).Content.ReadAsByteArrayAsync();
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        using var sheet = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var xml = await sheet.ReadToEndAsync();

        Assert.DoesNotContain("<x:f>", xml);
        Assert.Contains(">=1+1<", xml);
    }

    // Names, filters and roles

    [Fact]
    public async Task The_download_name_is_the_project_the_table_and_the_day()
    {
        await using var h = await WithPersonsAsync();
        await h.ExecuteAsync("UPDATE tamiza.projects SET name = 'Encuesta Cali 2026'");

        var export = await PrepareAsync(h, new { format = "csv" });
        var response = await DownloadAsync(h, export);

        var expected = $"encuesta-cali-2026_persons_{DateTime.UtcNow:yyyy-MM-dd}.csv";
        Assert.Equal(expected, export.GetProperty("fileName").GetString());
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal(expected, response.Content.Headers.ContentDisposition.FileName);
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("")]
    [InlineData("CSV")]
    public async Task An_unknown_format_is_a_validation_error(string format)
    {
        await using var h = await WithPersonsAsync();

        var response = await PostAsync(h, new { format });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("csv", problem.GetProperty("errors").GetProperty("format")[0].GetString());
        Assert.Empty(h.ExportFiles);
    }

    [Fact]
    public async Task A_request_without_a_body_or_a_format_is_a_validation_error()
    {
        await using var h = await WithPersonsAsync();

        var response = await h.Viewer.PostAsync(ExportUrl(h), content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_invalid_filter_is_a_validation_error_and_prepares_nothing()
    {
        await using var h = await WithPersonsAsync();

        var response = await PostAsync(h, new { format = "csv", filters = new[] { new { column = "age", op = "range", min = "adult", max = (string?)null } } });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("'age'", problem.GetProperty("errors").GetProperty("filters[0]")[0].GetString());
        Assert.Empty(h.ExportFiles);
    }

    [Theory]
    [InlineData("salaries")]
    [InlineData("persons\"; DROP SCHEMA public CASCADE; --")]
    public async Task A_table_that_is_not_published_is_404(string table)
    {
        await using var h = await WithPersonsAsync();

        var response = await PostAsync(h, new { format = "csv" }, table: table);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(h.ExportFiles);
        Assert.True(await TestData.ScalarAsync<bool>(h.Db, "SELECT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'public')"));
    }

    [Fact]
    public async Task Every_member_may_export_and_a_non_member_gets_404_without_a_link()
    {
        await using var h = await WithPersonsAsync();
        await TestData.SignInAsync(h.Factory, "mallory", "mallory@example.org");
        using var stranger = TestData.ClientFor(h.Factory, "mallory", "mallory@example.org");

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(h, new { format = "csv" }, h.Viewer)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(h, new { format = "csv" }, h.Analyst)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(h, new { format = "xlsx" }, h.Admin)).StatusCode);
        var files = h.ExportFiles.Length;
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(h, new { format = "csv" }, stranger)).StatusCode);
        Assert.Equal(files, h.ExportFiles.Length);
        Assert.Equal(files, h.Factory.Services.GetRequiredService<ExportRegistry>().Pending);
    }

    // The link

    [Fact]
    public async Task The_browser_downloads_with_no_access_token_and_the_file_is_deleted_afterwards()
    {
        await using var h = await WithPersonsAsync();
        var export = await PrepareAsync(h, new { format = "csv" });
        Assert.Single(h.ExportFiles);

        using var browser = h.Factory.CreateClient();
        Assert.Null(browser.DefaultRequestHeaders.Authorization);
        var response = await browser.GetAsync(export.GetProperty("downloadUrl").GetString());
        var bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.NotEmpty(bytes);
        Assert.Equal("", await WaitUntilEmptyAsync(h));
    }

    [Fact]
    public async Task A_link_opened_a_second_time_is_404_and_serves_nothing()
    {
        await using var h = await WithPersonsAsync();
        var export = await PrepareAsync(h, new { format = "csv" });

        var first = await DownloadAsync(h, export);
        await first.Content.ReadAsByteArrayAsync();
        var second = await DownloadAsync(h, export);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.DoesNotContain("Ana", await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_link_that_was_never_issued_is_404()
    {
        await using var h = await WithPersonsAsync();
        using var browser = h.Factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/api/v1/data-exports/not-a-real-link")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/api/v1/data-exports/" + new string('A', 43))).StatusCode);
    }

    [Fact]
    public async Task An_expired_link_is_404_and_its_file_is_gone()
    {
        var time = new AdjustableTimeProvider();
        await using var h = await WithPersonsAsync(configure: services => services.AddSingleton<TimeProvider>(time));
        var export = await PrepareAsync(h, new { format = "csv" });
        Assert.Single(h.ExportFiles);

        time.Advance(TimeSpan.FromMinutes(6));
        var response = await DownloadAsync(h, export);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(h.ExportFiles);
    }

    [Fact]
    public async Task The_result_says_when_the_link_expires()
    {
        await using var h = await WithPersonsAsync();

        var export = await PrepareAsync(h, new { format = "csv" });

        var expires = export.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.InRange(expires - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5.1));
    }

    [Fact]
    public async Task A_download_that_is_abandoned_still_deletes_the_file()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("output", ("notes", "id bigint, comment text", ""));
        await h.ExecuteAsync($"INSERT INTO \"{h.Schema("output")}\".notes SELECT g, repeat('z', 2000) FROM generate_series(1, 3000) g");
        var export = await PrepareAsync(h, new { format = "csv" }, table: "notes");
        Assert.Single(h.ExportFiles);

        using var browser = h.Factory.CreateClient();
        var response = await browser.GetAsync(export.GetProperty("downloadUrl").GetString(), HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        response.Dispose(); // the browser went away before the end

        Assert.Equal("", await WaitUntilEmptyAsync(h));
    }

    // Row order

    [Fact]
    public async Task Without_a_sort_the_export_keeps_the_order_rows_were_published_in_even_over_many_pages_of_the_heap()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("output", ("numbers", "n bigint, filler text", ""));
        // Written in this order, in several thousand blocks: neither the values nor their hashes follow it.
        await h.ExecuteAsync($"INSERT INTO \"{h.Schema("output")}\".numbers SELECT (g * 7919) % 100003, repeat('x', 400) FROM generate_series(1, 30000) g");
        var expected = Enumerable.Range(1, 30000).Select(g => ((long)g * 7919) % 100003).ToList();

        var all = CsvLines(await (await DownloadAsync(h, await PrepareAsync(h, new { format = "csv" }, table: "numbers"))).Content.ReadAsByteArrayAsync());
        var filtered = CsvLines(await (await DownloadAsync(h, await PrepareAsync(
            h, new { format = "csv", filters = new[] { new { column = "n", op = "range", min = "50000", max = (string?)null } } }, table: "numbers"))).Content.ReadAsByteArrayAsync());

        Assert.Equal(expected, all.Skip(1).Select(l => long.Parse(l.Split(',')[0])));
        Assert.Equal(expected.Where(n => n >= 50000), filtered.Skip(1).Select(l => long.Parse(l.Split(',')[0])));
    }

    [Fact]
    public async Task A_sorted_export_is_in_the_chosen_order_over_many_blocks_with_equal_values_in_publishing_order()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("output", ("numbers", "grp bigint, seq bigint, filler text", ""));
        await h.ExecuteAsync($"INSERT INTO \"{h.Schema("output")}\".numbers SELECT g % 3, g, repeat('x', 400) FROM generate_series(1, 9000) g");

        var lines = CsvLines(await (await DownloadAsync(h, await PrepareAsync(
            h, new { format = "csv", sort = new { column = "grp", direction = "desc" } }, table: "numbers"))).Content.ReadAsByteArrayAsync());

        var expected = Enumerable.Range(1, 9000).OrderByDescending(g => g % 3).ThenBy(g => g).Select(g => (long)g);
        Assert.Equal(expected, lines.Skip(1).Select(l => long.Parse(l.Split(',')[1])));
    }

    // Limits

    [Fact]
    public async Task Too_many_rows_for_xlsx_is_refused_with_a_suggestion_and_prepares_no_file()
    {
        await using var h = await WithPersonsAsync(settings: new() { ["DataDashboard:XlsxMaxRows"] = "3" });

        var response = await PostAsync(h, new { format = "xlsx" });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var csv = await PostAsync(h, new { format = "csv" });
        var filtered = await PostAsync(h, new { format = "xlsx", filters = new[] { new { column = "municipality", op = "equals", value = "Cali" } } });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("data.xlsxTooManyRows", problem.GetProperty("code").GetString());
        Assert.Contains("CSV", problem.GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);       // the same rows are fine as CSV
        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);  // and 3 rows fit
        Assert.Equal(2, h.ExportFiles.Length);
    }

    [Fact]
    public async Task A_file_that_grows_past_the_size_limit_fails_and_leaves_no_file()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres, settings: new() { ["DataDashboard:MaxExportFileSize"] = "50000" });
        await h.PublishAsync("output", ("notes", "id bigint, comment text", ""));
        await h.ExecuteAsync($"INSERT INTO \"{h.Schema("output")}\".notes SELECT g, repeat('z', 500) FROM generate_series(1, 5000) g");

        var response = await PostAsync(h, new { format = "csv" }, table: "notes");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("data.exportTooLarge", problem.GetProperty("code").GetString());
        Assert.Contains("50,000", problem.GetProperty("detail").GetString());
        Assert.Empty(h.ExportFiles);

        var small = await PostAsync(h, new { format = "csv", filters = new[] { new { column = "id", op = "range", min = "1", max = "10" } } }, table: "notes");
        Assert.Equal(HttpStatusCode.OK, small.StatusCode);
    }

    [Fact]
    public async Task An_export_that_takes_too_long_is_503_and_leaves_no_file()
    {
        await using var h = await WithPersonsAsync(settings: new()
        {
            ["DataDashboard:TestStatementSleep"] = "00:00:03",
            ["DataDashboard:ExportStatementTimeout"] = "00:00:01",
        });

        var response = await PostAsync(h, new { format = "csv" });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("data.timeout", problem.GetProperty("code").GetString());
        Assert.Empty(h.ExportFiles);
    }

    [Fact]
    public async Task When_every_export_slot_is_taken_a_new_export_waits_then_gets_503()
    {
        await using var h = await WithPersonsAsync(settings: new()
        {
            ["DataDashboard:MaxConcurrentExports"] = "1",
            ["DataDashboard:QueueTimeout"] = "00:00:00.300",
            ["DataDashboard:TestStatementSleep"] = "00:00:02",
        });

        var first = PostAsync(h, new { format = "csv" });
        await Task.Delay(500); // the first has its slot and is sleeping in the database
        var second = await PostAsync(h, new { format = "csv" });
        var problem = await second.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        Assert.Equal("data.exportBusy", problem.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await first).StatusCode);
    }

    [Fact]
    public async Task The_tables_lock_is_released_when_the_file_is_ready_not_when_it_is_downloaded()
    {
        await using var h = await WithPersonsAsync();
        await PrepareAsync(h, new { format = "csv" }); // prepared and waiting for its download

        // A run swapping the tables needs an exclusive lock: it must get it at once.
        await using var connection = new NpgsqlConnection(h.Db);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var timeout = new NpgsqlCommand("SET LOCAL lock_timeout = '1s'", connection, transaction))
        {
            await timeout.ExecuteNonQueryAsync();
        }

        await using var exclusive = new NpgsqlCommand($"LOCK TABLE \"{h.Schema("output")}\".persons IN ACCESS EXCLUSIVE MODE", connection, transaction);
        await exclusive.ExecuteNonQueryAsync();
        await transaction.RollbackAsync();
    }

    // Republishing

    [Fact]
    public async Task A_table_removed_by_a_run_is_404_to_export()
    {
        await using var h = await WithPersonsAsync();
        await h.PublishAsync("output", ("households", "size bigint", "(1)"));

        var response = await PostAsync(h, new { format = "csv" });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("data.tableNotFound", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_openapi_document_lists_the_export_operations()
    {
        await using var h = await WithPersonsAsync();
        using var client = h.Factory.CreateClient();

        var document = await client.GetFromJsonAsync<JsonElement>("/api/v1/openapi.json");
        var paths = document.GetProperty("paths");

        Assert.Equal("ExportDataTable", paths.GetProperty("/api/v1/projects/{projectId}/data/{source}/{table}/exports").GetProperty("post").GetProperty("operationId").GetString());
        Assert.Equal("DownloadDataExport", paths.GetProperty("/api/v1/data-exports/{token}").GetProperty("get").GetProperty("operationId").GetString());
        Assert.True(document.GetProperty("components").GetProperty("schemas").TryGetProperty("DataExportResult", out _));
    }
}
