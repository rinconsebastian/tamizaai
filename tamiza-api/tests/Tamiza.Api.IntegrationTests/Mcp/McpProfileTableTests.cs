using System.Text.Json;
using Npgsql;
using Tamiza.Api.Data;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Mcp;

[Collection(PostgresCollection.Name)]
public sealed class McpProfileTableTests(PostgresFixture postgres)
{
    private Task<McpProjectHarness> CreateAsync(IReadOnlyDictionary<string, string?>? settings = null) =>
        McpProjectHarness.CreateAsync(postgres, McpAccess.Aggregates, settings: settings, formFieldsJson: McpDataFixtures.FieldsJson, formChoicesJson: McpDataFixtures.ChoicesJson);

    private static JsonElement Column(JsonDocument body, string name) =>
        body.RootElement.GetProperty("columns").EnumerateArray().Single(c => c.GetProperty("name").GetString() == name);

    private static Dictionary<string, string> Counts(JsonElement list) =>
        list.EnumerateArray().ToDictionary(c => c.GetProperty("value").GetString()!, c => c.GetProperty("count").ToString());

    [Fact]
    public async Task A_select_question_gives_the_count_of_each_label_and_hides_small_counts()
    {
        await using var h = await CreateAsync();
        await McpDataFixtures.PublishMainAsync(h);

        using var body = (await h.CallAsync("analyst", "profile_table", new { source = "clean", table = "main" })).Json();

        var source = Column(body, "water_source");
        Assert.Equal(60, source.GetProperty("rows").GetInt64());
        Assert.Equal(new Dictionary<string, string> { ["Piped"] = "36", ["Well"] = "21", ["River"] = "fewer than 5" }, Counts(source.GetProperty("choices")));
        Assert.Equal(3, source.GetProperty("distinct").GetInt64());
        Assert.Equal(0, source.GetProperty("empty").GetInt64());
    }

    [Fact]
    public async Task A_select_multiple_question_is_split_on_the_separator()
    {
        await using var h = await CreateAsync();
        await McpDataFixtures.PublishMainAsync(h);

        using var body = (await h.CallAsync("analyst", "profile_table", new { source = "clean", table = "main" })).Json();

        Assert.Equal(
            new Dictionary<string, string> { ["Drinking"] = "58", ["Cooking"] = "30", ["Washing"] = "13", ["Irrigation"] = "fewer than 5" },
            Counts(Column(body, "uses").GetProperty("choices")));
    }

    [Fact]
    public async Task Numbers_give_the_range_the_quartiles_and_the_empty_values_so_a_sentinel_shows()
    {
        await using var h = await CreateAsync();
        await McpDataFixtures.PublishMainAsync(h);

        using var body = (await h.CallAsync("analyst", "profile_table", new { source = "clean", table = "main", columns = new[] { "age", "income" } })).Json();

        var age = Column(body, "age");
        Assert.Equal(-99, age.GetProperty("min").GetDouble());
        Assert.Equal(59, age.GetProperty("max").GetDouble());
        Assert.Equal(1, age.GetProperty("empty").GetInt64());
        Assert.True(age.GetProperty("q1").GetDouble() < age.GetProperty("median").GetDouble());
        Assert.True(age.GetProperty("median").GetDouble() < age.GetProperty("q3").GetDouble());
        var income = Column(body, "income");
        Assert.Equal(10.5, income.GetProperty("min").GetDouble());
        Assert.Equal(630, income.GetProperty("max").GetDouble());
        Assert.Equal(2, body.RootElement.GetProperty("columns").GetArrayLength());
    }

    [Fact]
    public async Task Dates_booleans_and_empty_text_are_described_without_listing_values()
    {
        await using var h = await CreateAsync();
        await McpDataFixtures.PublishMainAsync(h);

        using var body = (await h.CallAsync("analyst", "profile_table", new { source = "clean", table = "main", columns = new[] { "visit_date", "visited_at", "has_water", "note" } })).Json();

        Assert.Equal(("2026-03-02", "2026-04-30"), (Column(body, "visit_date").GetProperty("earliest").GetString(), Column(body, "visit_date").GetProperty("latest").GetString()));
        Assert.StartsWith("2026-03-02 10:00:00", Column(body, "visited_at").GetProperty("earliest").GetString());
        var hasWater = Column(body, "has_water");
        Assert.Equal((48, 11, 1), (hasWater.GetProperty("trueCount").GetInt32(), hasWater.GetProperty("falseCount").GetInt32(), hasWater.GetProperty("empty").GetInt32()));
        // Five blank and five missing notes are all empty; 'x' is the one value, held by 50 rows.
        var note = Column(body, "note");
        Assert.Equal(10, note.GetProperty("empty").GetInt64());
        Assert.Equal(new Dictionary<string, string> { ["x"] = "50" }, Counts(note.GetProperty("values")));
    }

    [Fact]
    public async Task A_short_list_gives_the_values_several_rows_share_and_folds_the_rest()
    {
        await using var h = await CreateAsync();
        await McpDataFixtures.PublishMainAsync(h);

        using var body = (await h.CallAsync("analyst", "profile_table", new { source = "clean", table = "main", columns = new[] { "sector" } })).Json();

        var sector = Column(body, "sector");
        Assert.Equal(new Dictionary<string, string> { ["Rural"] = "30", ["Urban"] = "27" }, Counts(sector.GetProperty("values")));
        Assert.Equal((1, 3), (sector.GetProperty("otherValues").GetInt32(), sector.GetProperty("otherRows").GetInt32()));
    }

