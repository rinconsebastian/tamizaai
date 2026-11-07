using System.Text.Json.Nodes;
using Tamiza.Api.Indicators;
using Tamiza.Api.UnitTests.Assistant;

namespace Tamiza.Api.UnitTests.Indicators;

/// <summary>
/// Whether a widget's own caption text is out of sight under its subtitle and shown in its foot (design D2 and D11 of
/// improve-widget-text-hierarchy), against the cases tamiza-ui's <c>captionInFoot</c> is tested with. The file is worked out by hand,
/// so the two implementations are checked against the same truth. The tests do nothing when the UI is not next to the API.
/// </summary>
public sealed class WidgetCaptionTests
{
    private const string FixtureFile = "tamiza-ui/src/app/pages/projects/indicators/testing/caption-in-foot.json";

    private static JsonArray? Cases()
    {
        var path = PromptKitChecks.FindUp(FixtureFile);
        return path is null ? null : JsonNode.Parse(File.ReadAllText(path))?["cases"]?.AsArray();
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

    /// <summary>The widget the case describes, as the API stores it.</summary>
    private static WidgetConfig ConfigOf(JsonNode testCase)
    {
        var config = new JsonObject
        {
            ["indicator"] = "encuesta_hoy",
            ["groupBy"] = "zona",
            ["seriesBy"] = testCase["seriesBy"]?.DeepClone(),
            ["display"] = testCase["display"]?.DeepClone(),
        };
        return ProductionShapesTests.Read(config.ToJsonString());
    }

    [Fact]
    public void The_fixture_has_the_cases_the_design_lists()
    {
        if (Cases() is not { } cases)
        {
            return;
        }

        string[] names = [.. cases.Select(c => c!["name"]!.GetValue<string>())];
        Assert.Equal(names.Length, names.Distinct().Count());
        foreach (var named in new[]
                 {
                     "Column chart with a subtitle and an own text", "Pie chart with a subtitle and an own text", "No subtitle", "Default caption line", "Caption hidden",
                     "Caption kept in sight with a text", "Column chart with a series", "Heat map keeps its line", "Stat tile keeps its label", "Histogram keeps its line",
                 })
        {
            Assert.Contains(named, names);
        }

        Assert.Contains(cases, c => c!["inFoot"]!.GetValue<bool>());
        Assert.Contains(cases, c => !c!["inFoot"]!.GetValue<bool>());
    }

    [Theory]
    [MemberData(nameof(CaseNumbers))]
    public void Every_shared_case_says_whether_the_text_is_in_the_foot(int number)
    {
        var testCase = Cases()![number]!;
        var name = testCase["name"]!.GetValue<string>();
        var chartType = testCase["chartType"]!.GetValue<string>();
        var config = ConfigOf(testCase);

        Assert.True(testCase["inFoot"]!.GetValue<bool>() == WidgetCaption.InFoot(chartType, config), name);
        // The text is the trimmed own text exactly when the foot shows it.
        var text = WidgetCaption.TextInFoot(chartType, config);
        Assert.True(testCase["inFoot"]!.GetValue<bool>() == (text is not null), name);
        Assert.True(text is null || text == text.Trim(), name);
    }

    [Fact]
    public void The_text_is_the_trimmed_own_text()
    {
        var config = ProductionShapesTests.Read("""{"indicator":"encuesta_hoy","groupBy":"zona","display":{"subtitle":"Pregunta","caption":{"text":"  % de los hogares  "}}}""");

        Assert.Equal("% de los hogares", WidgetCaption.TextInFoot(ChartTypes.Column, config));
        Assert.Null(WidgetCaption.TextInFoot(ChartTypes.Heatmap, config));
    }
}
