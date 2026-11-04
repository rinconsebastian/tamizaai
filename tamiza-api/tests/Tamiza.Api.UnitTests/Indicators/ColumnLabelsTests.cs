using System.Text.Json;
using System.Text.Json.Nodes;
using Tamiza.Api.Indicators;
using Tamiza.Api.Reports;
using Tamiza.Api.UnitTests.Assistant;

namespace Tamiza.Api.UnitTests.Indicators;

/// <summary>
/// The name shown for a column and what a widget plots, against the cases tamiza-ui's <c>columnLabels</c> and <c>plotted</c> (in
/// <c>indicators/format.ts</c>) are tested with (design D6 of the indicator column metadata, and D3 and D13 of the widget context for the
/// caption cases). The file is worked out by hand, so the two implementations are checked against the same truth. The tests do nothing when
/// the UI is not next to the API.
/// </summary>
public sealed class ColumnLabelsTests
{
    private const string FixtureFile = "tamiza-ui/src/app/pages/projects/indicators/testing/column-labels.json";

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

    private static (Func<string, string> Names, JsonNode Case) Namer(int number)
    {
        var node = Cases()![number]!;
        var indicator = node["indicator"]?.Deserialize<Dictionary<string, IndicatorColumnMetadata?>>(Web);
        var labels = node["display"]?["labels"]?.Deserialize<Dictionary<string, string>>(Web);
        return (ColumnLabels.For(ColumnMetadata.Normalize(indicator), labels), node);
    }

    [Fact]
    public void The_fixture_is_there_and_has_the_cases_the_requirements_name()
    {
        var cases = Cases();
        if (cases is null)
        {
            return;
        }

        var names = cases.Select(c => c!["name"]!.GetValue<string>()).ToList();
        foreach (var required in new[] { "Widget label over the indicator's", "Indicator label used", "Column's own name", "Default measure", "Own caption text over the label and the names", "Hidden caption keeps what is plotted" })
        {
            Assert.Contains(required, names);
        }
    }

    [Theory]
    [MemberData(nameof(CaseNumbers))]
    public void A_column_is_named_by_the_widget_then_the_indicator_then_itself(int number)
    {
        var (names, node) = Namer(number);

        foreach (var (column, expected) in node["names"]!.AsObject())
        {
            Assert.Equal(expected!.GetValue<string>(), names(column));
        }
    }

    [Theory]
    [MemberData(nameof(CaseNumbers))]
    public void What_a_widget_plots_is_the_label_with_the_names_of_its_columns(int number)
    {
        var (names, node) = Namer(number);
        // The case's whole display travels with the config, so a case may carry a caption as well as labels (design D3 of the widget context).
        var config = ((node["config"]!.Deserialize<WidgetConfigInput>(Web) ?? new WidgetConfigInput())
            with { Display = node["display"]?.Deserialize<WidgetDisplay>(Web) }).Normalize();

        Assert.Equal(node["plotted"]!.GetValue<string>(), ReportDataBuilder.Plotted(config, node["label"]!.GetValue<string>(), names));
    }

    [Fact]
    public void Without_a_namer_the_columns_are_written_as_they_are()
    {
        var config = new WidgetConfigInput("teatro_hoy", Ratio: new WidgetRatio("numerator", "denominator")).Normalize();

        Assert.Equal("Teatro hoy: numerator / denominator", ReportDataBuilder.Plotted(config, "Teatro hoy"));
        Assert.Equal("Teatro hoy", ReportDataBuilder.Plotted(new WidgetConfigInput("teatro_hoy").Normalize(), "Teatro hoy"));
    }

    [Fact]
    public void A_widget_that_names_no_measure_reads_as_the_label_alone_whatever_the_labels_are()
    {
        var names = ColumnLabels.For(
            new Dictionary<string, IndicatorColumnMetadata> { ["value"] = new("Personas") }, new Dictionary<string, string> { ["value"] = "Casos" });

        Assert.Equal("Quién respondió", ReportDataBuilder.Plotted(new WidgetConfigInput("quien_respondio").Normalize(), "Quién respondió", names));
        Assert.Equal("Casos", names("value"));
    }
}
