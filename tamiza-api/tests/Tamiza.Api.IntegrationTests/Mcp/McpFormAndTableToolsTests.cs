using System.Text.Json;
using Tamiza.Api.Data;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Mcp;

[Collection(PostgresCollection.Name)]
public sealed class McpFormAndTableToolsTests(PostgresFixture postgres)
{
    private Task<McpProjectHarness> CreateAsync(McpAccess level = McpAccess.Aggregates, IReadOnlyDictionary<string, string?>? settings = null, string? choices = null) =>
        McpProjectHarness.CreateAsync(postgres, level, settings: settings, formFieldsJson: McpDataFixtures.FieldsJson, formChoicesJson: choices ?? McpDataFixtures.ChoicesJson);

    [Fact]
    public async Task Get_form_gives_the_questions_the_choice_lists_with_codes_and_labels_and_the_versions_in_use()
    {
        await using var h = await CreateAsync();
        await TestData.ExecuteAsync(h.Db,
            "INSERT INTO tamiza.raw_submissions (project_id, kobo_id, kobo_uuid, form_version, data, origin, received_at, updated_at) VALUES (@p, 1, 'a', 'vABC', '{}', 'sync', now(), now()), (@p, 2, 'b', 'vABC', '{}', 'sync', now(), now()), (@p, 3, 'c', NULL, '{}', 'sync', now(), now())",
            ("p", h.ProjectId));

        using var body = (await h.CallAsync("analyst", "get_form")).Json();

        var form = body.RootElement;
        Assert.Equal("Seeded form", form.GetProperty("name").GetString());
        var source = form.GetProperty("fields").EnumerateArray().Single(f => f.GetProperty("name").GetString() == "water_source");
        Assert.Equal(("select_one", "Main source of water", "sources"), (source.GetProperty("type").GetString(), source.GetProperty("label").GetString(), source.GetProperty("list").GetString()));
        var sources = form.GetProperty("choiceLists").EnumerateArray().Single(l => l.GetProperty("name").GetString() == "sources");
        Assert.Equal(3, sources.GetProperty("total").GetInt32());
        Assert.Equal(["piped:Piped", "well:Well", "river:River"], sources.GetProperty("choices").EnumerateArray().Select(c => $"{c.GetProperty("code").GetString()}:{c.GetProperty("label").GetString()}").ToArray());
        Assert.Equal(1, form.GetProperty("formVersions").GetProperty("withoutVersion").GetInt64());
        Assert.Contains(form.GetProperty("formVersions").GetProperty("versions").EnumerateArray(), v => v.GetProperty("uid").GetString() == "vABC" && v.GetProperty("submissions").GetInt64() == 2);
        Assert.Equal(4, form.GetProperty("metrics").GetArrayLength());
    }

    [Fact]
    public async Task A_long_choice_list_is_shortened_and_read_in_full_by_name()
    {
        var many = string.Join(",", Enumerable.Range(1, 60).Select(i => $"{{\"listName\":\"sources\",\"name\":\"s{i}\",\"label\":\"Source {i}\"}}"));
        await using var h = await CreateAsync(choices: $"[{many}]");

        using var shortened = (await h.CallAsync("analyst", "get_form")).Json();
        using var full = (await h.CallAsync("analyst", "get_form", new { list = "sources" })).Json();
        var missing = await h.CallAsync("analyst", "get_form", new { list = "nothing" });

        var list = shortened.RootElement.GetProperty("choiceLists")[0];
        Assert.Equal((60, 25), (list.GetProperty("total").GetInt32(), list.GetProperty("choices").GetArrayLength()));
        Assert.Contains("list=\"sources\"", list.GetProperty("shortened").GetString());
        Assert.Equal(60, full.RootElement.GetProperty("choiceList").GetProperty("choices").GetArrayLength());
        Assert.Equal("mcp.notFound", missing.Code());
    }

    [Fact]
    public async Task A_form_larger_than_the_result_size_is_cut_and_says_which_argument_reads_the_rest()
    {
        var many = string.Join(",", Enumerable.Range(1, 40).SelectMany(l => Enumerable.Range(1, 25).Select(i => $"{{\"listName\":\"list{l}\",\"name\":\"c{i}\",\"label\":\"Choice {i} of list {l} with a long label\"}}")));
        await using var h = await CreateAsync(settings: new Dictionary<string, string?> { ["Mcp:MaxResultCharacters"] = "10000" }, choices: $"[{many}]");

        var result = await h.CallAsync("analyst", "get_form");

        Assert.True(result.Text().Length <= 10_000, $"{result.Text().Length} characters");
        using var body = result.Json();
        var note = body.RootElement.GetProperty("truncated");
        Assert.True(note.GetProperty("omitted").GetInt32() > 0);
        Assert.Contains("list", note.GetProperty("hint").GetString());
    }

