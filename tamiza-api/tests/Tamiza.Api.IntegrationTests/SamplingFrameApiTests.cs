using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MiniExcelLibs;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class SamplingFrameApiTests(PostgresFixture postgres)
{
    private sealed record Setup(TamizaApiFactory Factory, string Db, Guid ProjectId, HttpClient Analyst, HttpClient Viewer) : IAsyncDisposable
    {
        public string Url => $"/api/v1/projects/{ProjectId}/sampling-frame";

        public async ValueTask DisposeAsync()
        {
            Analyst.Dispose();
            Viewer.Dispose();
            await Factory.DisposeAsync();
        }
    }

    private async Task<Setup> ProjectAsync()
    {
        var db = await postgres.CreateDatabaseAsync();
        var factory = new TamizaApiFactory(db);
        var adminId = await TestData.SignInAsync(factory, "ada", "ada@example.org");
        var projectId = await TestData.InsertProjectAsync(db, adminId, formFieldsJson: TestData.SurveyFieldsJson);
        await TestData.AddMemberAsync(db, projectId, adminId, "admin");
        var analystId = await TestData.SignInAsync(factory, "grace", "grace@example.org");
        await TestData.AddMemberAsync(db, projectId, analystId, "analyst");
        var viewerId = await TestData.SignInAsync(factory, "vic", "vic@example.org");
        await TestData.AddMemberAsync(db, projectId, viewerId, "viewer");
        return new Setup(factory, db, projectId,
            TestData.ClientFor(factory, "grace", "grace@example.org"), TestData.ClientFor(factory, "vic", "vic@example.org"));
    }

    private static readonly object ValidFrame = new
    {
        dimensions = new[] { new { name = "Municipality", field = "municipality" }, new { name = "Sex", field = "sex" } },
        targets = new[] { new { values = new[] { "Cali", "F" }, target = 120 }, new { values = new[] { "Cali", "M" }, target = 110 } },
    };

    private static async Task<HttpResponseMessage> UploadAsync(Setup s, byte[] content, string fileName)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        return await s.Analyst.PostAsync($"{s.Url}/import", form);
    }

    private static byte[] Csv(string text) => Encoding.UTF8.GetBytes(text);

    // 5.2

    [Fact]
    public async Task A_saved_frame_reads_back_the_same_for_viewers()
    {
        await using var s = await ProjectAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await s.Viewer.GetAsync(s.Url)).StatusCode);

        var saved = await s.Analyst.PutAsJsonAsync(s.Url, ValidFrame);
        var read = await s.Viewer.GetFromJsonAsync<JsonElement>(s.Url);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(["Municipality", "Sex"], read.GetProperty("dimensions").EnumerateArray().Select(d => d.GetProperty("name").GetString()));
        Assert.Equal(["Cali", "M"], read.GetProperty("targets")[1].GetProperty("values").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(110, read.GetProperty("targets")[1].GetProperty("target").GetInt32());
    }

    [Fact]
    public async Task An_invalid_frame_returns_field_paths()
    {
        await using var s = await ProjectAsync();
        var frame = new
        {
            dimensions = new[] { new { name = "Age", field = "age" } },
            targets = new object[] { new { values = new[] { "18-24" }, target = -3 }, new { values = new[] { "18-24" }, target = 1.5 } },
        };

        var response = await s.Analyst.PutAsJsonAsync(s.Url, frame);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        foreach (var path in new[] { "dimensions[0].field", "targets[0].target", "targets[1].target", "targets[1]" })
        {
            Assert.True(errors.TryGetProperty(path, out _), path);
        }
    }

    [Fact]
    public async Task Viewers_cannot_save_and_the_frame_is_unchanged()
    {
        await using var s = await ProjectAsync();
        await s.Analyst.PutAsJsonAsync(s.Url, ValidFrame);
        var before = await s.Viewer.GetStringAsync(s.Url);

        var response = await s.Viewer.PutAsJsonAsync(s.Url, new { dimensions = new[] { new { name = "Sex", field = "sex" } }, targets = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await s.Viewer.GetStringAsync(s.Url));
    }

    // 5.3

    [Theory]
    [InlineData("Municipality,Sex,Age range,Target\nCali,F,18-24,120\nCali,M,18-24,110\n")]
    [InlineData("﻿Municipality;Sex;Age range;target\r\nCali;F;18-24;120\r\nCali;M;18-24;110\r\n")]
    public async Task A_valid_csv_produces_a_preview_without_saving(string csv)
    {
        await using var s = await ProjectAsync();

        var response = await UploadAsync(s, Csv(csv), "frame.csv");
        var preview = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["Municipality", "Sex", "Age range"], preview.GetProperty("dimensions").EnumerateArray().Select(d => d.GetString()));
        Assert.Equal([120, 110], preview.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("target").GetInt32()));
        Assert.Empty(preview.GetProperty("errors").EnumerateArray());
        Assert.Equal(HttpStatusCode.NoContent, (await s.Viewer.GetAsync(s.Url)).StatusCode);
    }

    [Fact]
    public async Task A_valid_xlsx_produces_a_preview()
    {
        await using var s = await ProjectAsync();
        using var xlsx = new MemoryStream();
        await xlsx.SaveAsAsync(new[]
        {
            new Dictionary<string, object> { ["municipality"] = "Cali", ["sex"] = "F", ["target"] = 120 },
            new Dictionary<string, object> { ["municipality"] = "Cali", ["sex"] = "M", ["target"] = 110 },
        }, excelType: ExcelType.XLSX);

        var response = await UploadAsync(s, xlsx.ToArray(), "frame.xlsx");
        var preview = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["municipality", "sex"], preview.GetProperty("dimensions").EnumerateArray().Select(d => d.GetString()));
        Assert.Equal(2, preview.GetProperty("rows")[1].GetProperty("row").GetInt32() - 1);
        Assert.Equal(110, preview.GetProperty("rows")[1].GetProperty("target").GetInt32());
    }

    [Fact]
    public async Task A_file_without_a_target_column_is_reported()
    {
        await using var s = await ProjectAsync();

        var preview = await (await UploadAsync(s, Csv("Municipality,Sex\nCali,F\n"), "frame.csv")).Content.ReadFromJsonAsync<JsonElement>();

        var error = Assert.Single(preview.GetProperty("errors").EnumerateArray());
        Assert.Equal(1, error.GetProperty("row").GetInt32());
        Assert.Contains("'target'", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task An_invalid_row_is_reported_with_its_spreadsheet_row_number()
    {
        await using var s = await ProjectAsync();
        var csv = new StringBuilder("Municipality,target\n");
        for (var i = 2; i <= 9; i++)
        {
            csv.Append($"Town {i},{(i == 7 ? "seven" : "10")}\n");
        }

        var preview = await (await UploadAsync(s, Csv(csv.ToString()), "frame.csv")).Content.ReadFromJsonAsync<JsonElement>();

        var error = Assert.Single(preview.GetProperty("errors").EnumerateArray());
        Assert.Equal(7, error.GetProperty("row").GetInt32());
        Assert.Contains("whole number", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Unsupported_extensions_and_oversized_files_are_rejected()
    {
        await using var s = await ProjectAsync();

        var json = await UploadAsync(s, Csv("{}"), "frame.json");
        var oversized = await UploadAsync(s, new byte[SamplingFrameImportLimits.MaxBytes + 1], "frame.csv");

        Assert.Equal(HttpStatusCode.BadRequest, json.StatusCode);
        Assert.Contains(".csv", (await json.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetProperty("file")[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        Assert.Contains("5 MB", (await oversized.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetProperty("file")[0].GetString());
    }

    [Fact]
    public async Task Too_many_rows_are_rejected_and_the_saved_frame_is_unchanged()
    {
        await using var s = await ProjectAsync();
        await s.Analyst.PutAsJsonAsync(s.Url, ValidFrame);
        var before = await s.Viewer.GetStringAsync(s.Url);
        var csv = new StringBuilder("Municipality,target\n");
        for (var i = 0; i <= 10_000; i++)
        {
            csv.Append($"Town {i},1\n");
        }

        var response = await UploadAsync(s, Csv(csv.ToString()), "frame.csv");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("10,000", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetProperty("file")[0].GetString());
        Assert.Equal(before, await s.Viewer.GetStringAsync(s.Url));
    }

    private static class SamplingFrameImportLimits
    {
        public const int MaxBytes = 5 * 1024 * 1024;
    }
}
