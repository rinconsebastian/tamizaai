using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MiniExcelLibs;
using Tamiza.Api.CleanData;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Projects;

/// <summary>The form's choices, the import template and the saved-frame download.</summary>
[Collection(PostgresCollection.Name)]
public sealed class SamplingFrameFilesApiTests(PostgresFixture postgres)
{
    public const string FieldsJson = """
        [{"name":"start","xpath":"start","type":"start"},
         {"name":"municipio","xpath":"municipio","type":"select_one","label":"Municipio","listName":"municipios"},
         {"name":"sexo","xpath":"sexo","type":"select_one","label":"Sexo","listName":"sexos"},
         {"name":"estrato","xpath":"estrato","type":"integer","label":"Estrato"},
         {"name":"barrio","xpath":"barrio","type":"text","label":"Barrio"},
         {"name":"edad","xpath":"hogar/edad","type":"integer","label":"Edad"},
         {"name":"edad","xpath":"info/edad","type":"note","label":"Sobre la edad"},
         {"name":"zona","xpath":"geo/zona","type":"select_one","label":"Zona","listName":"zonas"},
         {"name":"target","xpath":"hogar/target","type":"integer","label":"Meta del hogar"},
         {"name":"Target","xpath":"Target","type":"select_one","label":"Objetivo","listName":"sexos"},
         {"name":"idiomas","xpath":"idiomas","type":"select_multiple","label":"Idiomas","listName":"idiomas"},
         {"name":"edad_miembro","xpath":"miembros/edad_miembro","type":"select_one","label":"Edad del miembro","listName":"edades","inRepeat":true}]
        """;

    public const string ChoicesJson = """
        [{"listName":"municipios","name":"05001","label":"Medellín"},
         {"listName":"municipios","name":"05088","label":"Bello"},
         {"listName":"municipios","name":"05266","label":"Envigado"},
         {"listName":"sexos","name":"f","label":"Femenino"},
         {"listName":"sexos","name":"m","label":"Masculino"},
         {"listName":"zonas","name":"u","label":"Urbana"},
         {"listName":"zonas","name":"r","label":"Rural"},
         {"listName":"idiomas","name":"es","label":"Español"},
         {"listName":"edades","name":"18_29","label":"18 a 29"}]
        """;

    private sealed record Setup(
        TamizaApiFactory Factory, string Db, Guid ProjectId, string ExportDirectory, HttpClient Analyst, HttpClient Viewer, HttpClient Stranger)
        : IAsyncDisposable
    {
        public string ProjectUrl => $"/api/v1/projects/{ProjectId}";

        public string FrameUrl => $"{ProjectUrl}/sampling-frame";

        public string[] ExportFiles => Directory.Exists(ExportDirectory) ? Directory.GetFiles(ExportDirectory) : [];

        public int Pending => Factory.Services.GetRequiredService<ExportRegistry>().Pending;

        public async ValueTask DisposeAsync()
        {
            Analyst.Dispose();
            Viewer.Dispose();
            Stranger.Dispose();
            await Factory.DisposeAsync();
            if (Directory.Exists(ExportDirectory))
            {
                Directory.Delete(ExportDirectory, recursive: true);
            }
        }
    }

    private async Task<Setup> ProjectAsync(string fieldsJson = FieldsJson, string choicesJson = ChoicesJson)
    {
        var exportDirectory = Path.Combine(Path.GetTempPath(), "tamiza-frame-tests-" + Guid.NewGuid().ToString("N"));
        var db = await postgres.CreateDatabaseAsync();
        var factory = new TamizaApiFactory(db, new Dictionary<string, string?> { ["DataDashboard:ExportDirectory"] = exportDirectory });
        var adminId = await TestData.SignInAsync(factory, "ada", "ada@example.org");
        var projectId = await TestData.InsertProjectAsync(db, adminId, name: "Encuesta Antioquia", formFieldsJson: fieldsJson, formChoicesJson: choicesJson);
        await TestData.AddMemberAsync(db, projectId, adminId, "admin");
        var analystId = await TestData.SignInAsync(factory, "grace", "grace@example.org");
        await TestData.AddMemberAsync(db, projectId, analystId, "analyst");
        var viewerId = await TestData.SignInAsync(factory, "vic", "vic@example.org");
        await TestData.AddMemberAsync(db, projectId, viewerId, "viewer");
        await TestData.SignInAsync(factory, "sam", "sam@example.org");
        return new Setup(factory, db, projectId, exportDirectory,
            TestData.ClientFor(factory, "grace", "grace@example.org"),
            TestData.ClientFor(factory, "vic", "vic@example.org"),
            TestData.ClientFor(factory, "sam", "sam@example.org"));
    }

