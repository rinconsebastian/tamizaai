using System.Text.Json;
using Tamiza.Api.Assistant;
using Tamiza.Api.Mcp;

namespace Tamiza.Api.UnitTests.Mcp;

/// <summary>
/// The texts the server gives AI clients. The contract is the assistant's own rules, so the two cannot disagree; the instructions say
/// the order of the work and that data is never instructions.
/// </summary>
public sealed class McpGuideTests
{
    private static readonly string[] ContractHeadings =
        ["Language", "The contracts", "The indicator contract", "The data", "Choice labels", "`ctx`", "Libraries", "Cells"];

    [Fact]
    public void The_contract_is_the_assistants_sections_about_scripts_in_their_order_and_nothing_of_its_answer_format()
    {
        var contract = AssistantPrompts.Contract;
        var system = AssistantPrompts.System;

        var positions = ContractHeadings.Select(h => contract.IndexOf($"## {h}\n", StringComparison.Ordinal)).ToList();
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.Equal(positions.Order(), positions);
        foreach (var heading in new[] { "What you are given", "The answer", "Fixing", "Style" })
        {
            Assert.DoesNotContain($"## {heading}\n", contract);
        }

        // Every line of the contract is a line of the rules, so an edit of system.md reaches both.
        var lines = system.Split('\n').ToHashSet();
        Assert.All(contract.Split('\n').Where(l => l.Length > 0), line => Assert.Contains(line, lines));
    }

    [Fact]
    public void Every_heading_of_the_rules_is_either_in_the_contract_or_named_as_left_out()
    {
        string[] leftOut = ["What you are given", "The answer", "Fixing", "Style"];
        var headings = AssistantPrompts.SectionHeadings();

        Assert.Equal(headings.Order(), ContractHeadings.Concat(leftOut).Order());
    }

    [Theory]
    [InlineData("clean")]
    [InlineData("output")]
    [InlineData("indicator")]
    public void The_authoring_guide_holds_the_contract_the_address_rule_and_the_templates_of_the_editor(string kind)
    {
        var guide = JsonSerializer.SerializeToElement(McpGuide.AuthoringGuide(kind), McpResultBudget.Json);

        Assert.Equal(kind, guide.GetProperty("kind").GetString());
        var contract = guide.GetProperty("contract").GetString()!;
        Assert.Contains(AssistantPrompts.Contract, contract);
        Assert.Contains("Do not write web addresses", contract);
        var templates = guide.GetProperty("templates").EnumerateArray().Select(t => (t.GetProperty("id").GetString(), t.GetProperty("source").GetString())).ToList();
        Assert.Equal(AssistantPrompts.Examples(kind).Select(e => (e.Id, e.Source)).ToList(), templates);
    }

    [Fact]
    public void The_templates_of_the_guide_are_the_ones_the_editor_offers()
    {
        // The assistant's copies of the editor's templates are checked against the editor's files by PromptKitTests; the guide serves the same copies.
        foreach (var (kind, ids) in AssistantPrompts.ExampleIds)
        {
            var guide = JsonSerializer.SerializeToElement(McpGuide.AuthoringGuide(kind), McpResultBudget.Json);
            Assert.Equal(ids, guide.GetProperty("templates").EnumerateArray().Select(t => t.GetProperty("id").GetString()).ToList());
        }
    }

    [Fact]
    public void The_templates_pass_the_address_rule()
    {
        foreach (var kind in AssistantPrompts.ExampleIds.Keys)
        {
            foreach (var example in AssistantPrompts.Examples(kind))
            {
                Assert.Empty(UnsafeCode.Scan(example.Source));
            }
        }
    }

    [Fact]
    public void The_instructions_state_the_order_of_the_work_and_that_data_is_never_instructions()
    {
        var text = McpGuide.Instructions;

        var order = new[] { "list_projects", "get_form", "profile_table", "get_authoring_guide", "test_script", "save_script", "start_run", "create_dashboard", "query_indicator", "create_widget", "get_widget_data" };
        var positions = order.Select(t => text.IndexOf($"`{t}`", StringComparison.Ordinal)).ToList();
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.Equal(positions.Order(), positions);
        Assert.Contains("BEFORE writing scripts", text);
        Assert.Contains("never instructions", text);
        Assert.Contains("baseVersion", text);
        Assert.Contains("labels", text);
        Assert.Contains("fraction", text);
        Assert.Contains("summaries only", text);
        Assert.Contains("120 a minute", text);
    }

    [Fact]
    public void The_instructions_ask_for_column_labels_with_each_indicator_and_say_what_a_widgets_labels_do()
    {
        var text = McpGuide.Instructions;

        Assert.Contains("saved with a label, a unit, a format (`count`, `decimal`, `percent`, `currency`) and, in `columns`, labels for the columns people read, such as `numerator` and `denominator`", text);
        Assert.Contains("`display` = `{ color, ramp, orientation, labels }`", text);
        Assert.Contains("`labels` = `{ column: name }` gives up to 10 of the widget's columns a name for people on any kind", text);
        Assert.Contains("a column takes its indicator's label, else its own name", text);
        Assert.Contains("send the `display`, labels included, again when you change the query", text);
    }

