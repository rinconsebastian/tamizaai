using System.Text;
using Tamiza.Api.Assistant;
using Tamiza.Api.CleanData;
using Tamiza.Api.Data;
using Tamiza.Api.Pipeline;
using static Tamiza.Api.UnitTests.Assistant.ContextFixtures;

namespace Tamiza.Api.UnitTests.Assistant;

public sealed class ContextTrimmerTests
{
    private const long Plenty = 10_000_000;

    private static long Size(ContextSources sources, ContextRequest request) => ContextAssembler.Assemble(sources, request, Plenty).Bytes;

    /// <summary>A form of 400 questions, each with its own short choice list, and a municipality list of 1,100 labels.</summary>
    private static ContextSources BigForm(string? script1 = null, int labelLength = 20)
    {
        var fields = new List<FormField> { new("municipality", "municipality", "select_one_municipios", "Municipality", "municipios") };
        var choices = new List<FormChoice>(Enumerable.Range(1, 1100).Select(n => new FormChoice("municipios", $"m{n}", $"Municipio {n:0000} de Prueba")));
        var columns = new List<TableColumn> { Column("_id", DataColumnType.Integer), Column("municipality", label: "Municipality") };
        for (var question = 1; question <= 400; question++)
        {
            var name = $"q{question:000}";
            var label = $"Question {question:000} " + new string('x', labelLength);
            fields.Add(new FormField(name, name, "select_one_" + name, label, "list" + name));
            choices.AddRange(new[] { "Yes", "No", "Maybe" }.Select(l => new FormChoice("list" + name, l.ToLowerInvariant(), l)));
            columns.Add(Column(name, label: label));
        }

        var members = new InputTable("members", 3120, [.. RepeatKeys, Column("member_name", label: "Name of the member")]);
        var inputs = new ScriptInputs(
            "clean", Published, Guid.NewGuid(), [new InputTable("main", 1204, columns), members, Children()], [],
            [.. fields.Select(f => new InputQuestion(f.Name, f.Xpath, f.Type, f.Label))]);
        return new ContextSources(inputs, fields, choices, "America/Bogota", null, script1);
    }

    private static ContextRequest Mentioning(string draft, params string[] turns) =>
        Request(draft: draft, messages: [.. turns.Select((text, index) => new AssistantMessage(index % 2 == 0 ? "user" : "assistant", text))]);

    [Fact]
    public void ARequestThatFitsIsNotTrimmed()
    {
        var context = ContextAssembler.Assemble(Sources(), Request(), 1_000_000);

        Assert.True(context.Fits);
        Assert.Empty(context.Omitted);
        Assert.Contains("  communities: ", Block(context, "schema"));
    }

    [Fact]
    public void LargeForm_LeavesOutTheListsTheDraftDoesNotMention()
    {
        var sources = BigForm();
        var request = Mentioning("def compute(cleaned, ctx):\n    main = cleaned[\"main\"]\n    return main.groupby(\"q007\")\n", "rate of q007 by municipality");
        var full = Size(sources, request);

        var context = ContextAssembler.Assemble(sources, request, full - 1);

        Assert.True(context.Fits);
        Assert.Equal(["choice lists: 399 of 401 left out"], context.Omitted);
        var schema = Block(context, "schema");
        // The question q007 is named by the draft and the municipality list by the message ("municipality").
        Assert.Contains("  listq007: Yes | No | Maybe", schema);
        Assert.Contains("  municipios: Municipio 0001 de Prueba", schema);
        Assert.DoesNotContain("  listq008: ", schema);
        Assert.DoesNotContain("  listq400: ", schema);
        Assert.True(context.Bytes <= full - 1);
    }

    [Fact]
    public void AListIsMentionedByItsNameItsQuestionOrAColumn()
    {
        var sources = Sources();
        var request = Mentioning("x = 1  # the list services_list and the column community", "and water_source please");
        var full = Size(sources, request);

        var schema = Block(ContextAssembler.Assemble(sources, request, full - 1), "schema");

        Assert.Contains("  services_list: ", schema);
        Assert.Contains("  communities: ", schema);
        Assert.Contains("  water_list: ", schema);
        Assert.DoesNotContain("  municipalities: ", schema);
    }

