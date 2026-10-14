using Tamiza.Api.Data;

namespace Tamiza.Api.Kobo;

/// <summary>Whether a performance-dashboard metric can be computed from the form, and what is missing if not.</summary>
public sealed record MetricAvailability(string Metric, bool Available, IReadOnlyList<string> MissingFields, string? Hint);

public static class FieldCheck
{
    public const string DailyEvolution = "dailyEvolution";
    public const string CompletionTime = "completionTime";
    public const string PerEnumerator = "perEnumerator";
    public const string Coverage = "coverage";
    public const string SelectEnumeratorFieldHint = "selectEnumeratorField";
    public const string DefineSamplingFrameHint = "defineSamplingFrame";
    public const string UpdateSamplingFrameHint = "updateSamplingFrame";

    /// <summary>
    /// <paramref name="frameDimensions"/> are the dimensions of the project's sampling frame, or null when it has none.
    /// Coverage is available once there is a frame whose dimensions all map to fields the form still has.
    /// </summary>
    public static IReadOnlyList<MetricAvailability> Evaluate(
        IReadOnlyList<FormField> fields, string? enumeratorField, IReadOnlyList<SamplingDimension>? frameDimensions)
    {
        bool HasType(string type) => fields.Any(f => f.Type == type);

        var missingTimes = new[] { "start", "end" }.Where(type => !HasType(type)).ToArray();
        var hasEnumerator = EnumeratorKey(fields, enumeratorField) is not null;
        var missingFrameFields = frameDimensions is null
            ? []
            : frameDimensions.Select(d => d.Field).Where(field => fields.All(f => f.Xpath != field)).Distinct().ToArray();
        var coverage = frameDimensions is null
            ? new MetricAvailability(Coverage, false, [], DefineSamplingFrameHint)
            : new MetricAvailability(Coverage, missingFrameFields.Length == 0, missingFrameFields, missingFrameFields.Length == 0 ? null : UpdateSamplingFrameHint);

        return
        [
            new(DailyEvolution, true, [], null),
            new(CompletionTime, missingTimes.Length == 0, missingTimes, null),
            new(PerEnumerator, hasEnumerator, hasEnumerator ? [] : ["username"], hasEnumerator ? null : SelectEnumeratorFieldHint),
            coverage,
        ];
    }

    /// <summary>
    /// The key of the answer that identifies the enumerator in a submission: the question an admin selected while the
    /// form still has it, otherwise the <c>username</c> field; null when neither exists.
    /// </summary>
    public static string? EnumeratorKey(IReadOnlyList<FormField> fields, string? enumeratorField) =>
        enumeratorField is not null && fields.Any(f => f.Xpath == enumeratorField)
            ? enumeratorField
            : fields.FirstOrDefault(f => f.Type == "username")?.Xpath;

    /// <summary>The keys of the <c>start</c> and <c>end</c> answers in a submission; null for one the form lacks.</summary>
    public static (string? Start, string? End) TimeKeys(IReadOnlyList<FormField> fields) =>
        (fields.FirstOrDefault(f => f.Type == "start")?.Xpath, fields.FirstOrDefault(f => f.Type == "end")?.Xpath);
}
