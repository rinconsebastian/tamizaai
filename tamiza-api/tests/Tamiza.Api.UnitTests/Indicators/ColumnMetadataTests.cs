using System.Text.Json;
using Tamiza.Api.Http;
using Tamiza.Api.Indicators;
using Tamiza.Api.UnitTests.Assistant;

namespace Tamiza.Api.UnitTests.Indicators;

/// <summary>The map of what an indicator says about its columns (design D2 and D3 of the indicator column metadata).</summary>
public sealed class ColumnMetadataTests
{
    private const string ServiceFile = "tamiza-ui/src/app/core/projects/indicators.service.ts";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static Dictionary<string, IndicatorColumnMetadata?> Labels(params (string Column, string? Label)[] labels) =>
        labels.ToDictionary(l => l.Column, l => (IndicatorColumnMetadata?)new IndicatorColumnMetadata(l.Label), StringComparer.Ordinal);

    private static IDictionary<string, string[]> Errors(IReadOnlyDictionary<string, IndicatorColumnMetadata?>? columns)
    {
        var errors = new FieldErrors();
        ColumnMetadata.Check(columns, errors);
        return errors.ToDictionary();
    }

    /// <summary>The text of a source file of the page, or null when the page is not next to the API (a build of the API alone).</summary>
    private static string? PageSource(string relativePath)
    {
        if (PromptKitChecks.FindUp("tamiza-ui") is null)
        {
            return null;
        }

        // The page is there, so a file that moved is a copy this test no longer guards: fail instead of passing without reading it.
        var path = PromptKitChecks.FindUp(relativePath);
        Assert.True(path is not null, $"{relativePath} is not there: update the test to the file that holds the page's copy.");
        return File.ReadAllText(path!);
    }

    [Fact]
    public void The_page_holds_the_same_column_label_limits_as_the_api()
    {
        if (PageSource(ServiceFile) is not { } service)
        {
            return;
        }

        Assert.Contains($"export const maxColumnLabel = {ColumnLabels.MaxLength};", service, StringComparison.Ordinal);
        Assert.Contains($"export const maxWidgetLabels = {ColumnLabels.MaxWidgetLabels};", service, StringComparison.Ordinal);
        Assert.Contains($"export const maxIndicatorColumns = {ColumnMetadata.MaxColumns};", service, StringComparison.Ordinal);
    }

    [Fact]
    public void The_page_holds_the_same_widget_text_and_source_limits_as_the_api()
    {
        if (PageSource(ServiceFile) is not { } service)
        {
            return;
        }

        // Design D1 and D7 of the widget context: the page's copy of the four limits a widget's texts and a dashboard's source keep.
        Assert.Contains($"export const maxSubtitleLength = {WidgetDisplay.MaxSubtitleLength};", service, StringComparison.Ordinal);
        Assert.Contains($"export const maxFootnoteLength = {WidgetDisplay.MaxFootnoteLength};", service, StringComparison.Ordinal);
        Assert.Contains($"export const maxCaptionLength = {WidgetDisplay.MaxCaptionLength};", service, StringComparison.Ordinal);
        Assert.Contains($"export const maxSourceLength = {DashboardValidation.MaxSourceLength};", service, StringComparison.Ordinal);
    }

    [Fact]
    public void The_page_holds_the_same_reference_line_limits_as_the_api()
    {
        if (PageSource(ServiceFile) is not { } service)
        {
            return;
        }

        // Design D1 of the chart annotations: the page's copy of the two limits of a widget's reference lines, 4 and 40.
        Assert.Equal(4, WidgetDisplay.MaxReferenceLines);
        Assert.Equal(40, WidgetDisplay.MaxReferenceLineLabel);
        Assert.Contains($"export const maxReferenceLines = {WidgetDisplay.MaxReferenceLines};", service, StringComparison.Ordinal);
        Assert.Contains($"export const maxReferenceLineLabel = {WidgetDisplay.MaxReferenceLineLabel};", service, StringComparison.Ordinal);
    }

