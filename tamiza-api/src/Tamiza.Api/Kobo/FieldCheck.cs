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

    public static IReadOnlyList<MetricAvailability> Evaluate(IReadOnlyList<FormField> fields, string? enumeratorField)
    {
        bool HasType(string type) => fields.Any(f => f.Type == type);

        var missingTimes = new[] { "start", "end" }.Where(type => !HasType(type)).ToArray();
        var hasEnumerator = HasType("username") || (enumeratorField is not null && fields.Any(f => f.Xpath == enumeratorField));

        return
        [
            new(DailyEvolution, true, [], null),
            new(CompletionTime, missingTimes.Length == 0, missingTimes, null),
            new(PerEnumerator, hasEnumerator, hasEnumerator ? [] : ["username"], hasEnumerator ? null : SelectEnumeratorFieldHint),
            new(Coverage, true, [], null),
        ];
    }
}
