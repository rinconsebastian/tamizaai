namespace Tamiza.Api.Data;

/// <summary>
/// One tool call of an AI client through the MCP server. It records who called, which tool, the project when the call named one, the
/// access level the call got, the outcome, the duration and the size of the result, but never an argument or any part of the result.
/// </summary>
public sealed class McpCall
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Null for the tools that name no project and for a call refused before its project was known.</summary>
    public Guid? ProjectId { get; set; }

    public required string Tool { get; set; }

    /// <summary>The effective access level of the call; null when the call was refused before it was resolved.</summary>
    public McpAccess? Access { get; set; }

    public McpCallStatus Status { get; set; }

    /// <summary>The <c>mcp.*</c> or endpoint code of a refused or failed call.</summary>
    public string? ErrorCode { get; set; }

    public int DurationMs { get; set; }

    /// <summary>The characters of the result sent back, after the size budget; null when the call returned none.</summary>
    public int? ResultChars { get; set; }

    public DateTimeOffset StartedAt { get; set; }
}

/// <summary>
/// How much of a project's data an AI client may read: <c>off</c> nothing, <c>aggregates</c> summaries only, <c>rows</c> summaries and
/// rows. The order matters: a lower value is a stricter level, so the effective level is the smaller of two.
/// </summary>
public enum McpAccess
{
    Off,
    Aggregates,
    Rows,
}

public enum McpCallStatus
{
    Succeeded,
    Failed,
    Refused,
}

/// <summary>Where a script version was saved: <c>app</c> is the editor and the REST API, <c>mcp</c> an AI client.</summary>
public enum ScriptOrigin
{
    App,
    Mcp,
}
