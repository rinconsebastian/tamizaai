namespace Tamiza.Api.Data;

/// <summary>The project's sampling frame, replaced as a whole.</summary>
public sealed class SamplingFrame
{
    public Guid ProjectId { get; set; }

    public List<SamplingDimension> Dimensions { get; set; } = [];

    public List<SamplingTarget> Targets { get; set; } = [];

    public Guid UpdatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A dimension and the form field (by XPath) it maps to.</summary>
public sealed record SamplingDimension(string Name, string Field);

/// <summary>Target surveys for one combination; <see cref="Values"/> follow the dimension order.</summary>
public sealed record SamplingTarget(IReadOnlyList<string> Values, int Target);
