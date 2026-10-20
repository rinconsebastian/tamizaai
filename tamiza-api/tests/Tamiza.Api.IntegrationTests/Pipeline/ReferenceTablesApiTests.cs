using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MiniExcelLibs;
using Tamiza.Api.IntegrationTests.Infrastructure;
using Tamiza.Api.Projects;

namespace Tamiza.Api.IntegrationTests.Pipeline;

[Collection(PostgresCollection.Name)]
public sealed class ReferenceTablesApiTests(PostgresFixture postgres) : IDisposable
{
    private const string Population = "Municipality code;Year;Population\n05001;2026;2612958\n11001;2026;7968095\n";

    private readonly KoboStub _kobo = new KoboStub().WithForm();

    private static string Tables(IngestionHarness h) => $"/api/v1/projects/{h.ProjectId}/reference-tables";

    private static async Task<HttpClient> MemberAsync(IngestionHarness harness, string subject, string role)
    {
        var userId = await TestData.SignInAsync(harness.Factory, subject, $"{subject}@example.org");
        await TestData.AddMemberAsync(harness.Db, harness.ProjectId, userId, role);
        return TestData.ClientFor(harness.Factory, subject, $"{subject}@example.org");
    }

    private static MultipartFormDataContent Form(byte[] content, string fileName, string? name = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        if (name is not null)
        {
            form.Add(new StringContent(name), "name");
        }

        return form;
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, IngestionHarness h, string name, string csv, string fileName = "population.csv") =>
        client.PostAsync(Tables(h), Form(Encoding.UTF8.GetBytes(csv), fileName, name));

