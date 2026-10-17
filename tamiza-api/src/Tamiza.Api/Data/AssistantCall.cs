namespace Tamiza.Api.Data;

/// <summary>
/// One request to the AI assistant. It records who asked, for which script, the model, the token counts, the duration, the
/// outcome and the size of each part of the context, but never a message, the editor's text, the context or the answer.
/// </summary>
public sealed class AssistantCall
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public Guid UserId { get; set; }

    public ScriptKind Kind { get; set; }

    /// <summary>The indicator the request was made for; null for the clean and output scripts.</summary>
    public string? Name { get; set; }

    public AssistantAction Action { get; set; }

    /// <summary>The test a fix referred to. Not a foreign key: tests are deleted after 24 hours.</summary>
    public Guid? TestId { get; set; }

    public required string Model { get; set; }

    /// <summary><c>AssistantPrompts.Version</c> when the request was made.</summary>
    public int PromptKit { get; set; }

    public required AssistantCallContext Context { get; set; }

    public AssistantCallStatus Status { get; set; }

    /// <summary>The <c>assistant.*</c> code of a failed request, or <c>assistant.interrupted</c> for one the api lost on restart.</summary>
    public string? ErrorCode { get; set; }

    public int? PromptTokens { get; set; }

    public int? CompletionTokens { get; set; }

    public int? TotalTokens { get; set; }

    /// <summary>The token counts were estimated from the size of what was sent and received, because the provider reported none.</summary>
    public bool TokensEstimated { get; set; }

    /// <summary>How the analyst applied the proposal, when they did.</summary>
    public AssistantOutcome? Outcome { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }
}

public enum AssistantAction
{
    Script,
    Cell,
    Fix,
}

public enum AssistantCallStatus
{
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

/// <summary>How a proposal was applied: <c>script</c> replaced the script, <c>cell</c> replaced a cell, <c>insert</c> inserted at the cursor.</summary>
public enum AssistantOutcome
{
    Script,
    Cell,
    Insert,
}

/// <summary>The size of each block of the request in bytes, what was left out to fit and whether a test was attached. No text.</summary>
public sealed record AssistantCallContext(Dictionary<string, int> Blocks, List<string> Omitted, bool HasTest);