    [Fact]
    public void Omissions_AreMadeInTheOrderOfTheDesign()
    {
        var script1 = string.Join('\n', Enumerable.Range(1, 1500).Select(n => $"x{n:0000} = {n}  # a line of the first script"));
        var sources = BigForm(script1, labelLength: 120);
        var turns = Enumerable.Range(0, 7).Select(n => (n % 2 == 0 ? "user " : "assistant ") + n).ToArray();
        var request = Mentioning("def compute(cleaned, ctx):\n    return cleaned[\"main\"]\n", turns);

        var context = ContextAssembler.Assemble(sources, request, 1);

        Assert.False(context.Fits);
        Assert.Equal(6, context.Omitted.Count);
        string[] prefixes = ["earlier turns: ", "choice lists: ", "tables: ", "question labels: ", "script 1: ", "examples: "];
        for (var index = 0; index < prefixes.Length; index++)
        {
            Assert.StartsWith(prefixes[index], context.Omitted[index]);
        }

        Assert.Equal("earlier turns: 4 of 7 left out", context.Omitted[0]);
        Assert.Equal("tables: 2 of 3 left out", context.Omitted[2]);
        Assert.Equal("question labels: 400 cut to 80 characters", context.Omitted[3]);
        Assert.Equal("script 1: cut after 16 KB", context.Omitted[4]);
        Assert.Equal("examples: 2 of 3 left out", context.Omitted[5]);
    }

    [Fact]
    public void EarlierTurnsGoInPairsAndTheLastUserTurnAndTheAnswerBeforeItStay()
    {
        var turns = Enumerable.Range(0, 7).Select(n => $"{(n % 2 == 0 ? "user" : "assistant")} turn {n} " + new string('z', 5_000)).ToArray();
        var request = Mentioning(IndicatorDraft, turns);
        var full = Size(Sources(), request);

        var oneGoes = ContextAssembler.Assemble(Sources(), request, full - 9_000);
        Assert.Equal(["earlier turns: 2 of 7 left out"], oneGoes.Omitted);
        Assert.Equal(5, oneGoes.Turns.Count);
        Assert.Equal("user", oneGoes.Turns[0].Role);
        Assert.StartsWith("user turn 2 ", oneGoes.Turns[0].Content);

        var twoGo = ContextAssembler.Assemble(Sources(), request, full - 19_000);
        Assert.Equal("earlier turns: 4 of 7 left out", twoGo.Omitted[0]);
        Assert.Equal(3, twoGo.Turns.Count);
        Assert.StartsWith("user turn 4 ", twoGo.Turns[0].Content);
        Assert.StartsWith("assistant turn 5 ", twoGo.Turns[1].Content);
        Assert.StartsWith("user turn 6 ", twoGo.Turns[2].Content);

        var nothingElse = ContextAssembler.Assemble(Sources(), request, 1);
        Assert.Equal(3, nothingElse.Turns.Count);
        Assert.StartsWith("user turn 6 ", nothingElse.Turns[^1].Content);
        Assert.All(new[] { oneGoes, twoGo, nothingElse }, context => Assert.Equal("user", context.Turns[0].Role));
    }

    [Fact]
    public void ALongConversationOfJustTwoTurnsOrOneKeepsEverything()
    {
        var context = ContextAssembler.Assemble(Sources(), Mentioning(IndicatorDraft, "first"), 1);

        Assert.Single(context.Turns);
        Assert.DoesNotContain(context.Omitted, entry => entry.StartsWith("earlier turns", StringComparison.Ordinal));
    }

    [Fact]
    public void TablesOtherThanMainAreKeptWhenTheyAreMentioned()
    {
        var sources = Sources();
        var request = Mentioning("members = cleaned[\"members\"]\n", "go");
        var full = Size(sources, request);

        // The lists go first; the limit is below what that saves, so the tables are tried next.
        var context = ContextAssembler.Assemble(sources, request, 1);

        var schema = Block(context, "schema");
        Assert.Contains("\nmain: 1,204 rows", schema);
        Assert.Contains("\nmembers: 3,120 rows", schema);
        Assert.DoesNotContain("\nchildren:", schema);
        Assert.Contains("tables: 1 of 3 left out", context.Omitted);
        Assert.True(full > context.Bytes);
    }

    [Fact]
    public void LongQuestionLabelsAreCutWithAnEllipsis()
    {
        var sources = BigForm(labelLength: 120);

        var schema = Block(ContextAssembler.Assemble(sources, Request(), 1), "schema");

        var line = schema.Split('\n').First(l => l.StartsWith("  q001: ", StringComparison.Ordinal));
        var expected = "\"" + ("Question 001 " + new string('x', 120))[..80] + "…\"";
        Assert.Contains(expected, line);
        Assert.DoesNotContain(new string('x', 68), line);
    }

