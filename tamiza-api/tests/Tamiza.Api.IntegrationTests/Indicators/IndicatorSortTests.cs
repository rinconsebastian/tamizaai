using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;

namespace Tamiza.Api.IntegrationTests.Indicators;

/// <summary>The order of a bar or column widget: by the plotted value or by a column, compared as text or as a number, and the default order.</summary>
[Collection(PostgresCollection.Name)]
public sealed class IndicatorSortTests(PostgresFixture postgres)
{
    private const string Columns = "municipality text, value numeric, sort_key bigint, round text, code bigint, visited_on date, flag boolean, measure double precision";

    private async Task<IndicatorsHarness> WithAsync(string rows, string columns = Columns, Dictionary<string, string?>? settings = null)
    {
        var h = await IndicatorsHarness.CreateAsync(postgres, settings);
        await h.PublishAsync("ranked", columns, rows);
        return h;
    }

    private static object Sort(string by, string? column = null, string? compareAs = null, string direction = "asc") =>
        new { by, column, compareAs, direction };

    private static object Config(object? sort, string groupBy = "municipality", string? measure = null) =>
        new { indicator = "ranked", groupBy, measure, sort };

    private static async Task<string[]> GroupsAsync(IndicatorsHarness h, object config, string chartType = "column") =>
        IndicatorsHarness.Rows(await h.QueryAsync(config, chartType)).Select(r => r.Split('|')[0]).ToArray();

    private static string Row(string municipality, string value = "1", string sortKey = "NULL", string round = "NULL", string code = "NULL",
        string visitedOn = "NULL", string flag = "NULL", string measure = "NULL") =>
        $"('{municipality}', {value}, {sortKey}, {round}, {code}, {visitedOn}, {flag}, {measure})";

    private static string Rows(params string[] rows) => string.Join(",\n", rows);

    // Order by the plotted value

    [Fact]
    public async Task Largest_value_first()
    {
        await using var h = await WithAsync(Rows(Row("Cali", "40"), Row("Cali", "35"), Row("Palmira", "120"), Row("Buga", "40")));

        var result = await h.QueryAsync(Config(Sort("value", direction: "desc")), "bar");

        Assert.Equal(["Palmira|120", "Cali|75", "Buga|40"], IndicatorsHarness.Rows(result));
    }

    [Fact]
    public async Task Smallest_value_first_and_a_tie_keeps_the_default_order()
    {
        await using var h = await WithAsync(Rows(Row("Cali", "40"), Row("Palmira", "120"), Row("Buga", "40")));

        Assert.Equal(["Buga", "Cali", "Palmira"], await GroupsAsync(h, Config(Sort("value"))));
        Assert.Equal(["Palmira", "Buga", "Cali"], await GroupsAsync(h, Config(Sort("value", direction: "desc"))));
    }

    [Fact]
    public async Task A_ratio_can_be_the_plotted_value_to_sort_by()
    {
        await using var h = await WithAsync(
            "('Cali', 1, 4, 10), ('Palmira', 1, 9, 10), ('Buga', 1, 6, 10)", "municipality text, value numeric, numerator numeric, denominator numeric");

        var config = new { indicator = "ranked", groupBy = "municipality", ratio = new { numerator = "numerator", denominator = "denominator" }, sort = Sort("value", direction: "desc") };

        Assert.Equal(["Palmira|0.9", "Buga|0.6", "Cali|0.4"], IndicatorsHarness.Rows(await h.QueryAsync(config, "bar")));
    }

    [Fact]
    public async Task The_row_limit_keeps_the_first_bars_of_the_order()
    {
        await using var h = await WithAsync(
            Rows(Row("A", "10"), Row("B", "50"), Row("C", "30"), Row("D", "20"), Row("E", "40")),
            settings: new() { ["IndicatorDashboard:MaxWidgetQueryRows"] = "2" });

        var result = await h.QueryAsync(Config(Sort("value", direction: "desc")), "bar");

        Assert.True(result.GetProperty("rowLimitReached").GetBoolean());
        Assert.Equal(["B|50", "E|40"], IndicatorsHarness.Rows(result));
    }

    // Order by another column

    [Fact]
    public async Task Ordered_by_another_column()
    {
        await using var h = await WithAsync(Rows(Row("Cali", sortKey: "2"), Row("Palmira", sortKey: "1"), Row("Buga", sortKey: "3")));

        Assert.Equal(["Palmira", "Cali", "Buga"], await GroupsAsync(h, Config(Sort("column", "sort_key"))));
        Assert.Equal(["Buga", "Cali", "Palmira"], await GroupsAsync(h, Config(Sort("column", "sort_key", direction: "desc"))));
    }

