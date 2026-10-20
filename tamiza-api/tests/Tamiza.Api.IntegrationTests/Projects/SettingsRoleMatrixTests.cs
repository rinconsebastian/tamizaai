using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.CleanData;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Projects;

/// <summary>
/// Who may read what Settings shows: the endpoints only its sections use answer analysts and admins, a viewer gets 403 and a user who is
/// not a member gets the 404 of a project that does not exist. The project details stay open to every member.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SettingsRoleMatrixTests(PostgresFixture postgres)
{
    private static readonly string[] Reads =
    [
        "members", "sampling frame", "sampling frame download", "ingestion status", "synchronization log", "form versions", "runs", "run",
        "pipeline settings", "reference tables", "reference table", "scripts", "script versions", "script version", "indicator versions",
        "indicator version", "script inputs", "form fields", "form choices", "project details",
    ];

    public static TheoryData<string, string, HttpStatusCode> Matrix()
    {
        var data = new TheoryData<string, string, HttpStatusCode>();
        foreach (var role in new[] { "viewer", "analyst", "admin", "outsider" })
        {
            foreach (var read in Reads)
            {
                // The project details are the one read that the viewer keeps.
                var allowed = role is "analyst" or "admin" || (role == "viewer" && read == "project details");
                data.Add(role, read, role == "outsider" ? HttpStatusCode.NotFound : allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
            }
        }

        return data;
    }

    private const string Compute = "def compute(cleaned, ctx):\n    return cleaned['main']\n";

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Each_role_gets_the_status_its_rights_allow(string role, string read, HttpStatusCode expected)
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.ExecuteAsync("UPDATE tamiza.projects SET form_fields = @fields::jsonb", ("fields", TestData.SurveyFieldsJson));
        var runId = await h.PublishAsync("output", ("main", "_id bigint", "(1)"));
        // One of everything, so the answer an analyst or admin gets is the normal one and not a 404 for a missing item.
        var frame = await h.Admin.PutAsJsonAsync($"{Project(h)}/sampling-frame", new
        {
            dimensions = new[] { new { name = "Municipality", field = "municipality" } },
            targets = new[] { new { values = new[] { "Cali" }, target = 120 } },
        });
        var script = await h.Admin.PutAsJsonAsync($"{Project(h)}/scripts/clean", new { code = Compute });
        var indicator = await h.Admin.PutAsJsonAsync($"{Project(h)}/scripts/indicators/coverage",
            new { code = Compute, label = "Coverage", format = "decimal" });
        using var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes("code,people\n05001,10\n")), "file", "population.csv" }, { new StringContent("population"), "name" } };
        var table = await h.Admin.PostAsync($"{Project(h)}/reference-tables", form);
        Assert.All([frame, script, indicator, table], response => Assert.True(response.IsSuccessStatusCode, response.StatusCode.ToString()));
        using var outsider = TestData.ClientFor(h.Factory, "mallory", "mallory@example.org");
        var client = role switch { "viewer" => h.Viewer, "analyst" => h.Analyst, "admin" => h.Admin, _ => outsider };

        var response = await client.SendAsync(Request(h, read, runId));

        Assert.Equal(expected, response.StatusCode);
    }

    private static string Project(CleanDataHarness h) => $"/api/v1/projects/{h.ProjectId}";

    private static HttpRequestMessage Request(CleanDataHarness h, string read, Guid runId)
    {
        var project = Project(h);
        return read switch
        {
            "members" => new(HttpMethod.Get, $"{project}/members"),
            "sampling frame" => new(HttpMethod.Get, $"{project}/sampling-frame"),
            "sampling frame download" => new(HttpMethod.Post, $"{project}/sampling-frame/export"),
            "ingestion status" => new(HttpMethod.Get, $"{project}/ingestion"),
            "synchronization log" => new(HttpMethod.Get, $"{project}/syncs"),
            "form versions" => new(HttpMethod.Get, $"{project}/form-versions"),
            "runs" => new(HttpMethod.Get, $"{project}/runs"),
            "run" => new(HttpMethod.Get, $"{project}/runs/{runId}"),
            "pipeline settings" => new(HttpMethod.Get, $"{project}/pipeline"),
            "reference tables" => new(HttpMethod.Get, $"{project}/reference-tables"),
            "reference table" => new(HttpMethod.Get, $"{project}/reference-tables/population"),
            "scripts" => new(HttpMethod.Get, $"{project}/scripts"),
            "script versions" => new(HttpMethod.Get, $"{project}/scripts/clean/versions"),
            "script version" => new(HttpMethod.Get, $"{project}/scripts/clean/versions/1"),
            "indicator versions" => new(HttpMethod.Get, $"{project}/scripts/indicators/coverage/versions"),
            "indicator version" => new(HttpMethod.Get, $"{project}/scripts/indicators/coverage/versions/1"),
            "script inputs" => new(HttpMethod.Get, $"{project}/script-inputs?kind=clean"),
            "form fields" => new(HttpMethod.Get, $"{project}/form-fields"),
            "form choices" => new(HttpMethod.Get, $"{project}/form-choices"),
            "project details" => new(HttpMethod.Get, project),
            _ => throw new ArgumentOutOfRangeException(nameof(read)),
        };
    }

    [Fact]
    public async Task A_refused_viewer_gets_no_data_in_the_body()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        (await h.Admin.PutAsJsonAsync($"{Project(h)}/scripts/clean", new { code = Compute })).EnsureSuccessStatusCode();

        var members = await h.Viewer.GetAsync($"{Project(h)}/members");
        var scripts = await h.Viewer.GetAsync($"{Project(h)}/scripts");

        foreach (var response in new[] { members, scripts })
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("ada@example.org", body);
            Assert.DoesNotContain("def compute", body);
            Assert.Equal("Not allowed.", JsonDocument.Parse(body).RootElement.GetProperty("title").GetString());
        }
    }
}
