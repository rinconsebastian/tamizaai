using Tamiza.Api.Kobo;

namespace Tamiza.Api.Performance;

/// <summary>
/// The performance dashboard of a project (<c>GET /api/v1/projects/{id}/performance</c>), computed from the stored,
/// non-deleted submissions. Durations are in seconds. See <c>specs/dashboard-performance</c> of the change.
/// </summary>
/// <param name="ComputedAt">When the statement that produced the figures started; also what the cache expiry counts from.</param>
/// <param name="TimeZone">The project's time zone, which defines the days.</param>
/// <param name="From">First day of the range asked for, or null.</param>
/// <param name="To">Last day of the range asked for, or null.</param>
/// <param name="Availability">The same field check the Overview shows.</param>
public sealed record PerformanceDashboardResult(
    DateTimeOffset ComputedAt,
    string TimeZone,
    DateOnly? From,
    DateOnly? To,
    IReadOnlyList<MetricAvailability> Availability,
    SubmissionsSection Submissions,
    CompletionTimeSection CompletionTime,
    EnumeratorsSection Enumerators,
    CoverageSection Coverage);

/// <param name="Total">Submissions counted (all of them, including those without a reception time unless a range is set).</param>
/// <param name="WithoutReceptionTime">Submissions with no <c>_submission_time</c>; they belong to no day.</param>
/// <param name="Days">From the first to the last day with submissions, zero days between them included.</param>
public sealed record SubmissionsSection(long Total, long WithoutReceptionTime, IReadOnlyList<DayCount> Days);

public sealed record DayCount(DateOnly Day, long Count);

/// <summary>Figures are null when <see cref="Available"/> is false, or when no usable duration exists.</summary>
public sealed record CompletionTimeSection(
    bool Available,
    IReadOnlyList<string> MissingFields,
    long? UsableDurations,
    long? UnusableDurations,
    long? Outliers,
    double? MedianSeconds,
    double? MeanWithoutOutliersSeconds,
    double? LowerFenceSeconds,
    double? UpperFenceSeconds);

/// <param name="Hint"><see cref="FieldCheck.SelectEnumeratorFieldHint"/> when unavailable, otherwise null.</param>
public sealed record EnumeratorsSection(bool Available, string? Hint, IReadOnlyList<EnumeratorRow> Rows);

/// <param name="Name">One of the spellings seen, or null for submissions with no enumerator.</param>
public sealed record EnumeratorRow(string? Name, long Submissions, double? MeanSeconds, long Outliers, DateTimeOffset? LastSubmissionAt);

/// <param name="State"><c>notConfigured</c> (no sampling frame) or <c>configured</c>.</param>
/// <param name="Dimensions">The frame's dimensions with their warnings, in frame order.</param>
/// <param name="Rows">The frame's rows in frame order; values follow <paramref name="Dimensions"/>.</param>
/// <param name="OverallPercent">0 to 100 (75 means 75%). Null when no row has a target above 0.</param>
/// <param name="OutsideFrame">Submissions matching no row, or leaving a dimension empty.</param>
/// <param name="OutsideTop">The most frequent answer combinations outside the frame, up to 10.</param>
public sealed record CoverageSection(
    string State,
    IReadOnlyList<CoverageDimension> Dimensions,
    IReadOnlyList<CoverageRow> Rows,
    double? OverallPercent,
    long OutsideFrame,
    IReadOnlyList<OutsideCombination> OutsideTop);

/// <param name="Warning"><c>missingField</c>, <c>repeat</c>, <c>multipleChoice</c>, or null when it can be counted.</param>
public sealed record CoverageDimension(string Name, string Field, string? Warning);

/// <param name="Label">The choice's label in the form's default language, or null.</param>
public sealed record CoverageValue(string Code, string? Label);

/// <param name="Percent">100 means the target (75 means 75%). Not capped, so 130 is possible; null when the target is 0.</param>
public sealed record CoverageRow(IReadOnlyList<CoverageValue> Values, int Target, long Achieved, double? Percent);

/// <param name="Values">One per dimension; null where the answer was empty.</param>
public sealed record OutsideCombination(IReadOnlyList<CoverageValue?> Values, long Submissions);