    private static async Task<string> ErrorAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        return problem.GetProperty("errors").GetProperty(field)[0].GetString()!;
    }

    [Fact]
    public async Task An_analyst_uploads_a_table_that_analysts_and_admins_list_and_preview_and_viewers_cannot()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var analyst = await MemberAsync(h, "ana", "analyst");
        using var viewer = await MemberAsync(h, "vic", "viewer");

        var response = await UploadAsync(analyst, h, "population", Population);
        var upload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(["Municipality code", "Year", "Population"], upload.GetProperty("renamed").EnumerateArray().Select(r => r.GetProperty("header").GetString()));
        var listed = Assert.Single((await analyst.GetFromJsonAsync<JsonElement>(Tables(h))).EnumerateArray());
        Assert.Equal("population", listed.GetProperty("name").GetString());
        Assert.Equal("population.csv", listed.GetProperty("fileName").GetString());
        Assert.Equal(2, listed.GetProperty("rowCount").GetInt32());
        Assert.Equal("ana", listed.GetProperty("updatedBy").GetProperty("name").GetString());
        Assert.Equal(["text", "integer", "integer"], listed.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("type").GetString()));

        var preview = await h.Admin.GetFromJsonAsync<JsonElement>($"{Tables(h)}/population");
        Assert.Equal("05001", preview.GetProperty("rows")[0][0].GetString());
        Assert.Equal(7968095, preview.GetProperty("rows")[1][2].GetInt64());
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(Tables(h))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"{Tables(h)}/population")).StatusCode);
    }

    [Fact]
    public async Task The_preview_holds_the_first_100_rows()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        var csv = "n\n" + string.Concat(Enumerable.Range(1, 150).Select(i => $"{i}\n"));
        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(h.Admin, h, "numbers", csv)).StatusCode);

        var preview = await h.Admin.GetFromJsonAsync<JsonElement>($"{Tables(h)}/numbers");

        Assert.Equal(150, preview.GetProperty("table").GetProperty("rowCount").GetInt32());
        Assert.Equal(100, preview.GetProperty("rows").GetArrayLength());
        Assert.Equal(100, preview.GetProperty("rows")[99][0].GetInt32());
    }

    [Fact]
    public async Task An_xlsx_file_is_accepted()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var xlsx = new MemoryStream();
        await xlsx.SaveAsAsync(new[] { new Dictionary<string, object> { ["code"] = "05001", ["population"] = 2612958 } }, excelType: ExcelType.XLSX);

        var response = await h.Admin.PostAsync(Tables(h), Form(xlsx.ToArray(), "population.xlsx", "population"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task A_name_already_used_is_a_conflict_and_leaves_the_table_unchanged()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        await UploadAsync(h.Admin, h, "population", Population);

        var response = await UploadAsync(h.Admin, h, "population", "a\n1\n", "other.csv");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("reference.exists", problem.GetProperty("code").GetString());
        var table = await h.Admin.GetFromJsonAsync<JsonElement>($"{Tables(h)}/population");
        Assert.Equal("population.csv", table.GetProperty("table").GetProperty("fileName").GetString());
    }

    [Fact]
    public async Task Replacing_keeps_the_name_and_records_who_and_when()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        await UploadAsync(h.Admin, h, "population", Population);
        using var analyst = await MemberAsync(h, "ana", "analyst");

        var response = await analyst.PutAsync($"{Tables(h)}/population", Form("code,total\nx,1\ny,2\nz,3\n"u8.ToArray(), "population-2027.csv"));
        var replaced = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("table");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("population", replaced.GetProperty("name").GetString());
        Assert.Equal("population-2027.csv", replaced.GetProperty("fileName").GetString());
        Assert.Equal(3, replaced.GetProperty("rowCount").GetInt32());
        Assert.Equal(["code", "total"], replaced.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
        Assert.Equal("ana", replaced.GetProperty("updatedBy").GetProperty("name").GetString());
        Assert.True(replaced.GetProperty("updatedAt").GetDateTimeOffset() > replaced.GetProperty("createdAt").GetDateTimeOffset());
        var preview = await h.Admin.GetFromJsonAsync<JsonElement>($"{Tables(h)}/population");
        Assert.Equal("z", preview.GetProperty("rows")[2][0].GetString());
    }

    [Fact]
    public async Task Replacing_a_missing_table_is_not_found()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);

        var response = await h.Admin.PutAsync($"{Tables(h)}/population", Form("a\n1\n"u8.ToArray(), "a.csv"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_removes_the_table_and_its_rows()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        await UploadAsync(h.Admin, h, "population", Population);

        Assert.Equal(HttpStatusCode.NoContent, (await h.Admin.DeleteAsync($"{Tables(h)}/population")).StatusCode);

        Assert.Empty((await h.Admin.GetFromJsonAsync<JsonElement>(Tables(h))).EnumerateArray());
        Assert.Equal(0, await TestData.ScalarAsync<long>(h.Db, "SELECT count(*) FROM tamiza.reference_table_contents"));
        Assert.Equal(HttpStatusCode.NotFound, (await h.Admin.DeleteAsync($"{Tables(h)}/population")).StatusCode);
    }

    [Fact]
    public async Task A_viewer_cannot_list_preview_upload_replace_or_delete()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        await UploadAsync(h.Admin, h, "population", Population);
        using var viewer = await MemberAsync(h, "vic", "viewer");

        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(viewer, h, "other", Population)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsync($"{Tables(h)}/population", Form("a\n1\n"u8.ToArray(), "a.csv"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync($"{Tables(h)}/population")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(Tables(h))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"{Tables(h)}/population")).StatusCode);
        Assert.Single((await h.Admin.GetFromJsonAsync<JsonElement>(Tables(h))).EnumerateArray());
    }

    [Fact]
    public async Task A_non_member_gets_not_found()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        using var stranger = TestData.ClientFor(h.Factory, "sam", "sam@example.org");

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(Tables(h))).StatusCode);
    }

    [Theory]
    [InlineData("Population 2026")]
    [InlineData("2026")]
    [InlineData("")]
    public async Task An_invalid_name_is_rejected(string name)
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);

        var message = await ErrorAsync(await UploadAsync(h.Admin, h, name, Population), "name");

        Assert.Contains("lowercase letters, digits and underscores", message);
    }

    [Fact]
    public async Task Unsupported_oversized_and_header_only_files_are_rejected()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);

        Assert.Contains("Only CSV and XLSX", await ErrorAsync(await UploadAsync(h.Admin, h, "a", Population, "population.ods"), "file"));
        var large = new byte[ReferenceTableImport.MaxFileBytes + 1];
        Assert.Contains("10 MB", await ErrorAsync(await h.Admin.PostAsync(Tables(h), Form(large, "big.csv", "big")), "file"));
        Assert.Contains("no rows", await ErrorAsync(await UploadAsync(h.Admin, h, "a", "a,b\n"), "file"));
        Assert.Empty((await h.Admin.GetFromJsonAsync<JsonElement>(Tables(h))).EnumerateArray());
    }

    [Fact]
    public async Task A_project_holds_at_most_50_tables()
    {
        await using var h = await IngestionHarness.CreateAsync(postgres, _kobo);
        for (var i = 0; i < 50; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await UploadAsync(h.Admin, h, $"t{i}", "a\n1\n")).StatusCode);
        }

        var response = await UploadAsync(h.Admin, h, "t50", "a\n1\n");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("reference.limit", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    public void Dispose() => _kobo.Dispose();
}
