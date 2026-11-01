using System.Text.RegularExpressions;
using Tamiza.Api.CleanData;
using Tamiza.Api.Http;
using Tamiza.Api.Indicators;
using Tamiza.Api.UnitTests.Assistant;
using Tamiza.DbUp;

namespace Tamiza.Api.UnitTests.Indicators;

/// <summary>The palette and scale ids (dashboard-colors, design D2): the sets the page, the API and the database each hold a copy of.</summary>
public sealed class PalettesTests
{
    private const string ServiceFile = "tamiza-ui/src/app/core/projects/indicators.service.ts";
    private const string ColorsFile = "tamiza-ui/src/app/pages/projects/indicators/chart-colors.ts";
    private const string DialogFile = "tamiza-ui/src/app/pages/projects/indicators/dashboard-colors-dialog.ts";

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

    // The page's copy of the catalog and of the limits. A page that offers a palette the API refuses, or a limit the API does not
    // hold, fails in the browser; these tests read the page's sources the way PromptKitTests reads its templates.

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

    /// <summary>The quoted ids of a list or a union in the page: <c>['a', 'b']</c> or <c>'a' | 'b'</c>.</summary>
    private static string[] QuotedIds(string text) => [.. Regex.Matches(text, "'([^']+)'").Select(m => m.Groups[1].Value)];

    private static string Declaration(string source, string pattern)
    {
        var match = Regex.Match(source, pattern, RegexOptions.Singleline);
        Assert.True(match.Success, $"The page no longer declares {pattern}.");
        return match.Groups[1].Value;
    }

    private static int Number(string source, string name) => int.Parse(Declaration(source, $@"\b{name}\s*=\s*(\d+);"), System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void The_page_offers_the_same_palettes_and_scales_as_the_api()
    {
        if (PageSource(ServiceFile) is not { } service)
        {
            return;
        }

        // The union types and the lists of the same ids, which the page keeps apart, and the order the pickers show them in.
        Assert.Equal(Palettes.All, QuotedIds(Declaration(service, @"export const paletteIds[^=]*=\s*\[([^\]]*)\]")));
        Assert.Equal(Palettes.All, QuotedIds(Declaration(service, @"export type PaletteId\s*=\s*([^;]*);")));
        Assert.Equal(Palettes.Scales, QuotedIds(Declaration(service, @"export const scaleIds[^=]*=\s*\[([^\]]*)\]")));
        Assert.Equal(Palettes.Scales, QuotedIds(Declaration(service, @"export type ScaleId\s*=\s*([^;]*);")));
    }

    [Fact]
    public void The_page_draws_each_palettes_default_scale_as_the_api_names_it()
    {
        if (PageSource(ColorsFile) is not { } colors)
        {
            return;
        }

        var body = Declaration(colors, @"export function defaultScale\([^)]*\)[^{]*\{(.*?)\n\}");
        var onPage = Regex.Matches(body, @"case '([^']+)':\s*return '([^']+)';")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.Ordinal);

        Assert.Equal(Palettes.All.Count, onPage.Count);
        foreach (var palette in Palettes.All)
        {
            Assert.True(onPage.TryGetValue(palette, out var scale), $"The page names no default scale for {palette}.");
            Assert.Equal(Palettes.DefaultScale(palette), scale);
        }
    }

    [Fact]
    public void The_page_holds_the_limits_the_api_validates_8_colors_64_kept_by_value_and_500_characters()
    {
        if (PageSource(ServiceFile) is not { } service || PageSource(DialogFile) is not { } dialog)
        {
            return;
        }

        var paletteSize = Number(service, "paletteSize");
        var maxValueColors = Number(service, "maxValueColors");
        var maxValueLength = Number(dialog, "maxValueLength");

        Assert.Equal(Palettes.ColorCount, paletteSize);
        Assert.Equal(Palettes.MaxValueColors, maxValueColors);
        Assert.Equal(DataQueryBuilder.MaxTextLength, maxValueLength);

        // And they are the numbers where the validation turns an entry away, not only the constants it names.
        bool Accepts(IReadOnlyList<ValueColorInput?> entries) => DashboardValidation.Colors(Palettes.Tamiza, entries, new FieldErrors()) is not null;
        ValueColorInput Entry(int i, string value, int color) => new($"column_{i}", value, color);
        Assert.True(Accepts([.. Enumerable.Range(0, maxValueColors).Select(i => Entry(i, "v", 1))]));
        Assert.False(Accepts([.. Enumerable.Range(0, maxValueColors + 1).Select(i => Entry(i, "v", 1))]));
        Assert.True(Accepts([Entry(0, new string('v', maxValueLength), paletteSize)]));
        Assert.False(Accepts([Entry(0, new string('v', maxValueLength + 1), 1)]));
        Assert.False(Accepts([Entry(0, "v", paletteSize + 1)]));
    }
}
