using Tamiza.Api.Data;
using Tamiza.Api.Projects;

namespace Tamiza.Api.Mcp;

/// <summary>
/// The places a project's data can be read from through the MCP server (design D6): the published schemas of the pipeline and the
/// reference tables. The REST dashboards offer only <c>clean</c> and <c>output</c>; the others are for the people who write scripts.
/// A schema name is built only from the project id and one of these words, never from request text.
/// </summary>
public static class McpSources
{
    public const string Raw = "raw";
    public const string Clean = "clean";
    public const string Output = "output";
    public const string Indicators = "indicators";
    public const string Reference = "reference";

    public static readonly IReadOnlyList<string> All = [Raw, Clean, Output, Indicators, Reference];

    public static bool IsKnown(string? source) => source is not null && All.Contains(source);

    /// <summary>The role that reads the source: the dashboards' sources are open to every member, the rest to the people who write scripts.</summary>
    public static ProjectRole RoleToRead(string source) => source is Clean or Output or Indicators ? ProjectRole.Viewer : ProjectRole.Analyst;

    /// <summary><c>p_&lt;32 hex&gt;_&lt;source&gt;</c>, the name the analytics worker publishes under; the reference tables have no schema.</summary>
    public static string SchemaName(Guid projectId, string source) =>
        source is Raw or Clean or Output or Indicators
            ? $"p_{projectId:N}_{source}"
            : throw new ArgumentOutOfRangeException(nameof(source), source, "Not a published schema.");

    /// <summary>The run step that publishes a schema as a whole; null for the indicators, which publish one table per step.</summary>
    public static string? StepOf(string source) => source switch
    {
        Raw => "flatten",
        Clean => "clean",
        Output => "output",
        _ => null,
    };
}
