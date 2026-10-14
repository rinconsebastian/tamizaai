using System.Net;
using System.Text.Json;
using Tamiza.Api.IntegrationTests.Infrastructure;
using static Tamiza.Api.IntegrationTests.Performance.PerformanceHarness;

namespace Tamiza.Api.IntegrationTests.Performance;

/// <summary>The dashboard's sampling frame coverage (tasks 3.2).</summary>
[Collection(PostgresCollection.Name)]
public sealed class PerformanceCoverageTests(PostgresFixture postgres)
{
    private static JsonElement CoverageOf(JsonElement dashboard) => dashboard.GetProperty("coverage");

    private static List<JsonElement> Rows(JsonElement dashboard) => CoverageOf(dashboard).GetProperty("rows").EnumerateArray().ToList();

    private static string?[] Codes(JsonElement row) =>
        row.GetProperty("values").EnumerateArray().Select(v => v.GetProperty("code").GetString()).ToArray();

    private static string?[] Labels(JsonElement row) =>
        row.GetProperty("values").EnumerateArray().Select(v => v.GetProperty("label").GetString()).ToArray();

    private static async Task SaveAsync(PerformanceHarness h, object frame) =>
        Assert.Equal(HttpStatusCode.OK, (await h.SaveFrameAsync(frame)).StatusCode);

    private static JsonElement Availability(JsonElement dashboard, string metric) =>
        dashboard.GetProperty("availability").EnumerateArray().Single(m => m.GetProperty("metric").GetString() == metric);

    // Combination achieved, Over target

    [Fact]
    public async Task A_combination_shows_its_target_the_submissions_that_match_and_the_percentage()
    {
        await using var h = await CreateAsync(postgres);
        await SaveAsync(h, Frame(["municipality", "sex", "group/age"], (["cali", "f", "18_29"], 120), (["cali", "m", "18_29"], 100)));
        await h.AddManyAsync(90, Doc(("municipality", "cali"), ("sex", "f"), ("group/age", "18_29")));
        await h.AddManyAsync(130, Doc(("municipality", "cali"), ("sex", "m"), ("group/age", "18_29")));
        await h.AddManyAsync(5, Doc(("municipality", "cali"), ("sex", "f"), ("group/age", "30_44")));

        var dashboard = await h.GetAsync();
        var rows = Rows(dashboard);

        Assert.Equal("configured", CoverageOf(dashboard).GetProperty("state").GetString());
        Assert.Equal(["cali", "f", "18_29"], Codes(rows[0]));
        Assert.Equal(120, rows[0].GetProperty("target").GetInt32());
        Assert.Equal(90, rows[0].GetProperty("achieved").GetInt64());
        Assert.Equal(75, rows[0].GetProperty("percent").GetDouble(), 6);
        Assert.Equal(130, rows[1].GetProperty("achieved").GetInt64());
        Assert.Equal(130, rows[1].GetProperty("percent").GetDouble(), 6);
        Assert.Equal(5, CoverageOf(dashboard).GetProperty("outsideFrame").GetInt64());
        Assert.Equal(["municipality", "sex", "group/age"],
            CoverageOf(dashboard).GetProperty("dimensions").EnumerateArray().Select(d => d.GetProperty("field").GetString()));
        Assert.All(CoverageOf(dashboard).GetProperty("dimensions").EnumerateArray(), d => Assert.Equal(JsonValueKind.Null, d.GetProperty("warning").ValueKind));
    }

    [Fact]
    public async Task Case_and_surrounding_white_space_do_not_stop_a_match()
    {
        await using var h = await CreateAsync(postgres);
        await SaveAsync(h, Frame(["municipality"], (["Cali"], 10)));
        await h.AddAsync(Doc(("municipality", "cali")));
        await h.AddAsync(Doc(("municipality", " cali")));
        await h.AddAsync(Doc(("municipality", "CALI\t")));
        await h.AddAsync(Doc(("municipality", " Cali ")));
        await h.AddAsync(Doc(("municipality", "cal i")));

        var dashboard = await h.GetAsync();

        Assert.Equal(4, Rows(dashboard).Single().GetProperty("achieved").GetInt64());
        Assert.Equal(1, CoverageOf(dashboard).GetProperty("outsideFrame").GetInt64());
    }