    [Fact]
    public async Task A_bar_with_several_values_sorts_by_its_smallest_in_both_directions()
    {
        await using var h = await WithAsync(Rows(Row("Cali", sortKey: "5"), Row("Cali", sortKey: "1"), Row("Palmira", sortKey: "3")));

        Assert.Equal(["Cali", "Palmira"], await GroupsAsync(h, Config(Sort("column", "sort_key"))));
        Assert.Equal(["Palmira", "Cali"], await GroupsAsync(h, Config(Sort("column", "sort_key", direction: "desc"))));
    }

    [Theory]
    [InlineData("asc")]
    [InlineData("desc")]
    public async Task Bars_without_a_sort_value_come_last(string direction)
    {
        await using var h = await WithAsync(Rows(Row("Cali", sortKey: "2"), Row("Palmira", sortKey: "1"), Row("Buga")));

        var groups = await GroupsAsync(h, Config(Sort("column", "sort_key", direction: direction)));

        Assert.Equal("Buga", groups[^1]);
    }

    [Theory]
    [InlineData("asc")]
    [InlineData("desc")]
    public async Task Equal_sort_values_keep_the_default_order_in_both_directions(string direction)
    {
        await using var h = await WithAsync(Rows(Row("Cali", sortKey: "1"), Row("Buga", sortKey: "1"), Row("Palmira", sortKey: "0")));

        var groups = await GroupsAsync(h, Config(Sort("column", "sort_key", direction: direction)));

        Assert.Equal(direction == "asc" ? ["Palmira", "Buga", "Cali"] : ["Buga", "Cali", "Palmira"], groups);
    }

    [Fact]
    public async Task Sorting_by_the_group_column_itself_works()
    {
        await using var h = await WithAsync(Rows(Row("Cali"), Row("Buga"), Row("Palmira")));

        Assert.Equal(["Palmira", "Cali", "Buga"], await GroupsAsync(h, Config(Sort("column", "municipality", direction: "desc"))));
    }

    // Text or number

    [Fact]
    public async Task A_text_column_compared_as_a_number_and_as_text()
    {
        await using var h = await WithAsync(Rows(Row("a", round: "'10'"), Row("b", round: "'2'"), Row("c", round: "'1'")));

        Assert.Equal(["c", "b", "a"], await GroupsAsync(h, Config(Sort("column", "round", "number"))));
        Assert.Equal(["c", "a", "b"], await GroupsAsync(h, Config(Sort("column", "round", "text"))));
        Assert.Equal(["c", "a", "b"], await GroupsAsync(h, Config(Sort("column", "round")))); // text by default
    }

    [Fact]
    public async Task The_group_column_itself_can_be_compared_as_a_number()
    {
        await using var h = await WithAsync(Rows(Row("a", round: "'10'"), Row("b", round: "'2'"), Row("c", round: "'1'")));

        var groups = IndicatorsHarness.Rows(await h.QueryAsync(Config(Sort("column", "round", "number"), groupBy: "round"), "column"));

        Assert.Equal(["1|1", "2|1", "10|1"], groups);
    }

    [Fact]
    public async Task Signs_decimals_and_spaces_are_read_as_numbers()
    {
        await using var h = await WithAsync(Rows(
            Row("one-and-a-half", round: "'1.5'"), Row("minus-ten", round: "'-10'"), Row("one-point-ten", round: "'1.10'"),
            Row("minus-five", round: "' -5 '"), Row("plus", round: "'+7.'"), Row("dot", round: "'.5e1'")));

        var groups = await GroupsAsync(h, Config(Sort("column", "round", "number")));

        Assert.Equal(["minus-ten", "minus-five", "one-point-ten", "one-and-a-half", "dot", "plus"], groups);
    }