    [Fact]
    public async Task Free_text_with_many_distinct_values_gets_a_count_and_no_values()
    {
        await using var h = await CreateAsync();
        await McpDataFixtures.PublishMainAsync(h);

        var result = await h.CallAsync("analyst", "profile_table", new { source = "clean", table = "main", columns = new[] { "respondent_name" } });

        using var body = result.Json();
        var name = Column(body, "respondent_name");
        Assert.Equal(60, name.GetProperty("distinct").GetInt64());
        Assert.False(name.TryGetProperty("values", out _));
        Assert.False(name.TryGetProperty("choices", out _));
        Assert.Contains("free text", name.GetProperty("note").GetString());
        Assert.DoesNotContain("Person 1", result.Text());
    }

    [Fact]
    public async Task A_wide_table_is_profiled_for_its_first_forty_columns_and_the_others_are_listed()
    {
        await using var h = await CreateAsync();
        var columns = string.Join(", ", Enumerable.Range(1, 45).Select(i => $"c{i} bigint"));
        var values = string.Join(", ", Enumerable.Range(1, 45).Select(i => i.ToString()));
        await PublishedTables.PublishAsync(h.Db, h.ProjectId, "clean", ("wide", columns, $"({values}),({values})"));

        using var all = (await h.CallAsync("analyst", "profile_table", new { source = "clean", table = "wide" })).Json();
        using var named = (await h.CallAsync("analyst", "profile_table", new { source = "clean", table = "wide", columns = new[] { "c45" } })).Json();

        Assert.Equal(40, all.RootElement.GetProperty("columns").GetArrayLength());
        Assert.Equal(["c41", "c42", "c43", "c44", "c45"], all.RootElement.GetProperty("otherColumns").EnumerateArray().Select(c => c.GetString()).ToArray());
        Assert.Equal("c45", Column(named, "c45").GetProperty("name").GetString());
        Assert.Equal(0, named.RootElement.GetProperty("otherColumns").GetArrayLength());
    }

    [Fact]
    public async Task A_column_the_table_lacks_and_a_table_that_is_not_published_are_refused_with_what_to_do()
    {
        await using var h = await CreateAsync();
        await McpDataFixtures.PublishMainAsync(h);

        var column = await h.CallAsync("analyst", "profile_table", new { source = "clean", table = "main", columns = new[] { "nope" } });
        var table = await h.CallAsync("analyst", "profile_table", new { source = "clean", table = "nothing" });
        var source = await h.CallAsync("analyst", "profile_table", new { source = "reference", table = "main" });

        Assert.Equal("mcp.validation", column.Code());
        Assert.Contains("get_tables", column.Text());
        Assert.Equal("mcp.notFound", table.Code());
        Assert.Contains("get_tables", table.Text());
        Assert.Equal("mcp.validation", source.Code());
    }

    [Fact]
    public async Task A_profile_is_kept_for_the_same_publication_and_computed_again_after_a_new_one()
    {
        await using var h = await CreateAsync();
        await McpDataFixtures.PublishMainAsync(h);
        var args = new { source = "clean", table = "main", columns = new[] { "water_source" } };
        using var first = (await h.CallAsync("analyst", "profile_table", args)).Json();

        // Changed under the cache without a new run: the same publication answers the same.
        await TestData.ExecuteAsync(h.Db, $"UPDATE \"{PublishedTables.Schema(h.ProjectId, "clean")}\".main SET water_source = 'Well'");
        using var cached = (await h.CallAsync("analyst", "profile_table", args)).Json();
        // A new publication starts fresh.
        await PublishedTables.PublishAsync(h.Db, h.ProjectId, "clean", ("main", "water_source text", string.Join(",", Enumerable.Repeat("('Well')", 7))));
        using var fresh = (await h.CallAsync("analyst", "profile_table", args)).Json();

        Assert.Equal(Counts(Column(first, "water_source").GetProperty("choices")), Counts(Column(cached, "water_source").GetProperty("choices")));
        Assert.Equal(new Dictionary<string, string> { ["Well"] = "7" }, Counts(Column(fresh, "water_source").GetProperty("choices")));
    }

    [Fact]
    public async Task A_read_stopped_by_the_time_limit_is_refused_with_a_suggestion()
    {
        await using var h = await CreateAsync(new Dictionary<string, string?> { ["DataDashboard:LockTimeout"] = "00:00:01" });
        await McpDataFixtures.PublishMainAsync(h);

        // Another session holds the table, as a publish does while it replaces it.
        await using var holder = new NpgsqlConnection(h.Db);
        await holder.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();
        await using (var hold = new NpgsqlCommand($"LOCK TABLE \"{PublishedTables.Schema(h.ProjectId, "clean")}\".main IN ACCESS EXCLUSIVE MODE", holder, transaction))
        {
            await hold.ExecuteNonQueryAsync();
        }

        var result = await h.CallAsync("analyst", "profile_table", new { source = "clean", table = "main" });
        await transaction.RollbackAsync();

        Assert.Equal("mcp.timeout", result.Code());
        Assert.Contains("columns", result.Text());
    }

    [Fact]
    public async Task A_viewer_can_profile_the_dashboards_sources_and_not_the_raw_tables()
    {
        await using var h = await CreateAsync();
        await McpDataFixtures.PublishMainAsync(h, "raw");
        await McpDataFixtures.PublishMainAsync(h, "clean");

        Assert.Null((await h.CallAsync("viewer", "profile_table", new { source = "clean", table = "main", columns = new[] { "age" } })).Code());
        Assert.Equal("mcp.forbidden", (await h.CallAsync("viewer", "profile_table", new { source = "raw", table = "main" })).Code());
    }
}
