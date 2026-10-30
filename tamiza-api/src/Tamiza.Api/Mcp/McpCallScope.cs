using Tamiza.Api.Data;

namespace Tamiza.Api.Mcp;

/// <summary>
/// What one tool call tells the record that is written about it (design D12). It lives for the call: the tool and the access gate fill it
/// as they learn the project, the level and the outcome, and the call filter reads it when the call ends.
/// </summary>
public sealed class McpCallScope
{
    public string Tool { get; set; } = "";

    /// <summary>Set once the person is known to be a member, so a call that names a project of others never points a record at it.</summary>
    public Guid? ProjectId { get; set; }

    /// <summary>The access level the call got; null until the gate resolves it, and for a call it refuses.</summary>
    public McpAccess? Access { get; set; }

    public McpCallStatus Status { get; set; } = McpCallStatus.Succeeded;

    public string? ErrorCode { get; set; }

    /// <summary>The characters of the result sent back; set by the tool's output, or measured by the filter.</summary>
    public int? ResultChars { get; set; }

    public void Refused(McpRefusal refusal)
    {
        Status = McpCallStatus.Refused;
        ErrorCode = refusal.Code;
    }

    public void Failed(string code)
    {
        Status = McpCallStatus.Failed;
        ErrorCode = code;
    }
}
