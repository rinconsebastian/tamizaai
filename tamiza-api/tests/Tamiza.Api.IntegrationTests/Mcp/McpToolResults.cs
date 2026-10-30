using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Tamiza.Api.IntegrationTests.Mcp;

/// <summary>Reads the JSON a tool sends back.</summary>
public static class McpToolResults
{
    public static string Text(this CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(t => t.Text));

    public static JsonDocument Json(this CallToolResult result) => JsonDocument.Parse(result.Text());

    /// <summary>The <c>code</c> of a refusal, or null for a result that is not an error.</summary>
    public static string? Code(this CallToolResult result)
    {
        if (result.IsError != true)
        {
            return null;
        }

        using var body = result.Json();
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
