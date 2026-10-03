using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Ingestion;

[Collection(PostgresCollection.Name)]
public sealed class WebhookTests(PostgresFixture postgres) : IDisposable
{
    private readonly KoboStub _kobo = new KoboStub().WithForm();

    private static string Submission(long id, string uuid = "uuid-1", string answer = "cali") =>
        JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["_id"] = id,
            ["_uuid"] = uuid,
            ["__version__"] = "v1",
            ["_submission_time"] = "2026-10-01T10:00:00",
            ["municipality"] = answer,
        });

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("code", out var code) ? code.GetString() : null;

    [Fact]
    public async Task A_valid_delivery_is_stored_with_origin_webhook_and_answered_with_200()
    {
        await using var harness = await IngestionHarness.CreateAsync(postgres, _kobo);

        var response = await harness.DeliverAsync(Submission(4021));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, await harness.CountAsync("kobo_id = 4021 AND kobo_uuid = 'uuid-1' AND origin = 'webhook' AND form_version = 'v1' AND NOT deleted"));
        Assert.True(await TestData.ScalarAsync<bool>(harness.Db, "SELECT last_webhook_at IS NOT NULL FROM tamiza.projects"));
    }

    [Fact]
    public async Task Every_credential_failure_gets_the_same_401_and_stores_nothing()
    {
        await using var harness = await IngestionHarness.CreateAsync(postgres, _kobo);
        var body = Submission(1);

        var responses = new[]
        {
            await harness.DeliverAsync(body, password: "wrong"),
            await harness.DeliverAsync(body, username: null),
            await harness.DeliverAsync(body, username: "admin"),
            await harness.DeliverAsync(body, projectId: Guid.CreateVersion7()),
        };

        var bodies = new List<string>();
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("Basic realm=\"tamiza\"", response.Headers.WwwAuthenticate.ToString());
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            bodies.Add($"{problem.GetProperty("status")} {problem.GetProperty("title")}");
        }

        Assert.Single(bodies.Distinct());
        Assert.Equal(0, await harness.CountAsync());
        Assert.True(await TestData.ScalarAsync<bool>(harness.Db, "SELECT last_webhook_at IS NULL FROM tamiza.projects"));
    }

    [Fact]
    public async Task A_regenerated_secret_invalidates_the_previous_one()
    {
        await using var harness = await IngestionHarness.CreateAsync(postgres, _kobo);
        var regenerated = await harness.Admin.PostAsync($"/api/v1/projects/{harness.ProjectId}/webhook/secret", null);
        var newSecret = (await regenerated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secret").GetString()!;

        Assert.Equal(HttpStatusCode.Unauthorized, (await harness.DeliverAsync(Submission(1))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await harness.DeliverAsync(Submission(1), password: newSecret)).StatusCode);
        Assert.Equal(1, await harness.CountAsync());
    }

    [Fact]
    public async Task A_secret_that_can_no_longer_be_decrypted_rejects_every_delivery()
    {
        await using var harness = await IngestionHarness.CreateAsync(postgres, _kobo);
        await TestData.ExecuteAsync(harness.Db, "UPDATE tamiza.projects SET encrypted_webhook_secret = 'not-a-protected-value'");

        Assert.Equal(HttpStatusCode.Unauthorized, (await harness.DeliverAsync(Submission(1))).StatusCode);
        Assert.Equal(0, await harness.CountAsync());
    }

    [Fact]
    public async Task An_xml_delivery_gets_415()
    {
        await using var harness = await IngestionHarness.CreateAsync(postgres, _kobo);

        var response = await harness.DeliverAsync("<data id=\"a\"><_id>1</_id></data>", contentType: "application/xml");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("webhook.unsupported_media_type", await CodeAsync(response));
        Assert.Equal(0, await harness.CountAsync());
    }

    [Theory]
    [InlineData("""{"_uuid": "u-1"}""", "_id")]
    [InlineData("""{"_id": 1}""", "_uuid")]
    [InlineData("""[{"_id": 1, "_uuid": "u-1"}]""", "object")]
    [InlineData("""{"_id": 1, "_uuid": """, "JSON")]
    public async Task An_invalid_submission_gets_400_naming_the_problem(string body, string mentioned)
    {
        await using var harness = await IngestionHarness.CreateAsync(postgres, _kobo);

        var response = await harness.DeliverAsync(body);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("webhook.invalid_submission", problem.GetProperty("code").GetString());
        Assert.Contains(mentioned, problem.GetProperty("detail").GetString());
        Assert.Equal(0, await harness.CountAsync());
    }

    [Fact]
    public async Task A_body_over_10_mb_gets_413()
    {
        await using var harness = await IngestionHarness.CreateAsync(postgres, _kobo);
        var body = $$"""{"_id": 1, "_uuid": "u-1", "photo": "{{new string('x', 11 * 1024 * 1024)}}"}""";

        var response = await harness.DeliverAsync(body);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, await harness.CountAsync());
    }

    [Fact]
    public async Task A_resent_submission_keeps_one_row_and_an_edit_updates_it()
    {
        await using var harness = await IngestionHarness.CreateAsync(postgres, _kobo);

        await harness.DeliverAsync(Submission(9, uuid: "first"));
        await harness.DeliverAsync(Submission(9, uuid: "first"));
        var edited = await harness.DeliverAsync(Submission(9, uuid: "after-edit", answer: "palmira"));

        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(1, await harness.CountAsync());
        Assert.Equal(1, await harness.CountAsync("kobo_uuid = 'after-edit' AND data->>'municipality' = 'palmira'"));
    }

    [Fact]
    public async Task Neither_the_secret_nor_the_body_reaches_the_logs()
    {
        await using var harness = await IngestionHarness.CreateAsync(postgres, _kobo);
        const string marker = "marker-7d1f0c";

        await harness.DeliverAsync(Submission(1, answer: marker));
        await harness.DeliverAsync($$"""{"_uuid": "{{marker}}"}""");
        await harness.DeliverAsync(Submission(2, answer: marker), password: harness.Secret + "x");

        Assert.DoesNotContain(harness.Factory.Logs.Lines, line => line.Contains(marker));
        Assert.DoesNotContain(harness.Factory.Logs.Lines, line => line.Contains(harness.Secret));
        Assert.Contains(harness.Factory.Logs.Lines, line => line.Contains("rejected") && line.Contains(harness.ProjectId.ToString()));
    }

    public void Dispose() => _kobo.Dispose();
}
