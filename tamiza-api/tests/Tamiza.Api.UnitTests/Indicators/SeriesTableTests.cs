using System.Text.Json;
using System.Text.Json.Nodes;
using Tamiza.Api.Indicators;
using Tamiza.Api.UnitTests.Assistant;

namespace Tamiza.Api.UnitTests.Indicators;

/// <summary>
/// The grid of a widget with a series, against the cases tamiza-ui's <c>series-table.ts</c> is tested with. The file is worked out by hand,
/// so the two implementations are checked against the same truth. The tests do nothing when the UI is not next to the API.
/// </summary>
public sealed class SeriesTableTests
{
    private const string FixtureFile = "tamiza-ui/src/app/pages/projects/indicators/testing/series-tables.json";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static JsonArray? Cases()
    {
        var path = PromptKitChecks.FindUp(FixtureFile);
        return path is null ? null : JsonNode.Parse(File.ReadAllText(path))!["cases"]!.AsArray();
    }

    public static TheoryData<int> CaseNumbers()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < (Cases()?.Count ?? 0); i++)
        {
            data.Add(i);
        }

        return data;
    }

    private static IndicatorQueryResponse ResponseOf(JsonNode response) => new(
        true,
        response["rows"]!.Deserialize<List<IndicatorRow>>(Web)!,
        response["rowLimitReached"]?.GetValue<bool>() ?? false,
        MissingReferences.None,
        Series: response["series"]!.Deserialize<List<SeriesInfo>>(Web),
        Totals: response["totals"]!.Deserialize<SeriesTotals>(Web),
        LeftOut: response["leftOut"]?.GetValue<long>());

    [Fact]
    public void The_fixture_has_the_cases_the_design_lists()
    {
        if (Cases() is not { } cases)
        {
            return;
        }

        Assert.Equal(12, cases.Count);
        Assert.Contains(cases, c => c!["name"]!.GetValue<string>().Contains("Likert"));
        Assert.Contains(cases, c => c!["name"]!.GetValue<string>().Contains("pyramid"));
        // The cases of the figures of each cell (design D2 of add-small-sample-warnings).
        Assert.Contains(cases, c => c!["name"]!.GetValue<string>() == "ratio grid keeps the figures of each cell");
        Assert.Contains(cases, c => c!["name"]!.GetValue<string>() == "100% layout cells take the n of their group");
        Assert.Contains(cases, c => c!["name"]!.GetValue<string>() == "Likert cells take the n of their group");
    }

    [Fact]
    public void A_grid_of_an_answer_without_figures_leaves_them_out_of_its_json()
    {
        var response = new IndicatorQueryResponse(
            true,
            [new IndicatorRow("Cali", 35, null, 3, null, "Piped", SeriesKinds.Value)],
            false,
            MissingReferences.None,
            Series: [new SeriesInfo("Piped", SeriesKinds.Value)],
            Totals: new SeriesTotals([new GroupTotal("Cali", 35, 3)], [new SeriesTotal("Piped", SeriesKinds.Value, 35, 3)], new GrandTotal(35, 3)));

        var json = JsonSerializer.SerializeToNode(SeriesTable.From(response, "column", "stacked"), Web)!;

        Assert.False(json["rows"]![0]!.AsObject().ContainsKey("figures"));
    }

    [Fact]
    public void A_share_cell_has_no_figures_when_its_group_has_no_n_and_a_cell_without_a_value_has_none()
    {
        var response = new IndicatorQueryResponse(
            true,
            [new IndicatorRow("Cali", 3, null, 1, null, "A", SeriesKinds.Value, 3, 4, 4)],
            false,
            MissingReferences.None,
            Series: [new SeriesInfo("A", SeriesKinds.Value), new SeriesInfo("B", SeriesKinds.Value)],
            Totals: new SeriesTotals([new GroupTotal("Cali", 3, 1, N: 4)], [new SeriesTotal("A", SeriesKinds.Value, 3, 1), new SeriesTotal("B", SeriesKinds.Value, null, 0)], new GrandTotal(3, 1)));

        var shares = SeriesTable.From(response, "column", "percent");
        var plain = SeriesTable.From(response, "column", "grouped");

        // The cell with a value takes its group's n; the one with none has no figures.
        Assert.Equal(new CellFigures?[] { new(null, null, 4), null }, shares.Rows[0].Figures!);
        Assert.Equal(new CellFigures?[] { new(3, 4, 4), null }, plain.Rows[0].Figures!);
        var withoutGroupN = SeriesTable.From(response with { Totals = response.Totals! with { Groups = [new GroupTotal("Cali", 3, 1)] } }, "column", "percent");
        Assert.Null(withoutGroupN.Rows[0].Figures);
    }

    [Theory]
    [MemberData(nameof(CaseNumbers))]
    public void Every_shared_case_gives_the_grid_that_was_worked_out_by_hand(int number)
    {
        var testCase = Cases()![number]!;

        var table = SeriesTable.From(ResponseOf(testCase["response"]!), testCase["chartType"]!.GetValue<string>(), testCase["layout"]?.GetValue<string>());

        var message = testCase["name"]!.GetValue<string>();
        var actual = JsonSerializer.SerializeToNode(table, Web)!;
        AssertSame(testCase["expected"]!, actual, message, "$");
    }

    [Theory]
    [InlineData("likert", null, true)]
    [InlineData("bar", "percent", true)]
    [InlineData("column", "percent", true)]
    [InlineData("column", "stacked", false)]
    [InlineData("column", "grouped", false)]
    [InlineData("column", null, false)]
    [InlineData("pyramid", null, false)]
    [InlineData("heatmap", null, false)]
    [InlineData("crosstab", "percent", false)]
    [InlineData("line", "percent", false)]
    public void Only_likert_and_a_percent_bar_or_column_show_shares(string chartType, string? layout, bool shares) =>
        Assert.Equal(shares, SeriesTable.ShowsShares(chartType, layout));

    [Fact]
    public void A_response_without_a_series_is_an_empty_grid()
    {
        var table = SeriesTable.From(new IndicatorQueryResponse(true, [], false, MissingReferences.None), "column", null);

        Assert.Empty(table.Columns);
        Assert.Empty(table.Rows);
        Assert.Null(table.GrandTotal);
        Assert.Equal(0, table.GrandRows);
    }

    [Fact]
    public void A_group_named_like_a_missing_one_is_not_mistaken_for_it()
    {
        var response = new IndicatorQueryResponse(
            true,
            [new IndicatorRow("", 1, null, 1, null, "a", SeriesKinds.Value), new IndicatorRow(null, 2, null, 1, null, "a", SeriesKinds.Value)],
            false,
            MissingReferences.None,
            Series: [new SeriesInfo("a", SeriesKinds.Value)],
            Totals: new SeriesTotals(
                [new GroupTotal("", 1, 1), new GroupTotal(null, 2, 1)], [new SeriesTotal("a", SeriesKinds.Value, 3, 2)], new GrandTotal(3, 2)));

        var table = SeriesTable.From(response, "column", null);

        Assert.Equal([1d, 2d], table.Rows.Select(r => r.Cells[0]!.Value));
    }

    private static void AssertSame(JsonNode expected, JsonNode? actual, string message, string path)
    {
        switch (expected)
        {
            case JsonObject o:
                var actualObject = Assert.IsType<JsonObject>(actual, exactMatch: false);
                Assert.True(o.Select(p => p.Key).Order().SequenceEqual(actualObject.Select(p => p.Key).Order()), $"{message}: the members at {path} differ");
                foreach (var (name, value) in o)
                {
                    AssertSame(value!, actualObject[name], message, $"{path}.{name}");
                }

                break;
            case JsonArray a:
                var actualArray = Assert.IsType<JsonArray>(actual, exactMatch: false);
                Assert.True(a.Count == actualArray.Count, $"{message}: {path} has {actualArray.Count} items, not {a.Count}");
                for (var i = 0; i < a.Count; i++)
                {
                    AssertSame(a[i]!, actualArray[i], message, $"{path}[{i}]");
                }

                break;
            case JsonValue v when v.TryGetValue<double>(out var number) && v.GetValueKind() == JsonValueKind.Number:
                Assert.True(actual is not null && actual.GetValueKind() == JsonValueKind.Number, $"{message}: {path} is not a number");
                Assert.True(Math.Abs(number - actual!.GetValue<double>()) < 1e-9, $"{message}: {path} is {actual.GetValue<double>()}, not {number}");
                break;
            default:
                Assert.True(
                    JsonNode.DeepEquals(expected, actual) || (expected is null && actual is null),
                    $"{message}: {path} is {actual?.ToJsonString() ?? "null"}, not {expected?.ToJsonString() ?? "null"}");
                break;
        }
    }
}
