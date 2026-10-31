using Tamiza.Api.Indicators;
using Tamiza.Api.UnitTests.Assistant;

namespace Tamiza.Api.UnitTests.Indicators;

/// <summary>The dashboard documentation says every kind of widget there is. The tests do nothing when the docs folder is not next to the API.</summary>
public sealed class IndicatorDocsTests
{
    /// <summary>How the widget form and the documentation call each kind.</summary>
    private static readonly Dictionary<string, string> Names = new()
    {
        [ChartTypes.Stat] = "Stat tile",
        [ChartTypes.Bar] = "Bar chart",
        [ChartTypes.Column] = "Column chart",
        [ChartTypes.Line] = "Line chart",
        [ChartTypes.Histogram] = "Histogram",
        [ChartTypes.Pie] = "Pie chart",
        [ChartTypes.Map] = "Map",
        [ChartTypes.Heatmap] = "Heat map",
        [ChartTypes.Pyramid] = "Population pyramid",
        [ChartTypes.Likert] = "Likert bars",
        [ChartTypes.Crosstab] = "Cross table",
    };

    [Fact]
    public void Every_chart_type_has_a_name_here()
    {
        Assert.Equal(ChartTypes.All.Order(), Names.Keys.Order());
    }

    [Fact]
    public void Every_chart_type_is_named_in_the_dashboard_documentation()
    {
        if (PromptKitChecks.FindUp("docs/indicators-dashboard.md") is not { } path)
        {
            return;
        }

        var text = File.ReadAllText(path);
        var missing = Names.Where(n => !text.Contains($"**{n.Value}**", StringComparison.Ordinal)).Select(n => n.Value).ToList();

        Assert.True(missing.Count == 0, $"docs/indicators-dashboard.md does not name: {string.Join(", ", missing)}");
    }

    [Fact]
    public void The_series_settings_are_explained_in_the_documentation_and_the_mcp_guide()
    {
        if (PromptKitChecks.FindUp("docs/indicators-dashboard.md") is not { } dashboard || PromptKitChecks.FindUp("docs/mcp.md") is not { } mcp)
        {
            return;
        }

        var dashboardText = File.ReadAllText(dashboard);
        var mcpText = File.ReadAllText(mcp);

        foreach (var layout in SeriesLayouts.All.Where(l => l != SeriesLayouts.Percent))
        {
            Assert.Contains($"**{char.ToUpperInvariant(layout[0])}{layout[1..]}**", dashboardText, StringComparison.Ordinal);
        }

        foreach (var setting in new[] { "seriesBy", "layout", "pyramid", "scale" })
        {
            Assert.Contains($"`{setting}`", mcpText, StringComparison.Ordinal);
        }
    }
}
