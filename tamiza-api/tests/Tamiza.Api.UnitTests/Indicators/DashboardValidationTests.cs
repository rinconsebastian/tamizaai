using Tamiza.Api.Indicators;

namespace Tamiza.Api.UnitTests.Indicators;

public sealed class DashboardValidationTests
{
    private static string? Error(string? name) => DashboardValidation.Name(name).Error;

    private static string? Accepted(string? name)
    {
        var (accepted, error) = DashboardValidation.Name(name);
        Assert.Null(error);
        return accepted;
    }

    [Fact]
    public void The_name_is_required_and_at_most_40_characters()
    {
        Assert.Equal("The name is required.", Error(null));
        Assert.Equal("The name is required.", Error(""));
        Assert.Equal("The name is required.", Error("   "));
        Assert.Equal(new string('n', 40), Accepted(new string('n', 40)));
        Assert.Equal("The name can have at most 40 characters.", Error(new string('n', 41)));
        Assert.Equal(new string('n', 40), Accepted("  " + new string('n', 40) + "  ")); // trimmed first, as the endpoints store it
        Assert.Equal("The name can have at most 40 characters.", Error("  " + new string('n', 41) + "  "));
    }

    [Fact]
    public void The_name_is_returned_trimmed_and_otherwise_as_typed() =>
        Assert.Equal("Salud <b>2026</b>", Accepted("  Salud <b>2026</b>  "));

    [Theory]
    [InlineData("Field\tprogress")]
    [InlineData("Field\nprogress")]
    [InlineData("Field\r\nprogress")]
    [InlineData("Salud\n")] // trimming would drop it, so the name as sent is checked
    [InlineData("\tSalud")]
    [InlineData("Bell\u0007")]
    public void A_name_with_a_control_character_is_refused(string name) =>
        Assert.Equal("The name cannot contain control characters.", Error(name));

    [Theory]
    [InlineData("Performance", "Performance")]
    [InlineData("PERFORMANCE", "Performance")]
    [InlineData("  performance  ", "Performance")]
    [InlineData("clean data", "Clean data")]
    [InlineData("Clean Data", "Clean data")]
    [InlineData("  CLEAN DATA  ", "Clean data")]
    [InlineData("Settings", "Settings")]
    [InlineData(" sEtTiNgS ", "Settings")]
    public void The_names_of_the_permanent_tabs_are_reserved_in_any_case(string name, string tab) =>
        Assert.Equal($"\"{tab}\" is the name of a permanent tab. Choose another name.", Error(name));

    [Theory]
    [InlineData("Performance 2026")]
    [InlineData("Clean")]
    [InlineData("Data")]
    [InlineData("Settings and notes")]
    [InlineData("Indicators")]
    public void A_name_that_only_contains_a_permanent_tabs_name_is_accepted(string name) => Assert.Equal(name, Accepted(name));

    [Fact]
    public void The_normalized_name_is_trimmed_and_in_lower_case()
    {
        Assert.Equal("salud", DashboardValidation.Normalize("  SaLuD  "));
        Assert.Equal("indicators", DashboardValidation.Normalize("Indicators")); // what the migration stores for every project
    }

    [Fact]
    public void A_taken_name_is_named_as_it_was_saved() =>
        Assert.Equal("Another dashboard of this project is named \"Salud\".", DashboardValidation.TakenMessage("Salud"));

    // A dashboard's filters (design D4)

    private static (List<DashboardFilter>? Filters, IDictionary<string, string[]> Errors) Filters(params DashboardFilterInput?[] input)
    {
        var errors = new Tamiza.Api.Http.FieldErrors();
        var filters = DashboardValidation.Filters(input, errors);
        return (filters, errors.ToDictionary());
    }

    private static DashboardFilterInput Text(string label = "Municipality", string column = "municipality", DashboardFilterValue? value = null) =>
        new(column, label, DashboardFilterKinds.Text, value);

