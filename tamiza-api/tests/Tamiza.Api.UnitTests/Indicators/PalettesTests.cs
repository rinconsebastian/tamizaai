using System.Text.RegularExpressions;
using Tamiza.Api.Indicators;
using Tamiza.DbUp;

namespace Tamiza.Api.UnitTests.Indicators;

/// <summary>The palette and scale ids (dashboard-colors, design D2): the sets the page, the API and the database each hold a copy of.</summary>
public sealed class PalettesTests
{
    [Fact]
    public void There_are_four_palettes_and_seven_scales()
    {
        Assert.Equal(["tamiza", "institutional", "earth", "vivid"], Palettes.All);
        Assert.Equal(["blue", "teal", "green", "orange", "red", "purple", "gray"], Palettes.Scales);
        Assert.Equal(8, Palettes.ColorCount);
        Assert.Equal(64, Palettes.MaxValueColors);
    }

    [Fact]
    public void Every_palette_has_a_default_scale_that_is_one_of_the_scales()
    {
        foreach (var palette in Palettes.All)
        {
            Assert.Contains(Palettes.DefaultScale(palette), Palettes.Scales);
        }
    }

    [Theory]
    [InlineData("tamiza", "blue")]
    [InlineData("institutional", "teal")]
    [InlineData("earth", "orange")]
    [InlineData("vivid", "purple")]
    public void Each_palette_has_its_own_default_scale(string palette, string scale) => Assert.Equal(scale, Palettes.DefaultScale(palette));

    [Theory]
    [InlineData("neon")]
    [InlineData("Earth")]
    [InlineData("")]
    public void The_default_scale_of_something_that_is_no_palette_is_a_programming_error(string palette) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Palettes.DefaultScale(palette));

    [Fact]
    public void Palettes_and_scales_are_known_by_their_exact_id()
    {
        Assert.All(Palettes.All, palette => Assert.True(Palettes.IsKnown(palette)));
        Assert.All(Palettes.Scales, scale => Assert.True(Palettes.IsScale(scale)));
        Assert.False(Palettes.IsKnown(null));
        Assert.False(Palettes.IsKnown("Earth"));
        Assert.False(Palettes.IsKnown("#ff0000"));
        Assert.False(Palettes.IsScale(null));
        Assert.False(Palettes.IsScale("Green"));
        Assert.False(Palettes.IsScale("earth")); // a palette is not a scale
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(8, true)]
    [InlineData(0, false)]
    [InlineData(9, false)]
    [InlineData(-1, false)]
    [InlineData(null, false)]
    public void A_color_is_a_number_from_1_to_8(int? color, bool expected) => Assert.Equal(expected, Palettes.IsColor(color));

    [Fact]
    public void The_palettes_are_the_ones_the_check_of_the_database_lists()
    {
        using var reader = new StreamReader(typeof(SchemaMigrator).Assembly.GetManifestResourceStream("0019_add_dashboard_palettes.sql")!);
        var sql = reader.ReadToEnd();

        var check = Regex.Match(sql, @"CONSTRAINT ck_dashboards_palette CHECK \(palette IN \(([^)]*)\)\)");

        Assert.True(check.Success, "The script has no check on the palette.");
        var listed = Regex.Matches(check.Groups[1].Value, "'([^']*)'").Select(m => m.Groups[1].Value);
        Assert.Equal(Palettes.All, listed);
        // The column starts every dashboard on the first palette, which is the one a dashboard saved before palettes has.
        Assert.Contains($"palette text NOT NULL DEFAULT '{Palettes.Tamiza}'", sql);
    }
}
