using Tamiza.Api.Data;
using Tamiza.Api.Http;
using Tamiza.Api.Kobo;
using Tamiza.Api.Performance;

namespace Tamiza.Api.UnitTests.Performance;

public sealed class PerformanceResultsTests
{
    private static FormField Field(string type, string xpath, string? list = null, bool inRepeat = false) =>
        new(xpath, xpath, type, null, list, false, inRepeat);

    // Range validation

    [Fact]
    public void A_range_may_have_neither_bound_one_bound_or_both()
    {
        var errors = new FieldErrors();

        Assert.Equal((null, null), PerformanceResults.ParseRange(null, null, errors));
        Assert.Equal((null, null), PerformanceResults.ParseRange("", "  ", errors));
        Assert.Equal((new DateOnly(2026, 9, 1), null), PerformanceResults.ParseRange("2026-09-01", null, errors));
        Assert.Equal((null, new DateOnly(2026, 9, 7)), PerformanceResults.ParseRange(null, "2026-09-07", errors));
        Assert.Equal((new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 7)), PerformanceResults.ParseRange("2026-09-07", "2026-09-07", errors));
        Assert.Equal((DateOnly.MinValue, DateOnly.MaxValue), PerformanceResults.ParseRange("0001-01-01", "9999-12-31", errors));
        Assert.False(errors.Any);
    }

    [Theory]
    [InlineData("2026-9-1")]
    [InlineData("2026-02-30")]
    [InlineData("01/09/2026")]
    [InlineData("2026-09-01T10:00:00Z")]
    [InlineData("tomorrow")]
    public void A_date_that_is_not_yyyy_mm_dd_is_an_error_on_its_own_field(string text)
    {
        var errors = new FieldErrors();

        PerformanceResults.ParseRange(text, null, errors);
        PerformanceResults.ParseRange(null, text, errors);

        var all = errors.ToDictionary();
        Assert.Equal(["from", "to"], all.Keys.Order());
        Assert.All(all.Values, messages => Assert.Contains("YYYY-MM-DD", Assert.Single(messages)));
    }

    [Fact]
    public void A_first_day_after_the_last_is_an_error_on_the_first()
    {
        var errors = new FieldErrors();

        PerformanceResults.ParseRange("2026-09-07", "2026-09-01", errors);

        var all = errors.ToDictionary();
        Assert.Equal(["from"], all.Keys);
        Assert.Contains("first day is after the last", all["from"].Single());
    }

    // Overall progress

    [Fact]
    public void Overall_progress_caps_each_row_at_its_target()
    {
        var progress = PerformanceResults.OverallPercent([(100, 150), (100, 50)]);

        Assert.Equal(75, progress);
    }

    [Fact]
    public void Rows_with_a_target_of_zero_are_not_part_of_overall_progress()
    {
        Assert.Equal(50, PerformanceResults.OverallPercent([(100, 50), (0, 999)]));
    }

    [Fact]
    public void Overall_progress_is_null_when_no_row_has_a_target_above_zero()
    {
        Assert.Null(PerformanceResults.OverallPercent([(0, 5), (0, 0)]));
        Assert.Null(PerformanceResults.OverallPercent([]));
    }

    [Fact]
    public void Overall_progress_can_reach_exactly_100_and_never_exceeds_it()
    {
        Assert.Equal(100, PerformanceResults.OverallPercent([(10, 10), (5, 500)]));
    }

    [Fact]
    public void A_row_percent_is_not_capped_and_is_null_for_a_target_of_zero()
    {
        Assert.Equal(75, PerformanceResults.RowPercent(120, 90));
        Assert.Equal(130, PerformanceResults.RowPercent(100, 130));
        Assert.Equal(0, PerformanceResults.RowPercent(100, 0));
        Assert.Null(PerformanceResults.RowPercent(0, 7));
    }

    // Labels

    private static readonly FormChoice[] Choices =
    [
        new("municipalities", "cali", "Cali municipality"),
        new("municipalities", "Bogota", "Bogotá D.C."),
        new("municipalities", "bogota", "Bogotá (lower)"),
        new("municipalities", " pasto ", "Pasto"),
        new("sexes", "f", null),
    ];

    [Theory]
    [InlineData("cali", "Cali municipality")]
    [InlineData("Cali", "Cali municipality")]
    [InlineData("  CALI\t", "Cali municipality")]
    [InlineData(" cali", "Cali municipality")]
    [InlineData("pasto", "Pasto")]
    public void A_label_is_found_trimmed_and_ignoring_case(string code, string label)
    {
        Assert.Equal(label, new ChoiceLabels(Choices).Find("municipalities", code));
    }

    [Fact]
    public void An_exact_match_wins_when_two_choices_differ_only_in_case()
    {
        var labels = new ChoiceLabels(Choices);

        Assert.Equal("Bogotá D.C.", labels.Find("municipalities", "Bogota"));
        Assert.Equal("Bogotá (lower)", labels.Find("municipalities", "bogota"));
        Assert.Equal("Bogotá D.C.", labels.Find("municipalities", "BOGOTA"));
    }

    [Fact]
    public void A_code_without_a_label_a_choice_without_a_label_or_a_question_without_a_list_has_none()
    {
        var labels = new ChoiceLabels(Choices);

        Assert.Null(labels.Find("municipalities", "medellin"));
        Assert.Null(labels.Find("sexes", "f"));
        Assert.Null(labels.Find("sexes", "m"));
        Assert.Null(labels.Find("unknown_list", "cali"));
        Assert.Null(labels.Find(null, "cali"));
    }

    // Dimension warnings and keys

    [Fact]
    public void A_dimension_warns_for_a_missing_field_a_repeat_or_a_multiple_choice_question()
    {
        FormField[] fields =
        [
            Field("select_one", "municipality"),
            Field("select_multiple", "languages"),
            Field("select_multiple_langs", "dialects"),
            Field("select_one", "members/member_age", inRepeat: true),
            Field("select_multiple", "members/hobbies", inRepeat: true),
        ];

        Assert.Null(PerformancePlan.WarningFor(fields, "municipality"));
        Assert.Equal("missingField", PerformancePlan.WarningFor(fields, "removed"));
        Assert.Equal("multipleChoice", PerformancePlan.WarningFor(fields, "languages"));
        Assert.Equal("multipleChoice", PerformancePlan.WarningFor(fields, "dialects"));
        Assert.Equal("repeat", PerformancePlan.WarningFor(fields, "members/member_age"));
        Assert.Equal("repeat", PerformancePlan.WarningFor(fields, "members/hobbies"));
    }

    [Fact]
    public void The_plan_resolves_the_keys_of_the_time_the_enumerator_and_the_dimensions()
    {
        FormField[] fields =
        [
            Field("start", "grp/start"),
            Field("end", "grp/end"),
            Field("username", "username"),
            Field("text", "enumerator_code"),
            Field("select_one", "municipality"),
        ];
        SamplingDimension[] dimensions = [new("Municipality", "municipality"), new("Gone", "gone")];

        var plan = PerformancePlan.Create(fields, "enumerator_code", dimensions);

        Assert.Equal("grp/start", plan.StartKey);
        Assert.Equal("grp/end", plan.EndKey);
        Assert.Equal("enumerator_code", plan.EnumeratorKey);
        Assert.Equal([("Municipality", "municipality", (string?)null), ("Gone", "gone", "missingField")],
            plan.Dimensions.Select(d => (d.Name, d.Field, d.Warning)));
    }

    [Fact]
    public void Without_start_end_or_an_enumerator_their_keys_are_null()
    {
        var plan = PerformancePlan.Create([Field("select_one", "municipality")], "removed", null);

        Assert.Null(plan.StartKey);
        Assert.Null(plan.EndKey);
        Assert.Null(plan.EnumeratorKey);
        Assert.Empty(plan.Dimensions);
    }

    // Assembly

    private static RawFigures Raw(IReadOnlyList<long>? achieved = null) => new(
        DateTimeOffset.Parse("2026-09-01T10:00:00Z"), 10, 1, [new(new DateOnly(2026, 9, 1), 9)],
        8, 2, 1, 1500, 1400, 600, 2400,
        [new("ana", 6, 1300, 1, DateTimeOffset.Parse("2026-09-01T09:00:00Z")), new(null, 4, null, 0, null)],
        achieved ?? [90, 130, 5], 12, [new(["05001", null], 7)]);

    private static PerformanceInputs Inputs(IReadOnlyList<FormField> fields, FrameInput? frame, string? enumerator = null) =>
        new(Guid.NewGuid(), null, null, "America/Bogota", fields, Choices, enumerator, DateTimeOffset.UtcNow, frame, null, null);

    private static readonly FormField[] FullForm =
    [
        Field("start", "start"), Field("end", "end"), Field("username", "username"),
        Field("select_one", "municipality", "municipalities"), Field("select_one", "sex", "sexes"),
    ];

    private static FrameInput Frame(params (string[] Values, int Target)[] rows) => new(
        [new("Municipality", "municipality"), new("Sex", "sex")],
        rows.Select(r => new SamplingTarget(r.Values, r.Target)).ToList(),
        DateTimeOffset.UtcNow);

    [Fact]
    public void Without_a_frame_the_coverage_is_not_configured()
    {
        var result = PerformanceResults.Build(Inputs(FullForm, null), Raw());

        Assert.Equal("notConfigured", result.Coverage.State);
        Assert.Empty(result.Coverage.Rows);
        Assert.Null(result.Coverage.OverallPercent);
        Assert.Equal(0, result.Coverage.OutsideFrame);
        Assert.Equal("America/Bogota", result.TimeZone);
        Assert.Equal(10, result.Submissions.Total);
    }

    [Fact]
    public void A_frame_gives_rows_with_labels_percentages_overall_progress_and_the_outside_list()
    {
        var frame = Frame((["Cali", "f"], 120), (["bogota", "m"], 100), (["pasto", "m"], 0));

        var coverage = PerformanceResults.Build(Inputs(FullForm, frame), Raw()).Coverage;

        Assert.Equal("configured", coverage.State);
        Assert.Equal([90L, 130L, 5L], coverage.Rows.Select(r => r.Achieved));
        Assert.Equal([75.0, 130.0, null], coverage.Rows.Select(r => r.Percent));
        Assert.Equal("Cali municipality", coverage.Rows[0].Values[0].Label);
        Assert.Equal("Cali", coverage.Rows[0].Values[0].Code);
        Assert.Null(coverage.Rows[0].Values[1].Label); // the choice has no label
        Assert.Equal("Bogotá (lower)", coverage.Rows[1].Values[0].Label);
        // (90 + 100) / (120 + 100): the row with a target of 0 is not part of it.
        Assert.Equal(190.0 / 220 * 100, coverage.OverallPercent!.Value, 9);
        Assert.Equal(12, coverage.OutsideFrame);
        var outside = Assert.Single(coverage.OutsideTop);
        Assert.Equal("05001", outside.Values[0]!.Code);
        Assert.Null(outside.Values[1]);
        Assert.Equal(7, outside.Submissions);
    }

    [Fact]
    public void Completion_and_enumerators_follow_the_field_check()
    {
        var full = PerformanceResults.Build(Inputs(FullForm, null), Raw());
        var noEnd = PerformanceResults.Build(Inputs([Field("start", "start"), Field("select_one", "municipality")], null), Raw());

        Assert.True(full.CompletionTime.Available);
        Assert.Equal(8, full.CompletionTime.UsableDurations);
        Assert.Equal(2, full.CompletionTime.UnusableDurations);
        Assert.Equal(1400, full.CompletionTime.MeanWithoutOutliersSeconds);
        Assert.True(full.Enumerators.Available);
        Assert.Equal(2, full.Enumerators.Rows.Count);
        Assert.Null(full.Enumerators.Rows[1].Name);

        Assert.False(noEnd.CompletionTime.Available);
        Assert.Equal(["end"], noEnd.CompletionTime.MissingFields);
        Assert.Null(noEnd.CompletionTime.UsableDurations);
        Assert.Null(noEnd.CompletionTime.MedianSeconds);
        Assert.False(noEnd.Enumerators.Available);
        Assert.Equal(FieldCheck.SelectEnumeratorFieldHint, noEnd.Enumerators.Hint);
        Assert.Empty(noEnd.Enumerators.Rows);
        Assert.Equal(4, noEnd.Availability.Count);
    }

    [Fact]
    public void A_frame_row_the_statement_did_not_report_counts_zero()
    {
        var coverage = PerformanceResults.Build(Inputs(FullForm, Frame((["cali", "f"], 10), (["cali", "m"], 10))), Raw([4])).Coverage;

        Assert.Equal([4L, 0L], coverage.Rows.Select(r => r.Achieved));
    }
}
