using Tamiza.Api.Data;
using Tamiza.Api.Kobo;

namespace Tamiza.Api.UnitTests.Kobo;

public sealed class FieldCheckTests
{
    private static FormField Field(string type, string xpath) => new(xpath, xpath, type, null);

    private static readonly IReadOnlyList<SamplingDimension> Frame = [new("Municipality", "municipality")];

    private static MetricAvailability Metric(
        IReadOnlyList<FormField> fields, string metric, string? enumerator = null, IReadOnlyList<SamplingDimension>? frame = null) =>
        FieldCheck.Evaluate(fields, enumerator, frame ?? Frame).Single(m => m.Metric == metric);

    [Fact]
    public void A_complete_form_with_a_frame_has_every_metric_available()
    {
        var fields = new[] { Field("start", "start"), Field("end", "end"), Field("username", "username"), Field("select_one", "municipality") };
        Assert.All(FieldCheck.Evaluate(fields, null, Frame), metric => Assert.True(metric.Available));
    }

    [Fact]
    public void Missing_start_or_end_makes_completion_time_unavailable_and_names_them()
    {
        var metric = Metric([Field("end", "end"), Field("username", "username")], FieldCheck.CompletionTime);
        Assert.False(metric.Available);
        Assert.Equal(["start"], metric.MissingFields);

        Assert.Equal(["start", "end"], Metric([], FieldCheck.CompletionTime).MissingFields);
    }

    [Fact]
    public void Without_username_the_enumerator_detail_needs_a_selected_question()
    {
        var fields = new[] { Field("start", "start"), Field("end", "end"), Field("text", "survey/enumerator") };

        var unselected = Metric(fields, FieldCheck.PerEnumerator);
        Assert.False(unselected.Available);
        Assert.Equal(FieldCheck.SelectEnumeratorFieldHint, unselected.Hint);

        Assert.True(Metric(fields, FieldCheck.PerEnumerator, "survey/enumerator").Available);
        Assert.False(Metric(fields, FieldCheck.PerEnumerator, "survey/removed_question").Available);
    }

    [Fact]
    public void The_selected_question_is_the_enumerator_key_even_when_the_form_has_username()
    {
        var fields = new[] { Field("username", "username"), Field("text", "enumerator_code") };

        Assert.Equal("enumerator_code", FieldCheck.EnumeratorKey(fields, "enumerator_code"));
        Assert.Equal("username", FieldCheck.EnumeratorKey(fields, null));
    }

    [Fact]
    public void A_selected_question_the_form_no_longer_has_falls_back_to_username_or_to_none()
    {
        var withUsername = new[] { Field("username", "username") };
        var without = new[] { Field("text", "other") };

        Assert.Equal("username", FieldCheck.EnumeratorKey(withUsername, "enumerator_code"));
        Assert.True(Metric(withUsername, FieldCheck.PerEnumerator, "enumerator_code").Available);
        Assert.Null(FieldCheck.EnumeratorKey(without, "enumerator_code"));
        Assert.False(Metric(without, FieldCheck.PerEnumerator, "enumerator_code").Available);
    }

    [Fact]
    public void The_time_keys_are_the_paths_of_the_start_and_end_fields_also_inside_a_group()
    {
        Assert.Equal(("meta/start", "end"), FieldCheck.TimeKeys([Field("start", "meta/start"), Field("end", "end")]));
        Assert.Equal((null, "end"), FieldCheck.TimeKeys([Field("end", "end")]));
    }

    [Fact]
    public void Daily_evolution_is_always_available()
    {
        Assert.True(FieldCheck.Evaluate([], null, null).Single(m => m.Metric == FieldCheck.DailyEvolution).Available);
    }

    [Fact]
    public void Coverage_is_unavailable_without_a_frame_and_suggests_defining_one()
    {
        var metric = FieldCheck.Evaluate([Field("select_one", "municipality")], null, null).Single(m => m.Metric == FieldCheck.Coverage);

        Assert.False(metric.Available);
        Assert.Equal(FieldCheck.DefineSamplingFrameHint, metric.Hint);
        Assert.Empty(metric.MissingFields);
    }

    [Fact]
    public void Coverage_is_unavailable_when_a_dimension_maps_to_a_field_the_form_no_longer_has_and_names_it()
    {
        var frame = new SamplingDimension[] { new("Municipality", "municipality"), new("Sex", "sex") };

        var metric = Metric([Field("select_one", "sex")], FieldCheck.Coverage, frame: frame);

        Assert.False(metric.Available);
        Assert.Equal(["municipality"], metric.MissingFields);
        Assert.Equal(FieldCheck.UpdateSamplingFrameHint, metric.Hint);
    }

    [Fact]
    public void Coverage_is_available_once_every_dimension_maps_to_a_field_of_the_form()
    {
        var metric = Metric([Field("select_one", "municipality")], FieldCheck.Coverage);

        Assert.True(metric.Available);
        Assert.Null(metric.Hint);
    }
}
