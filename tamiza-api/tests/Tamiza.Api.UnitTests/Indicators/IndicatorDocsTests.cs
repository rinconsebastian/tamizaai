using Microsoft.AspNetCore.Http;
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

    [Fact]
    public void The_color_settings_are_explained_in_the_mcp_documentation_with_every_palette_and_scale_a_client_can_name()
    {
        if (PromptKitChecks.FindUp("docs/mcp.md") is not { } mcp)
        {
            return;
        }

        var text = File.ReadAllText(mcp);

        foreach (var setting in new[] { "palette", "valueColors", "display", "display.color", "display.ramp", "update_dashboard" })
        {
            Assert.Contains($"`{setting}`", text, StringComparison.Ordinal);
        }

        var missing = Palettes.All.Concat(Palettes.Scales).Where(id => !text.Contains($"`{id}`", StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0, $"docs/mcp.md does not name: {string.Join(", ", missing)}");
    }

    [Fact]
    public void The_mcp_documentation_warns_that_changing_the_chart_type_of_a_widget_with_a_color_or_a_scale_needs_a_config()
    {
        if (PromptKitChecks.FindUp("docs/mcp.md") is not { } mcp)
        {
            return;
        }

        var text = File.ReadAllText(mcp);

        Assert.Contains("Changing the `chartType` of a widget that has a color or a scale also needs a `config` without the `display` fields the new type does not take", text, StringComparison.Ordinal);
        Assert.Contains("a new `chartType` alone keeps the old `config`", text, StringComparison.Ordinal);
        Assert.Contains("`config.display.color` or `config.display.ramp`", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_mcp_documentation_says_that_palette_and_scale_names_and_kept_values_are_matched_exactly()
    {
        if (PromptKitChecks.FindUp("docs/mcp.md") is not { } mcp)
        {
            return;
        }

        var text = File.ReadAllText(mcp);

        // The sentences are true of the validation: a name that is not written exactly as listed is no palette and no scale.
        Assert.False(Palettes.IsKnown("Vivid"));
        Assert.False(Palettes.IsScale("Green"));
        Assert.Contains("**Names are matched exactly.**", text, StringComparison.Ordinal);
        Assert.Contains("`Vivid` or `Green` is refused with `mcp.validation` on `palette` or `config.display.ramp`", text, StringComparison.Ordinal);
        Assert.Contains("`Sex` is refused, on `valueColors[0].column`", text, StringComparison.Ordinal);
        Assert.Contains("`value` is matched as exact text, case and spaces included", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_mcp_documentation_lists_every_kind_of_change_a_version_can_list_and_says_the_page_words_colorsChanged_as_colors_changed()
    {
        if (PromptKitChecks.FindUp("docs/mcp.md") is not { } mcp)
        {
            return;
        }

        var text = File.ReadAllText(mcp);

        // One row for each kind, so a kind added to the code and left out of the documentation fails here.
        var missing = DashboardChangeKinds.All.Where(kind => !text.Contains($"| `{kind}` |", StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0, $"docs/mcp.md does not list the kinds of change: {string.Join(", ", missing)}");
        Assert.Contains("`colorsChanged` as \"Colors changed\"", text, StringComparison.Ordinal);
        Assert.Contains("whose `changes` list `colorsChanged` (the page words it \"Colors changed\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_mcp_documentation_lists_the_two_dashboard_refusals_that_keep_the_pages_code_and_give_a_status()
    {
        if (PromptKitChecks.FindUp("docs/mcp.md") is not { } mcp)
        {
            return;
        }

        var text = File.ReadAllText(mcp);

        Assert.Contains($"`create_dashboard` or `duplicate_dashboard` says `{DashboardEndpoints.LimitCode}`", text, StringComparison.Ordinal);
        Assert.Contains($"The project already has {IndicatorDashboardOptions.MaxDashboards} dashboards, the most it can have (`status` {StatusCodes.Status409Conflict})", text, StringComparison.Ordinal);
        Assert.Contains($"`get_dashboard_version` or `restore_dashboard_version` says `{DashboardEndpoints.VersionNotFoundCode}`", text, StringComparison.Ordinal);
        Assert.Contains($"The dashboard keeps only its {DashboardVersions.Kept} newest versions, and the number asked for is older than those or was never recorded (`status` {StatusCodes.Status404NotFound})", text, StringComparison.Ordinal);
        Assert.Contains("keeps the page's code instead of an `mcp.` one and adds the HTTP `status`", text, StringComparison.Ordinal);
    }
}
