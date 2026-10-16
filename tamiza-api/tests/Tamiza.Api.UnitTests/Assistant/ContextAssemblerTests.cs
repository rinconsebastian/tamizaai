using System.Text;
using System.Text.Json;
using Tamiza.Api.Assistant;
using Tamiza.Api.Data;
using static Tamiza.Api.UnitTests.Assistant.ContextFixtures;

namespace Tamiza.Api.UnitTests.Assistant;

public sealed class ContextAssemblerTests
{
    private const long Plenty = 1_000_000;

    private static AssembledContext Assemble(ContextSources sources, ContextRequest request) =>
        ContextAssembler.Assemble(sources, request, Plenty);

    private static AssembledContext Assemble(ContextRequest? request = null, ContextSources? sources = null) =>
        Assemble(sources ?? Sources(), request ?? Request());

    [Fact]
    public void BlocksAreWrappedInTheOrderOfTheDesign()
    {
        var context = Assemble(Request(test: Test(ScriptKind.Indicator, IndicatorDraft, [Step("indicator", "failed", error: "The script failed.")]), action: "fix"));

        var tags = new[] { "rules", "examples", "project", "schema", "script1", "test", "draft" };
        var positions = tags.Select(tag => context.System.IndexOf($"<{tag}>\n", StringComparison.Ordinal)).ToArray();
        Assert.All(positions, position => Assert.True(position >= 0));
        Assert.Equal(positions.Order(), positions);
        foreach (var tag in tags)
        {
            Assert.Contains($"\n</{tag}>", context.System);
        }

        Assert.StartsWith("<rules>\n", context.System);
        Assert.EndsWith("\n</draft>", context.System);
        Assert.Equal(AssistantPrompts.System.Replace("\r\n", "\n").Trim('\n'), Block(context, "rules"));
    }

    [Fact]
    public void TheRulesAreFollowedByTheExamplesOfTheKindWithTheFirstTemplateFirst()
    {
        var context = Assemble();

        var examples = Block(context, "examples");
        foreach (var example in AssistantPrompts.Examples("indicator"))
        {
            Assert.Contains($"--- example: {example.Id} ---", examples);
        }

        Assert.True(examples.IndexOf("count-by-group", StringComparison.Ordinal) < examples.IndexOf("monthly-series", StringComparison.Ordinal));
        Assert.DoesNotContain("drop-test-submissions", examples);
    }

    [Fact]
    public void Libraries()
    {
        var rules = Block(Assemble(), "rules");

        Assert.Contains("Libraries available: pandas", rules);
        Assert.Contains("shapely", rules);
        Assert.Contains("scikit-learn, SciPy, statsmodels and matplotlib are not available", rules);
    }

    [Fact]
    public void SchemaAndChoices_MainHasItsRowCountAndRepeatsNameTheirKeys()
    {
        var schema = Block(Assemble(), "schema");

        Assert.Contains("\nmain: 1,204 rows\n", schema);
        Assert.Contains("  water_source: text \"What is your main water source?\" [select_one water_list] [or other]", schema);
        Assert.Contains("  _id: integer (Int64)", schema);
        Assert.Contains("  _submission_time: timestamp (datetime64, UTC)", schema);
        Assert.Contains("  visit_date: date \"Visit date\"", schema);
        // A first-level repeat: _parent_id is the submission's _id.
        Assert.Contains("members: 3,120 rows. A repeat table, one row per repeat answer: `_parent_id` is the `_id` of the submission in `main`, and `_parent_table` is `main`.", schema);
        // A nested repeat: _parent_id is the _row_id of the enclosing repeat row, named by _parent_table.
        Assert.Contains("children: 410 rows. A nested repeat table, one row per repeat answer: `_parent_id` is the `_row_id` of the row of `members` it sits in, and `_parent_table` names `members`.", schema);
        Assert.Contains("  child_age: integer (Int64) \"Age of the child\"", schema);
        Assert.Contains("Input tables: the clean tables of the last run, published 2026-10-10T12:00:00Z. A script receives them in `cleaned`", schema);
    }

    [Fact]
    public void ScriptOneReceivesTheFlattenedTables()
    {
        var schema = Block(Assemble(Request("clean", draft: "def clean(raw, ctx):\n    return raw\n"), Sources("clean")), "schema");

        Assert.Contains("the flattened (raw) tables of the last run", schema);
        Assert.Contains("in `raw`", schema);
    }