    private static string[] ErrorsOn(string field, params DashboardFilterInput?[] input)
    {
        var (filters, errors) = Filters(input);
        Assert.Null(filters);
        return errors[field];
    }

    [Fact]
    public void No_filters_and_eight_filters_are_accepted_and_nine_are_not()
    {
        Assert.Empty(Filters().Filters!);
        Assert.Equal(8, Filters([.. Enumerable.Range(0, 8).Select(i => Text($"F{i}", $"c{i}"))]).Filters!.Count);
        Assert.Equal(["A dashboard can have at most 8 filters."], ErrorsOn("filters", [.. Enumerable.Range(0, 9).Select(i => Text($"F{i}", $"c{i}"))]));

        var errors = new Tamiza.Api.Http.FieldErrors();
        Assert.Null(DashboardValidation.Filters(null, errors));
        Assert.Contains("filters", errors.ToDictionary().Keys);
    }

    [Fact]
    public void A_label_is_required_trimmed_and_at_most_40_characters()
    {
        Assert.Equal(["The label is required."], ErrorsOn("filters[0].label", Text(label: null!)));
        Assert.Equal(["The label is required."], ErrorsOn("filters[0].label", Text(label: "   ")));
        Assert.Equal(new string('l', 40), Filters(Text(label: "  " + new string('l', 40) + "  ")).Filters!.Single().Label);
        Assert.Equal(["The label can have at most 40 characters."], ErrorsOn("filters[0].label", Text(label: new string('l', 41))));
        Assert.Equal(["The label can have at most 40 characters."], ErrorsOn("filters[0].label", Text(label: "  " + new string('l', 41) + "  ")));
        Assert.Equal(["The label cannot contain control characters."], ErrorsOn("filters[0].label", Text(label: "Zona\tnorte")));
        Assert.Equal("Municipio", Filters(Text(label: "  Municipio  ")).Filters!.Single().Label); // Label trimmed
    }

    [Fact]
    public void Two_labels_that_differ_only_in_case_are_refused_on_the_second() =>
        Assert.Equal(
            ["Another filter of this dashboard is labelled \"ROUND\"."],
            ErrorsOn("filters[1].label", Text("Round", "round"), Text("ROUND", "visit_round")));

    [Theory]
    [InlineData("Municipality")]
    [InlineData("1round")]
    [InlineData("municipality name")]
    [InlineData("")]
    public void A_column_must_be_a_published_columns_name(string column) =>
        Assert.Single(ErrorsOn("filters[0].column", Text(column: column)));

    [Fact]
    public void A_column_of_64_characters_is_refused_and_one_of_63_or_kobos_own_is_accepted()
    {
        Assert.Single(ErrorsOn("filters[0].column", Text(column: new string('c', 64))));
        Assert.NotNull(Filters(Text(column: new string('c', 63))).Filters);
        Assert.NotNull(Filters(Text(column: "_version")).Filters);
    }

    [Fact]
    public void Two_filters_on_one_column_are_refused_on_the_second() =>
        Assert.Equal(
            ["Column 'municipality' already has a filter on this dashboard."],
            ErrorsOn("filters[1].column", Text("Municipality"), Text("Town")));

    [Fact]
    public void An_unknown_kind_is_refused() =>
        Assert.Single(ErrorsOn("filters[0].kind", new DashboardFilterInput("municipality", "Municipality", "color")));

    [Fact]
    public void A_missing_filter_is_refused() =>
        Assert.Single(ErrorsOn("filters[0]", (DashboardFilterInput?)null));

    [Fact]
    public void An_invalid_default_is_refused_and_nothing_is_returned()
    {
        var errors = ErrorsOn("filters[0].default", new DashboardFilterInput("round", "Round", DashboardFilterKinds.Number, new DashboardFilterValue(Min: "three")));

        Assert.Contains("'three'", errors.Single());
    }

