namespace Tamiza.Api.CleanData;

/// <summary>
/// The published schemas the dashboard reads. The raw and indicator schemas are not offered. A schema name is built only
/// from the route's project id and one of these two words, never from request text.
/// </summary>
public static class DataSources
{
    public const string Output = "output";
    public const string Clean = "clean";

    /// <summary>In the order the dashboard prefers them: the output tables first.</summary>
    public static readonly IReadOnlyList<string> All = [Output, Clean];

    public static bool IsKnown(string? source) => source is Output or Clean;

    /// <summary><c>p_&lt;32 hex&gt;_&lt;source&gt;</c>, the name the analytics worker publishes under.</summary>
    public static string SchemaName(Guid projectId, string source)
    {
        if (!IsKnown(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source), source, "Not a source of the dashboard.");
        }

        return $"p_{projectId:N}_{source}";
    }

    /// <summary>The identifier quoted for SQL: wrapped in double quotes, with any double quote doubled.</summary>
    public static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
