using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tamiza.Api.Indicators;
using Tamiza.Api.Reports;
using Tamiza.Api.UnitTests.Assistant;

namespace Tamiza.Api.UnitTests.Indicators;

/// <summary>
/// The base figures of a widget (settings in force, small-base rule, tooltip figures, n line, no-data line and the notes about small
/// bases), against the cases tamiza-ui's <c>base-figures.ts</c> is tested with. The file is worked out by hand, so the two implementations
/// are checked against the same truth, in English and in Spanish. The tests do nothing when the UI is not next to the API.
/// </summary>
public sealed class BaseFiguresTests
{
    private const string FixtureFile = "tamiza-ui/src/app/pages/projects/indicators/testing/base-figures.json";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyDictionary<string, ReportFormat> Formats = new Dictionary<string, ReportFormat>
    {
        ["en"] = new(CultureInfo.GetCultureInfo("en-US"), TimeZoneInfo.Utc),
        ["es"] = new(CultureInfo.GetCultureInfo("es-CO"), TimeZoneInfo.Utc),
    };

    private static JsonObject? Fixture()
    {
        var path = PromptKitChecks.FindUp(FixtureFile);
        return path is null ? null : JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }

    private static JsonArray Cases(string section) => Fixture()![section]!.AsArray();

    private static TheoryData<int> Numbers(string section)
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < (Fixture()?[section]?.AsArray().Count ?? 0); i++)
        {
            data.Add(i);
        }

        return data;
    }

    public static TheoryData<int> Settings() => Numbers("settings");

    public static TheoryData<int> Smalls() => Numbers("small");

    public static TheoryData<int> Tooltips() => Numbers("tooltips");

    public static TheoryData<int> NLines() => Numbers("nLines");

    public static TheoryData<int> NoDataLines() => Numbers("noDataLines");

    public static TheoryData<int> Sentences() => Numbers("sentences");

    private static double? Number(JsonNode? node) => node is null ? null : node.GetValue<double>();

    private static int? Whole(JsonNode? node) => node is null ? null : node.GetValue<int>();

    private static string? Text(JsonNode? node) => node?.GetValue<string>();

    [Fact]
    public void The_fixture_has_the_cases_the_design_lists()
    {
        if (Fixture() is not { } fixture)
        {
            return;
        }

        string[] Names(string section) => Cases(section).Select(c => c!["name"]!.GetValue<string>()).ToArray();
        Assert.Contains("Comunas below 30", Names("settings"));
        Assert.Contains("Turned off for a widget", Names("settings"));
        Assert.Contains("Own threshold", Names("settings"));
        Assert.Contains("Value with no n", Names("small"));
        Assert.Contains("Figures of a percent indicator", Names("tooltips"));
        Assert.Contains("Rate of the left-out rows", Names("noDataLines"));
        Assert.Contains("Share of a count", Names("noDataLines"));
        Assert.Equal(["en", "es"], fixture["locales"]!.AsObject().Select(l => l.Key));
        foreach (var language in new[] { "en", "es" })
        {
            Assert.Equal(CultureInfo.GetCultureInfo(fixture["locales"]![language]!.GetValue<string>()), Formats[language].Culture);
        }
    }

    [Theory]
    [MemberData(nameof(Settings))]
    public void The_settings_in_force_are_the_widgets_over_the_dashboards(int index)
    {
        if (Fixture() is null)
        {
            return;
        }

        var c = Cases("settings")[index]!;
        var display = c["display"] is { } own ? new WidgetDisplay(MinN: Whole(own["minN"]), ShowN: own["showN"]?.GetValue<bool>()) : null;
        var dashboard = c["dashboard"];

        var inForce = BaseFigures.InForce(display, Whole(dashboard?["minN"]), dashboard?["showN"]?.GetValue<bool>() ?? false);

        Assert.Equal(new BaseSettings(Whole(c["expected"]!["minN"]), c["expected"]!["showN"]!.GetValue<bool>()), inForce);
    }

    [Theory]
    [MemberData(nameof(Smalls))]
    public void A_small_base_is_an_n_below_the_threshold_and_a_value_with_no_n_never_has_one(int index)
    {
        if (Fixture() is null)
        {
            return;
        }

        var c = Cases("small")[index]!;

        Assert.Equal(c["expected"]!.GetValue<bool>(), BaseFigures.Small(Number(c["n"]), Whole(c["minN"])));
    }

    [Theory]
    [MemberData(nameof(Tooltips))]
    public void A_tooltip_gives_the_figures_after_its_value_under_their_names_in_both_languages(int index)
    {
        if (Fixture() is null)
        {
            return;
        }

        var c = Cases("tooltips")[index]!;
        var labels = c["columns"]!.AsObject().ToDictionary(p => p.Key, p => p.Value!.GetValue<string>());
        var ratio = c["ratio"]!.GetValue<bool>() ? new WidgetRatio("numerator", "denominator") : null;
        var names = BaseFigures.Names(ratio, column => labels.GetValueOrDefault(column, column), Text(c["baseColumn"]));
        var figures = c["figures"]!;

        foreach (var language in new[] { "en", "es" })
        {
            var tooltip = BaseFigures.Tooltip(Number(figures["numerator"]), Number(figures["denominator"]), Number(figures["n"]), names, Whole(c["minN"]), Formats[language]);

            var expected = c[language]!;
            Assert.Equal(
                expected["lines"]!.AsArray().Select(line => (line![0]!.GetValue<string>(), line[1]!.GetValue<string>())),
                tooltip.Lines);
            Assert.Equal(Text(expected["footer"]), tooltip.Footer);
        }
    }

    [Fact]
    public void A_figure_left_out_because_the_caller_shows_it_is_not_written()
    {
        var names = BaseFigures.Names(new WidgetRatio("registros", "meta"), column => column, null);

        var tooltip = BaseFigures.Tooltip(9, 20, 20, names, null, Formats["en"], omitNumerator: true, omitDenominator: true);

        Assert.Empty(tooltip.Lines);
        Assert.Equal([("meta", "20")], BaseFigures.Tooltip(9, 20, 20, names, null, Formats["en"], omitNumerator: true).Lines);
    }

    [Theory]
    [MemberData(nameof(NLines))]
    public void The_n_line_reads_n_the_figure_and_the_name_of_the_column_it_counts(int index)
    {
        if (Fixture() is null)
        {
            return;
        }

        var c = Cases("nLines")[index]!;

        foreach (var language in new[] { "en", "es" })
        {
            Assert.Equal(Text(c[language]), BaseFigures.NLine(Number(c["n"]), Text(c["label"]), Formats[language]));
        }
    }

    [Theory]
    [MemberData(nameof(NoDataLines))]
    public void The_no_data_line_names_what_was_left_out_in_the_widgets_format_in_both_languages(int index)
    {
        if (Fixture() is null)
        {
            return;
        }

        var c = Cases("noDataLines")[index]!;
        var noData = c["noData"] is { } held ? held.Deserialize<IndicatorNoData>(Web) : null;

        foreach (var language in new[] { "en", "es" })
        {
            var format = Formats[language];
            var line = BaseFigures.NoDataLine(noData, value => format.Value(value, Text(c["format"]), Text(c["unit"])), c["showN"]!.GetValue<bool>(), format);

            Assert.Equal(Text(c[language]), line);
        }
    }

    [Theory]
    [MemberData(nameof(Sentences))]
    public void The_notes_about_small_bases_are_written_in_both_languages(int index)
    {
        if (Fixture() is null)
        {
            return;
        }

        var c = Cases("sentences")[index]!;
        var args = c["args"]!;

        foreach (var language in new[] { "en", "es" })
        {
            var format = Formats[language];
            string? written = c["kind"]!.GetValue<string>() switch
            {
                "smallNote" => BaseFigures.SmallNote(Whole(args["minN"])!.Value, format),
                "tableNote" => BaseFigures.SmallTableNote(Whole(args["minN"])!.Value, format),
                "summaryCount" => BaseFigures.SmallSummary(Whole(args["count"])!.Value, format),
                "smallTooltip" => BaseFigures.SmallTooltip(Number(args["n"]), Whole(args["minN"]), format),
                "smallSome" => BaseFigures.SmallSome(Whole(args["minN"])!.Value, format),
                "seriesValueWithN" => BaseFigures.SeriesValueWithN(Text(args["value"])!, Number(args["n"]), format),
                "histogramRows" => BaseFigures.HistogramRows(format),
                "emailNote" => BaseFigures.SmallValuesNote(Whole(args["count"])!.Value, Whole(args["minN"])!.Value, format),
                var other => throw new InvalidOperationException($"The fixture has no sentence of the kind '{other}'."),
            };

            Assert.Equal(Text(c[language]), written);
        }
    }

    [Fact]
    public void The_figures_of_a_percent_indicator_are_counts_and_never_a_percentage_or_with_a_unit()
    {
        var english = Formats["en"];

        // 56 of 121 households: the indicator's format and unit are for the plotted value only.
        Assert.Equal("5,600% de los hogares", english.Value(56, ReportFormat.Percent, "de los hogares"));
        Assert.Equal(["56", "121"], new[] { english.Figure(56), english.Figure(121) });
        Assert.Equal("0.5", english.Figure(0.5));
        Assert.Equal("12.450", Formats["es"].Figure(12450));
        Assert.Equal("0,13", Formats["es"].Figure(0.125));
    }

    [Fact]
    public void Both_languages_have_the_same_words()
    {
        var (english, spanish) = BaseFigures.Keys;

        Assert.Equal(english.Order(StringComparer.Ordinal), spanish.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_language_the_server_does_not_translate_reads_as_english()
    {
        var portuguese = new ReportFormat(CultureInfo.GetCultureInfo("pt-BR"), TimeZoneInfo.Utc);

        Assert.Equal("Marks with a base below 30 are dimmed.", BaseFigures.SmallNote(30, portuguese));
    }

    [Fact]
    public void The_no_data_line_of_a_widget_that_shows_no_n_leaves_the_n_out_and_a_threshold_of_zero_is_off()
    {
        var noData = new IndicatorNoData(["Sin dato"], 0.5, N: 12);
        var english = Formats["en"];

        Assert.Equal("Sin dato: 0.5", BaseFigures.NoDataLine(noData, value => english.Figure(value), false, english));
        Assert.Equal("Sin dato: 0.5 · n = 12", BaseFigures.NoDataLine(noData, value => english.Figure(value), true, english));
        Assert.False(BaseFigures.Small(5, 0));
        Assert.Equal(new BaseSettings(null, false), BaseFigures.InForce(new WidgetDisplay(MinN: 0), 30, false));
    }
}