    [Fact]
    public void A_valid_default_of_each_kind_is_kept_with_only_what_its_kind_takes()
    {
        var (filters, errors) = Filters(
            new DashboardFilterInput("municipality", "Municipality", DashboardFilterKinds.Text, new DashboardFilterValue(["Cali"])),
            new DashboardFilterInput("consent", "Consent", DashboardFilterKinds.Boolean, new DashboardFilterValue(["true"])),
            new DashboardFilterInput("round", "Round", DashboardFilterKinds.Number, new DashboardFilterValue(Min: "3", Max: "3")),
            new DashboardFilterInput("visit", "Visit", DashboardFilterKinds.Date, new DashboardFilterValue(Min: "", Max: "2026-09-30")),
            new DashboardFilterInput("zone", "Zone", DashboardFilterKinds.Text));

        Assert.Empty(errors);
        Assert.Equal(["Cali"], filters![0].Default!.Values!);
        Assert.Equal(["true"], filters[1].Default!.Values!);
        Assert.Equal(new DashboardFilterValue(null, "3", "3"), filters[2].Default);
        Assert.Equal(new DashboardFilterValue(null, null, "2026-09-30"), filters[3].Default);
        Assert.Null(filters[4].Default);
        Assert.Equal(["municipality", "consent", "round", "visit", "zone"], filters.Select(f => f.Column));
    }

    // A dashboard's palette and colors kept by value (dashboard-colors, design D4)

    private static (DashboardColors? Colors, IDictionary<string, string[]> Errors) Colors(string? palette, params ValueColorInput?[] input)
    {
        var errors = new Tamiza.Api.Http.FieldErrors();
        var colors = DashboardValidation.Colors(palette, input, errors);
        return (colors, errors.ToDictionary());
    }

    private static ValueColorInput Kept(string column, string value, int color) => new(column, value, color);

    private static string[] ColorErrorsOn(string field, string? palette, params ValueColorInput?[] input)
    {
        var (colors, errors) = Colors(palette, input);
        Assert.Null(colors);
        return errors[field];
    }

    [Fact]
    public void Each_palette_is_accepted_with_no_colors_kept()
    {
        foreach (var palette in Palettes.All)
        {
            var (colors, errors) = Colors(palette);

            Assert.Empty(errors);
            Assert.Equal(palette, colors!.Palette);
            Assert.Empty(colors.ValueColors);
        }
    }

    [Theory]
    [InlineData("neon")]
    [InlineData("#ff0000")] // No color codes
    [InlineData("rgb(255, 0, 0)")]
    [InlineData("Earth")] // an id, not a name
    [InlineData("")]
    [InlineData(null)]
    public void A_palette_that_is_not_one_of_the_four_is_refused_on_the_palette(string? palette) =>
        Assert.Equal(["Choose one of the palettes: tamiza, institutional, earth, vivid."], ColorErrorsOn("palette", palette));

