using System.Text.Json;
using System.Text.Json.Nodes;
using Tamiza.Api.Indicators;
using Tamiza.Api.UnitTests.Assistant;

namespace Tamiza.Api.UnitTests.Indicators;

/// <summary>
/// The values of a widget's reference lines (design D3 of add-chart-annotations), against the cases tamiza-ui's
/// <c>reference-lines.ts</c> is tested with. The file is worked out by hand, so the two implementations are checked against the same
/// truth. The tests do nothing when the UI is not next to the API.
/// </summary>
public sealed class ReferenceLinesTests
{
    private const string FixtureFile = "tamiza-ui/src/app/pages/projects/indicators/testing/reference-lines.json";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static JsonNode? Fixture()
    {
        var path = PromptKitChecks.FindUp(FixtureFile);
        return path is null ? null : JsonNode.Parse(File.ReadAllText(path));
    }

    private static JsonArray? Cases() => Fixture()?["cases"]?.AsArray();

    public static TheoryData<int> CaseNumbers()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < (Cases()?.Count ?? 0); i++)
        {
            data.Add(i);
        }

        return data;
    }

    private static IndicatorQueryResponse ResponseOf(JsonNode answer) => new(
        true,
        answer["rows"]!.Deserialize<List<IndicatorRow>>(Web)!,
        false,
        MissingReferences.None,
        Totals: answer["totals"]?.Deserialize<SeriesTotals>(Web),
        Overall: answer["overall"]?.GetValue<double>());

    private static string Name(JsonNode testCase) => testCase["name"]!.GetValue<string>();

    [Fact]
    public void The_fixture_has_the_cases_the_design_lists()
    {
        if (Cases() is not { } cases)
        {
            return;
        }

        string[] names = [.. cases.Select(c => Name(c!))];
        Assert.Equal(names.Length, names.Distinct().Count());
        foreach (var named in new[]
                 {
                     "Four bars", "Three bars", "One value", "Missing group value counted", "Overall unlike the mean", "Overall from the grand total of a widget with a series",
                     "Overall that the engine could not give", "Answers of one question", "Value lines keep their number and their label", "The lines come in the order the widget lists them",
                     "Sums that depend on the order of addition",
                 })
        {
            Assert.Contains(named, names);
        }
    }

    [Theory]
    [MemberData(nameof(CaseNumbers))]
    public void Every_shared_case_gives_the_lines_that_were_worked_out_by_hand(int number)
    {
        var testCase = Cases()![number]!;
        var tolerance = Fixture()!["tolerance"]!.GetValue<double>();
        var config = ProductionShapesTests.Read(testCase["config"]!.ToJsonString());

        var lines = ReferenceLines.Of(testCase["chartType"]!.GetValue<string>(), config, ResponseOf(testCase["answer"]!));

        var expected = testCase["expected"]!.AsArray();
        var message = Name(testCase);
        Assert.True(expected.Count == lines.Count, $"{message}: {lines.Count} lines for {expected.Count} expected");
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.True(expected[i]!["kind"]!.GetValue<string>() == lines[i].Kind, $"{message}: kind of line {i}");
            Assert.True(expected[i]!["label"]?.GetValue<string>() == lines[i].Label, $"{message}: label of line {i}");
            var want = expected[i]!["value"]!.GetValue<double>();
            Assert.True(Math.Abs(want - lines[i].Value) <= tolerance, $"{message}: value of line {i} is {lines[i].Value}, expected {want}");
        }
    }

    [Fact]
    public void The_mean_is_summed_in_the_order_of_the_rows()
    {
        var config = ProductionShapesTests.Read("""{"indicator":"encuesta_hoy","groupBy":"zona","display":{"referenceLines":[{"kind":"mean"}]}}""");
        IndicatorRow[] forward = [new("a", 1e16, null), new("b", 1, null), new("c", -1e16, null)];
        IndicatorRow[] reordered = [forward[0], forward[2], forward[1]];

        var one = ReferenceLines.Of(ChartTypes.Bar, config, new IndicatorQueryResponse(true, forward, false, MissingReferences.None));
        var other = ReferenceLines.Of(ChartTypes.Bar, config, new IndicatorQueryResponse(true, reordered, false, MissingReferences.None));

        Assert.Equal(0, Assert.Single(one).Value);
        Assert.Equal(1.0 / 3, Assert.Single(other).Value, 12);
    }

    [Fact]
    public void A_response_the_lines_are_taken_from_is_not_changed_and_the_rows_are_not_reordered()
    {
        var config = ProductionShapesTests.Read("""{"indicator":"encuesta_hoy","groupBy":"zona","display":{"referenceLines":[{"kind":"median"}]}}""");
        IndicatorRow[] rows = [new("a", 0.9, null), new("b", 0.1, null), new("c", 0.5, null)];

        var lines = ReferenceLines.Of(ChartTypes.Column, config, new IndicatorQueryResponse(true, rows, false, MissingReferences.None));

        Assert.Equal(0.5, Assert.Single(lines).Value);
        Assert.Equal([0.9, 0.1, 0.5], rows.Select(r => r.Value));
    }

    [Fact]
    public void A_config_with_no_display_has_no_lines()
    {
        var config = ProductionShapesTests.Read("""{"indicator":"encuesta_hoy","groupBy":"zona"}""");

        Assert.Empty(ReferenceLines.Of(ChartTypes.Column, config, new IndicatorQueryResponse(true, [new("a", 0.5, null)], false, MissingReferences.None)));
    }
}
