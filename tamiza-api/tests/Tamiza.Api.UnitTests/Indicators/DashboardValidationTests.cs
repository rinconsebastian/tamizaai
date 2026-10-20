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
}