    [Fact]
    public void A_palette_error_does_not_hide_the_errors_of_the_entries()
    {
        var (colors, errors) = Colors("neon", Kept("sex", "Female", 9));

        Assert.Null(colors);
        Assert.Equal(["palette", "valueColors[0].color"], errors.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_list_is_required_and_a_dashboard_keeps_at_most_64_colors()
    {
        var errors = new Tamiza.Api.Http.FieldErrors();
        Assert.Null(DashboardValidation.Colors("earth", null, errors));
        Assert.Equal(["The colors kept by value are required; send an empty list for none."], errors.ToDictionary()["valueColors"]);

        // 64 values of 8 columns, each column using each color once.
        ValueColorInput[] Entries(int count) =>
            [.. Enumerable.Range(0, count).Select(i => Kept($"column_{i / 8}", $"value {i}", i % 8 + 1))];
        Assert.Equal(64, Colors("earth", Entries(64)).Colors!.ValueColors.Count);
        Assert.Equal(["A dashboard keeps at most 64 colors by value."], ColorErrorsOn("valueColors", "earth", Entries(65)));
    }

    [Theory]
    [InlineData("Sex")]
    [InlineData("1sex")]
    [InlineData("water source")]
    [InlineData("")]
    [InlineData(null)]
    public void A_column_must_be_a_published_columns_name_on_the_entry(string? column) =>
        Assert.Single(ColorErrorsOn("valueColors[1].column", "earth", Kept("sex", "Female", 5), new ValueColorInput(column, "Male", 1)));

    [Fact]
    public void A_column_of_63_characters_or_kobos_own_is_accepted_and_one_of_64_is_not()
    {
        Assert.NotNull(Colors("earth", Kept(new string('c', 63), "A", 1), Kept("_version", "B", 1)).Colors);
        Assert.Single(ColorErrorsOn("valueColors[0].column", "earth", Kept(new string('c', 64), "A", 1)));
    }

    [Fact]
    public void A_value_is_text_that_may_be_empty_and_at_most_500_characters()
    {
        Assert.Equal(["", new string('v', 500)], Colors("earth", Kept("sex", "", 1), Kept("sex", new string('v', 500), 2)).Colors!.ValueColors.Select(c => c.Value));
        Assert.Equal(["The value is required; an empty text is a value too."], ColorErrorsOn("valueColors[0].value", "earth", new ValueColorInput("sex", null, 1)));
        Assert.Equal(["The value is longer than 500 characters."], ColorErrorsOn("valueColors[0].value", "earth", Kept("sex", new string('v', 501), 1)));
    }

    [Fact]
    public void A_value_cannot_contain_a_NUL_character_but_keeps_any_other_control_character()
    {
        Assert.Equal(["The value cannot contain a NUL character."], ColorErrorsOn("valueColors[0].value", "earth", Kept("sex", "Fe\0male", 5)));
        Assert.Equal(["The value cannot contain a NUL character."], ColorErrorsOn("valueColors[0].value", "earth", Kept("sex", "\0", 5)));
        // An entry refused for its value is not kept, so it takes nothing from the entries after it.
        Assert.Equal(["valueColors[0].value"], Colors("earth", Kept("sex", "Fe\0male", 5), Kept("sex", "Male", 5)).Errors.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["Fe\nmale", "Ma\tle"], Colors("earth", Kept("sex", "Fe\nmale", 5), Kept("sex", "Ma\tle", 2)).Colors!.ValueColors.Select(c => c.Value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(-1)]
    [InlineData(null)]
    public void A_color_outside_1_to_8_is_refused_on_the_entry(int? color) =>
        Assert.Equal(["The color must be a number from 1 to 8."], ColorErrorsOn("valueColors[0].color", "earth", new ValueColorInput("sex", "Female", color)));

    [Fact]
    public void Colors_1_and_8_are_accepted() =>
        Assert.Equal([1, 8], Colors("earth", Kept("sex", "Female", 1), Kept("sex", "Male", 8)).Colors!.ValueColors.Select(c => c.Color));

    [Fact]
    public void A_missing_entry_is_refused_on_its_place() =>
        Assert.Single(ColorErrorsOn("valueColors[1]", "earth", Kept("sex", "Female", 5), null));

    [Fact]
    public void A_value_repeated_in_a_column_is_refused_on_the_second_entry() =>
        Assert.Equal(["sex already keeps a color for Female."], ColorErrorsOn("valueColors[1]", "earth", Kept("sex", "Female", 5), Kept("sex", "Female", 6)));

    [Fact]
    public void An_empty_value_repeated_in_a_column_is_named_as_an_empty_text() =>
        Assert.Equal(["sex already keeps a color for an empty text."], ColorErrorsOn("valueColors[1]", "earth", Kept("sex", "", 5), Kept("sex", "", 6)));

    [Fact]
    public void A_color_used_twice_in_a_column_is_refused_on_the_second_entry() =>
        Assert.Equal(["sex already uses color 5."], ColorErrorsOn("valueColors[1]", "earth", Kept("sex", "Female", 5), Kept("sex", "Male", 5)));

    [Fact]
    public void Only_the_entries_that_repeat_are_refused()
    {
        var (colors, errors) = Colors("earth",
            Kept("sex", "Female", 5), Kept("sex", "Male", 5), Kept("sex", "Other", 2), Kept("sex", "Other", 3), Kept("sex", "Unknown", 3));

        Assert.Null(colors);
        Assert.Equal(["valueColors[1]", "valueColors[3]"], errors.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_same_color_in_two_columns_and_the_same_value_in_two_columns_are_accepted()
    {
        var (colors, errors) = Colors("earth", Kept("sex", "Female", 5), Kept("zone", "Rural", 5), Kept("answer", "Female", 1));

        Assert.Empty(errors);
        Assert.Equal(3, colors!.ValueColors.Count);
    }

    [Fact]
    public void Values_are_compared_as_text_so_a_value_in_another_case_is_another_value()
    {
        var (colors, errors) = Colors("earth", Kept("water_source", "Piped", 1), Kept("water_source", "piped", 2), Kept("water_source", "Piped ", 3));

        Assert.Empty(errors);
        Assert.Equal(["Piped", "piped", "Piped "], colors!.ValueColors.Select(c => c.Value)); // not trimmed either
    }

    [Fact]
    public void The_colors_are_kept_in_the_order_they_are_sent()
    {
        var (colors, _) = Colors("vivid", Kept("zone", "Rural", 3), Kept("sex", "Female", 5), Kept("sex", "Male", 1));

        Assert.Equal("vivid", colors!.Palette);
        Assert.Equal(
            [new ValueColor("zone", "Rural", 3), new ValueColor("sex", "Female", 5), new ValueColor("sex", "Male", 1)],
            colors.ValueColors);
    }

    // The source (design D7 of the widget context)

    private static (string? Source, string Errors) CheckSource(string? input)
    {
        var errors = new Tamiza.Api.Http.FieldErrors();
        var source = DashboardValidation.Source(input, errors);
        return (source, errors.ToDictionary().TryGetValue("source", out var messages) ? string.Join(" | ", messages) : "");
    }

    [Fact]
    public void A_source_is_trimmed_and_a_blank_one_is_null_with_no_error()
    {
        Assert.Equal(("Encuesta de ejemplo", ""), CheckSource("  Encuesta de ejemplo  "));
        Assert.Equal((null, ""), CheckSource(null));
        Assert.Equal((null, ""), CheckSource(""));
        Assert.Equal((null, ""), CheckSource("     "));
        Assert.Equal(("Encuesta <b>2026</b> & \"otra\"", ""), CheckSource("Encuesta <b>2026</b> & \"otra\""));
    }

    [Fact]
    public void A_source_of_300_characters_is_kept_and_301_are_refused_on_source_naming_300()
    {
        Assert.Equal((new string('s', 300), ""), CheckSource(new string('s', 300)));
        Assert.Equal((new string('s', 300), ""), CheckSource("  " + new string('s', 300) + "  ")); // counted once trimmed
        Assert.Equal((null, "The source is longer than 300 characters."), CheckSource(new string('s', 301)));
        Assert.Equal((null, "The source is longer than 300 characters."), CheckSource("  " + new string('s', 301) + "  "));
        Assert.Equal(300, DashboardValidation.MaxSourceLength);
    }

    [Theory]
    [InlineData("Encuesta\0 de ejemplo")]
    [InlineData("Encuesta\nde ejemplo")]
    [InlineData("Encuesta\r\nde ejemplo")]
    [InlineData("Encuesta\tde ejemplo")]
    [InlineData("Encuesta\u0085de ejemplo")]
    [InlineData("Encuesta de ejemplo\n")] // trimming would drop it, so the source as sent is checked
    [InlineData("\tEncuesta de ejemplo")]
    public void A_source_with_a_control_character_is_refused_on_source(string input) =>
        Assert.Equal((null, "The source cannot contain line breaks or other control characters."), CheckSource(input));
}