    [Fact]
    public void SelectColumnsNameTheirListsAndLongListsAreCutAtFifty()
    {
        var schema = Block(Assemble(), "schema");

        Assert.Contains("  services: text \"Which services does the household have?\" [select_multiple services_list]", schema);
        Assert.Contains("  municipality: text \"Municipality\" [select_one municipalities]", schema);
        Assert.Contains("  water_list: Piped | Well | River | Rainwater | Other\n", schema);
        Assert.EndsWith("  services_list: Water | Electric power | Waste water", schema);
        Assert.Contains("  municipalities: Santa Ana | Gómez Plata | Bogotá\n", schema);

        var communities = schema.Split('\n').Single(line => line.StartsWith("  communities: ", StringComparison.Ordinal));
        Assert.Contains("Community 050 (and 70 more)", communities);
        Assert.DoesNotContain("Community 051", communities);
        Assert.Equal(50, communities.Split(" | ").Length);
        Assert.Contains("older form versions may have used other labels", schema);
        Assert.Contains("`ctx.form.name`", schema);
    }

    [Fact]
    public void ChoiceListsOfQuestionsWithoutColumnsAreNotListed()
    {
        var schema = Block(Assemble(sources: Sources(tables: [MainTable()])), "schema");

        Assert.Contains("  water_list: ", schema);
        Assert.DoesNotContain("children:", schema);
        Assert.DoesNotContain("members:", schema);
    }

    [Fact]
    public void ReferenceTablesHaveColumnsAndTypesOnly()
    {
        var schema = Block(Assemble(), "schema");

        Assert.Contains("  population: municipality (text), people (integer)\n", schema);
    }

    [Fact]
    public void TimeZoneEnumeratorAndUnsavedDetails()
    {
        var details = new IndicatorDetails("Households with piped water", "Share of households\nwith piped water", "households", "decimal");

        var project = Block(Assemble(Request(details: details)), "project");

        Assert.Contains("Indicator script `piped_water_rate`", project);
        Assert.Contains("- label: Households with piped water", project);
        Assert.Contains("- description: Share of households with piped water", project);
        Assert.Contains("- unit: households", project);
        Assert.Contains("- format: decimal", project);
        Assert.Contains("Project time zone: America/Bogota", project);
        Assert.Contains("Enumerator question: group_a/enumerator", project);
        Assert.Contains("Input tables: published 2026-10-10T12:00:00Z", project);
        Assert.DoesNotContain("percent", project);
    }

    [Fact]
    public void NoEnumeratorQuestionAndNoDetails()
    {
        var project = Block(Assemble(Request("output", draft: "def prepare_output(cleaned, ctx):\n    return cleaned\n"), Sources("output", enumerator: null)), "project");

        Assert.Contains("Script 2 (output)", project);
        Assert.Contains("Enumerator question: none set", project);
        Assert.DoesNotContain("details", project);
    }

    [Fact]
    public void ScriptOneIsSentForAnIndicatorAndNotForScriptOneItself()
    {
        var indicator = Assemble(Request(), Sources(script1: "def clean(raw, ctx):\n    return {**raw}\n"));
        var clean = Assemble(Request("clean", draft: "def clean(raw, ctx):\n    return raw\n"), Sources("clean"));

        Assert.Contains("return {**raw}", Block(indicator, "script1"));
        Assert.DoesNotContain("\n<script1>\n", clean.System);
    }

    [Fact]
    public void ScriptOneNeverSavedIsSaidSo()
    {
        var script1 = Block(Assemble(sources: Sources(script1: null)), "script1");

        Assert.Contains("Script 1 has not been saved", script1);
    }

    [Fact]
    public void NothingPublishedYet_TheFormsQuestionsAndChoiceListsAreSent()
    {
        var schema = Block(Assemble(sources: Sources(published: false)), "schema");

        Assert.Contains("No input tables are known yet", schema);
        Assert.Contains("  water_source: select_one_water_list \"What is your main water source?\" [select_one water_list] [or other]", schema);
        Assert.Contains("  age: integer \"Age\" [in a repeat group]", schema);
        Assert.Contains("  water_list: Piped | Well | River | Rainwater | Other\n", schema);
        Assert.Contains("  municipalities: Santa Ana | Gómez Plata | Bogotá\n", schema);
        Assert.Contains("Community 050 (and 70 more)", schema);
        Assert.DoesNotContain("main:", schema);
        Assert.Contains("not published yet", Block(Assemble(sources: Sources(published: false)), "project"));
    }

    [Fact]
    public void TurnsAreCarriedAsSent()
    {
        AssistantMessage[] turns =
        [
            new("user", "share of households with piped water"),
            new("assistant", "It counts the households that report Piped.\n[proposed code: 34 lines]"),
            new("user", "exclude households with no members"),
        ];

        var context = Assemble(Request(messages: turns));

        Assert.Equal(turns, context.Turns);
        Assert.Equal(turns, context.Messages.Skip(1));
        Assert.Equal("system", context.Messages[0].Role);
        Assert.Equal(context.System, context.Messages[0].Content);
        Assert.Contains("=== user ===\nshare of households with piped water\n\n=== assistant ===\nIt counts the households that report Piped.\n[proposed code: 34 lines]\n\n=== user ===\nexclude households with no members", context.Text);
    }

