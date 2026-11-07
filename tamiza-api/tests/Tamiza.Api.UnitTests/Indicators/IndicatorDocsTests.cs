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
using Tamiza.Api.Pipeline;
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

        Assert.Contains("Changing the `chartType` of a widget that has a color, a scale, an orientation, reference lines, steps, data labels, an order, no-data values left out, a statistic or lines under its figure also needs a `config` without the `display` fields the new type does not take", text, StringComparison.Ordinal);
        Assert.Contains("a new `chartType` alone keeps the old `config`", text, StringComparison.Ordinal);
        Assert.Contains("`config.display.color`, `config.display.ramp`, `config.display.orientation`, `config.display.referenceLines`, `config.display.interpolation`, `config.display.dataLabels`, `config.display.order`, `config.missingFooter`, `config.statistic` or `config.display.secondary`", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_mcp_documentation_explains_the_chart_annotations_their_refusal_keys_and_the_lines_of_widget_data()
    {
        if (PromptKitChecks.FindUp("docs/mcp.md") is not { } mcp)
        {
            return;
        }

        var text = File.ReadAllText(mcp);

        // The limits written are the ones the API checks.
        Assert.Equal((4, 40), (WidgetDisplay.MaxReferenceLines, WidgetDisplay.MaxReferenceLineLabel));
        Assert.Contains("`display.referenceLines` is up to 4 lines on a bar, column or line, `[{ kind, value, label }]`", text, StringComparison.Ordinal);
        Assert.Contains("`label` is optional, 1 to 40 characters", text, StringComparison.Ordinal);
        foreach (var kind in ReferenceLineKinds.All.Concat(LineInterpolations.All))
        {
            Assert.Contains($"`{kind}`", text, StringComparison.Ordinal);
        }

        foreach (var key in new[] { "referenceLines", "referenceLines[i].kind", "referenceLines[i].value", "referenceLines[i].label", "interpolation", "dataLabels" })
        {
            Assert.Contains($"`config.display.{key}`", text, StringComparison.Ordinal);
        }

        Assert.Contains("`display.dataLabels: true`", text, StringComparison.Ordinal);
        Assert.Contains("over the answers of one question it is only the mean of the bars, not a total", text, StringComparison.Ordinal);
        Assert.Contains("return `referenceLines` for a widget that has lines: `[{ kind, label, value, formatted }]`", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dashboard_documentation_explains_the_reference_lines_with_their_four_kinds_the_caution_about_the_overall_line_and_the_limits()
    {
        if (PromptKitChecks.FindUp("docs/indicators-dashboard.md") is not { } dashboard)
        {
            return;
        }

        var text = File.ReadAllText(dashboard);
        var section = AnnotationsSection(text);

        // One name for each kind the API takes, so a kind added to the code and left out of the documentation fails here.
        var names = new Dictionary<string, string>
        {
            [ReferenceLineKinds.Mean] = "Mean",
            [ReferenceLineKinds.Median] = "Median",
            [ReferenceLineKinds.Overall] = "Overall",
            [ReferenceLineKinds.Value] = "Fixed value",
        };
        Assert.Equal(ReferenceLineKinds.All.Order(), names.Keys.Order());
        foreach (var name in names.Values)
        {
            Assert.Contains($"| **{name}** |", section, StringComparison.Ordinal);
        }

        // The overall line is the figure of a stat tile, needs a ratio, and is only the mean of the bars over the answers of one question.
        Assert.Contains("the figure a stat tile with the same indicator, ratio and filters shows", section, StringComparison.Ordinal);
        Assert.Contains("It needs a ratio because over a measure", section, StringComparison.Ordinal);
        Assert.Contains("**Over the answers of one question, the overall line is not a total.**", section, StringComparison.Ordinal);
        Assert.Contains("they share one base", section, StringComparison.Ordinal);
        Assert.Contains("is only the mean of the bars, and it reads like a total but is not", section, StringComparison.Ordinal);

        // The limits written are the ones the API checks, in the text and in the table of limits.
        Assert.Equal((4, 40), (WidgetDisplay.MaxReferenceLines, WidgetDisplay.MaxReferenceLineLabel));
        Assert.Contains("A widget takes up to 4 lines, each with an optional **label** of up to 40 characters.", section, StringComparison.Ordinal);
        Assert.Contains("| Reference lines in a widget | 4 |", text, StringComparison.Ordinal);
        Assert.Contains("| Length of a reference line's label | 40 characters |", text, StringComparison.Ordinal);

        // The widget types say what a widget can be given, and the section is the one the other documents link to.
        Assert.Contains("(see [Reference lines, steps and data labels](#reference-lines-steps-and-data-labels))", text, StringComparison.Ordinal);
        if (PromptKitChecks.FindUp("docs/mcp.md") is { } mcp)
        {
            Assert.Contains("indicators-dashboard.md#reference-lines-steps-and-data-labels", File.ReadAllText(mcp), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_dashboard_documentation_explains_steps_and_the_data_labels_with_the_kinds_that_take_them_and_the_stacked_refusal()
    {
        if (PromptKitChecks.FindUp("docs/indicators-dashboard.md") is not { } dashboard)
        {
            return;
        }

        var text = File.ReadAllText(dashboard);
        var section = AnnotationsSection(text);

        // Steps: both values the API takes, why a value holds until the next point, and the line chart's row links to the section.
        Assert.Equal(new[] { "linear", "step" }, LineInterpolations.All.Order());
        Assert.Contains("**Steps.**", section, StringComparison.Ordinal);
        Assert.Contains("saved as the widget's `interpolation`, `linear` or `step`", section, StringComparison.Ordinal);
        Assert.Contains("That share holds until the next price on the list", section, StringComparison.Ordinal);
        var lineRow = Assert.Single(text.Split('\n'), row => row.StartsWith("| **Line chart** |", StringComparison.Ordinal));
        Assert.Contains("[steps](#reference-lines-steps-and-data-labels)", lineRow, StringComparison.Ordinal);

        // Data labels: the four kinds, when each is left out, why horizontal bars suit them, and the refusal of a stacked layout.
        foreach (var kind in new[] { "Bar chart", "Column chart", "Line chart", "Pie chart" })
        {
            Assert.Contains($"| **{kind}**", section, StringComparison.Ordinal);
        }

        Assert.Contains("Horizontal bars suit data labels better than grouped columns", section, StringComparison.Ordinal);
        Assert.Contains("is refused (400, with an error on `config.display.dataLabels`); data labels turned off are always accepted", section, StringComparison.Ordinal);
        Assert.Contains("**Not offered** on bars stacked or stacked to 100 %", section, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dashboard_documentation_says_where_the_reference_lines_and_the_data_labels_appear_and_that_they_move_with_the_widget()
    {
        if (PromptKitChecks.FindUp("docs/indicators-dashboard.md") is not { } dashboard)
        {
            return;
        }

        var text = File.ReadAllText(dashboard);
        const string link = "(#reference-lines-steps-and-data-labels)";

        // The texts are on the plot, which takes no room for them, and there is no key (improve-widget-text-hierarchy, design D7).
        Assert.Contains($"The texts of a chart's [reference lines]{link} are drawn on the plot and take no room of their own.", text, StringComparison.Ordinal);
        Assert.Contains("There is **no key under the chart**", text, StringComparison.Ordinal);
        Assert.Contains("the **Reference lines** chip of the widget's [foot](#the-foot-of-a-widget) opens it whole", text, StringComparison.Ordinal);
        // Show as table, images, workbook and paper.
        Assert.Contains($"A bar, column or line chart with [reference lines]{link} lists them under its table", text, StringComparison.Ordinal);
        Assert.Contains($"The legend lists no [reference line]{link}: each is drawn with its text on its box", text, StringComparison.Ordinal);
        Assert.Contains($"what is plotted, its [reference lines]{link} when it has some", text, StringComparison.Ordinal);
        Assert.Contains("The data sheet holds the widget's table, with no row or column for the lines.", text, StringComparison.Ordinal);
        Assert.Contains("A text that did not fit at its line prints as a line of the foot, whole.", text, StringComparison.Ordinal);
        // A copy, a move and a version keep them.
        Assert.Contains($"and so are its [reference lines, steps and data labels]{link}", text, StringComparison.Ordinal);
        Assert.Contains($"and its [reference lines, steps and data labels]{link}, and goes to the left edge", text, StringComparison.Ordinal);
        Assert.Contains($"its [reference lines, steps and data labels]{link} and its own [column names](#column-names)", text, StringComparison.Ordinal);
        Assert.Contains("(with their words, reference lines, steps and data labels) become those of the version", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_mcp_documentation_explains_the_column_labels_the_tools_take_and_return()
    {
        if (PromptKitChecks.FindUp("docs/mcp.md") is not { } mcp)
        {
            return;
        }

        var text = File.ReadAllText(mcp);

        // The limits written are the ones the API checks.
        Assert.Equal((80, 10, 100), (ColumnLabels.MaxLength, ColumnLabels.MaxWidgetLabels, ColumnMetadata.MaxColumns));
        Assert.Contains("each label of 1 to 80 characters with no line break, for at most 100 columns", text, StringComparison.Ordinal);
        Assert.Contains("**Left out, the current version's labels are kept**", text, StringComparison.Ordinal);
        Assert.Contains("`{}` clears them, and given, it replaces them all", text, StringComparison.Ordinal);
        Assert.Contains("`columns.numerator.label`", text, StringComparison.Ordinal);
        Assert.Contains("`display.labels`", text, StringComparison.Ordinal);
        Assert.Contains("`{ column: name }` for up to 10 columns, each of 1 to 80 characters, on every kind of widget", text, StringComparison.Ordinal);
        Assert.Contains("a `config` without `display.labels` removes the widget's own labels", text, StringComparison.Ordinal);
        Assert.Contains("`get_widget_data` and `query_indicator` return `labels` for each widget", text, StringComparison.Ordinal);
        Assert.Contains("`get_indicator_columns` gives each column its `label`", text, StringComparison.Ordinal);
        Assert.Contains("A widget whose columns have none has no `labels`", text, StringComparison.Ordinal);
        Assert.Contains("a column given `{ \"label\": \"Hogares\", \"colour\": \"red\" }` is saved with its label only", text, StringComparison.Ordinal);
        Assert.Contains("(color, scale or column labels) changed", text, StringComparison.Ordinal);
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
        Assert.Contains("To change the chart type of a widget that has a color, a scale, an orientation, reference lines, steps, data labels, an order, no-data values left out, a statistic or lines under its figure", Description("update_widget"), StringComparison.Ordinal);
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

    // The same for the widget's texts and the dashboard's source: attribute strings cannot read the constants.
    [Theory]
    [InlineData("create_widget")]
    [InlineData("update_widget")]
    public void The_widget_tools_say_the_lengths_of_the_texts_of_a_widget_that_the_api_enforces(string tool)
    {
        var description = Description(tool);

        Assert.Contains($"subtitle (1 to {WidgetDisplay.MaxSubtitleLength} characters)", description, StringComparison.Ordinal);
        Assert.Contains($"footnote (1 to {WidgetDisplay.MaxFootnoteLength})", description, StringComparison.Ordinal);
        Assert.Contains($"caption = {{ text }} (1 to {WidgetDisplay.MaxCaptionLength};", description, StringComparison.Ordinal);
        Assert.Contains($"unit (1 to {ScriptEndpoints.MaxUnitLength};", description, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dashboard_tools_say_the_length_of_a_source_that_the_api_enforces()
    {
        var method = Tools()["update_dashboard"];
        var parameter = method.GetParameters().Single(p => p.Name == "source");
        var description = parameter.GetCustomAttribute<DescriptionAttribute>()!.Description;

        Assert.Contains($"1 to {DashboardValidation.MaxSourceLength} characters", description, StringComparison.Ordinal);
        Assert.Contains($"1 to {DashboardValidation.MaxSourceLength} characters", Description("update_dashboard"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_dashboard_documentation_quotes_the_words_the_source_form_and_the_widget_form_show()
    {
        if (PromptKitChecks.FindUp("docs/indicators-dashboard.md") is not { } dashboard || PageSource(EnglishTexts) is not { } english)
        {
            return;
        }

        var text = File.ReadAllText(dashboard);
        var texts = JsonDocument.Parse(english).RootElement;
        var tooLong = texts.GetProperty("dashboardSource").GetProperty("errors").GetProperty("long").GetString()!
            .Replace("{{ max }}", DashboardValidation.MaxSourceLength.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

        Assert.Contains($"(\"{tooLong}\")", text, StringComparison.Ordinal);
        Assert.DoesNotContain("The form shows the line the default makes", text, StringComparison.Ordinal);
        // The crowded-tile hint is in the "Text on the widget" group, not in the preview.
        Assert.DoesNotContain("The warning for crowded tiles shows here too", text, StringComparison.Ordinal);
        Assert.Contains("the **Text on the widget** group shows a hint under its fields", text, StringComparison.Ordinal);
        Assert.Contains("texts that need more room than the whole tile has are cut by the tile", text, StringComparison.Ordinal);
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

        Assert.Contains("with every setting the widget is saved with (its title, chart type, query, display settings, the dashboard filters it ignores, position and size", text, StringComparison.Ordinal);
        Assert.Contains("(its filters, in the same order with the same defaults, its palette, its colors kept by value and its other display settings; see [Colors](#colors))", text, StringComparison.Ordinal);
        Assert.Contains("or changing its colors, leaves the other as it is", text, StringComparison.Ordinal);
        Assert.Contains("choose their palette and the colors kept by value (and, in the widget form, a widget's color or scale)", text, StringComparison.Ordinal);
        Assert.Contains("in the colors chosen, but none of these actions, and the API refuses their requests (403)", text, StringComparison.Ordinal);
        Assert.Contains("(renaming, moving, duplicating or removing it, or saving its colors)", text, StringComparison.Ordinal);
    }

    /// <summary>The section of the dashboard documentation on reference lines, steps and data labels: from its heading to the next section.</summary>
    private static string AnnotationsSection(string text)
    {
        const string heading = "### Reference lines, steps and data labels\n";
        var start = text.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, "docs/indicators-dashboard.md has no section 'Reference lines, steps and data labels'.");
        var end = text.IndexOf("\n## ", start, StringComparison.Ordinal);
        Assert.True(end > start, "The section on reference lines is not followed by another section.");
        // It stays under "Widget types", before the section on series.
        Assert.True(text.LastIndexOf("\n## ", start, StringComparison.Ordinal) == text.IndexOf("\n## Widget types", StringComparison.Ordinal), "The section on reference lines is not under 'Widget types'.");
        return text[start..end];
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
