namespace Tamiza.Api.Indicators;

/// <summary>
/// What a dashboard's name must satisfy on its own (design D5). That no other dashboard of the project has it is checked against the
/// project's dashboards by <see cref="DashboardEndpoints"/>, and by the unique index for a race.
/// </summary>
public static class DashboardValidation
{
    public const int MaxNameLength = 40;

    /// <summary>The permanent tabs no dashboard may be named after, by normalized name, with the name each is shown under.</summary>
    private static readonly Dictionary<string, string> Reserved = new(StringComparer.Ordinal)
    {
        ["performance"] = "Performance",
        ["clean data"] = "Clean data",
        ["settings"] = "Settings",
    };

    /// <summary>The trimmed name, or the error on <c>name</c> that refuses it.</summary>
    public static (string? Name, string? Error) Name(string? input)
    {
        var name = input?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return (null, "The name is required.");
        }

        // Checked on the name as sent: trimming would quietly drop a trailing line break.
        if (input!.Any(char.IsControl))
        {
            return (null, "The name cannot contain control characters.");
        }

        if (name.Length > MaxNameLength)
        {
            return (null, $"The name can have at most {MaxNameLength} characters.");
        }

        if (Reserved.TryGetValue(Normalize(name), out var tab))
        {
            return (null, $"\"{tab}\" is the name of a permanent tab. Choose another name.");
        }

        return (name, null);
    }

    /// <summary>The name as <c>dashboards.normalized_name</c> stores it: trimmed and in lower case.</summary>
    public static string Normalize(string name) => name.Trim().ToLowerInvariant();

    public static string TakenMessage(string name) => $"Another dashboard of this project is named \"{name}\".";
}
