using System.Globalization;
using Tamiza.Api.Data;
using Tamiza.Api.Http;
using Tamiza.Api.Kobo;

namespace Tamiza.Api.Performance;

/// <summary>The sampling frame as the computation sees it.</summary>
public sealed record FrameInput(IReadOnlyList<SamplingDimension> Dimensions, IReadOnlyList<SamplingTarget> Targets, DateTimeOffset UpdatedAt);

/// <summary>Everything one computation reads from the project, taken in the request that asks for it.</summary>
public sealed record PerformanceInputs(
    Guid ProjectId,
    DateOnly? From,
    DateOnly? To,
    string TimeZone,
    IReadOnlyList<FormField> Fields,
    IReadOnlyList<FormChoice> Choices,
    string? EnumeratorField,
    DateTimeOffset FormCheckedAt,
    FrameInput? Frame,
    DateTimeOffset? LastWebhookAt,
    DateTimeOffset? LastSyncFinishedAt);

/// <summary>Where the answers are in a submission, resolved from the form snapshot (design D4, step 1).</summary>
public sealed record PerformancePlan(string? StartKey, string? EndKey, string? EnumeratorKey, IReadOnlyList<CoverageDimension> Dimensions)
{
    public const string MissingField = "missingField";
    public const string Repeat = "repeat";
    public const string MultipleChoice = "multipleChoice";

    public static PerformancePlan Create(IReadOnlyList<FormField> fields, string? enumeratorField, IReadOnlyList<SamplingDimension>? frameDimensions)
    {
        var (start, end) = FieldCheck.TimeKeys(fields);
        var dimensions = (frameDimensions ?? []).Select(d => new CoverageDimension(d.Name, d.Field, WarningFor(fields, d.Field))).ToList();
        return new PerformancePlan(start, end, FieldCheck.EnumeratorKey(fields, enumeratorField), dimensions);
    }

    /// <summary>Why a dimension cannot be counted reliably, or null when it can.</summary>
    public static string? WarningFor(IReadOnlyList<FormField> fields, string xpath)
    {
        var field = fields.FirstOrDefault(f => f.Xpath == xpath);
        if (field is null)
        {
            return MissingField;
        }

        if (field.InRepeat)
        {
            return Repeat;
        }

        return field.Type.StartsWith("select_multiple", StringComparison.Ordinal) ? MultipleChoice : null;
    }
}

/// <summary>What the statement returns, before labels and the overall progress are added.</summary>
public sealed record RawFigures(
    DateTimeOffset ComputedAt,
    long Total,
    long WithoutReceptionTime,
    IReadOnlyList<DayCount> Days,
    long Usable,
    long Unusable,
    long Outliers,
    double? Median,
    double? MeanWithoutOutliers,
    double? LowerFence,
    double? UpperFence,
    IReadOnlyList<RawEnumerator> Enumerators,
    IReadOnlyList<long> Achieved,
    long OutsideFrame,
    IReadOnlyList<RawOutside> OutsideTop);

public sealed record RawEnumerator(string? Name, long Submissions, double? MeanSeconds, long Outliers, DateTimeOffset? LastSubmissionAt);

public sealed record RawOutside(IReadOnlyList<string?> Values, long Submissions);

/// <summary>The pure parts of the dashboard: range validation, labels, overall progress and the assembly of the response.</summary>
public static class PerformanceResults
{
    /// <summary>White space removed from answers and codes: the characters of the frame's own trimming plus the non-breaking space.</summary>
    public static readonly char[] TrimChars = [' ', '\t', '\r', '\n', ' '];

    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>Reads the optional <c>from</c> and <c>to</c> days. Problems go to <paramref name="errors"/> under those field names.</summary>
    public static (DateOnly? From, DateOnly? To) ParseRange(string? from, string? to, FieldErrors errors)
    {
        var first = ParseDay(from, "from", errors);
        var last = ParseDay(to, "to", errors);
        if (first is { } a && last is { } b && a > b)
        {
            errors.Add("from", "The first day is after the last day.");
        }

        return (first, last);
    }

    private static DateOnly? ParseDay(string? text, string field, FieldErrors errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateOnly.TryParseExact(text.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return day;
        }

        errors.Add(field, "Enter a date as YYYY-MM-DD.");
        return null;
    }

    /// <summary>
    /// Sum of the lesser of achieved and target over the rows with a target above 0, divided by the sum of those targets,
    /// as a percentage; null when no row has a target above 0.
    /// </summary>
    public static double? OverallPercent(IEnumerable<(int Target, long Achieved)> rows)
    {
        long targets = 0;
        long counted = 0;
        foreach (var (target, achieved) in rows)
        {
            if (target > 0)
            {
                targets += target;
                counted += Math.Min(achieved, target);
            }
        }

        return targets == 0 ? null : counted * 100.0 / targets;
    }

