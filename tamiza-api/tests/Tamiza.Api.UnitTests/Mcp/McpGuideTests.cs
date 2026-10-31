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
    }
}
