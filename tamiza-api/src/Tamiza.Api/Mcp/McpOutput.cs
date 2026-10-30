using ModelContextProtocol.Protocol;

namespace Tamiza.Api.Mcp;

/// <summary>Builds the tool results: the JSON of a success within the size budget, or the body of a refusal.</summary>
public static class McpOutput
{
    public static CallToolResult Ok(object? value, int maxCharacters, string? hint, McpCallScope? call = null)
    {
        var text = McpResultBudget.Serialize(value, maxCharacters, hint);
        if (call is not null)
        {
            call.ResultChars = text.Length;
        }

        return new CallToolResult { Content = [new TextContentBlock { Text = text }] };
    }

    /// <summary>The refusal as <c>{ code, message, ...details }</c> with <c>isError</c>, so the client reads why and what to change.</summary>
    public static CallToolResult Error(McpRefusal refusal, McpCallScope? call = null)
    {
        var body = new Dictionary<string, object?> { ["code"] = refusal.Code, ["message"] = refusal.Message };
        foreach (var (name, value) in refusal.Details)
        {
            body[name] = value;
        }

        var text = McpResultBudget.Serialize(body, int.MaxValue);
        if (call is not null)
        {
            call.ResultChars = text.Length;
        }

        return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = text }] };
    }
}
