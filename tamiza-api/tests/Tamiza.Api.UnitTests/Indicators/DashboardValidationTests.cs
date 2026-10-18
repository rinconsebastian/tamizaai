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
}
