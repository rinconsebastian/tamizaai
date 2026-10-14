using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.CleanData;

/// <summary><c>GET /projects/{id}/data/{source}/{table}/columns/{column}/values</c>: the values offered to the "is one of" filter.</summary>
[Collection(PostgresCollection.Name)]
public sealed class DataValuesApiTests(PostgresFixture postgres)
{
    private static string ValuesUrl(CleanDataHarness h, string column, string? search = null, string source = "output", string table = "persons") =>
        $"{h.Url}/{source}/{table}/columns/{Uri.EscapeDataString(column)}/values" + (search is null ? "" : $"?search={Uri.EscapeDataString(search)}");

    private static async Task<(string Value, long Count)[]> ValuesAsync(CleanDataHarness h, string column, string? search = null)
    {
        var response = await h.Viewer.GetAsync(ValuesUrl(h, column, search));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("values").EnumerateArray().Select(v => (v.GetProperty("value").GetString()!, v.GetProperty("count").GetInt64())).ToArray();
    }

    private async Task<CleanDataHarness> WithMunicipalitiesAsync()
    {
        var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("output", ("persons", "municipality text, age bigint, consent boolean", ""));
        await h.ExecuteAsync($"""
            INSERT INTO "{h.Schema("output")}".persons VALUES
              ('Cali', 20, true), ('Cali', 21, true), ('Cali', 22, false),
              ('Palmira', 30, NULL), ('Palmira', 31, true),
              ('Buga', 40, false), (NULL, 50, NULL), ('', 51, false)
            """);
        return h;
    }

    [Fact]
    public async Task The_most_frequent_values_come_first_with_their_counts_and_ties_by_value()
    {
        await using var h = await WithMunicipalitiesAsync();

        var values = await ValuesAsync(h, "municipality");

        Assert.Equal([("Cali", 3L), ("Palmira", 2L), ("", 1L), ("Buga", 1L)], values);
    }

    [Fact]
    public async Task Empty_values_are_not_counted_as_a_value_but_an_empty_text_is()
    {
        await using var h = await WithMunicipalitiesAsync();

        var values = await ValuesAsync(h, "municipality");

        Assert.DoesNotContain(values, v => v.Count == 0);
        Assert.Equal(4, values.Length); // Cali, Palmira, Buga and the empty text; the missing value is not listed
        Assert.Contains(("", 1L), values);
    }

    [Fact]
    public async Task A_search_restricts_the_list_to_values_that_contain_it_ignoring_case()
    {
        await using var h = await WithMunicipalitiesAsync();

        Assert.Equal([("Palmira", 2L)], await ValuesAsync(h, "municipality", "PAL"));
        Assert.Equal([("Cali", 3L)], await ValuesAsync(h, "municipality", "ali"));
        Assert.Empty(await ValuesAsync(h, "municipality", "zzz"));
    }

    [Fact]
    public async Task A_search_with_wildcards_matches_only_those_characters()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("output", ("persons", "label text", "('50% off'), ('50 percent'), ('a_b'), ('axb')"));

        Assert.Equal([("50% off", 1L)], await ValuesAsync(h, "label", "50%"));
        Assert.Equal([("a_b", 1L)], await ValuesAsync(h, "label", "a_b"));
    }

    [Fact]
    public async Task At_most_50_values_are_listed()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("output", ("persons", "code text", ""));
        await h.ExecuteAsync($"""INSERT INTO "{h.Schema("output")}".persons SELECT 'v' || lpad(g::text, 3, '0') FROM generate_series(1, 120) g""");

        var values = await ValuesAsync(h, "code");

        Assert.Equal(50, values.Length);
        Assert.Equal("v001", values[0].Value);
    }

    [Fact]
    public async Task A_true_false_column_lists_true_and_false()
    {
        await using var h = await WithMunicipalitiesAsync();

        var values = await ValuesAsync(h, "consent");

        Assert.Equal([("false", 3L), ("true", 3L)], values);
    }

    [Fact]
    public async Task A_value_longer_than_a_filter_accepts_is_left_out()
    {
        await using var h = await CleanDataHarness.CreateAsync(postgres);
        await h.PublishAsync("output", ("persons", "note text", "('short')"));
        await h.ExecuteAsync($"""INSERT INTO "{h.Schema("output")}".persons VALUES (repeat('x', 500)), (repeat('y', 501))""");

        var values = await ValuesAsync(h, "note");

        Assert.Equal(["short", new string('x', 500)], values.Select(v => v.Value));
    }

    [Fact]
    public async Task A_number_column_has_no_list_of_values()
    {
        await using var h = await WithMunicipalitiesAsync();

        var response = await h.Viewer.GetAsync(ValuesUrl(h, "age"));
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("'age'", problem.GetProperty("errors").GetProperty("column")[0].GetString());
    }

    [Fact]
    public async Task A_column_the_table_does_not_have_is_404_with_its_code()
    {
        await using var h = await WithMunicipalitiesAsync();

        var response = await h.Viewer.GetAsync(ValuesUrl(h, "salary"));
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("data.columnNotFound", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_hostile_column_name_is_just_a_column_that_does_not_exist()
    {
        await using var h = await WithMunicipalitiesAsync();

        var response = await h.Viewer.GetAsync(ValuesUrl(h, "municipality\"; DROP SCHEMA public CASCADE; --"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(await TestData.ScalarAsync<bool>(h.Db, "SELECT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'public')"));
    }

    [Fact]
    public async Task A_table_that_is_not_published_is_404_with_its_code()
    {
        await using var h = await WithMunicipalitiesAsync();

        var response = await h.Viewer.GetAsync(ValuesUrl(h, "municipality", table: "salaries"));
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("data.tableNotFound", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_search_text_over_500_characters_is_rejected()
    {
        await using var h = await WithMunicipalitiesAsync();

        var response = await h.Viewer.GetAsync(ValuesUrl(h, "municipality", new string('a', 501)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_non_member_gets_404()
    {
        await using var h = await WithMunicipalitiesAsync();
        await TestData.SignInAsync(h.Factory, "mallory", "mallory@example.org");
        using var stranger = TestData.ClientFor(h.Factory, "mallory", "mallory@example.org");

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(ValuesUrl(h, "municipality"))).StatusCode);
    }
}