    [Fact]
    public void A_label_is_trimmed()
    {
        var map = ColumnMetadata.Normalize(Labels(("comuna", "  Comuna  ")));

        Assert.Equal("Comuna", map!["comuna"].Label);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_label_means_none(string? label)
    {
        Assert.Null(ColumnMetadata.Normalize(Labels(("comuna", label))));
        Assert.Empty(Errors(Labels(("comuna", label))));
    }

    [Fact]
    public void A_null_entry_or_one_with_no_property_is_dropped_and_the_others_are_kept()
    {
        var columns = new Dictionary<string, IndicatorColumnMetadata?>
        {
            ["numerator"] = new IndicatorColumnMetadata("Hogares que asistieron"),
            ["denominator"] = null,
            ["comuna"] = new IndicatorColumnMetadata(),
        };

        var map = ColumnMetadata.Normalize(columns)!;

        Assert.Equal(["numerator"], map.Keys);
    }

    [Fact]
    public void An_empty_map_is_none_and_writes_null()
    {
        Assert.Null(ColumnMetadata.Normalize(new Dictionary<string, IndicatorColumnMetadata?>()));
        Assert.Null(ColumnMetadata.Normalize(null));
        Assert.Null(ColumnMetadata.Write(null));
        Assert.Null(ColumnMetadata.Write(new Dictionary<string, IndicatorColumnMetadata>()));
        Assert.Null(ColumnMetadata.Read(null));
        Assert.Null(ColumnMetadata.Read(""));
    }

    [Fact]
    public void Keys_are_written_as_given()
    {
        var map = ColumnMetadata.Normalize(Labels(("_version", "Version"), ("hogar_menores", "Hogares con menores")));

        Assert.Equal("""{"_version":{"label":"Version"},"hogar_menores":{"label":"Hogares con menores"}}""", ColumnMetadata.Write(map));
        Assert.Equal(["_version", "hogar_menores"], ColumnMetadata.Read(ColumnMetadata.Write(map))!.Keys);
    }

    [Fact]
    public void A_label_with_accents_survives_a_write_and_a_read()
    {
        var map = ColumnMetadata.Normalize(Labels(("comuna", "Población ñandú")));

        Assert.Equal("Población ñandú", ColumnMetadata.Read(ColumnMetadata.Write(map))!["comuna"].Label);
    }

    [Fact]
    public void An_unknown_property_is_dropped_when_the_map_is_read()
    {
        var sent = JsonSerializer.Deserialize<Dictionary<string, IndicatorColumnMetadata?>>(
            """{"numerator":{"label":"Hogares","colour":"red"},"denominator":{"colour":"blue"}}""", Web);

        var map = ColumnMetadata.Normalize(sent)!;

        Assert.Equal(["numerator"], map.Keys);
        Assert.Equal("""{"numerator":{"label":"Hogares"}}""", ColumnMetadata.Write(map));
        Assert.Empty(Errors(sent));
    }

    [Fact]
    public void A_stored_text_with_a_property_this_release_does_not_know_still_reads()
    {
        var map = ColumnMetadata.Read("""{"numerator":{"label":"Hogares","format":"count"},"orden":{"format":"count"}}""")!;

        Assert.Equal(["numerator"], map.Keys);
        Assert.Equal("Hogares", map["numerator"].Label);
    }

    [Theory]
    [InlineData(80, true)]
    [InlineData(81, false)]
    public void A_label_holds_at_most_80_characters_once_trimmed(int length, bool accepted)
    {
        var errors = Errors(Labels(("numerator", " " + new string('a', length) + " ")));

        if (accepted)
        {
            Assert.Empty(errors);
            return;
        }

        var error = Assert.Single(errors);
        Assert.Equal("columns.numerator.label", error.Key);
        Assert.Equal(["A column label can have up to 80 characters."], error.Value);
    }

    [Theory]
    [InlineData("Hogares\0")]
    [InlineData("Hogares\nque asistieron")]
    [InlineData("Hogares\r\nque")]
    [InlineData("Hogares\tque")]
    [InlineData("\nHogares\n\u0001")]
    public void A_label_with_a_control_character_is_refused(string label)
    {
        var error = Assert.Single(Errors(Labels(("numerator", label))));

        Assert.Equal("columns.numerator.label", error.Key);
        Assert.Equal(["A column label cannot hold a line break or another control character."], error.Value);
    }

    [Fact]
    public void A_line_break_around_a_label_is_trimmed_with_the_spaces_and_not_refused()
    {
        var columns = Labels(("numerator", "\nHogares\n"));

        Assert.Empty(Errors(columns));
        Assert.Equal("Hogares", ColumnMetadata.Normalize(columns)!["numerator"].Label);
    }

    [Theory]
    [InlineData("Hogar Menores")]
    [InlineData("2020_total")]
    [InlineData("comuna-1")]
    [InlineData("Comuna")]
    [InlineData("")]
    public void A_key_that_is_not_a_columns_name_is_refused_on_the_columns(string key)
    {
        var error = Assert.Single(Errors(Labels((key, "Hogares"))));

        Assert.Equal("columns", error.Key);
        Assert.Equal(
            [$"'{key}' is not a column's name: use the name the indicator's table gives the column, in lowercase letters, digits and underscores."],
            error.Value);
    }

    [Fact]
    public void A_key_of_200_characters_is_cut_to_63_in_the_message_and_never_makes_a_field_path()
    {
        var key = new string('a', 200);

        var error = Assert.Single(Errors(Labels((key, new string('x', 200)))));

        Assert.Equal("columns", error.Key);
        Assert.Equal(
            [$"'{new string('a', 63)}' is not a column's name: use the name the indicator's table gives the column, in lowercase letters, digits and underscores."],
            error.Value);
    }

    [Theory]
    [InlineData("_version")]
    [InlineData("a")]
    public void The_names_a_published_column_can_have_are_accepted(string key)
    {
        Assert.Empty(Errors(Labels((key, "Hogares"))));
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void An_indicator_labels_at_most_100_columns(int count, bool accepted)
    {
        var columns = Labels(Enumerable.Range(1, count).Select(i => ($"columna_{i}", (string?)$"Columna {i}")).ToArray());

        var errors = Errors(columns);

        if (accepted)
        {
            Assert.Empty(errors);
            return;
        }

        var error = Assert.Single(errors);
        Assert.Equal("columns", error.Key);
        Assert.Equal(["An indicator labels at most 100 columns."], error.Value);
    }

    [Fact]
    public void Columns_without_a_label_are_not_counted()
    {
        var columns = Labels(Enumerable.Range(1, 150).Select(i => ($"columna_{i}", i <= 100 ? (string?)$"Columna {i}" : null)).ToArray());

        Assert.Empty(Errors(columns));
    }

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        var errors = Errors(Labels(("numerator", new string('a', 81)), ("Hogar Menores", "Hogares"), ("comuna", "Co\nmuna")));

        Assert.Equal(["columns", "columns.comuna.label", "columns.numerator.label"], errors.Keys.Order());
    }

    [Fact]
    public void The_limits_are_the_documented_ones()
    {
        Assert.Equal(100, ColumnMetadata.MaxColumns);
        Assert.Equal(80, ColumnLabels.MaxLength);
        Assert.Equal(10, ColumnLabels.MaxWidgetLabels);
    }

    // The declared category order of a column (design D6 and D7 of add-declared-category-order)

    private static Dictionary<string, IndicatorColumnMetadata?> One(string column, IndicatorColumnMetadata entry) =>
        new(StringComparer.Ordinal) { [column] = entry };

    private static IReadOnlyList<string?> Values(int count, string prefix = "Valor") =>
        Enumerable.Range(1, count).Select(i => (string?)$"{prefix} {i}").ToList();

    [Fact]
    public void An_order_values_kept_last_and_the_prefix_choice_round_trip_unchanged()
    {
        var map = ColumnMetadata.Normalize(One(
            "categoria", new IndicatorColumnMetadata("Respuesta", ["1. Sí", "2. Tal vez", "3. No"], ["4. No sabe"], true)))!;

        var stored = ColumnMetadata.Write(map)!;
        var back = ColumnMetadata.Read(stored)!["categoria"];

        Assert.Equal("Respuesta", back.Label);
        Assert.Equal(["1. Sí", "2. Tal vez", "3. No"], back.Order);
        Assert.Equal(["4. No sabe"], back.Last);
        Assert.True(back.StripPrefix);
        Assert.Equal(stored, ColumnMetadata.Write(ColumnMetadata.Read(stored)));
        Assert.Empty(Errors(One("categoria", back)));
    }

    [Fact]
    public void The_properties_are_written_in_the_order_of_the_record()
    {
        var stored = ColumnMetadata.Write(ColumnMetadata.Normalize(One("comuna", new IndicatorColumnMetadata("Comuna", ["A"], ["Sin dato"], true))))!;

        Assert.Matches("""^\{"comuna":\{"label":"Comuna","order":\["A"\],"last":\["Sin dato"\],"stripPrefix":true\}\}$""", stored);
    }

    [Fact]
    public void A_column_with_only_the_prefix_choice_is_kept()
    {
        var map = ColumnMetadata.Normalize(One("respuesta", new IndicatorColumnMetadata(StripPrefix: true)))!;

        Assert.Equal("""{"respuesta":{"stripPrefix":true}}""", ColumnMetadata.Write(map));
    }

    [Fact]
    public void A_column_with_only_an_order_or_only_values_kept_last_is_kept()
    {
        var map = ColumnMetadata.Normalize(new Dictionary<string, IndicatorColumnMetadata?>
        {
            ["a"] = new(Order: ["x", "y"]),
            ["b"] = new(Last: ["Sin dato"]),
        })!;

        Assert.Equal("""{"a":{"order":["x","y"]},"b":{"last":["Sin dato"]}}""", ColumnMetadata.Write(map));
    }

    [Fact]
    public void Empty_lists_and_an_unticked_prefix_choice_are_not_saved()
    {
        var empty = new IndicatorColumnMetadata(Order: [], Last: [], StripPrefix: false);

        Assert.Null(ColumnMetadata.Normalize(One("comuna", empty)));

        var withLabel = ColumnMetadata.Normalize(One("comuna", empty with { Label = "  Comuna " }))!;
        Assert.Equal("""{"comuna":{"label":"Comuna"}}""", ColumnMetadata.Write(withLabel));
    }

    [Fact]
    public void Values_are_exact_text_never_trimmed_and_the_empty_text_is_a_value()
    {
        var map = ColumnMetadata.Normalize(One("c", new IndicatorColumnMetadata(Order: [" a ", "", "A"], Last: [" "])))!;

        Assert.Equal([" a ", "", "A"], map["c"].Order);
        Assert.Equal([" "], map["c"].Last);
        Assert.Empty(Errors(One("c", map["c"])));
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void An_order_lists_at_most_100_values(int count, bool accepted)
    {
        var errors = Errors(One("categoria", new IndicatorColumnMetadata(Order: Values(count))));

        if (accepted)
        {
            Assert.Empty(errors);
            return;
        }

        var error = Assert.Single(errors);
        Assert.Equal("columns.categoria.order", error.Key);
        Assert.Equal(["An order lists at most 100 values."], error.Value);
    }

    [Theory]
    [InlineData(20, true)]
    [InlineData(21, false)]
    public void At_most_20_values_are_kept_last(int count, bool accepted)
    {
        var errors = Errors(One("categoria", new IndicatorColumnMetadata(Last: Values(count))));

        if (accepted)
        {
            Assert.Empty(errors);
            return;
        }

        var error = Assert.Single(errors);
        Assert.Equal("columns.categoria.last", error.Key);
        Assert.Equal(["At most 20 values are kept last."], error.Value);
    }

    [Fact]
    public void A_null_value_and_a_value_over_500_characters_are_refused_on_their_own_item()
    {
        var errors = Errors(One("categoria", new IndicatorColumnMetadata(Order: ["a", null, new string('x', 501), new string('y', 500)], Last: [new string('z', 501)])));

        Assert.Equal(["columns.categoria.last[0]", "columns.categoria.order[1]", "columns.categoria.order[2]"], errors.Keys.Order(StringComparer.Ordinal));
        Assert.All(errors.Values, messages => Assert.Equal(["Each value is a text of up to 500 characters."], messages));
    }

    [Fact]
    public void A_value_with_a_NUL_character_is_refused()
    {
        var errors = Errors(One("categoria", new IndicatorColumnMetadata(Order: ["a", "b\0c"])));

        Assert.Equal(["The value cannot contain a NUL character."], errors["columns.categoria.order[1]"]);
    }

    [Fact]
    public void A_value_twice_in_one_list_is_refused_on_the_second_one()
    {
        var errors = Errors(One("categoria", new IndicatorColumnMetadata(Order: ["Sí", "No", "Sí"], Last: ["Otro", "Otro"])));

        Assert.Equal(["columns.categoria.last[1]", "columns.categoria.order[2]"], errors.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["'Sí' is listed twice."], errors["columns.categoria.order[2]"]);
        Assert.Equal(["'Otro' is listed twice."], errors["columns.categoria.last[1]"]);
    }

    [Fact]
    public void A_value_in_both_lists_is_refused_on_the_values_kept_last()
    {
        var errors = Errors(One("respuesta", new IndicatorColumnMetadata(Order: ["Sí", "No", "No sabe"], Last: ["Otro", "No sabe"])));

        var error = Assert.Single(errors);
        Assert.Equal("columns.respuesta.last[1]", error.Key);
        Assert.Equal(["'No sabe' is already in the order."], error.Value);
    }

    [Fact]
    public void Values_that_differ_by_case_or_spaces_are_different_values()
    {
        Assert.Empty(Errors(One("c", new IndicatorColumnMetadata(Order: ["Sí", "sí", "SÍ"], Last: ["Sí "]))));
    }

    [Fact]
    public void An_indicator_that_orders_a_101st_column_with_only_an_order_is_refused_with_the_existing_message()
    {
        var columns = Labels(Enumerable.Range(1, 100).Select(i => ($"columna_{i}", (string?)$"Columna {i}")).ToArray());
        columns["columna_101"] = new IndicatorColumnMetadata(Order: ["a"]);

        var error = Assert.Single(Errors(columns));

        Assert.Equal("columns", error.Key);
        Assert.Equal(["An indicator labels at most 100 columns."], error.Value);
    }

    [Fact]
    public void Columns_with_only_empty_lists_are_not_counted()
    {
        var columns = Labels(Enumerable.Range(1, 100).Select(i => ($"columna_{i}", (string?)$"Columna {i}")).ToArray());
        columns["columna_101"] = new IndicatorColumnMetadata(Order: [], Last: [], StripPrefix: false);

        Assert.Empty(Errors(columns));
    }

    [Fact]
    public void A_column_with_only_a_prefix_choice_counts_toward_the_limit_of_100()
    {
        var columns = Enumerable.Range(1, 101).ToDictionary(
            i => $"columna_{i}", i => (IndicatorColumnMetadata?)new IndicatorColumnMetadata(StripPrefix: true), StringComparer.Ordinal);

        Assert.Equal(["columns"], Errors(columns).Keys);
    }

    [Fact]
    public void The_values_of_a_column_whose_key_is_refused_are_not_checked()
    {
        var errors = Errors(One("Hogar Menores", new IndicatorColumnMetadata(Order: Values(101))));

        Assert.Equal(["columns"], errors.Keys);
    }

    [Fact]
    public void Every_problem_of_an_order_is_reported_at_once()
    {
        var errors = Errors(One("categoria", new IndicatorColumnMetadata(
            Label: new string('a', 81), Order: [..Values(100), "Valor 1"], Last: [null, "Valor 2"])));

        Assert.Equal(
            ["columns.categoria.label", "columns.categoria.last[0]", "columns.categoria.last[1]", "columns.categoria.order", "columns.categoria.order[100]"],
            errors.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_order_limits_are_the_documented_ones()
    {
        Assert.Equal(100, CategoryOrder.MaxValues);
        Assert.Equal(20, CategoryOrder.MaxLast);
    }
}
