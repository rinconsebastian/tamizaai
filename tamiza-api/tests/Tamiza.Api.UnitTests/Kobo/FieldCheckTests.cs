using Tamiza.Api.Data;
using Tamiza.Api.Kobo;

namespace Tamiza.Api.UnitTests.Kobo;

public sealed class FieldCheckTests
{
    private static FormField Field(string type, string xpath) => new(xpath, xpath, type, null);

    private static MetricAvailability Metric(IReadOnlyList<FormField> fields, string metric, string? enumerator = null) =>
        FieldCheck.Evaluate(fields, enumerator).Single(m => m.Metric == metric);

    [Fact]
    public void A_complete_form_has_every_metric_available()
    {
        var fields = new[] { Field("start", "start"), Field("end", "end"), Field("username", "username") };
        Assert.All(FieldCheck.Evaluate(fields, null), metric => Assert.True(metric.Available));
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
    public void Daily_evolution_and_coverage_are_always_available()
    {
        Assert.True(Metric([], FieldCheck.DailyEvolution).Available);
        Assert.True(Metric([], FieldCheck.Coverage).Available);
    }
}
