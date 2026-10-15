using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Tamiza.Api.IntegrationTests.CleanData;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Pipeline;

/// <summary><c>GET /projects/{id}/script-inputs</c>: the tables a script receives, as the last run published them.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ScriptInputsApiTests(PostgresFixture postgres)
{
    private static string Url(CleanDataHarness h, string kind) => $"/api/v1/projects/{h.ProjectId}/script-inputs?kind={kind}";

    private static JsonElement Table(JsonElement inputs, string name) =>
        inputs.GetProperty("tables").EnumerateArray().Single(t => t.GetProperty("name").GetString() == name);

    private static string[] Columns(JsonElement table) =>
        table.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("name").GetString()!).ToArray();

    [Fact]
    public async Task Script_1_gets_the_flattened_tables_with_rows_types_and_question_labels()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("raw",
            ("main", "_id bigint, has_water text, area numeric", "(1, 'Yes', 2.5), (2, 'No', 3)"),
            ("members", "_row_id bigint, _parent_id bigint, age bigint", "(1, 1, 30), (2, 1, 4), (3, 2, 50)"));
        await PublishedTables.CommentAsync(h.Db, h.ProjectId, "raw", "main", "has_water", "Does the household have running water?");
        await h.PublishAsync("clean", ("main", "_id bigint", "(1)"));

        var inputs = await h.Analyst.GetFromJsonAsync<JsonElement>(Url(h, "clean"));

        Assert.Equal("raw", inputs.GetProperty("source").GetString());
        Assert.NotEqual(JsonValueKind.Null, inputs.GetProperty("publishedAt").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, inputs.GetProperty("runId").ValueKind);
        Assert.Equal(["main", "members"], inputs.GetProperty("tables").EnumerateArray().Select(t => t.GetProperty("name").GetString()));
        var main = Table(inputs, "main");
        Assert.Equal(2, main.GetProperty("rows").GetInt64());
        Assert.Equal(3, Table(inputs, "members").GetProperty("rows").GetInt64());
        Assert.Equal(["_id", "has_water", "area"], Columns(main));
        var hasWater = main.GetProperty("columns")[1];
        Assert.Equal("text", hasWater.GetProperty("type").GetString());
        Assert.Equal("Does the household have running water?", hasWater.GetProperty("label").GetString());
        Assert.Equal("integer", main.GetProperty("columns")[0].GetProperty("type").GetString());
        Assert.Equal("decimal", main.GetProperty("columns")[2].GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, main.GetProperty("columns")[0].GetProperty("label").ValueKind);
    }

    [Theory]
    [InlineData("output")]
    [InlineData("indicator")]
    public async Task Script_2_and_the_indicators_get_the_clean_tables_after_script_1_changed_them(string kind)
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("raw", ("main", "_id bigint, notes text", "(1, 'a')"));
        await h.PublishAsync("clean", ("main", "_id bigint", "(1)"), ("summary", "total bigint", "(7)"));

        var inputs = await h.Analyst.GetFromJsonAsync<JsonElement>(Url(h, kind));

        Assert.Equal("clean", inputs.GetProperty("source").GetString());
        Assert.Equal(["main", "summary"], inputs.GetProperty("tables").EnumerateArray().Select(t => t.GetProperty("name").GetString()));
        Assert.Equal(["_id"], Columns(Table(inputs, "main")));
        Assert.Equal(1, Table(inputs, "main").GetProperty("rows").GetInt64());
    }

    [Fact]
    public async Task A_table_a_script_changed_after_the_last_run_is_not_listed_until_the_next_run_and_the_publish_time_is_shown()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        var published = DateTimeOffset.UtcNow.AddHours(-3);
        await PublishedTables.PublishAsync(h.Db, h.ProjectId, "clean", ("main", "_id bigint", "(1)"));
        await TestData.ExecuteAsync(h.Db, "UPDATE tamiza.run_steps SET finished_at = @at", ("at", published));

        var inputs = await h.Analyst.GetFromJsonAsync<JsonElement>(Url(h, "indicator"));

        Assert.Equal(["_id"], Columns(Table(inputs, "main")));
        Assert.Equal(published.ToUnixTimeSeconds(), inputs.GetProperty("publishedAt").GetDateTimeOffset().ToUnixTimeSeconds());
    }

    [Fact]
    public async Task With_nothing_published_the_tables_are_empty_and_there_is_no_publish_time()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);

        var inputs = await h.Analyst.GetFromJsonAsync<JsonElement>(Url(h, "clean"));

        Assert.Empty(inputs.GetProperty("tables").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, inputs.GetProperty("publishedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, inputs.GetProperty("runId").ValueKind);
    }

    [Fact]
    public async Task A_viewer_sees_the_same_tables_as_an_analyst()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("clean", ("main", "_id bigint, note text", "(1, 'a')"));

        var viewer = await h.Viewer.GetStringAsync(Url(h, "output"));
        var analyst = await h.Analyst.GetStringAsync(Url(h, "output"));

        Assert.Equal(analyst, viewer);
        Assert.Contains("\"note\"", viewer);
    }

    [Fact]
    public async Task Someone_who_is_not_a_member_gets_the_answer_for_a_project_that_does_not_exist()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await TestData.SignInAsync(h.Factory, "eve", "eve@example.org");
        using var stranger = TestData.ClientFor(h.Factory, "eve", "eve@example.org");

        var response = await stranger.GetAsync(Url(h, "clean"));
        var missing = await stranger.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/script-inputs?kind=clean");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Reference_tables_and_form_questions_are_listed_whichever_script_is_selected()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        var user = await TestData.ScalarAsync<Guid>(h.Db, "SELECT id FROM tamiza.users LIMIT 1");
        await TestData.ExecuteAsync(h.Db,
            """
            INSERT INTO tamiza.reference_tables (id, project_id, name, file_name, columns, row_count, created_at, updated_by, updated_at)
            VALUES (gen_random_uuid(), @p, 'population', 'population.xlsx',
                    '[{"name":"municipality","header":"Municipality","type":"text"},{"name":"population_2026","header":"Population 2026","type":"integer"}]'::jsonb,
                    10, now(), @u, now())
            """, ("p", h.ProjectId), ("u", user));
        await TestData.ExecuteAsync(h.Db,
            """
            UPDATE tamiza.projects SET form_fields =
                '[{"name":"municipality","xpath":"respondent/municipality","type":"select_one municipalities","label":"Municipality","listName":"municipalities"}]'::jsonb
            """);

        foreach (var kind in new[] { "clean", "output", "indicator" })
        {
            var inputs = await h.Viewer.GetFromJsonAsync<JsonElement>(Url(h, kind));

            var reference = inputs.GetProperty("referenceTables").EnumerateArray().Single();
            Assert.Equal("population", reference.GetProperty("name").GetString());
            Assert.Equal(["municipality", "population_2026"], reference.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
            Assert.Equal("integer", reference.GetProperty("columns")[1].GetProperty("type").GetString());
            var question = inputs.GetProperty("questions").EnumerateArray().Single();
            Assert.Equal("municipality", question.GetProperty("name").GetString());
            Assert.Equal("respondent/municipality", question.GetProperty("path").GetString());
            Assert.Equal("Municipality", question.GetProperty("label").GetString());
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("&kind=")]
    [InlineData("&kind=indicators")]
    public async Task An_unknown_or_missing_kind_is_rejected(string suffix)
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);

        var response = await h.Analyst.GetAsync($"/api/v1/projects/{h.ProjectId}/script-inputs?x=1{suffix}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("kind", out _));
    }

    [Fact]
    public async Task A_publish_in_progress_does_not_delay_the_read_and_the_read_leaves_no_lock()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("clean", ("main", "_id bigint", "(1)"));
        var table = $"\"{h.Schema("clean")}\".\"main\"";

        // A publish holds the table exclusively while it swaps it; the panel's read must not wait for that.
        await using var publisher = new NpgsqlConnection(h.Db);
        await publisher.OpenAsync();
        await using var transaction = await publisher.BeginTransactionAsync();
        await using (var lockTable = new NpgsqlCommand($"LOCK TABLE {table} IN ACCESS EXCLUSIVE MODE", publisher, transaction))
        {
            await lockTable.ExecuteNonQueryAsync();
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var inputs = await h.Analyst.GetFromJsonAsync<JsonElement>(Url(h, "output"), timeout.Token);

        Assert.Equal(["_id"], Columns(Table(inputs, "main")));
        await transaction.RollbackAsync();
        // Nothing the read took is still held: an exclusive lock is granted at once.
        await TestData.ExecuteAsync(h.Db, $"BEGIN; LOCK TABLE {table} IN ACCESS EXCLUSIVE MODE NOWAIT; ROLLBACK;");
    }

    [Fact]
    public async Task The_openapi_document_lists_the_route()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        using var client = h.Factory.CreateClient();

        var document = await client.GetFromJsonAsync<JsonElement>("/api/v1/openapi.json");

        Assert.True(document.GetProperty("paths").TryGetProperty("/projects/{projectId}/script-inputs", out var path)
                    || document.GetProperty("paths").TryGetProperty("/api/v1/projects/{projectId}/script-inputs", out path));
        Assert.True(path.TryGetProperty("get", out _));
        Assert.True(document.GetProperty("components").GetProperty("schemas").TryGetProperty("ScriptInputs", out _));
    }
}