    [Fact]
    public async Task A_viewer_cannot_read_the_form()
    {
        await using var h = await CreateAsync();

        Assert.Equal("mcp.forbidden", (await h.CallAsync("viewer", "get_form")).Code());
    }

    [Fact]
    public async Task Get_tables_describes_every_source_with_columns_types_labels_and_the_questions_they_answer()
    {
        await using var h = await CreateAsync();
        await McpDataFixtures.PublishMainAsync(h, "raw");
        await McpDataFixtures.PublishMainAsync(h, "clean");
        await PublishedTables.PublishAsync(h.Db, h.ProjectId, "output", ("report", "municipality text, households bigint", "('Santa Ana', 30)"));
        await PublishedTables.CommentAsync(h.Db, h.ProjectId, "clean", "main", "water_source", "Main source of water");
        await PublishedTables.PublishTableAsync(h.Db, h.ProjectId, "indicators", "piped_water_rate", "municipality text, value numeric, numerator numeric, denominator numeric", "('Santa Ana', 0.5, 15, 30)");
        await TestData.ExecuteAsync(h.Db,
            "INSERT INTO tamiza.reference_tables (id, project_id, name, file_name, columns, row_count, updated_by, created_at, updated_at) VALUES (gen_random_uuid(), @p, 'population', 'population.csv', '[{\"name\":\"municipality\",\"header\":\"Municipality\",\"type\":\"text\"},{\"name\":\"people\",\"header\":\"People\",\"type\":\"integer\"}]'::jsonb, 2, @u, now(), now())",
            ("p", h.ProjectId), ("u", h.Users["analyst"]));

        using var raw = (await h.CallAsync("analyst", "get_tables", new { source = "raw" })).Json();
        using var clean = (await h.CallAsync("viewer", "get_tables", new { source = "clean" })).Json();
        using var output = (await h.CallAsync("viewer", "get_tables", new { source = "output" })).Json();
        using var indicators = (await h.CallAsync("viewer", "get_tables", new { source = "indicators" })).Json();
        using var reference = (await h.CallAsync("analyst", "get_tables", new { source = "reference" })).Json();

        var main = raw.RootElement.GetProperty("tables")[0];
        Assert.Equal(("main", 60), (main.GetProperty("name").GetString(), main.GetProperty("rows").GetInt64()));
        var source = main.GetProperty("columns").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "water_source");
        Assert.Equal("text", source.GetProperty("type").GetString());
        Assert.Equal(("select_one", "sources"), (source.GetProperty("answers").GetProperty("kind").GetString(), source.GetProperty("answers").GetProperty("list").GetString()));
        var uses = main.GetProperty("columns").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "uses");
        Assert.Equal("select_multiple", uses.GetProperty("answers").GetProperty("kind").GetString());
        var cleanSource = clean.RootElement.GetProperty("tables")[0].GetProperty("columns").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "water_source");
        Assert.Equal("Main source of water", cleanSource.GetProperty("label").GetString());
        Assert.Equal("report", output.RootElement.GetProperty("tables")[0].GetProperty("name").GetString());
        var rate = indicators.RootElement.GetProperty("tables")[0];
        Assert.Equal("piped_water_rate", rate.GetProperty("name").GetString());
        Assert.Contains("value", rate.GetProperty("measures").EnumerateArray().Select(m => m.GetString()));
        var population = reference.RootElement.GetProperty("tables")[0];
        Assert.Equal(("population", 2), (population.GetProperty("name").GetString(), population.GetProperty("rows").GetInt32()));
    }

    [Theory]
    [InlineData("raw")]
    [InlineData("reference")]
    public async Task A_viewer_cannot_read_the_tables_scripts_read(string source)
    {
        await using var h = await CreateAsync();

        Assert.Equal("mcp.forbidden", (await h.CallAsync("viewer", "get_tables", new { source })).Code());
    }

    [Fact]
    public async Task A_source_nothing_has_published_says_how_to_publish_it_and_an_unknown_source_is_refused()
    {
        await using var h = await CreateAsync();

        using var clean = (await h.CallAsync("analyst", "get_tables", new { source = "clean" })).Json();
        var unknown = await h.CallAsync("analyst", "get_tables", new { source = "everything" });

        Assert.False(clean.RootElement.GetProperty("published").GetBoolean());
        Assert.Contains("start_run", clean.RootElement.GetProperty("note").GetString());
        Assert.Equal("mcp.validation", unknown.Code());
    }

    [Fact]
    public async Task Tables_are_summaries_so_they_work_at_summaries_only_and_not_at_all_when_off()
    {
        await using var h = await CreateAsync(McpAccess.Aggregates);
        await McpDataFixtures.PublishMainAsync(h);
        Assert.Null((await h.CallAsync("analyst", "get_tables", new { source = "clean" })).Code());

        await h.SetLevelAsync(McpAccess.Off);

        Assert.Equal("mcp.projectOff", (await h.CallAsync("analyst", "get_tables", new { source = "clean" })).Code());
    }
}
