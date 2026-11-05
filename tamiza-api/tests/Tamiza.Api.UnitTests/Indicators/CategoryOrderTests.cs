using System.Text.Json.Nodes;
using Tamiza.Api.Indicators;
using Tamiza.Api.UnitTests.Assistant;

namespace Tamiza.Api.UnitTests.Indicators;

/// <summary>
/// The rule that orders the values of a place and hides their numbers (add-declared-category-order, design D2, D3 and D5), against the
/// cases tamiza-ui's <c>category-order.ts</c> is tested with. The file is worked out by hand, so the two implementations are checked
/// against the same truth. The fixture tests do nothing when the UI is not next to the API.
/// </summary>
public sealed class CategoryOrderTests
{
    private const string FixtureFile = "tamiza-ui/src/app/pages/projects/indicators/testing/category-orders.json";
    private const string ServiceFile = "tamiza-ui/src/app/core/projects/indicators.service.ts";

    private static JsonNode? Fixture()
    {
        var path = PromptKitChecks.FindUp(FixtureFile);
        return path is null ? null : JsonNode.Parse(File.ReadAllText(path));
    }

    private static TheoryData<int> Numbers(string section)
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < (Fixture()?[section]?.AsArray().Count ?? 0); i++)
        {
            data.Add(i);
        }

        return data;
    }

    public static TheoryData<int> ArrangeCases() => Numbers("arrange");

    public static TheoryData<int> ShownCases() => Numbers("shown");

    public static TheoryData<int> PrefixCases() => Numbers("prefixes");

    /// <summary>An entry of the fixture: a string is a value, null is the missing value and <c>{ "kind": "other" }</c> is "Other".</summary>
    private static OrderEntry EntryOf(JsonNode? node) => node switch
    {
        null => new OrderEntry(null, SeriesKinds.None),
        JsonObject => new OrderEntry(null, SeriesKinds.Other),
        _ => new OrderEntry(node.GetValue<string>(), SeriesKinds.Value),
    };

    private static JsonNode? NodeOf(OrderEntry entry) => entry.Kind switch
    {
        SeriesKinds.None => null,
        SeriesKinds.Other => new JsonObject { ["kind"] = "other" },
        _ => JsonValue.Create(entry.Value),
    };

    private static string[] Texts(JsonNode? list) => list!.AsArray().Select(v => v!.GetValue<string>()).ToArray();

    [Fact]
    public void The_fixture_has_the_cases_the_design_lists()
    {
        if (Fixture() is not { } fixture)
        {
            return;
        }

        var arrange = fixture["arrange"]!.AsArray();
        Assert.Equal(24, arrange.Count);
        Assert.Equal(8, fixture["shown"]!.AsArray().Count);
        Assert.Equal(29, fixture["prefixes"]!.AsArray().Count);
        foreach (var name in new[]
        {
            "no declaration", "a sorted place with only an order", "an indicator order", "a widget order that replaces", "values not listed",
            "a value kept last", "an own order that lists a value the indicator keeps last", "values kept last with a sort",
            "none and Other after the values kept last", "a listed value the entries do not hold", "exact text: case", "exact text: spaces",
            "the empty text is a value",
        })
        {
            Assert.Contains(arrange, c => c!["name"]!.GetValue<string>().StartsWith(name, StringComparison.Ordinal));
        }

        var names = arrange.Select(c => c!["name"]!.GetValue<string>()).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        foreach (var separator in new[] { "1. ", "2) ", "3 - ", "4: ", "5 – " })
        {
            Assert.Contains(fixture["prefixes"]!.AsArray(), c => c!["value"]!.GetValue<string>().StartsWith(separator, StringComparison.Ordinal));
        }

        foreach (var kept in new[] { "10 - 14", "0 - 4", "1.5 horas", "1.Sí", "100.000", "$10.000", "1234. x", "1. " })
        {
            Assert.Contains(fixture["prefixes"]!.AsArray(), c => c!["value"]!.GetValue<string>() == kept);
        }

        Assert.Contains(fixture["shown"]!.AsArray(), c => c!["values"]!.AsArray().Any(v => v?.GetValue<string>() == "6. Otro"));
    }

    [Theory]
    [MemberData(nameof(ArrangeCases))]
    public void Every_shared_arrangement_gives_the_list_that_was_worked_out_by_hand(int number)
    {
        if (Fixture() is not { } fixture)
        {
            return;
        }

        var testCase = fixture["arrange"]![number]!;
        var entries = testCase["entries"]!.AsArray().Select(EntryOf).ToList();
        var declared = testCase["declared"] is { } d
            ? new PlaceOrder(Texts(d["order"]), Texts(d["last"]), d["sorted"]!.GetValue<bool>())
            : null;

        var arranged = CategoryOrder.Arrange(entries, entry => entry, declared);

        var expected = testCase["expected"]!.AsArray().ToJsonString();
        Assert.Equal(expected, new JsonArray(arranged.Select(NodeOf).ToArray()).ToJsonString());
    }

    [Theory]
    [MemberData(nameof(ShownCases))]
    public void Every_shared_set_of_values_is_shown_as_it_was_worked_out_by_hand(int number)
    {
        if (Fixture() is not { } fixture)
        {
            return;
        }

        var testCase = fixture["shown"]![number]!;
        var values = testCase["values"]!.AsArray().Select(v => v?.GetValue<string>()).ToList();

        var shown = CategoryOrder.ShownValues(values, testCase["strip"]!.GetValue<bool>());

        var expected = testCase["expected"]!.AsArray().Select(v => v?.GetValue<string>()).ToList();
        Assert.Equal(expected, values.Select(v => CategoryOrder.Shown(shown, v)).ToList());
    }

    [Theory]
    [MemberData(nameof(PrefixCases))]
    public void Every_shared_text_loses_its_number_only_when_the_rule_says_so(int number)
    {
        if (Fixture() is not { } fixture)
        {
            return;
        }

        var testCase = fixture["prefixes"]![number]!;

        Assert.Equal(testCase["expected"]!.GetValue<string>(), CategoryOrder.StripPrefix(testCase["value"]!.GetValue<string>()));
    }

    [Fact]
    public void The_page_holds_the_same_order_limits_as_the_api()
    {
        var path = PromptKitChecks.FindUp(ServiceFile);
        if (path is null)
        {
            return;
        }

        var service = File.ReadAllText(path);
        Assert.Equal(100, CategoryOrder.MaxValues);
        Assert.Equal(20, CategoryOrder.MaxLast);
        Assert.Contains($"export const maxOrderValues = {CategoryOrder.MaxValues};", service, StringComparison.Ordinal);
        Assert.Contains($"export const maxKeptLast = {CategoryOrder.MaxLast};", service, StringComparison.Ordinal);
    }

    // What a widget declares (design D3 and D4)

    private static readonly string[] Answers = ["1. Sí", "2. Tal vez", "3. No"];

    private static Dictionary<string, IndicatorColumnMetadata> Columns(params (string Column, IndicatorColumnMetadata Meta)[] entries) =>
        entries.ToDictionary(e => e.Column, e => e.Meta, StringComparer.Ordinal);

    private static WidgetConfig Widget(
        string? groupBy = "comuna", string? seriesBy = null, WidgetSort? sort = null, WidgetDisplay? display = null) =>
        new WidgetConfigInput("teatro_hoy", GroupBy: groupBy, SeriesBy: seriesBy, Sort: sort, Display: display).Normalize();

    private static readonly WidgetSort ByValue = new(SortValues.ByValue, null, null, SortValues.Desc);
    private static readonly WidgetSort ByOrden = new(SortValues.ByColumn, "orden", SortValues.Text, SortValues.Asc);

    [Fact]
    public void Nothing_is_declared_when_there_is_no_chart_type_no_metadata_or_only_labels()
    {
        var labelled = Columns(("comuna", new IndicatorColumnMetadata("Comuna")), ("respuesta", new IndicatorColumnMetadata("Respuesta")));

        Assert.Null(CategoryOrder.For(null, Widget(), Columns(("comuna", new IndicatorColumnMetadata(Order: ["a"])))));
        Assert.Null(CategoryOrder.For(ChartTypes.Column, Widget(), null));
        Assert.Null(CategoryOrder.For(ChartTypes.Column, Widget(), new Dictionary<string, IndicatorColumnMetadata>()));
        foreach (var chartType in ChartTypes.All)
        {
            Assert.Null(CategoryOrder.For(chartType, Widget(seriesBy: "respuesta"), labelled));
        }
    }

    [Fact]
    public void Nothing_is_declared_for_a_column_the_widget_does_not_show()
    {
        var columns = Columns(("otra", new IndicatorColumnMetadata(Order: ["a"], Last: ["b"], StripPrefix: true)));

        Assert.Null(CategoryOrder.For(ChartTypes.Column, Widget(seriesBy: "respuesta"), columns));
    }

    [Fact]
    public void An_indicators_order_and_values_kept_last_reach_the_groups_of_a_bar()
    {
        var columns = Columns(("comuna", new IndicatorColumnMetadata(Order: Answers, Last: ["Sin dato"])));

        var orders = CategoryOrder.For(ChartTypes.Bar, Widget(), columns)!;

        Assert.Equal(Answers, orders.Groups!.Order);
        Assert.Equal(["Sin dato"], orders.Groups.Last);
        Assert.False(orders.Groups.Sorted);
        Assert.Null(orders.Series);
        Assert.False(orders.HideGroupPrefixes);
    }

    [Fact]
    public void A_widgets_own_order_replaces_the_indicators_order_and_never_its_values_kept_last()
    {
        var columns = Columns(("respuesta", new IndicatorColumnMetadata(Order: Answers, Last: ["4. No sabe"])));
        var display = new WidgetDisplay(Order: new WidgetCategoryOrder(Series: ["4. No sabe", "1. Sí"]));

        var orders = CategoryOrder.For(ChartTypes.Column, Widget(seriesBy: "respuesta", display: display), columns)!;

        Assert.Equal(["4. No sabe", "1. Sí"], orders.Series!.Order);
        Assert.Equal(["4. No sabe"], orders.Series.Last);
        Assert.Null(orders.Groups);
    }

    [Fact]
    public void An_empty_own_order_is_no_order_so_the_indicators_applies()
    {
        var columns = Columns(("comuna", new IndicatorColumnMetadata(Order: Answers)));
        var display = new WidgetDisplay(Order: new WidgetCategoryOrder(Group: []));

        var orders = CategoryOrder.For(ChartTypes.Column, Widget(display: display), columns)!;

        Assert.Equal(Answers, orders.Groups!.Order);
    }

    [Theory]
    [InlineData(ChartTypes.Bar)]
    [InlineData(ChartTypes.Column)]
    public void A_sort_keeps_only_the_values_kept_last(string chartType)
    {
        var columns = Columns(("comuna", new IndicatorColumnMetadata(Order: Answers, Last: ["Otro"])));

        var byValue = CategoryOrder.For(chartType, Widget(sort: ByValue), columns)!;
        var byColumn = CategoryOrder.For(chartType, Widget(sort: ByOrden), columns)!;

        Assert.True(byValue.Groups!.Sorted);
        Assert.Equal(["Otro"], byValue.Groups.Last);
        Assert.True(byColumn.Groups!.Sorted);
    }

    [Fact]
    public void A_sort_with_only_an_order_declares_nothing_for_the_groups()
    {
        var columns = Columns(("comuna", new IndicatorColumnMetadata(Order: Answers)));

        Assert.Null(CategoryOrder.For(ChartTypes.Column, Widget(sort: ByValue), columns));
    }

    [Fact]
    public void A_sort_does_not_touch_the_series()
    {
        var columns = Columns(("respuesta", new IndicatorColumnMetadata(Order: Answers)));

        var orders = CategoryOrder.For(ChartTypes.Bar, Widget(seriesBy: "respuesta", sort: ByValue), columns)!;

        Assert.Null(orders.Groups);
        Assert.Equal(Answers, orders.Series!.Order);
        Assert.False(orders.Series.Sorted);
    }

    [Fact]
    public void A_box_plot_is_sorted_only_by_median_and_with_a_group_column()
    {
        var columns = Columns(("vehiculo", new IndicatorColumnMetadata(Order: ["Carro", "Moto"], Last: ["Otro"])));

        var natural = CategoryOrder.For(ChartTypes.Boxplot, Widget(groupBy: "vehiculo"), columns)!;
        var byMedian = CategoryOrder.For(ChartTypes.Boxplot, Widget(groupBy: "vehiculo", sort: ByValue), columns)!;
        var noGroup = CategoryOrder.For(ChartTypes.Boxplot, Widget(groupBy: null, sort: ByValue), columns);

        Assert.False(natural.Groups!.Sorted);
        Assert.Equal(["Carro", "Moto"], natural.Groups.Order);
        Assert.True(byMedian.Groups!.Sorted);
        Assert.Equal(["Otro"], byMedian.Groups.Last);
        Assert.Null(noGroup);
    }

    [Theory]
    [InlineData(ChartTypes.Line)]
    [InlineData(ChartTypes.Heatmap)]
    [InlineData(ChartTypes.Crosstab)]
    [InlineData(ChartTypes.Bar)]
    [InlineData(ChartTypes.Column)]
    public void Groups_and_series_are_both_arranged_on_the_kinds_that_take_a_series_order(string chartType)
    {
        var columns = Columns(("comuna", new IndicatorColumnMetadata(Last: ["Sin dato"])), ("respuesta", new IndicatorColumnMetadata(Order: Answers, StripPrefix: true)));

        var orders = CategoryOrder.For(chartType, Widget(seriesBy: "respuesta"), columns)!;

        Assert.Equal(["Sin dato"], orders.Groups!.Last);
        Assert.Equal(Answers, orders.Series!.Order);
        Assert.False(orders.HideGroupPrefixes);
        Assert.True(orders.HideSeriesPrefixes);
    }

    [Theory]
    [InlineData(ChartTypes.Pyramid)]
    [InlineData(ChartTypes.Likert)]
    public void The_sides_of_a_pyramid_and_the_scale_of_a_Likert_widget_keep_their_places_but_lose_their_numbers(string chartType)
    {
        var columns = Columns(("comuna", new IndicatorColumnMetadata(Order: ["B", "A"])), ("respuesta", new IndicatorColumnMetadata(Order: Answers, StripPrefix: true)));

        var orders = CategoryOrder.For(chartType, Widget(seriesBy: "respuesta"), columns)!;

        Assert.Equal(["B", "A"], orders.Groups!.Order);
        Assert.Null(orders.Series);
        Assert.True(orders.HideSeriesPrefixes);
    }

    [Fact]
    public void A_pie_arranges_its_groups_and_has_no_series()
    {
        var columns = Columns(("comuna", new IndicatorColumnMetadata(Order: ["B", "A"], StripPrefix: true)), ("respuesta", new IndicatorColumnMetadata(Order: Answers, StripPrefix: true)));

        var orders = CategoryOrder.For(ChartTypes.Pie, Widget(seriesBy: "respuesta"), columns)!;

        Assert.Equal(["B", "A"], orders.Groups!.Order);
        Assert.True(orders.HideGroupPrefixes);
        Assert.Null(orders.Series);
        Assert.False(orders.HideSeriesPrefixes);
    }

    [Theory]
    [InlineData(ChartTypes.Stat)]
    [InlineData(ChartTypes.Histogram)]
    public void A_stat_tile_and_a_histogram_have_no_groups_to_order_or_to_show(string chartType)
    {
        var columns = Columns(("comuna", new IndicatorColumnMetadata(Order: ["B", "A"], Last: ["C"], StripPrefix: true)));

        Assert.Null(CategoryOrder.For(chartType, Widget(), columns));
    }

    [Fact]
    public void A_map_shows_the_text_of_its_groups_without_numbers_but_does_not_order_them()
    {
        var columns = Columns(("comuna", new IndicatorColumnMetadata(Order: ["B", "A"], Last: ["C"], StripPrefix: true)));

        var orders = CategoryOrder.For(ChartTypes.Map, Widget(), columns)!;

        Assert.Null(orders.Groups);
        Assert.Null(orders.Series);
        Assert.True(orders.HideGroupPrefixes);
    }

    [Fact]
    public void Hidden_prefixes_alone_declare_no_order()
    {
        var columns = Columns(("comuna", new IndicatorColumnMetadata(StripPrefix: true)));

        var orders = CategoryOrder.For(ChartTypes.Column, Widget(), columns)!;

        Assert.Null(orders.Groups);
        Assert.Null(orders.Series);
        Assert.True(orders.HideGroupPrefixes);
    }

    // The grid of a widget with a series arranged

    private static SeriesTable Grid(bool withShares = true) => new(
        [
            new SeriesTableColumn("4. No sabe", SeriesKinds.Value, 40, 4),
            new SeriesTableColumn("1. Sí", SeriesKinds.Value, 10, 1),
            new SeriesTableColumn(null, SeriesKinds.None, 5, 1),
            new SeriesTableColumn("2. No", SeriesKinds.Value, 20, 2),
            new SeriesTableColumn(null, SeriesKinds.Other, 7, 1),
        ],
        [
            new SeriesTableRow("Sin dato", 11, 3, [1, 2, 3, 4, 5], withShares ? [0.1, 0.2, 0.3, 0.4, 0.5] : null),
            new SeriesTableRow("COMUNA 1", 22, 3, [10, null, 30, 40, 50], withShares ? [0.01, null, 0.03, 0.04, 0.05] : null),
            new SeriesTableRow(null, 33, 3, [100, 200, 300, 400, 500], withShares ? [0.9, 0.8, 0.7, 0.6, 0.5] : null),
            new SeriesTableRow("COMUNA 2", 44, 3, [7, 8, 9, 10, 11], withShares ? [0.7, 0.8, 0.9, 0.1, 0.11] : null),
        ],
        66, 12, 0, 0, false);

    [Fact]
    public void A_grid_with_no_declared_place_is_returned_as_it_is()
    {
        var grid = Grid();

        Assert.Same(grid, CategoryOrder.ArrangeTable(grid, null));
        Assert.Same(grid, CategoryOrder.ArrangeTable(grid, new CategoryOrders(null, null, true, true)));
    }

    [Fact]
    public void A_column_keeps_its_cells_shares_and_total_when_the_series_are_arranged()
    {
        var orders = new CategoryOrders(null, new PlaceOrder(["1. Sí", "2. No"], ["4. No sabe"], false), false, false);

        var arranged = CategoryOrder.ArrangeTable(Grid(), orders);

        Assert.Equal(["1. Sí", "2. No", "4. No sabe", null, null], arranged.Columns.Select(c => c.Series));
        Assert.Equal([SeriesKinds.Value, SeriesKinds.Value, SeriesKinds.Value, SeriesKinds.None, SeriesKinds.Other], arranged.Columns.Select(c => c.Kind));
        Assert.Equal([10d, 20d, 40d, 5d, 7d], arranged.Columns.Select(c => c.Total));
        Assert.Equal(["Sin dato", "COMUNA 1", null, "COMUNA 2"], arranged.Rows.Select(r => r.Group));
        Assert.Equal<double?>([2, 4, 1, 3, 5], arranged.Rows[0].Cells);
        Assert.Equal<double?>([null, 40, 10, 30, 50], arranged.Rows[1].Cells);
        Assert.Equal<double?>([0.2, 0.4, 0.1, 0.3, 0.5], arranged.Rows[0].Shares);
        Assert.Equal<double?>([null, 0.04, 0.01, 0.03, 0.05], arranged.Rows[1].Shares);
        Assert.Equal([11d, 22d, 33d, 44d], arranged.Rows.Select(r => r.Total));
        Assert.Equal(66, arranged.GrandTotal);
    }

    [Fact]
    public void A_row_keeps_its_cells_and_total_when_the_groups_are_arranged_and_none_goes_last()
    {
        var orders = new CategoryOrders(new PlaceOrder(["COMUNA 1", "COMUNA 2"], ["Sin dato"], false), null, false, false);

        var arranged = CategoryOrder.ArrangeTable(Grid(), orders);

        Assert.Equal(["COMUNA 1", "COMUNA 2", "Sin dato", null], arranged.Rows.Select(r => r.Group));
        Assert.Equal([22d, 44d, 11d, 33d], arranged.Rows.Select(r => r.Total));
        Assert.Equal<double?>([10, null, 30, 40, 50], arranged.Rows[0].Cells);
        Assert.Equal<double?>([100, 200, 300, 400, 500], arranged.Rows[3].Cells);
        Assert.Equal(Grid().Columns, arranged.Columns);
    }

    [Fact]
    public void A_grid_without_shares_keeps_none_and_a_sorted_place_moves_only_the_values_kept_last()
    {
        var orders = new CategoryOrders(new PlaceOrder(["COMUNA 2"], ["COMUNA 1"], true), null, false, false);

        var arranged = CategoryOrder.ArrangeTable(Grid(withShares: false), orders);

        Assert.All(arranged.Rows, row => Assert.Null(row.Shares));
        Assert.Equal(["Sin dato", "COMUNA 2", "COMUNA 1", null], arranged.Rows.Select(r => r.Group));
    }

    [Fact]
    public void Every_cell_stays_with_its_column_and_its_row_whatever_the_declaration()
    {
        var grid = Grid();
        var orders = new CategoryOrders(
            new PlaceOrder(["COMUNA 2", "Sin dato"], ["COMUNA 1"], false), new PlaceOrder(["2. No", "1. Sí"], ["4. No sabe"], false), false, false);

        var arranged = CategoryOrder.ArrangeTable(grid, orders);

        double? CellOf(SeriesTable table, string? group, string? series, string kind) =>
            table.Rows.Single(r => r.Group == group).Cells[table.Columns.ToList().FindIndex(c => c.Series == series && c.Kind == kind)];

        foreach (var row in grid.Rows)
        {
            foreach (var column in grid.Columns)
            {
                Assert.Equal(CellOf(grid, row.Group, column.Series, column.Kind), CellOf(arranged, row.Group, column.Series, column.Kind));
            }
        }

        Assert.Equal(grid.Rows.Count, arranged.Rows.Count);
        Assert.Equal(grid.Columns.Count, arranged.Columns.Count);
    }
}
