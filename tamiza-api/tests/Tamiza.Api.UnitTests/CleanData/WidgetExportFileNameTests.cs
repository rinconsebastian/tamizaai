using System.Text.Json.Nodes;
using Tamiza.Api.CleanData;
using Tamiza.Api.UnitTests.Assistant;

namespace Tamiza.Api.UnitTests.CleanData;

/// <summary>
/// The names of a widget's files, against the cases tamiza-ui's <c>file-name.ts</c> is tested with. The names are worked out by hand, so
/// the two implementations are checked against the same truth. The tests do nothing when the UI is not next to the API.
/// </summary>
public sealed class WidgetExportFileNameTests
{
    private const string FixtureFile = "tamiza-ui/src/app/pages/projects/indicators/testing/export-file-names.json";

    private static JsonArray? Cases()
    {
        var path = PromptKitChecks.FindUp(FixtureFile);
        return path is null ? null : JsonNode.Parse(File.ReadAllText(path))!["cases"]!.AsArray();
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

    [Fact]
    public void The_fixture_has_the_cases_the_design_lists()
    {
        if (Cases() is not { } cases)
        {
            return;
        }

        Assert.Equal(9, cases.Count);
        Assert.Contains(cases, c => c!["version"] is not null);
        Assert.Contains(cases, c => c!["expected"]!.GetValue<string>().Contains("_widget_"));
    }

    [Theory]
    [MemberData(nameof(CaseNumbers))]
    public void Every_shared_case_gives_the_name_that_was_worked_out_by_hand(int number)
    {
        var testCase = Cases()![number]!;

        var name = ExportFileName.ForWidget(
            testCase["project"]!.GetValue<string>(),
            testCase["dashboard"]!.GetValue<string>(),
            testCase["title"]!.GetValue<string>(),
            testCase["version"]?.GetValue<int>(),
            DateOnly.Parse(testCase["day"]!.GetValue<string>()),
            testCase["extension"]!.GetValue<string>());

        Assert.Equal(testCase["expected"]!.GetValue<string>(), name);
    }

    [Fact]
    public void A_name_is_made_of_ascii_only()
    {
        var name = ExportFileName.ForWidget("Año 2026", "Vivienda", "Población / 住房 ‮", 3, new DateOnly(2026, 11, 2), "png");

        Assert.All(name, c => Assert.True(c <= 127));
        Assert.DoesNotContain('/', name);
    }
}
