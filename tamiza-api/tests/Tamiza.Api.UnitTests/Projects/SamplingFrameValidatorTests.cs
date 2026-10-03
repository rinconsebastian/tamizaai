using Tamiza.Api.Http;
using Tamiza.Api.Projects;

namespace Tamiza.Api.UnitTests.Projects;

public sealed class SamplingFrameValidatorTests
{
    private static readonly HashSet<string> Fields = ["municipality", "sex"];

    private static IDictionary<string, string[]> Errors(SamplingFrameInput input)
    {
        var errors = new FieldErrors();
        SamplingFrameValidator.Validate(input, Fields, errors);
        return errors.ToDictionary();
    }

    private static SamplingFrameInput Frame(params TargetInput[] targets) =>
        new([new("Municipality", "municipality"), new("Sex", "sex")], targets.ToList());

    private static TargetInput Row(string? a, string? b, decimal? target) => new([a, b], target);

    [Fact]
    public void A_valid_frame_has_no_errors_and_values_are_trimmed()
    {
        var errors = new FieldErrors();
        var (dimensions, targets) = SamplingFrameValidator.Validate(Frame(Row(" Bogotá ", "F", 120), Row("Bogotá", "M", 0)), Fields, errors);

        Assert.False(errors.Any);
        Assert.Equal(2, dimensions.Count);
        Assert.Equal(["Bogotá", "F"], targets[0].Values);
        Assert.Equal(120, targets[0].Target);
    }

    [Fact]
    public void Duplicate_dimension_names_are_rejected_ignoring_case() =>
        Assert.Contains("dimensions[1].name",
            Errors(new SamplingFrameInput([new("Sex", "sex"), new(" sex ", "municipality")], [new(["F", "Cali"], 1)])).Keys);

    [Fact]
    public void A_dimension_mapped_to_an_unknown_field_names_both()
    {
        var errors = Errors(new SamplingFrameInput([new("Age", "age")], [new(["18-24"], 1)]));
        Assert.Contains("'Age'", errors["dimensions[0].field"][0]);
        Assert.Contains("'age'", errors["dimensions[0].field"][0]);
    }

    [Fact]
    public void Missing_values_are_reported_per_row() =>
        Assert.Contains("targets[1].values", Errors(Frame(Row("Cali", "F", 1), Row("Cali", " ", 2))).Keys);

    [Theory]
    [InlineData(-1)]
    [InlineData(2.5)]
    [InlineData(1_000_001)]
    public void Targets_must_be_whole_numbers_in_range(double target) =>
        Assert.Contains("targets[0].target", Errors(Frame(Row("Cali", "F", (decimal)target))).Keys);

    [Fact]
    public void A_missing_target_is_rejected() =>
        Assert.Contains("targets[0].target", Errors(Frame(Row("Cali", "F", null))).Keys);

    [Fact]
    public void Repeated_combinations_are_rejected_trimmed_and_ignoring_case()
    {
        var errors = Errors(Frame(Row("Cali", "F", 1), Row(" CALI", "f ", 2)));
        Assert.Contains("repeats row 1", errors["targets[1]"][0]);
    }

    [Fact]
    public void Dimension_and_row_limits_are_enforced()
    {
        var tooManyDimensions = new SamplingFrameInput(
            Enumerable.Range(0, 11).Select(i => new DimensionInput($"d{i}", "sex")).ToList(), []);
        Assert.Contains("dimensions", Errors(tooManyDimensions).Keys);
        Assert.Contains("dimensions", Errors(new SamplingFrameInput([], [])).Keys);

        var tooManyRows = new SamplingFrameInput([new("Sex", "sex")],
            Enumerable.Range(0, 10_001).Select(i => new TargetInput([$"v{i}"], 1)).ToList());
        Assert.Contains("targets", Errors(tooManyRows).Keys);
    }
}
