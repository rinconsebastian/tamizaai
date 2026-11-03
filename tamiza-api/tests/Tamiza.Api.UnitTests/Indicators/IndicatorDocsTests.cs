using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using Tamiza.Api.Http;
using Tamiza.Api.Indicators;
using Tamiza.Api.Mcp;
using Tamiza.Api.UnitTests.Assistant;

namespace Tamiza.Api.UnitTests.Indicators;

/// <summary>The dashboard documentation says every kind of widget there is. The tests do nothing when the docs folder is not next to the API.</summary>
public sealed class IndicatorDocsTests
{
    private const string DashboardPage = "tamiza-ui/src/app/pages/projects/project-indicators.ts";
    private const string EnglishTexts = "tamiza-ui/public/i18n/en.json";
    private const string WidgetGridPage = "tamiza-ui/src/app/pages/projects/indicators/widget-grid.ts";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

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
        [ChartTypes.Boxplot] = "Box plot",
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

        Assert.Contains("Changing the `chartType` of a widget that has a color, a scale or an orientation also needs a `config` without the `display` fields the new type does not take", text, StringComparison.Ordinal);
        Assert.Contains("a new `chartType` alone keeps the old `config`", text, StringComparison.Ordinal);
        Assert.Contains("`config.display.color`, `config.display.ramp` or `config.display.orientation`", text, StringComparison.Ordinal);
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
    public void The_mcp_documentation_says_what_exact_matching_does_not_catch_and_what_it_ignores()
    {
        if (PromptKitChecks.FindUp("docs/mcp.md") is not { } mcp)
        {
            return;
        }

        var text = File.ReadAllText(mcp);

        // An empty scale is none, while an empty palette is no palette.
        Assert.Null(new WidgetDisplay(Ramp: "").Normalize());
        Assert.False(Palettes.IsKnown(""));
        Assert.Contains("An empty `display.ramp` is read as not set, but an empty `palette` is refused", text, StringComparison.Ordinal);

        // A column is checked for its shape and never against the tables, so a column no table has is kept.
        Assert.NotNull(DashboardValidation.Colors(Palettes.Tamiza, [new ValueColorInput("zzz_unknown_9", "Female", 5)], new FieldErrors()));
        Assert.Contains("a well-formed name that no indicator table publishes is saved and colors nothing: a misspelled column is not caught", text, StringComparison.Ordinal);

        // A fraction cannot be read as a color, so the argument is malformed before any rule names the color.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ValueColorInput>("{\"column\":\"sex\",\"value\":\"Female\",\"color\":3.5}", Json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WidgetDisplay>("{\"color\":3.5}", Json));
        Assert.Contains("a fraction such as 3.5 is refused as a malformed argument, on `arguments` and not on the color key", text, StringComparison.Ordinal);
        Assert.Contains("a fraction where a whole number goes (a `color` of 3.5)", text, StringComparison.Ordinal);

        // A key the settings do not have is skipped, so a misspelled one sets nothing.
        Assert.Null(JsonSerializer.Deserialize<WidgetDisplay>("{\"colour\":3}", Json)!.Normalize());
        Assert.Contains("A key that is not listed is ignored, not refused, so check the spelling: `display: { \"colour\": 3 }` sets no color", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_documentation_says_that_a_NUL_character_in_a_kept_value_is_refused_with_the_message_of_the_validation()
    {
        if (PromptKitChecks.FindUp("docs/mcp.md") is not { } mcp || PromptKitChecks.FindUp("docs/indicators-dashboard.md") is not { } dashboard)
        {
            return;
        }

        var errors = new FieldErrors();
        Assert.Null(DashboardValidation.Colors(Palettes.Tamiza, [new ValueColorInput("sex", "Fe\0male", 5)], errors));
        var refused = Assert.Single(errors.ToDictionary());
        var message = Assert.Single(refused.Value);

        Assert.Equal("valueColors[0].value", refused.Key);
        Assert.Contains($"A NUL character in a value is refused on `valueColors[i].value` (\"{message}\")", File.ReadAllText(mcp), StringComparison.Ordinal);
        Assert.Contains($"A value can hold any character but NUL: a NUL character is refused, next to the value, with \"{message}\"", File.ReadAllText(dashboard), StringComparison.Ordinal);
    }

    [Fact]
    public void The_mcp_documentation_gives_the_chart_type_sentence_to_update_widget_only_as_the_tool_descriptions_do()
    {
        if (PromptKitChecks.FindUp("docs/mcp.md") is not { } mcp)
        {
            return;
        }

        var text = File.ReadAllText(mcp);

        // A widget that is created has no earlier config to keep, so only the change of one carries the sentence.
        Assert.DoesNotContain("To change the chart type", Description("create_widget"), StringComparison.Ordinal);
        Assert.Contains("To change the chart type of a widget that has a color, a scale or an orientation", Description("update_widget"), StringComparison.Ordinal);
        Assert.Contains("Only `update_widget` changes the `chartType` of a widget.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("`create_widget` changes the `chartType`", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_mcp_documentation_says_a_dashboard_is_versioned_for_its_content_and_not_for_its_place()
    {
        if (PromptKitChecks.FindUp("docs/mcp.md") is not { } mcp)
        {
            return;
        }

        var text = File.ReadAllText(mcp);

        // No kind of change records a place, so moving a dashboard among the project's makes no version.
        Assert.DoesNotContain(DashboardChangeKinds.All, kind => kind.Contains("position", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Every change to a dashboard's name, filters, colors or widgets is recorded as a version", text, StringComparison.Ordinal);
        Assert.Contains("`update_dashboard` with only a `position` makes no version", text, StringComparison.Ordinal);
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
    public void The_mcp_documentation_lists_the_limit_and_version_refusals_that_keep_the_pages_code_and_give_a_status()
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

    // The descriptions are attribute strings, which cannot read the constant: this keeps the number in them the one the API enforces.
    [Theory]
    [InlineData("list_dashboards", "(1 to {0})")]
    [InlineData("create_dashboard", "at most {0},")]
    [InlineData("duplicate_dashboard", "Fails at {0} dashboards.")]
    public void The_dashboard_tools_say_the_most_dashboards_a_project_can_have(string tool, string phrase)
    {
        Assert.Contains(string.Format(phrase, IndicatorDashboardOptions.MaxDashboards), Description(tool), StringComparison.Ordinal);
    }

    [Fact]
    public void The_mcp_documentation_lists_the_refusal_of_a_dashboard_the_project_does_not_have_for_every_tool_that_names_a_dashboard()
    {
        if (PromptKitChecks.FindUp("docs/mcp.md") is not { } mcp)
        {
            return;
        }

        var text = File.ReadAllText(mcp);

        Assert.Contains($"| A tool that names a dashboard says `{DashboardEndpoints.NotFoundCode}` |", text, StringComparison.Ordinal);
        var row = Assert.Single(text.Split('\n'), line => line.Contains($"says `{DashboardEndpoints.NotFoundCode}`", StringComparison.Ordinal));
        Assert.Contains($"(`status` {StatusCodes.Status404NotFound})", row, StringComparison.Ordinal);

        // Every tool that takes a dashboard is in the row, so one added later is not left out, and the row names no tool that is not there.
        var tools = Tools();
        var takingADashboard = tools.Where(t => t.Value.GetParameters().Any(p => p.Name == "dashboardId")).Select(t => t.Key).ToList();
        Assert.NotEmpty(takingADashboard);
        var missing = takingADashboard.Where(name => !row.Contains($"`{name}`", StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0, $"The dashboards.notFound row of docs/mcp.md does not name: {string.Join(", ", missing)}");
        var unknown = Regex.Matches(row, "`([a-z]+_[a-z_]+)`").Select(m => m.Groups[1].Value).Where(name => !tools.ContainsKey(name)).ToList();
        Assert.True(unknown.Count == 0, $"The dashboards.notFound row of docs/mcp.md names tools that do not exist: {string.Join(", ", unknown)}");
    }

    [Fact]
    public void The_dashboard_documentation_lists_the_actions_of_the_dashboard_menu_in_the_order_the_page_has_them()
    {
        if (PromptKitChecks.FindUp("docs/indicators-dashboard.md") is not { } dashboard || PageSource(DashboardPage) is not { } page || PageSource(EnglishTexts) is not { } english)
        {
            return;
        }

        // The keys in the order the page builds its menu, and the texts it shows for them.
        var keys = Regex.Matches(page, @"label: t\('dashboards\.menu\.(\w+)'\)").Select(m => m.Groups[1].Value).ToList();
        using var texts = JsonDocument.Parse(english);
        var menu = texts.RootElement.GetProperty("dashboards").GetProperty("menu");
        var labels = keys.Select(key => menu.GetProperty(key).GetString()!).ToList();

        var text = File.ReadAllText(dashboard);
        const string opening = "**Dashboard actions** menu (the button with three dots), with ";
        var start = text.IndexOf(opening, StringComparison.Ordinal);
        Assert.True(start >= 0, "docs/indicators-dashboard.md no longer introduces the actions of the dashboard menu.");
        var sentence = text[(start + opening.Length)..].Split(';')[0];
        var listed = Regex.Matches(sentence, @"\*\*([^*]+)\*\*").Select(m => m.Groups[1].Value).ToList();

        Assert.Contains("Colors", labels);
        Assert.Equal(labels, listed);
    }

    [Fact]
    public void The_dashboard_documentation_lists_the_items_of_the_widget_menu_in_the_order_the_grid_has_them()
    {
        if (PromptKitChecks.FindUp("docs/indicators-dashboard.md") is not { } dashboard || PageSource(WidgetGridPage) is not { } grid || PageSource(EnglishTexts) is not { } english)
        {
            return;
        }

        // The text each item shows, and the items of each group, in the order the grid names them.
        using var texts = JsonDocument.Parse(english);
        var indicators = texts.RootElement.GetProperty("indicators");
        string Text(string section, string key) => indicators.GetProperty(section).GetProperty(key).GetString()!;
        var groups = new (string Id, (string TestId, string Label)[] Items)[]
        {
            ("view", [("widget-show-table", Text("widget", "showTable"))]),
            ("export", [("widget-export-png", Text("widgetExport", "png")), ("widget-export-svg", Text("widgetExport", "svg")), ("widget-export-xlsx", Text("widgetExport", "xlsx"))]),
            ("edit", [("widget-edit", Text("grid", "editItem")), ("widget-remove", Text("grid", "removeItem"))]),
        };
        var methodStart = grid.IndexOf("protected menuGroups(", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "widget-grid.ts no longer builds its menu in menuGroups: update this test and the documentation.");
        var method = grid[methodStart..grid.IndexOf("\n  }\n", methodStart, StringComparison.Ordinal)];
        foreach (var (id, items) in groups)
        {
            Assert.True(method.Contains($"id: '{id}'", StringComparison.Ordinal), $"The widget menu no longer has the group {id}: update this test and the documentation.");
            foreach (var (testId, _) in items)
            {
                Assert.True(grid.Contains($"'{testId}'", StringComparison.Ordinal), $"widget-grid.ts no longer has the item {testId}: update this test and the documentation.");
            }
        }

        // The groups in the order menuGroups pushes them, each with its items in the order the grid names them.
        var inTheGrid = groups
            .OrderBy(group => method.IndexOf($"id: '{group.Id}'", StringComparison.Ordinal))
            .SelectMany(group => group.Items.OrderBy(item => grid.IndexOf($"'{item.TestId}'", StringComparison.Ordinal)))
            .Select(item => item.Label)
            .ToList();

        var text = File.ReadAllText(dashboard);
        const string opening = "**widget menu** (the button with three dots at the right of its title), with ";
        var start = text.IndexOf(opening, StringComparison.Ordinal);
        Assert.True(start >= 0, "docs/indicators-dashboard.md no longer introduces the items of the widget menu.");
        var sentence = text[(start + opening.Length)..].Split(';')[0];
        var exportGroup = Text("grid", "exportGroup");
        var listed = Regex.Matches(sentence, @"\*\*([^*]+)\*\*").Select(m => m.Groups[1].Value).ToList();

        // The heading of the Export group stands between Show as table and the formats.
        Assert.Equal(inTheGrid[0], listed[0]);
        Assert.Equal(exportGroup, listed[1]);
        Assert.Equal(inTheGrid, listed.Where(item => item != exportGroup).ToList());
    }

    [Fact]
    public void The_dashboard_documentation_names_the_colors_of_each_palette_and_its_scale_as_the_page_does()
    {
        if (PromptKitChecks.FindUp("docs/indicators-dashboard.md") is not { } dashboard || PageSource(EnglishTexts) is not { } english)
        {
            return;
        }

        var text = File.ReadAllText(dashboard);
        using var texts = JsonDocument.Parse(english);
        var colors = texts.RootElement.GetProperty("colors");
        string Hue(string palette, int place) => colors.GetProperty("hues").GetProperty(palette).GetProperty(place.ToString(CultureInfo.InvariantCulture)).GetString()!;

        foreach (var palette in Palettes.All)
        {
            var names = Enumerable.Range(1, Palettes.ColorCount).Select(place => Hue(palette, place));
            var scale = colors.GetProperty("scales").GetProperty(Palettes.DefaultScale(palette)).GetString();
            Assert.Contains($"| {string.Join(", ", names)} | {scale} |", text, StringComparison.Ordinal);
        }

        // The example of what a screen reader says is the third color of the palette every dashboard starts with.
        Assert.Contains($"(\"Color 3, {Hue(Palettes.Tamiza, 3)}\")", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dashboard_documentation_says_a_copy_takes_the_colors_and_who_sets_them()
    {
        if (PromptKitChecks.FindUp("docs/indicators-dashboard.md") is not { } dashboard)
        {
            return;
        }

        var text = File.ReadAllText(dashboard);

        Assert.Contains("with the same title, type, query, ignored filters, color or scale, position and size", text, StringComparison.Ordinal);
        Assert.Contains("and it has the same palette and colors kept by value (see [Colors](#colors))", text, StringComparison.Ordinal);
        Assert.Contains("or changing its colors, leaves the other as it is", text, StringComparison.Ordinal);
        Assert.Contains("choose their palette and the colors kept by value (and, in the widget form, a widget's color or scale)", text, StringComparison.Ordinal);
        Assert.Contains("in the colors chosen, but none of these actions, and the API refuses their requests (403)", text, StringComparison.Ordinal);
        Assert.Contains("(renaming, moving, duplicating or removing it, or saving its colors)", text, StringComparison.Ordinal);
    }

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

    /// <summary>The MCP tools by name, as the server registers them.</summary>
    private static Dictionary<string, MethodInfo> Tools() =>
        typeof(McpRefusal).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Select(method => (Name: method.GetCustomAttribute<McpServerToolAttribute>()?.Name, Method: method))
            .Where(tool => tool.Name is not null)
            .ToDictionary(tool => tool.Name!, tool => tool.Method);

    private static string Description(string tool) => Tools()[tool].GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
}