    [Theory]
    [InlineData("asc")]
    [InlineData("desc")]
    public async Task Text_that_is_not_a_number_goes_last_and_never_fails_the_query(string direction)
    {
        await using var h = await WithAsync(Rows(
            Row("number", round: "'3'"), Row("n-a", round: "'n/a'"), Row("empty", round: "''"), Row("nan", round: "'NaN'"),
            Row("infinity", round: "'Infinity'"), Row("comma", round: "'1,5'"), Row("underscore", round: "'1_000'"),
            Row("hex", round: "'0x1F'"), Row("huge", round: "'1e1000000'"), Row("em-space", round: "'5' || chr(8195)"), Row("missing")));

        var response = await h.PostQueryAsync(Config(Sort("column", "round", "number", direction)), "column");
        var groups = IndicatorsHarness.Rows(await response.Content.ReadFromJsonAsync<JsonElement>()).Select(r => r.Split('|')[0]).ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(11, groups.Length);
        // '5' padded with an em space is not a number to the database, so only '3' has a sort value.
        Assert.Equal("number", groups[0]);
        // The rest have no sort value, so they all come after it (their order among themselves is the database's text order).
        Assert.Equal(
            ["comma", "em-space", "empty", "hex", "huge", "infinity", "missing", "n-a", "nan", "underscore"],
            groups.Skip(1).OrderBy(g => g, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task A_number_column_compared_as_text()
    {
        await using var h = await WithAsync(Rows(Row("nine", code: "9"), Row("ten", code: "10")));

        Assert.Equal(["ten", "nine"], await GroupsAsync(h, Config(Sort("column", "code", "text"))));
        Assert.Equal(["nine", "ten"], await GroupsAsync(h, Config(Sort("column", "code", "number"))));
        Assert.Equal(["nine", "ten"], await GroupsAsync(h, Config(Sort("column", "code"))));
    }

    [Fact]
    public async Task A_decimal_column_with_only_NaN_values_goes_last_in_both_directions()
    {
        await using var h = await WithAsync(Rows(
            Row("nan", measure: "'NaN'"), Row("small", measure: "1.5"), Row("large", measure: "1e20"), Row("mixed", measure: "'NaN'"), Row("mixed", measure: "3")));

        Assert.Equal(["small", "mixed", "large", "nan"], await GroupsAsync(h, Config(Sort("column", "measure"))));
        Assert.Equal(["large", "mixed", "small", "nan"], await GroupsAsync(h, Config(Sort("column", "measure", direction: "desc"))));
    }

    // Own order

    [Fact]
    public async Task A_date_column_is_in_date_order()
    {
        await using var h = await WithAsync(Rows(
            Row("old", visitedOn: "'2024-01-05'"), Row("new", visitedOn: "'2026-03-01'"), Row("middle", visitedOn: "'2025-12-31'")));

        Assert.Equal(["new", "middle", "old"], await GroupsAsync(h, Config(Sort("column", "visited_on", direction: "desc"))));
    }

    [Fact]
    public async Task A_true_false_column_puts_false_first_when_ascending()
    {
        await using var h = await WithAsync(Rows(Row("yes", flag: "true"), Row("no", flag: "false"), Row("both", flag: "true"), Row("both", flag: "false")));

        Assert.Equal(["both", "no", "yes"], await GroupsAsync(h, Config(Sort("column", "flag"))));
        Assert.Equal(["yes", "both", "no"], await GroupsAsync(h, Config(Sort("column", "flag", direction: "desc"))));
    }

    [Fact]
    public async Task Comparing_a_date_column_as_a_number_is_a_400_under_sort_column()
    {
        await using var h = await WithAsync(Rows(Row("Cali", visitedOn: "'2025-01-01'")));

        var response = await h.PostQueryAsync(Config(Sort("column", "visited_on", "number")), "column");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("'visited_on' is a date column", IndicatorsHarness.Errors(problem, "sort.column")[0]);
    }

    // Default order

    [Fact]
    public async Task Without_an_order_integer_groups_follow_their_own_type_and_a_missing_group_comes_last()
    {
        await using var h = await WithAsync(Rows(Row("a", code: "10"), Row("b", code: "2"), Row("c", code: "1"), Row("d", code: "-5"), Row("e")));

        var groups = (await h.QueryAsync(Config(null, groupBy: "code"), "column")).GetProperty("rows").EnumerateArray()
            .Select(r => r.GetProperty("group").GetString()).ToArray();

        Assert.Equal(["-5", "1", "2", "10", null], groups);
    }

    [Fact]
    public async Task Without_an_order_decimal_groups_follow_their_own_type()
    {
        await using var h = await WithAsync(Rows(
            Row("a", value: "10", sortKey: "1"), Row("b", value: "2.5", sortKey: "1"), Row("c", value: "-5", sortKey: "1"), Row("d", value: "1", sortKey: "1")));

        var groups = await GroupsAsync(h, Config(null, groupBy: "value", measure: "sort_key"));

        Assert.Equal(["-5", "1", "2.5", "10"], groups);
    }

    [Fact]
    public async Task Without_an_order_an_empty_text_group_comes_first_and_a_missing_one_last()
    {
        await using var h = await WithAsync(Rows(Row("b"), Row("a"), Row("")));
        await h.ExecuteAsync($"INSERT INTO \"{h.Schema}\".ranked (municipality, value) VALUES (NULL, 1)");

        var rows = (await h.QueryAsync(Config(null), "column")).GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("group").GetString()).ToArray();

        Assert.Equal(["", "a", "b", null], rows);
    }

    [Fact]
    public async Task A_widget_stored_without_a_sort_queries_in_the_default_order()
    {
        await using var h = await WithAsync(Rows(Row("b"), Row("a")));
        var id = (await h.AddWidgetAsync(new { title = "Plain", chartType = "column", config = new { indicator = "ranked", groupBy = "municipality" } })).GetProperty("id").GetGuid();
        await h.ExecuteAsync("UPDATE tamiza.dashboard_widgets SET config = config - 'sort' WHERE id = @id", ("id", id));

        var widget = (await h.GetWidgetsAsync())[0];
        var result = await h.QueryAsync(widget.GetProperty("config"), "column");

        Assert.Equal(JsonValueKind.Null, widget.GetProperty("config").GetProperty("sort").ValueKind);
        Assert.Equal(["a|1", "b|1"], IndicatorsHarness.Rows(result));
    }

    // Stale

    [Fact]
    public async Task A_sort_column_a_script_change_removed_is_reported_as_missing()
    {
        await using var h = await WithAsync(Rows(Row("Cali", sortKey: "1")));
        await h.PublishAsync("ranked", "municipality text, value numeric", "('Cali', 1)");

        var result = await h.QueryAsync(Config(Sort("column", "sort_key")), "column");

        Assert.Equal(["sort_key"], IndicatorsHarness.Strings(result.GetProperty("missing").GetProperty("columns")));
        Assert.Empty(result.GetProperty("rows").EnumerateArray());
    }

    // Widgets

    [Fact]
    public async Task A_column_widget_keeps_its_sort_as_saved_and_returns_it_from_the_dashboard()
    {
        await using var h = await WithAsync(Rows(Row("Cali")));
        var sort = Sort("column", "municipality", "text", "desc");

        var widget = await h.AddWidgetAsync(new { title = "Sorted", chartType = "column", config = Config(sort) });
        var widgets = await h.GetWidgetsAsync();

        var saved = widget.GetProperty("config").GetProperty("sort");
        Assert.Equal(("column", "municipality", "text", "desc"),
            (saved.GetProperty("by").GetString(), saved.GetProperty("column").GetString(), saved.GetProperty("compareAs").GetString(), saved.GetProperty("direction").GetString()));
        Assert.Equal(widget.ToString(), widgets[0].ToString());
    }

    [Fact]
    public async Task A_widget_that_is_not_a_bar_or_column_cannot_have_a_sort()
    {
        await using var h = await WithAsync(Rows(Row("Cali")));

        var response = await h.PostWidgetAsync(new { title = "Trend", chartType = "line", config = Config(Sort("value")) });
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Only bar and column widgets can be sorted.", IndicatorsHarness.Errors(problem, "config.sort")[0]);
    }

    [Fact]
    public async Task A_saved_widget_cannot_compare_a_date_column_as_a_number_and_an_unknown_column_is_named()
    {
        await using var h = await WithAsync(Rows(Row("Cali")));

        var date = await h.PostWidgetAsync(new { title = "Dates", chartType = "bar", config = Config(Sort("column", "visited_on", "number")) });
        var unknown = await h.PostWidgetAsync(new { title = "Unknown", chartType = "bar", config = Config(Sort("column", "nope")) });

        Assert.Contains("'visited_on' is a date column", IndicatorsHarness.Errors(await date.Content.ReadFromJsonAsync<JsonElement>(), "config.sort.column")[0]);
        Assert.Equal("The table has no column 'nope'.", IndicatorsHarness.Errors(await unknown.Content.ReadFromJsonAsync<JsonElement>(), "config.sort.column")[0]);
    }

    [Fact]
    public async Task Changing_only_the_chart_type_of_a_sorted_widget_to_a_line_is_rejected_and_moving_it_keeps_its_sort()
    {
        await using var h = await WithAsync(Rows(Row("Cali")));
        var id = (await h.AddWidgetAsync(new { title = "Sorted", chartType = "column", config = Config(Sort("value", direction: "desc")) })).GetProperty("id").GetGuid();

        var line = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new { chartType = "line" });
        var moved = await h.Analyst.PatchJsonAsync(h.WidgetUrl(id), new { layout = new { x = 3, y = 0, w = 3, h = 2 } });

        Assert.Equal(HttpStatusCode.BadRequest, line.StatusCode);
        Assert.Contains("config.sort", (await line.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").EnumerateObject().Select(p => p.Name));
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal("desc", (await moved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("config").GetProperty("sort").GetProperty("direction").GetString());
    }
}