    /// <summary>Achieved over target as a percentage, not capped; null when the target is 0.</summary>
    public static double? RowPercent(int target, long achieved) => target > 0 ? achieved * 100.0 / target : null;

    public static PerformanceDashboardResult Build(PerformanceInputs inputs, RawFigures raw)
    {
        var availability = FieldCheck.Evaluate(inputs.Fields, inputs.EnumeratorField, inputs.Frame?.Dimensions);
        var plan = PerformancePlan.Create(inputs.Fields, inputs.EnumeratorField, inputs.Frame?.Dimensions);
        var completion = availability.Single(m => m.Metric == FieldCheck.CompletionTime);
        var enumerators = availability.Single(m => m.Metric == FieldCheck.PerEnumerator);

        var completionSection = completion.Available
            ? new CompletionTimeSection(true, [], raw.Usable, raw.Unusable, raw.Outliers, raw.Median, raw.MeanWithoutOutliers, raw.LowerFence, raw.UpperFence)
            : new CompletionTimeSection(false, completion.MissingFields, null, null, null, null, null, null, null);
        var enumeratorSection = enumerators.Available
            ? new EnumeratorsSection(true, null,
                raw.Enumerators.Select(e => new EnumeratorRow(e.Name, e.Submissions, e.MeanSeconds, e.Outliers, e.LastSubmissionAt)).ToList())
            : new EnumeratorsSection(false, enumerators.Hint, []);

        return new PerformanceDashboardResult(
            raw.ComputedAt,
            inputs.TimeZone,
            inputs.From,
            inputs.To,
            availability,
            new SubmissionsSection(raw.Total, raw.WithoutReceptionTime, raw.Days),
            completionSection,
            enumeratorSection,
            BuildCoverage(inputs, plan, raw));
    }

    private static CoverageSection BuildCoverage(PerformanceInputs inputs, PerformancePlan plan, RawFigures raw)
    {
        if (inputs.Frame is not { } frame)
        {
            return new CoverageSection("notConfigured", [], [], null, 0, []);
        }

        var labels = new ChoiceLabels(inputs.Choices);
        var lists = plan.Dimensions.Select(d => inputs.Fields.FirstOrDefault(f => f.Xpath == d.Field)?.ListName).ToList();
        CoverageValue Value(int dimension, string code) => new(code, labels.Find(lists[dimension], code));

        var rows = frame.Targets.Select((target, i) =>
            {
                var achieved = i < raw.Achieved.Count ? raw.Achieved[i] : 0;
                return new CoverageRow(
                    target.Values.Select((code, dimension) => Value(dimension, code)).ToList(),
                    target.Target,
                    achieved,
                    RowPercent(target.Target, achieved));
            })
            .ToList();
        var outside = raw.OutsideTop
            .Select(o => new OutsideCombination(o.Values.Select((code, dimension) => code is null ? null : Value(dimension, code)).ToList(), o.Submissions))
            .ToList();
        return new CoverageSection("configured", plan.Dimensions, rows, OverallPercent(rows.Select(r => (r.Target, r.Achieved))), raw.OutsideFrame, outside);
    }
}

/// <summary>
/// The labels of the form's choices, found by list and code with the same comparison as answers: white space removed, case
/// ignored, and an exact match winning when two choices differ only in case.
/// </summary>
public sealed class ChoiceLabels
{
    private readonly Dictionary<string, Dictionary<string, string?>> _exact = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string?>> _ignoringCase = new(StringComparer.Ordinal);

    public ChoiceLabels(IEnumerable<FormChoice> choices)
    {
        foreach (var choice in choices)
        {
            var name = choice.Name.Trim(PerformanceResults.TrimChars);
            Add(_exact, StringComparer.Ordinal, choice.ListName, name, choice.Label);
            Add(_ignoringCase, StringComparer.OrdinalIgnoreCase, choice.ListName, name, choice.Label);
        }
    }

    private static void Add(Dictionary<string, Dictionary<string, string?>> lists, StringComparer comparer, string list, string name, string? label)
    {
        if (!lists.TryGetValue(list, out var names))
        {
            lists[list] = names = new Dictionary<string, string?>(comparer);
        }

        names.TryAdd(name, label);
    }

    /// <summary>The label of <paramref name="code"/> in <paramref name="list"/>, or null when there is none.</summary>
    public string? Find(string? list, string code)
    {
        if (list is null)
        {
            return null;
        }

        var name = code.Trim(PerformanceResults.TrimChars);
        if (_exact.TryGetValue(list, out var exact) && exact.TryGetValue(name, out var label))
        {
            return label;
        }

        return _ignoringCase.TryGetValue(list, out var loose) && loose.TryGetValue(name, out label) ? label : null;
    }
}