    [Fact]
    public void TheSameInputsGiveTheSameTextByteForByte()
    {
        AssistantMessage[] turns = [new("user", "rate by municipality")];
        var test = Test(ScriptKind.Indicator, IndicatorDraft, [Step("indicator", "failed", error: Traceback("KeyError: 'water_source'", (12, "compute", "x")))]);

        var first = Assemble(Request(action: "fix", messages: turns, test: test, warning: new AssistantWarning("duplicateRows", Count: 4)));
        var second = Assemble(Request(action: "fix", messages: turns, test: test, warning: new AssistantWarning("duplicateRows", Count: 4)));

        Assert.Equal(Encoding.UTF8.GetBytes(first.Text), Encoding.UTF8.GetBytes(second.Text));
        Assert.Equal(first.Bytes, second.Bytes);
    }

    [Fact]
    public void TheDraftIsNumberedAndNeverChanged()
    {
        var draft = string.Join('\n', Enumerable.Range(1, 12).Select(n => $"x{n} = {n}"));

        var block = Block(Assemble(Request(draft: draft)), "draft");

        Assert.Contains("(the numbers are not part of the code)", block);
        Assert.Contains("\n 1 | x1 = 1\n", block);
        Assert.EndsWith("\n12 | x12 = 12", block);
        Assert.Contains("Request: write or change the whole script", block);
    }

    [Fact]
    public void AnEmptyEditorIsSaidSo()
    {
        Assert.EndsWith("(the editor is empty)", Block(Assemble(Request(draft: "")), "draft"));
    }

    [Fact]
    public void ACellRequestNamesTheFirstAndLastLineOfTheFocusedCell()
    {
        var draft = "def compute(cleaned, ctx):\n    # %% Look\n    main = cleaned[\"main\"]\n\n    # %% Rate\n    a = 1\n    b = 2\n\n\n    # %% Return\n    return a\n";

        var block = Block(Assemble(Request(action: "cell", draft: draft, focus: 5)), "draft");

        Assert.Contains("rewrite one cell. The cell is lines 5 to 7 below, named \"Rate\"; it starts with the marker on line 5.", block);
        Assert.Contains("\n 5 |     # %% Rate\n", block);
    }

    [Fact]
    public void ACellRequestNeedsAMarkerLine()
    {
        Assert.Throws<ArgumentException>(() => Assemble(Request(action: "cell", draft: "x = 1\n# %% Rate\ny = 2", focus: 1)));
        Assert.Throws<ArgumentException>(() => Assemble(Request(action: "cell", draft: "x = 1", focus: null)));
        Assert.Throws<ArgumentException>(() => Assemble(Request(action: "cell", draft: "x = 1", focus: 9)));
    }

    [Fact]
    public void AFixHasTheTestBlockOnlyWhenTheTestIsThere()
    {
        var withoutTest = Assemble(Request(action: "fix"));
        var script = Assemble(Request(test: Test(ScriptKind.Indicator, IndicatorDraft, [Step("flatten")])));

        Assert.DoesNotContain("\n<test>\n", withoutTest.System);
        Assert.False(withoutTest.HasTest);
        Assert.Contains("no test facts", Block(withoutTest, "draft"));
        Assert.DoesNotContain("\n<test>\n", script.System);
    }

    [Fact]
    public void BlockSizesAreRecordedWithoutText()
    {
        var context = Assemble(Request(messages: [new AssistantMessage("user", "héllo")]));

        Assert.Equal(new[] { "draft", "examples", "project", "rules", "schema", "script1", "turns" }, context.BlockBytes.Keys.Order());
        Assert.Equal(6, context.BlockBytes["turns"]);
        Assert.Equal(Encoding.UTF8.GetByteCount(context.System) + 6, context.Bytes);
        Assert.True(context.Fits);
        Assert.Empty(context.Omitted);
    }

    [Fact]
    public void TheCtxMembersTheMaskerKnowsAreTheEditorsOwn()
    {
        var path = PromptKitChecks.FindUp("tamiza-ui/src/app/pages/projects/script-editor/ctx-members.json");
        if (path is null)
        {
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var members = document.RootElement.EnumerateObject()
            .SelectMany(group => group.Value.EnumerateArray().Select(member => member.GetProperty("name").GetString()!))
            .Append("ctx");

        Assert.Equal(members.Order(StringComparer.Ordinal), ContextAssembler.CtxMembers.Order(StringComparer.Ordinal));
    }
}