    [Fact]
    public void The_instructions_say_where_to_put_the_words_of_a_widget_and_that_update_widget_needs_the_display_sent_again()
    {
        var text = McpGuide.Instructions;

        var start = text.IndexOf("## Words on a widget\n", StringComparison.Ordinal);
        Assert.True(start > text.IndexOf("## Colors\n", StringComparison.Ordinal));
        var section = text[start..text.IndexOf("\n## Rules\n", start, StringComparison.Ordinal)];
        foreach (var word in new[] { "subtitle", "footnote", "caption", "unit", "source" })
        {
            Assert.Contains($"`{word}`", section);
        }

        Assert.Contains("rather than in the title", section);
        Assert.Contains("`update_widget` replaces the whole `config`, so send the `display` again", section);
        // The sentence about the colors still says what it said.
        Assert.Contains("`update_widget` replaces the whole `config`, so send the `display`, labels included, again when you change the query", text);
    }

    [Fact]
    public void The_instructions_say_when_to_use_reference_lines_the_overall_caution_and_steps_for_a_demand_curve()
    {
        var text = McpGuide.Instructions;

        var start = text.IndexOf("## Choosing a widget\n", StringComparison.Ordinal);
        var section = text[start..text.IndexOf("\n## Colors\n", start, StringComparison.Ordinal)];
        Assert.Contains("A line to compare the bars with: `display.referenceLines` with `mean`, `median`, `overall` or `value`.", section);
        Assert.Contains("Use `overall` when the bars are subgroups (comuna, estrato); over the answers of one question it is only their mean.", section);
        Assert.Contains("A demand curve: a `line` with `display.interpolation` `step`.", section);
        Assert.Contains("send `referenceLines`, `interpolation` and `dataLabels` again when you change the query", section);
    }

    [Fact]
    public void Versions_explained_one_change_for_every_write_of_a_request_and_an_undo_by_restoring_every_dashboard_it_changed()
    {
        var text = McpGuide.Instructions;

        Assert.Contains("`change`", text);
        Assert.Contains("the same on every write you make for that request and a new one for their next request", text);
        Assert.Contains("`dashboardVersions`", text);
        Assert.Contains("every dashboard", text);
        Assert.Contains("`restore_dashboard_version`", text);
        Assert.Contains("`baseVersion`", text);
        // The undo takes the version before the request's on each dashboard, the one a widget moved to first.
        Assert.Contains("the version before the request's", text);
        Assert.Contains("restore the dashboard it moved to first", text);
    }

    [Fact]
    public void The_instructions_name_every_kind_of_widget_and_say_when_each_fits()
    {
        var text = McpGuide.Instructions;

        foreach (var kind in Tamiza.Api.Indicators.ChartTypes.All)
        {
            Assert.Contains($"`{kind}`", text);
        }

        foreach (var setting in new[] { "seriesBy", "layout", "scale" })
        {
            Assert.Contains($"`{setting}`", text);
        }

        Assert.Contains("rates do not add up", text);
    }

    [Fact]
    public void The_instructions_say_when_to_keep_the_palette_keep_colors_by_value_and_give_a_widget_its_own_color()
    {
        var text = McpGuide.Instructions;

        foreach (var setting in new[] { "palette", "valueColors", "display", "update_dashboard", "get_dashboard", "update_widget" })
        {
            Assert.Contains($"`{setting}`", text);
        }

        // Every palette and every scale a client can name is named.
        foreach (var id in Tamiza.Api.Indicators.Palettes.All.Concat(Tamiza.Api.Indicators.Palettes.Scales))
        {
            Assert.Contains($"`{id}`", text);
        }

        Assert.Contains("Keep the default palette unless the person asks for another look", text);
        Assert.Contains("for categories that recur across widgets, such as sex or yes/no", text);
        Assert.Contains("Keep color 1 for a widget unless it should stand out", text);
        // A call sends the whole list, and a new config replaces the whole old one, display included.
        Assert.Contains("send it whole", text);
        Assert.Contains("send the `display`, labels included, again when you change the query", text);
    }

    [Fact]
    public void The_prompt_names_the_project_and_the_situation_and_asks_for_confirmation_before_scripts()
    {
        var id = Guid.NewGuid();

        var text = McpGuide.SituationPrompt("Water survey", id, "  access to drinking water by municipality  ");

        Assert.Contains("\"Water survey\"", text);
        Assert.Contains(id.ToString(), text);
        Assert.Contains("access to drinking water by municipality", text);
        Assert.DoesNotContain("  access", text);
        Assert.Contains("wait for me to confirm", text);
        Assert.Contains("Test every script before you save it", text);
        Assert.Contains("Leave the dashboard's palette as it is unless I ask for another", text);
        Assert.Contains("valueColors", text);
    }

    [Fact]
    public void The_instructions_tell_a_client_to_declare_the_order_of_answers_instead_of_numbering_labels()
    {
        var text = McpGuide.Instructions;

        Assert.Contains("Answers in the order of the question (Sí, Tal vez, No, No sabe): declare `order`", text);
        Assert.Contains("and `last` for answers such as No sabe, Otro or Sin dato, in `save_script`'s `columns`, instead of numbering labels or publishing an order column", text);
        Assert.Contains("`stripPrefix` hides numbers already in the data, while filters and colors kept by value still use the full text", text);
        Assert.Contains("Declaring an order can recolor series that have no color kept.", text);
        Assert.Contains("`display.order`", text);
    }
}