    [Fact]
    public async Task A_target_of_zero_shows_what_was_achieved_and_no_percentage()
    {
        await using var h = await CreateAsync(postgres);
        await SaveAsync(h, Frame(["municipality"], (["cali"], 0)));
        await h.AddManyAsync(7, Doc(("municipality", "cali")));

        var row = Rows(await h.GetAsync()).Single();

        Assert.Equal(7, row.GetProperty("achieved").GetInt64());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("percent").ValueKind);
    }

    // Overall progress

    [Fact]
    public async Task Overall_progress_does_not_let_a_surplus_cover_a_shortfall()
    {
        await using var h = await CreateAsync(postgres);
        await SaveAsync(h, Frame(["municipality"], (["cali"], 100), (["bogota"], 100), (["pasto"], 0)));
        await h.AddManyAsync(150, Doc(("municipality", "cali")));
        await h.AddManyAsync(50, Doc(("municipality", "bogota")));
        await h.AddManyAsync(30, Doc(("municipality", "pasto")));

        var coverage = CoverageOf(await h.GetAsync());

        Assert.Equal(75, coverage.GetProperty("overallPercent").GetDouble(), 6);
    }

    [Fact]
    public async Task There_is_no_overall_progress_when_every_target_is_zero()
    {
        await using var h = await CreateAsync(postgres);
        await SaveAsync(h, Frame(["municipality"], (["cali"], 0), (["bogota"], 0)));
        await h.AddManyAsync(3, Doc(("municipality", "cali")));

        var coverage = CoverageOf(await h.GetAsync());

        Assert.Equal(JsonValueKind.Null, coverage.GetProperty("overallPercent").ValueKind);
        Assert.Equal(2, coverage.GetProperty("rows").GetArrayLength());
    }

    // Labels

    [Fact]
    public async Task Rows_show_the_codes_with_the_labels_of_the_form()
    {
        await using var h = await CreateAsync(postgres);
        await SaveAsync(h, Frame(["municipality", "sex", "group/age"], (["cali", "f", "18_29"], 120), (["cali", "f", "no_such_choice"], 1)));

        var rows = Rows(await h.GetAsync());

        Assert.Equal(["cali", "f", "18_29"], Codes(rows[0]));
        Assert.Equal(["Cali municipality", "Female", "18 to 29"], Labels(rows[0]));
        Assert.Equal(["Cali municipality", "Female", null], Labels(rows[1]));
    }

    [Fact]
    public async Task A_label_is_found_even_when_the_case_of_the_code_differs()
    {
        await using var h = await CreateAsync(postgres);
        await SaveAsync(h, Frame(["municipality"], (["Cali"], 10), (["  BOGOTA "], 10)));

        var rows = Rows(await h.GetAsync());

        Assert.Equal(["Cali"], Codes(rows[0]));
        Assert.Equal(["Cali municipality"], Labels(rows[0]));
        Assert.Equal(["Bogotá"], Labels(rows[1]));
    }

    // Outside the frame

    [Fact]
    public async Task A_lost_leading_zero_is_counted_outside_the_frame_and_listed()
    {
        await using var h = await CreateAsync(postgres);
        await SaveAsync(h, Frame(["municipality"], (["5001"], 100)));
        await h.AddManyAsync(40, Doc(("municipality", "05001")));
        await h.AddManyAsync(10, Doc(("municipality", "5001")));

        var coverage = CoverageOf(await h.GetAsync());

        Assert.Equal(40, coverage.GetProperty("outsideFrame").GetInt64());
        var top = coverage.GetProperty("outsideTop")[0];
        Assert.Equal("05001", top.GetProperty("values")[0].GetProperty("code").GetString());
        Assert.Equal(40, top.GetProperty("submissions").GetInt64());
        Assert.Equal(10, coverage.GetProperty("rows")[0].GetProperty("achieved").GetInt64());
    }

    [Fact]
    public async Task Submissions_that_leave_a_dimension_empty_are_outside_the_frame_with_their_other_answers_listed()
    {
        await using var h = await CreateAsync(postgres);
        await SaveAsync(h, Frame(["municipality", "sex"], (["cali", "f"], 10)));
        await h.AddManyAsync(2, Doc(("municipality", "cali"), ("sex", "f")));
        await h.AddManyAsync(3, Doc(("municipality", "cali")));
        await h.AddManyAsync(2, Doc(("municipality", "cali"), ("sex", "  ")));
        await h.AddManyAsync(1, Doc(("municipality", "Bogota"), ("sex", "f")));
        await h.AddManyAsync(1, Doc(("sex", "m")));

        var coverage = CoverageOf(await h.GetAsync());
        var top = coverage.GetProperty("outsideTop").EnumerateArray().ToList();

        Assert.Equal(2, Rows(await h.GetAsync()).Single().GetProperty("achieved").GetInt64());
        Assert.Equal(7, coverage.GetProperty("outsideFrame").GetInt64());
        Assert.Equal(5, top[0].GetProperty("submissions").GetInt64());
        Assert.Equal("cali", top[0].GetProperty("values")[0].GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, top[0].GetProperty("values")[1].ValueKind);
        Assert.Equal(3, top.Count);
        var bogota = top.Single(t => t.GetProperty("values")[0].ValueKind == JsonValueKind.Object && t.GetProperty("values")[0].GetProperty("code").GetString() == "Bogota");
        Assert.Equal("Bogotá", bogota.GetProperty("values")[0].GetProperty("label").GetString());
    }

    [Fact]
    public async Task Only_the_ten_most_frequent_outside_combinations_are_listed_but_all_are_counted()
    {
        await using var h = await CreateAsync(postgres);
        await SaveAsync(h, Frame(["municipality"], (["cali"], 10)));
        for (var i = 1; i <= 12; i++)
        {
            await h.AddManyAsync(i, Doc(("municipality", $"code{i:00}")));
        }

        var coverage = CoverageOf(await h.GetAsync());
        var top = coverage.GetProperty("outsideTop").EnumerateArray().ToList();

        Assert.Equal(78, coverage.GetProperty("outsideFrame").GetInt64());
        Assert.Equal(10, top.Count);
        Assert.Equal("code12", top[0].GetProperty("values")[0].GetProperty("code").GetString());
        Assert.Equal(12, top[0].GetProperty("submissions").GetInt64());
        Assert.Equal(3, top[9].GetProperty("submissions").GetInt64());
    }

    // Dimension warnings

    [Fact]
    public async Task A_question_removed_by_a_redeploy_is_a_warning_and_coverage_is_still_shown()
    {
        await using var h = await CreateAsync(postgres);
        await SaveAsync(h, Frame(["municipality", "sex"], (["cali", "f"], 10)));
        await h.ExecuteAsync("""UPDATE tamiza.projects SET form_fields = '[{"name":"sex","xpath":"sex","type":"select_one"}]'::jsonb""");
        await h.AddManyAsync(4, Doc(("municipality", "cali"), ("sex", "f")));

        var dashboard = await h.GetAsync();
        var dimensions = CoverageOf(dashboard).GetProperty("dimensions").EnumerateArray().ToList();

        Assert.Equal("configured", CoverageOf(dashboard).GetProperty("state").GetString());
        Assert.Equal("missingField", dimensions[0].GetProperty("warning").GetString());
        Assert.Equal(JsonValueKind.Null, dimensions[1].GetProperty("warning").ValueKind);
        Assert.Equal(4, Rows(dashboard).Single().GetProperty("achieved").GetInt64());
        var coverage = Availability(dashboard, "coverage");
        Assert.False(coverage.GetProperty("available").GetBoolean());
        Assert.Equal("updateSamplingFrame", coverage.GetProperty("hint").GetString());
        Assert.Equal(["municipality"], coverage.GetProperty("missingFields").EnumerateArray().Select(f => f.GetString()));
    }

    [Fact]
    public async Task A_multiple_choice_dimension_is_a_warning()
    {
        await using var h = await CreateAsync(postgres);
        await SaveAsync(h, Frame(["languages", "sex"], (["es", "f"], 10)));
        await h.AddManyAsync(2, Doc(("languages", "es en"), ("sex", "f")));

        var dimensions = CoverageOf(await h.GetAsync()).GetProperty("dimensions").EnumerateArray().ToList();

        Assert.Equal("multipleChoice", dimensions[0].GetProperty("warning").GetString());
        Assert.Equal(JsonValueKind.Null, dimensions[1].GetProperty("warning").ValueKind);
    }

    [Fact]
    public async Task A_dimension_inside_a_repeat_group_is_a_warning()
    {
        await using var h = await CreateAsync(postgres);
        await SaveAsync(h, Frame(["members/member_age"], (["18_29"], 10)));

        var dimension = CoverageOf(await h.GetAsync()).GetProperty("dimensions")[0];

        Assert.Equal("repeat", dimension.GetProperty("warning").GetString());
        Assert.Equal("members/member_age", dimension.GetProperty("field").GetString());
    }

    // No sampling frame

    [Theory]
    [InlineData("analyst")]
    [InlineData("viewer")]
    public async Task Without_a_frame_the_coverage_is_not_configured(string role)
    {
        await using var h = await CreateAsync(postgres);
        await h.AddManyAsync(3, Doc(("municipality", "cali")));

        var dashboard = await h.GetAsync(client: role == "analyst" ? h.Analyst : h.Viewer);
        var coverage = CoverageOf(dashboard);

        Assert.Equal("notConfigured", coverage.GetProperty("state").GetString());
        Assert.Equal(0, coverage.GetProperty("rows").GetArrayLength());
        Assert.Equal(0, coverage.GetProperty("dimensions").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, coverage.GetProperty("overallPercent").ValueKind);
        Assert.Equal(0, coverage.GetProperty("outsideFrame").GetInt64());
        Assert.Equal("defineSamplingFrame", Availability(dashboard, "coverage").GetProperty("hint").GetString());
        Assert.Equal(3, dashboard.GetProperty("submissions").GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task A_frame_with_every_dimension_set_to_a_known_question_makes_coverage_available()
    {
        await using var h = await CreateAsync(postgres);
        await SaveAsync(h, Frame(["municipality"], (["cali"], 10)));

        var dashboard = await h.GetAsync();

        Assert.True(Availability(dashboard, "coverage").GetProperty("available").GetBoolean());
    }
}