    [Fact]
    public void ScriptOneIsCutAfterItsFirstSixteenKilobytes()
    {
        var script1 = string.Join('\n', Enumerable.Range(1, 1500).Select(n => $"x{n:0000} = {n}  # a line of the first script"));
        var sources = Sources(script1: script1);

        var block = Block(ContextAssembler.Assemble(sources, Request(), 1), "script1");

        Assert.EndsWith("\n[the rest of script 1 is left out]", block);
        Assert.Contains("x0001 = 1", block);
        Assert.DoesNotContain("x1500", block);
        var code = block[(block.IndexOf('\n') + 1)..block.IndexOf("\n[the rest", StringComparison.Ordinal)];
        Assert.InRange(Encoding.UTF8.GetByteCount(code), 15_000, 16 * 1024);
        Assert.EndsWith("a line of the first script", code);
    }

    [Fact]
    public void OnlyTheFirstExampleIsKeptAtTheEnd()
    {
        var examples = Block(ContextAssembler.Assemble(Sources(), Request(), 1), "examples");

        Assert.Contains("--- example: count-by-group ---", examples);
        Assert.DoesNotContain("rate-by-group", examples);
        Assert.DoesNotContain("monthly-series", examples);
    }

    [Fact]
    public void LargeDraft_IsNeverShortenedAndDoesNotFit()
    {
        var draft = string.Join('\n', Enumerable.Range(1, 4000).Select(n => $"x{n:0000} = {n}  # a long line of the draft"));
        Assert.True(Encoding.UTF8.GetByteCount(draft) > 70 * 1024);

        var context = ContextAssembler.Assemble(Sources(), Request(draft: draft), 64 * 1024);

        Assert.False(context.Fits);
        Assert.Contains("x4000 = 4000  # a long line of the draft", context.System);
        Assert.Contains("x0001 = 1  # a long line of the draft", context.System);
    }

    [Fact]
    public void StillTooLarge_AfterEverythingThatCanBeLeftOut()
    {
        var draft = string.Join('\n', Enumerable.Range(1, 1500).Select(n => $"x{n:0000} = {n}  # a line of the draft text"));
        Assert.InRange(Encoding.UTF8.GetByteCount(draft), 55 * 1024, 62 * 1024);

        var context = ContextAssembler.Assemble(Sources(), Request(draft: draft), 64 * 1024);

        Assert.False(context.Fits);
        Assert.Contains(context.Omitted, entry => entry.StartsWith("examples: ", StringComparison.Ordinal));
        Assert.Contains("x1500 = 1500", context.System);
        Assert.True(context.Bytes > 64 * 1024);
    }

    [Fact]
    public void ARequestOneByteOverIsTrimmedJustEnough()
    {
        var sources = BigForm();
        var draft = string.Join('\n', Enumerable.Range(1, 800).Select(n => $"x{n:0000} = {n}  # a line"));
        var request = Request(draft: draft);
        var full = Size(sources, request);

        var context = ContextAssembler.Assemble(sources, request, full - 1);

        Assert.True(context.Fits);
        Assert.NotEmpty(context.Omitted);
        Assert.True(context.Bytes <= full - 1);
    }

    [Fact]
    public void MaskingRunsOnTheTrimmedText()
    {
        const string error = "ValueError: invalid literal for int() with base 10: 'Santa Ana'";
        var test = Test(ScriptKind.Indicator, "def compute(cleaned, ctx):\n    return cleaned[\"main\"]\n", [Step("indicator", "failed", error: Traceback(error, (2, "compute", "int(x)")))]);
        var request = Request(
            action: "fix", draft: "def compute(cleaned, ctx):\n    return cleaned[\"main\"]\n", test: test,
            messages: [new AssistantMessage("user", "fix the error")]);
        var full = Size(Sources(), request);

        var kept = ContextAssembler.Assemble(Sources(), request, full);
        var trimmed = ContextAssembler.Assemble(Sources(), request, full - 1);

        // With the list in the request the label is protected; with the list left out it is just a name in an error.
        Assert.Contains("  municipalities: Santa Ana | Gómez Plata | Bogotá", Block(kept, "schema"));
        Assert.Contains("  error: ValueError: invalid literal for int() with base 10: 'Santa Ana'\n", Block(kept, "test"));
        Assert.Contains("choice lists: ", trimmed.Omitted[0]);
        Assert.DoesNotContain("municipalities:", Block(trimmed, "schema"));
        Assert.Contains("  error: ValueError: invalid literal for int() with base 10: '…'\n", Block(trimmed, "test"));
    }

    [Fact]
    public void ATrimmedRequestIsStillByteForByteReproducible()
    {
        var sources = BigForm();
        var request = Mentioning(IndicatorDraft, "rate by municipality");
        var limit = Size(sources, request) - 1;

        var first = ContextAssembler.Assemble(sources, request, limit);
        var second = ContextAssembler.Assemble(sources, request, limit);

        Assert.Equal(first.Text, second.Text);
        Assert.Equal(first.Omitted, second.Omitted);
    }
}
