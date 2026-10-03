using System.Globalization;
using Tamiza.Api.Data;
using Tamiza.Api.Http;

namespace Tamiza.Api.Projects;

public sealed record DimensionInput(string? Name, string? Field);

/// <summary><see cref="Target"/> is a decimal so non-integers reach validation instead of failing deserialization.</summary>
public sealed record TargetInput(List<string?>? Values, decimal? Target);

public sealed record SamplingFrameInput(List<DimensionInput>? Dimensions, List<TargetInput>? Targets);

/// <summary>Rules shared by saving a frame and previewing an imported file.</summary>
public static class SamplingFrameValidator
{
    public const int MaxDimensions = 10;
    public const int MaxRows = 10_000;
    public const int MaxTarget = 1_000_000;

    /// <summary>Messages are English, so numbers use invariant formatting whatever the server's culture.</summary>
    internal static string Format(int number) => number.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>Validates a frame for saving; <paramref name="formFields"/> are the XPaths of the form's fields.</summary>
    public static (List<SamplingDimension> Dimensions, List<SamplingTarget> Targets) Validate(
        SamplingFrameInput input, IReadOnlySet<string> formFields, FieldErrors errors)
    {
        var dimensionInputs = input.Dimensions ?? [];
        var names = ValidateDimensionNames(dimensionInputs.Select(d => d.Name).ToList(), (index, message) =>
            errors.Add(index is null ? "dimensions" : $"dimensions[{index}].name", message));

        var dimensions = new List<SamplingDimension>();
        for (var i = 0; i < dimensionInputs.Count; i++)
        {
            var field = dimensionInputs[i].Field?.Trim() ?? "";
            if (field.Length == 0)
            {
                errors.Add($"dimensions[{i}].field", "Choose the form field for this dimension.");
            }
            else if (!formFields.Contains(field))
            {
                errors.Add($"dimensions[{i}].field", $"Dimension '{names[i]}' maps to '{field}', which is not a field of the form.");
            }

            dimensions.Add(new SamplingDimension(names[i], field));
        }

        var rows = (input.Targets ?? []).Select(t => (Values: (IReadOnlyList<string?>?)t.Values, t.Target)).ToList();
        var targets = ValidateTargets(dimensionInputs.Count, rows,
            (index, field, message) => errors.Add(index is null ? "targets" : field is null ? $"targets[{index}]" : $"targets[{index}].{field}", message),
            index => $"row {index + 1}");
        return (dimensions, targets);
    }

    /// <summary>Checks count, emptiness and uniqueness of dimension names; returns them trimmed.</summary>
    public static List<string> ValidateDimensionNames(IReadOnlyList<string?> names, Action<int?, string> report)
    {
        if (names.Count is 0 or > MaxDimensions)
        {
            report(null, $"Define between 1 and {MaxDimensions} dimensions.");
        }

        var trimmed = names.Select(n => n?.Trim() ?? "").ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < trimmed.Count; i++)
        {
            if (trimmed[i].Length == 0)
            {
                report(i, "Enter a name for this dimension.");
            }
            else if (!seen.Add(trimmed[i]))
            {
                report(i, $"Another dimension is already named '{trimmed[i]}'.");
            }
        }

        return trimmed;
    }

    /// <summary>
    /// Checks every target row. <paramref name="report"/> receives the row index, the failing part (<c>values</c>,
    /// <c>target</c> or <c>null</c> for the whole row) and a message; <paramref name="describe"/> names a row in messages.
    /// </summary>
    public static List<SamplingTarget> ValidateTargets(
        int dimensionCount,
        IReadOnlyList<(IReadOnlyList<string?>? Values, decimal? Target)> rows,
        Action<int?, string?, string> report,
        Func<int, string> describe)
    {
        if (rows.Count > MaxRows)
        {
            report(null, null, $"A sampling frame can have at most {Format(MaxRows)} rows.");
        }

        var targets = new List<SamplingTarget>(rows.Count);
        var firstRowByCombination = new Dictionary<string, int>();
        for (var i = 0; i < rows.Count; i++)
        {
            var values = (rows[i].Values ?? []).Select(v => v?.Trim() ?? "").ToList();
            if (values.Count != dimensionCount || values.Any(v => v.Length == 0))
            {
                report(i, "values", "Enter a value for every dimension.");
            }

            var target = rows[i].Target;
            var validTarget = target is not null && target == decimal.Truncate(target.Value) && target is >= 0 and <= MaxTarget;
            if (!validTarget)
            {
                report(i, "target", $"The target must be a whole number from 0 to {Format(MaxTarget)}.");
            }

            var key = string.Join('\u001f', values.Select(v => v.ToUpperInvariant()));
            if (!firstRowByCombination.TryAdd(key, i))
            {
                report(i, null, $"This combination repeats {describe(firstRowByCombination[key])}.");
            }

            targets.Add(new SamplingTarget(values, validTarget ? (int)target!.Value : 0));
        }

        return targets;
    }
}