    private static object Dim(string field, params string[]? values) => new { field, values };

    private static Task<HttpResponseMessage> TemplateAsync(Setup s, params object[] dimensions) =>
        s.Analyst.PostAsJsonAsync($"{s.FrameUrl}/template", new { dimensions });

    /// <summary>Requests a template, checks the response and downloads the file once.</summary>
    private static async Task<(JsonElement File, byte[] Bytes)> DownloadTemplateAsync(Setup s, params object[] dimensions)
    {
        var response = await TemplateAsync(s, dimensions);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body.ToString());
        return (body, await FetchAsync(s, body));
    }

    private static async Task<byte[]> FetchAsync(Setup s, JsonElement file)
    {
        using var browser = s.Factory.CreateClient();
        var download = await browser.GetAsync(file.GetProperty("downloadUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        return await download.Content.ReadAsByteArrayAsync();
    }

    private static List<IDictionary<string, object?>> Rows(byte[] xlsx, string sheet = "Frame") =>
        MiniExcel.Query(new MemoryStream(xlsx), useHeaderRow: true, sheetName: sheet).Cast<IDictionary<string, object?>>().ToList();

    private static List<string> Headers(byte[] xlsx) =>
        MiniExcel.Query(new MemoryStream(xlsx), useHeaderRow: false).Cast<IDictionary<string, object?>>().First().Values.Select(v => $"{v}").ToList();

    /// <summary>The Frame sheet with its rows changed by <paramref name="fill"/>, saved as a new workbook with that sheet only.</summary>
    private static byte[] Filled(byte[] xlsx, Action<int, IDictionary<string, object?>> fill)
    {
        var headers = Headers(xlsx);
        var rows = Rows(xlsx).Select((row, i) =>
        {
            var copy = headers.ToDictionary(h => h, h => row[h]);
            fill(i, copy);
            return copy;
        }).ToList();
        using var output = new MemoryStream();
        output.SaveAs(rows, sheetName: "Frame", excelType: ExcelType.XLSX);
        return output.ToArray();
    }

    private static async Task<JsonElement> ImportAsync(Setup s, byte[] xlsx)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(xlsx);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", "frame.xlsx");
        var response = await s.Analyst.PostAsync($"{s.FrameUrl}/import", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static IEnumerable<string?> Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString());

    private static IEnumerable<int> Ints(JsonElement array) => array.EnumerateArray().Select(e => e.GetInt32());

    // Form choices

    [Fact]
    public async Task Choices_listed()
    {
        await using var s = await ProjectAsync();

        var choices = await s.Viewer.GetFromJsonAsync<JsonElement[]>($"{s.ProjectUrl}/form-choices");

        Assert.Equal(9, choices!.Length);
        Assert.Equal(["05001", "05088", "05266"], choices.Where(c => c.GetProperty("listName").GetString() == "municipios").Select(c => c.GetProperty("name").GetString()));
        var medellin = choices[0];
        Assert.Equal("municipios", medellin.GetProperty("listName").GetString());
        Assert.Equal("05001", medellin.GetProperty("name").GetString());
        Assert.Equal("Medellín", medellin.GetProperty("label").GetString());
    }

    [Fact]
    public async Task Choices_not_a_member()
    {
        await using var s = await ProjectAsync();

        var response = await s.Stranger.GetAsync($"{s.ProjectUrl}/form-choices");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Template

    [Fact]
    public async Task Template_for_chosen_choices()
    {
        await using var s = await ProjectAsync();

        var (file, xlsx) = await DownloadTemplateAsync(s, Dim("municipio", "05001", "05088"), Dim("sexo", null));

        Assert.Equal(4, file.GetProperty("rowCount").GetInt32());
        Assert.Matches(@"^encuesta-antioquia_sampling-frame-template_\d{4}-\d{2}-\d{2}\.xlsx$", file.GetProperty("fileName").GetString());
        Assert.Equal(["Frame", "Guide", "Choices"], MiniExcel.GetSheetNames(new MemoryStream(xlsx)));
        Assert.Equal(["municipio", "# municipio label", "sexo", "# sexo label", "target"], Headers(xlsx));
        var rows = Rows(xlsx);
        Assert.Equal(4, rows.Count);
        Assert.Equal("05001", rows[0]["municipio"]);
        Assert.All(rows, r => Assert.Null(r["target"]));
    }

    [Fact]
    public async Task Template_question_without_choices_and_typed_values()
    {
        await using var s = await ProjectAsync();

        var (file, xlsx) = await DownloadTemplateAsync(s, Dim("sexo", null), Dim("estrato", "1", "2", "3"));

        Assert.Equal(6, file.GetProperty("rowCount").GetInt32());
        Assert.Equal(["sexo", "# sexo label", "estrato", "target"], Headers(xlsx));
        Assert.Equal(["1", "2", "3", "1", "2", "3"], Rows(xlsx).Select(r => r["estrato"]?.ToString()));
    }

    [Fact]
    public async Task Template_question_without_choices_and_no_values()
    {
        await using var s = await ProjectAsync();
        var (file, xlsx) = await DownloadTemplateAsync(s, Dim("barrio"), Dim("sexo", null));

        var targetsOnly = await ImportAsync(s, Filled(xlsx, (i, row) => row["target"] = i == 0 ? 10 : null));
        var complete = await ImportAsync(s, Filled(xlsx, (i, row) =>
        {
            row["target"] = i == 0 ? 10 : null;
            row["barrio"] = "Centro";
        }));

        Assert.Equal(2, file.GetProperty("rowCount").GetInt32());
        Assert.All(Rows(xlsx), r => Assert.Null(r["barrio"]));
        var error = Assert.Single(targetsOnly.GetProperty("errors").EnumerateArray());
        Assert.Equal(2, error.GetProperty("row").GetInt32());
        Assert.Equal("Enter a value for every dimension.", error.GetProperty("message").GetString());
        Assert.Empty(complete.GetProperty("errors").EnumerateArray());
        Assert.Equal([3], Ints(complete.GetProperty("rowsWithoutTarget")));
    }

    [Fact]
    public async Task Template_without_rows()
    {
        await using var s = await ProjectAsync();

        var (file, xlsx) = await DownloadTemplateAsync(s, Dim("barrio"));
        var preview = await ImportAsync(s, xlsx);

        Assert.Equal(0, file.GetProperty("rowCount").GetInt32());
        Assert.Equal(["barrio", "target"], Headers(xlsx));
        Assert.Equal(["barrio"], Strings(preview.GetProperty("dimensions")));
        Assert.Empty(preview.GetProperty("rows").EnumerateArray());
        Assert.Empty(preview.GetProperty("errors").EnumerateArray());
    }

    [Fact]
    public async Task Template_name_shared_by_two_questions()
    {
        await using var s = await ProjectAsync();

        var (_, xlsx) = await DownloadTemplateAsync(s, Dim("hogar/edad", "18"), Dim("geo/zona", null));

        Assert.Equal(["hogar/edad", "zona", "# zona label", "target"], Headers(xlsx));
    }

    [Fact]
    public async Task Template_long_choice_list()
    {
        var random = new Random(7);
        var choices = Enumerable.Range(0, 40_000).Select(i => new
        {
            listName = "veredas",
            name = $"v{i:00000}",
            label = new string(Enumerable.Range(0, 80).Select(_ => (char)random.Next(0x4E00, 0x9FFF)).ToArray()),
        });
        const string fields = """[{"name":"vereda","xpath":"vereda","type":"select_one","label":"Vereda","listName":"veredas"}]""";
        await using var s = await ProjectAsync(fields, JsonSerializer.Serialize(choices));
        var chosen = Enumerable.Range(0, 50).Select(i => $"v{i * 13:00000}").ToArray();

        var (_, xlsx) = await DownloadTemplateAsync(s, Dim("vereda", chosen));

        Assert.True(xlsx.Length < 5 * 1024 * 1024);
        Assert.Equal(chosen, Rows(xlsx, "Choices").Select(r => (string?)r["choice code"]));
        Assert.Contains(Rows(xlsx, "Guide"), r => ((string)r["How to fill this file"]!).StartsWith("Your form has more choices"));
    }

    [Fact]
    public async Task Filled_template_imports()
    {
        await using var s = await ProjectAsync();
        var response = await TemplateAsync(s, Dim("municipio", null), Dim("sexo", null));
        var file = await response.Content.ReadFromJsonAsync<JsonElement>();
        var xlsx = await FetchAsync(s, file);
        using var browser = s.Factory.CreateClient();
        var again = await browser.GetAsync(file.GetProperty("downloadUrl").GetString());

        var preview = await ImportAsync(s, Filled(xlsx, (i, row) => row["target"] = i % 2 == 0 ? 10 * (i + 1) : null));

        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Empty(preview.GetProperty("errors").EnumerateArray());
        Assert.Equal(["municipio", "sexo"], Strings(preview.GetProperty("dimensions")));
        Assert.Equal([10, 30, 50], preview.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("target").GetInt32()));
        Assert.Equal(["05001", "f"], Strings(preview.GetProperty("rows")[0].GetProperty("values")));
        Assert.Equal([3, 5, 7], Ints(preview.GetProperty("rowsWithoutTarget")));
        Assert.Equal(["# municipio label", "# sexo label"],
            preview.GetProperty("ignoredColumns").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task Viewer_asks_for_a_template()
    {
        await using var s = await ProjectAsync();

        var response = await s.Viewer.PostAsJsonAsync($"{s.FrameUrl}/template", new { dimensions = new[] { Dim("sexo", null) } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, s.Pending);
        Assert.Empty(s.ExportFiles);
    }

    [Theory]
    [InlineData("idiomas", "an answer can hold several choices")]
    [InlineData("miembros/edad_miembro", "answered once per repeat")]
    [InlineData("Target", "reserved for the target column")]
    public async Task A_refused_template_leaves_no_file(string field, string message)
    {
        await using var s = await ProjectAsync();

        var response = await TemplateAsync(s, Dim(field, null));
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(message, errors.GetProperty("dimensions[0].field")[0].GetString());
        Assert.Equal(0, s.Pending);
        Assert.Empty(s.ExportFiles);
    }

    [Fact]
    public async Task An_unknown_choice_is_refused()
    {
        await using var s = await ProjectAsync();

        var response = await TemplateAsync(s, Dim("municipio", "zz"));
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("'zz' is not a choice of 'municipio'.", errors.GetProperty("dimensions[0].values")[0].GetString());
    }

    // Saved-frame download

    private static readonly object SavedFrame = new
    {
        dimensions = new[] { new { name = "Municipality", field = "municipio" }, new { name = "sexo", field = "sexo" } },
        targets = new[]
        {
            new { values = new[] { "05001", "f" }, target = 120 },
            new { values = new[] { "05001", "m" }, target = 0 },
            new { values = new[] { "05088", "f" }, target = 35 },
        },
    };

    private static async Task<JsonElement> ExportAsync(Setup s, HttpClient client)
    {
        var response = await client.PostAsync($"{s.FrameUrl}/export", null);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body.ToString());
        return body;
    }

    [Fact]
    public async Task Viewer_downloads_the_frame()
    {
        await using var s = await ProjectAsync();
        Assert.Equal(HttpStatusCode.OK, (await s.Analyst.PutAsJsonAsync(s.FrameUrl, SavedFrame)).StatusCode);

        var file = await ExportAsync(s, s.Viewer);
        var xlsx = await FetchAsync(s, file);

        Assert.Equal(3, file.GetProperty("rowCount").GetInt32());
        Assert.Matches(@"^encuesta-antioquia_sampling-frame_\d{4}-\d{2}-\d{2}\.xlsx$", file.GetProperty("fileName").GetString());
        Assert.Equal(["Municipality", "# Municipality label", "sexo", "# sexo label", "target"], Headers(xlsx));
        var rows = Rows(xlsx);
        Assert.Equal(["05001", "05001", "05088"], rows.Select(r => r["Municipality"]));
        Assert.Equal([120.0, 0.0, 35.0], rows.Select(r => Convert.ToDouble(r["target"])));
        Assert.Equal("Bello", rows[2]["# Municipality label"]);
    }

    [Fact]
    public async Task No_frame_yet()
    {
        await using var s = await ProjectAsync();

        var response = await s.Viewer.PostAsync($"{s.FrameUrl}/export", null);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("samplingFrame.none", problem.GetProperty("code").GetString());
        Assert.Equal("This project has no sampling frame yet.", problem.GetProperty("detail").GetString());
        Assert.Equal(0, s.Pending);
        Assert.Empty(s.ExportFiles);
    }

    [Fact]
    public async Task Downloaded_frame_imports_again()
    {
        var longLabels = ChoicesJson.Replace("\"Medellín\"", $"\"{new string('M', 260)}\"").Replace("\"Femenino\"", $"\"{new string('F', 300)}\"");
        await using var s = await ProjectAsync(choicesJson: longLabels);
        await s.Analyst.PutAsJsonAsync(s.FrameUrl, SavedFrame);

        var xlsx = await FetchAsync(s, await ExportAsync(s, s.Analyst));
        var preview = await ImportAsync(s, xlsx);

        Assert.Empty(preview.GetProperty("errors").EnumerateArray());
        Assert.Equal(["Municipality", "sexo"], Strings(preview.GetProperty("dimensions")));
        Assert.Equal([["05001", "f"], ["05001", "m"], ["05088", "f"]],
            preview.GetProperty("rows").EnumerateArray().Select(r => Strings(r.GetProperty("values")).ToArray()));
        Assert.Equal([120, 0, 35], preview.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("target").GetInt32()));
        Assert.Empty(preview.GetProperty("rowsWithoutTarget").EnumerateArray());
        Assert.Equal(200, ((string)Rows(xlsx)[0]["# Municipality label"]!).Length);
    }

    [Fact]
    public async Task Dimension_named_like_a_note_or_the_target()
    {
        await using var s = await ProjectAsync();
        var frame = new
        {
            dimensions = new[] { new { name = "#zona", field = "geo/zona" }, new { name = "Target", field = "hogar/target" } },
            targets = new[] { new { values = new[] { "u", "3" }, target = 12 }, new { values = new[] { "r", "3" }, target = 4 } },
        };
        Assert.Equal(HttpStatusCode.OK, (await s.Analyst.PutAsJsonAsync(s.FrameUrl, frame)).StatusCode);

        var xlsx = await FetchAsync(s, await ExportAsync(s, s.Analyst));
        var preview = await ImportAsync(s, xlsx);

        Assert.Equal(["geo/zona", "# geo/zona label", "hogar/target", "target"], Headers(xlsx));
        Assert.Empty(preview.GetProperty("errors").EnumerateArray());
        Assert.Equal(["geo/zona", "hogar/target"], Strings(preview.GetProperty("dimensions")));
        Assert.Equal([12, 4], preview.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("target").GetInt32()));
    }

    [Fact]
    public async Task Frame_without_rows()
    {
        await using var s = await ProjectAsync();
        var frame = new { dimensions = new[] { new { name = "sexo", field = "sexo" } }, targets = Array.Empty<object>() };
        Assert.Equal(HttpStatusCode.OK, (await s.Analyst.PutAsJsonAsync(s.FrameUrl, frame)).StatusCode);

        var file = await ExportAsync(s, s.Viewer);
        var xlsx = await FetchAsync(s, file);
        var preview = await ImportAsync(s, xlsx);

        Assert.Equal(0, file.GetProperty("rowCount").GetInt32());
        Assert.Equal(["sexo", "# sexo label", "target"], Headers(xlsx));
        Assert.Equal(["sexo"], Strings(preview.GetProperty("dimensions")));
        Assert.Empty(preview.GetProperty("rows").EnumerateArray());
        Assert.Empty(preview.GetProperty("errors").EnumerateArray());
    }

    [Fact]
    public async Task A_non_member_gets_404_and_no_link()
    {
        await using var s = await ProjectAsync();
        await s.Analyst.PutAsJsonAsync(s.FrameUrl, SavedFrame);

        var template = await s.Stranger.PostAsJsonAsync($"{s.FrameUrl}/template", new { dimensions = new[] { Dim("sexo", null) } });
        var export = await s.Stranger.PostAsync($"{s.FrameUrl}/export", null);

        Assert.Equal(HttpStatusCode.NotFound, template.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, export.StatusCode);
        Assert.Equal(0, s.Pending);
        Assert.Empty(s.ExportFiles);
    }

    [Fact]
    public async Task The_openapi_document_lists_the_operations()
    {
        await using var s = await ProjectAsync();
        using var client = s.Factory.CreateClient();

        var document = await client.GetFromJsonAsync<JsonElement>("/api/v1/openapi.json");
        var paths = document.GetProperty("paths");

        Assert.Equal("GetFormChoices", paths.GetProperty("/api/v1/projects/{projectId}/form-choices").GetProperty("get").GetProperty("operationId").GetString());
        Assert.Equal("DownloadSamplingFrameTemplate",
            paths.GetProperty("/api/v1/projects/{projectId}/sampling-frame/template").GetProperty("post").GetProperty("operationId").GetString());
        Assert.Equal("DownloadSamplingFrame",
            paths.GetProperty("/api/v1/projects/{projectId}/sampling-frame/export").GetProperty("post").GetProperty("operationId").GetString());
        Assert.True(document.GetProperty("components").GetProperty("schemas").TryGetProperty("SamplingFrameFile", out _));
    }

    [Fact]
    public async Task The_preview_lists_the_dimensions_after_ignoring_notes_in_csv_too()
    {
        await using var s = await ProjectAsync();
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("municipio;# nota;target\n05001;x;1\n"));
        form.Add(file, "file", "frame.csv");

        var preview = await (await s.Analyst.PostAsync($"{s.FrameUrl}/import", form)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(["municipio"], Strings(preview.GetProperty("dimensions")));
    }
}
